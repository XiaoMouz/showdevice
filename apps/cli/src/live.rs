//! Independent device actors keep one failed plugin from delaying other devices.
use super::*;
use std::io::Read;
use std::{
    collections::BTreeSet,
    sync::mpsc::{self, Receiver, SyncSender},
};
enum Event {
    Update {
        generation: u64,
        view: Box<DeviceView>,
    },
    Refresh,
    Settings(PollPreferences),
    Visibility(bool, Vec<String>),
    Stop,
}
enum Control {
    Refresh,
    Reschedule,
    Stop,
}
#[derive(Clone)]
struct Demand {
    all: bool,
    ids: BTreeSet<String>,
    revision: u64,
    stopping: bool,
}
impl Demand {
    fn wants(&self, id: &str) -> bool {
        self.all || self.ids.contains(id)
    }
}
fn poll_due(wanted: bool, changed: bool, elapsed: Option<Duration>, interval: u64) -> bool {
    wanted
        && elapsed.is_none_or(|age| {
            age >= Duration::from_secs(2) && (changed || age >= Duration::from_secs(interval))
        })
}
struct Actor {
    generation: u64,
    control: SyncSender<Control>,
    thread: Option<std::thread::JoinHandle<()>>,
}
fn remove_disconnected(
    actors: &mut BTreeMap<String, Actor>,
    views: &mut BTreeMap<String, DeviceView>,
    current: &BTreeSet<String>,
) -> bool {
    actors.retain(|id, actor| {
        if current.contains(id) {
            return true;
        }
        let _ = actor.control.try_send(Control::Stop);
        false
    });
    let before = views.len();
    views.retain(|id, _| current.contains(id));
    before != views.len()
}
fn apply_update(
    actors: &BTreeMap<String, Actor>,
    views: &mut BTreeMap<String, DeviceView>,
    generation: u64,
    view: DeviceView,
) -> bool {
    if !actors
        .get(&view.device.id)
        .is_some_and(|a| a.generation == generation)
    {
        return false;
    }
    views.insert(view.device.id.clone(), view);
    true
}
fn with_broker<T>(
    broker: &mut Option<Broker>,
    open: impl FnOnce() -> Result<Broker>,
    poll: impl FnOnce(&mut Broker) -> Result<T>,
) -> Result<T> {
    if broker.is_none() {
        *broker = Some(open()?);
    }
    let result = poll(broker.as_mut().unwrap());
    if broker.as_ref().is_some_and(Broker::needs_reopen) {
        // Drop the stale handle even if the HID path never disappeared between scans.
        *broker = None;
    }
    result
}

fn worker(
    package: Package,
    device: Device,
    generation: u64,
    events: SyncSender<Event>,
    control: Receiver<Control>,
    demo: bool,
    preferences: std::sync::Arc<std::sync::RwLock<PollPreferences>>,
    demand: std::sync::Arc<std::sync::RwLock<Demand>>,
) {
    let mut view = DeviceView {
        surface_templates: package.descriptor.surface_templates.clone(),
        product_image: package.product_image(),
        device_kind: package.descriptor.device_kind,
        device: device.clone(),
        plugin: package.descriptor.manifest.id.clone(),
        state: package
            .descriptor
            .capabilities
            .iter()
            .filter(|c| c.readable)
            .map(|c| {
                (
                    c.id.clone(),
                    Reading::unknown(&package.descriptor.manifest.id),
                )
            })
            .collect(),
        pages: sd_ui_schema::validate(&package.descriptor).unwrap_or_default(),
        capabilities: package.descriptor.capabilities.clone(),
        error: None,
    };
    let mut session = match Session::launch(&package, &device) {
        Ok(session) => session,
        Err(e) => {
            view.error = Some(format!("{e}. Reconnect the device to retry."));
            let _ = events.send(Event::Update {
                generation,
                view: Box::new(view),
            });
            return;
        }
    };
    let mut broker = None;
    let mut last: Option<Instant> = None;
    let mut revision = 0;
    loop {
        if preferences.read().unwrap().excluded(&device.id) {
            return;
        }
        let current = demand.read().unwrap().clone();
        if current.stopping {
            return;
        }
        let seconds = preferences.read().unwrap().interval(&device.id);
        if !poll_due(
            current.wants(&device.id),
            current.revision != revision,
            last.map(|t| t.elapsed()),
            seconds,
        ) {
            let wait = if !current.wants(&device.id) {
                Duration::from_secs(3600)
            } else if current.revision != revision {
                Duration::from_secs(2).saturating_sub(last.unwrap().elapsed())
            } else {
                Duration::from_secs(seconds).saturating_sub(last.unwrap().elapsed())
            };
            match control.recv_timeout(wait) {
                Ok(Control::Stop) | Err(mpsc::RecvTimeoutError::Disconnected) => return,
                _ => continue,
            }
        }
        let result = with_broker(
            &mut broker,
            || {
                if demo {
                    return Ok(Broker::new(|_| Ok(vec![])));
                }
                let inventory = sd_hid::Inventory::enumerate()?;
                let current = inventory
                    .devices
                    .iter()
                    .find(|d| d.id == device.id)
                    .context("Device is reconnecting")?;
                anyhow::ensure!(
                    package
                        .descriptor
                        .manifest
                        .matches
                        .iter()
                        .any(|m| m.score(current).is_some()),
                    "Device no longer matches plugin"
                );
                let mut handle = inventory.open(&device.id)?;
                Ok(Broker::new(move |q| {
                    handle.execute(q).map_err(|e| format!("{e:#}"))
                }))
            },
            |broker| session.poll(broker),
        );
        if broker.as_ref().is_some_and(|b| !b.is_healthy()) {
            session.stop();
        }
        match result {
            Ok(state) => {
                sd_device_features_mark_stale(&mut view.state);
                view.state.extend(state);
                view.error = None;
            }
            Err(e) => {
                sd_device_features_mark_stale(&mut view.state);
                view.error = Some(if session.active {
                    format!("{e}. Waiting for device; retrying automatically.")
                } else {
                    format!("{e}. Reconnect the device to retry.")
                });
            }
        }
        if events
            .send(Event::Update {
                generation,
                view: Box::new(view.clone()),
            })
            .is_err()
        {
            return;
        }
        if !session.active {
            return;
        }
        // Requests arriving during this query share its result, rather than queueing a second query.
        revision = demand.read().unwrap().revision;
        last = Some(Instant::now());
    }
}
pub fn run(root: &Path, demo: bool) -> Result<()> {
    let initial = std::env::var("SHOWDEVICE_POLL_PREFERENCES")
        .ok()
        .and_then(|s| serde_json::from_str::<PollPreferences>(&s).ok())
        .filter(PollPreferences::valid)
        .unwrap_or_default();
    let demand = std::sync::Arc::new(std::sync::RwLock::new(Demand {
        all: std::env::var("SHOWDEVICE_VISIBILITY_MANAGED")
            .ok()
            .as_deref()
            != Some("1"),
        ids: BTreeSet::new(),
        revision: 1,
        stopping: false,
    }));
    let preferences = std::sync::Arc::new(std::sync::RwLock::new(initial));
    let (packages, mut diagnostics) = sd_runtime::discover(root);
    if demo {
        diagnostics.push("SIMULATION — no hardware is queried".into());
    }
    let (tx, rx) = mpsc::sync_channel(32);
    let commands = tx.clone();
    std::thread::spawn(move || {
        // Core lifetime is tied to the UI pipe. This also stops plugin children
        // normally on macOS/Linux instead of leaving orphan processes.
        let mut reader = std::io::BufReader::new(std::io::stdin());
        use std::io::BufRead;
        loop {
            let mut line = String::new();
            match reader
                .by_ref()
                .take(MAX_FRAME as u64 + 1)
                .read_line(&mut line)
            {
                Ok(0) | Err(_) => {
                    let _ = commands.send(Event::Stop);
                    return;
                }
                Ok(_) if line.trim() == "refresh" => {
                    let _ = commands.try_send(Event::Refresh);
                }
                Ok(_) => {
                    if line.len() > MAX_FRAME || !line.ends_with('\n') {
                        let _ = commands.send(Event::Stop);
                        return;
                    }
                    match serde_json::from_str::<AppCommand>(&line) {
                        Ok(AppCommand::Settings { preferences }) if preferences.valid() => {
                            let _ = commands.send(Event::Settings(preferences));
                        }
                        Ok(AppCommand::Visibility { all, device_ids })
                            if device_ids.len() <= 256
                                && device_ids.iter().all(|id| id.len() <= 1024) =>
                        {
                            let _ = commands.send(Event::Visibility(all, device_ids));
                        }
                        _ => {}
                    }
                }
            }
        }
    });
    let mut actors: BTreeMap<String, Actor> = BTreeMap::new();
    let mut views: BTreeMap<String, DeviceView> = BTreeMap::new();
    let mut generation = 0;
    let mut scan_at = Instant::now();
    let mut refresh_at = Instant::now() - Duration::from_secs(3);
    let mut dirty = true;
    loop {
        if Instant::now() >= scan_at {
            let devices = if demo {
                Some(vec![demo_device()])
            } else {
                match sd_hid::Inventory::enumerate() {
                    Ok(i) => Some(i.devices),
                    Err(e) => {
                        diagnostics = vec![format!("Device enumeration failed: {e}")];
                        dirty = true;
                        None // Enumeration failure is not evidence of disconnection.
                    }
                }
            };
            if let Some(devices) = devices {
                let devices: Vec<_> = devices
                    .into_iter()
                    .filter(|d| !preferences.read().unwrap().excluded(&d.id))
                    .collect();
                let current: BTreeSet<_> = devices.iter().map(|d| d.id.clone()).collect();
                dirty |= remove_disconnected(&mut actors, &mut views, &current);
                for device in devices {
                    if actors.contains_key(&device.id) {
                        continue;
                    }
                    if actors.len() >= 16 {
                        break;
                    }
                    let package = match sd_runtime::select(&packages, &device) {
                        Ok(Some(p)) => p.clone(),
                        Ok(None) => continue,
                        Err(e) => {
                            let error = e.to_string();
                            if !diagnostics.contains(&error) {
                                diagnostics.push(error);
                                dirty = true;
                            }
                            continue;
                        }
                    };
                    generation += 1;
                    let g = generation;
                    let id = device.id.clone();
                    let (control, jobs) = mpsc::sync_channel(1);
                    let events = tx.clone();
                    let prefs = preferences.clone();
                    let interest = demand.clone();
                    let thread = std::thread::spawn(move || {
                        worker(package, device, g, events, jobs, demo, prefs, interest)
                    });
                    actors.insert(
                        id,
                        Actor {
                            generation: g,
                            control,
                            thread: Some(thread),
                        },
                    );
                }
            }
            scan_at = Instant::now() + Duration::from_secs(5);
        }
        if dirty {
            let snapshot = AppSnapshot {
                api: API_VERSION.into(),
                generated_at_ms: now_ms(),
                writes_enabled: false,
                devices: views.values().cloned().collect(),
                diagnostics: diagnostics.clone(),
            };
            write_app_frame(&mut std::io::stdout(), &snapshot)?;
            dirty = false;
        }
        match rx.recv_timeout(scan_at.saturating_duration_since(Instant::now())) {
            Ok(Event::Settings(next)) => {
                *preferences.write().unwrap() = next;
                let current = actors
                    .keys()
                    .filter(|id| !preferences.read().unwrap().excluded(id))
                    .cloned()
                    .collect();
                dirty |= remove_disconnected(&mut actors, &mut views, &current);
                for actor in actors.values() {
                    let _ = actor.control.try_send(Control::Reschedule);
                }
                scan_at = Instant::now();
            }
            Ok(Event::Update { generation, view }) => {
                dirty |= apply_update(&actors, &mut views, generation, *view);
            }
            Ok(Event::Visibility(all, ids)) => {
                let mut interest = demand.write().unwrap();
                interest.all = all;
                interest.ids = ids.into_iter().collect();
                interest.revision += 1;
                drop(interest);
                for actor in actors.values() {
                    let _ = actor.control.try_send(Control::Reschedule);
                }
                scan_at = Instant::now();
            }
            Ok(Event::Refresh) => {
                if refresh_at.elapsed() >= Duration::from_secs(2) {
                    demand.write().unwrap().revision += 1;
                    for actor in actors.values() {
                        let _ = actor.control.try_send(Control::Refresh);
                    }
                    scan_at = Instant::now();
                    refresh_at = Instant::now();
                }
            }
            Ok(Event::Stop) | Err(mpsc::RecvTimeoutError::Disconnected) => break,
            Err(mpsc::RecvTimeoutError::Timeout) => {}
        }
    }
    demand.write().unwrap().stopping = true;
    for actor in actors.values() {
        let _ = actor.control.try_send(Control::Stop);
    }
    drop(rx);
    for actor in actors.values_mut() {
        if let Some(thread) = actor.thread.take() {
            let _ = thread.join();
        }
    }
    // Workers receive shutdown immediately while idle, or after their bounded poll.
    Ok(())
}

#[cfg(test)]
mod reconnect_tests {
    use super::*;
    fn view(id: &str) -> DeviceView {
        let mut device = demo_device();
        device.id = id.into();
        DeviceView {
            surface_templates: vec![],
            product_image: None,
            device_kind: DeviceKind::Other,
            device,
            plugin: "test".into(),
            state: State::new(),
            pages: vec![],
            capabilities: vec![],
            error: None,
        }
    }
    fn actor(generation: u64) -> (Actor, Receiver<Control>) {
        let (control, rx) = mpsc::sync_channel(1);
        (
            Actor {
                generation,
                control,
                thread: None,
            },
            rx,
        )
    }
    #[test]
    fn disconnect_removes_view_and_late_updates_cannot_restore_it() {
        let (a, rx) = actor(1);
        let mut actors = BTreeMap::from([("a".into(), a)]);
        let mut views = BTreeMap::from([("a".into(), view("a"))]);
        assert!(remove_disconnected(
            &mut actors,
            &mut views,
            &BTreeSet::new()
        ));
        assert!(actors.is_empty() && views.is_empty());
        assert!(matches!(rx.try_recv(), Ok(Control::Stop)));
        assert!(!apply_update(&actors, &mut views, 1, view("a")));
        let (replacement, _rx) = actor(2);
        actors.insert("a".into(), replacement);
        assert!(!apply_update(&actors, &mut views, 1, view("a")));
        assert!(apply_update(&actors, &mut views, 2, view("a")));
        assert_eq!(views.len(), 1);
    }
    #[test]
    fn remaining_devices_and_read_errors_are_not_removed() {
        let (a, _rx_a) = actor(1);
        let (b, _rx_b) = actor(2);
        let mut actors = BTreeMap::from([("a".into(), a), ("b".into(), b)]);
        let mut connected = view("b");
        connected.error = Some("temporary read failure".into());
        let mut views = BTreeMap::from([("a".into(), view("a")), ("b".into(), connected)]);
        let current = BTreeSet::from(["b".into()]);
        assert!(remove_disconnected(&mut actors, &mut views, &current));
        assert_eq!(views.keys().cloned().collect::<Vec<_>>(), vec!["b"]);
        assert!(views["b"].error.is_some());
        assert!(!remove_disconnected(&mut actors, &mut views, &current));
    }
    fn query(b: &mut Broker) -> Result<Vec<u8>> {
        b.query(
            Query::HidInput {
                response_len: 1,
                latest: false,
            },
            Duration::from_millis(100),
        )
    }
    #[test]
    fn stale_handle_is_replaced_without_a_path_change() {
        let mut broker = Some(Broker::new(|_| Err("device disconnected".into())));
        assert!(with_broker(&mut broker, || panic!("existing handle"), query).is_err());
        assert!(broker.is_none());
        assert_eq!(
            with_broker(&mut broker, || Ok(Broker::new(|_| Ok(vec![42]))), query).unwrap(),
            vec![42]
        );
        assert_eq!(
            with_broker(
                &mut broker,
                || panic!("healthy handle must be reused"),
                query
            )
            .unwrap(),
            vec![42]
        );
    }
    #[test]
    fn failed_open_can_recover_on_next_attempt() {
        let mut broker = None;
        assert!(with_broker(&mut broker, || anyhow::bail!("not ready"), query).is_err());
        assert!(broker.is_none());
        assert!(with_broker(&mut broker, || Ok(Broker::new(|_| Ok(vec![1]))), query).is_ok());
    }
    #[test]
    fn retired_transport_is_not_reopened() {
        let mut broker = Some(Broker::new(|_| Ok(vec![0; 2])));
        assert!(with_broker(&mut broker, || panic!("existing handle"), query).is_err());
        assert!(!broker.as_ref().unwrap().is_healthy());
        assert!(!broker.as_ref().unwrap().needs_reopen());
    }
}

#[cfg(test)]
mod demand_tests {
    use super::*;
    #[test]
    fn hidden_never_polls_and_reveal_debounces() {
        assert!(!poll_due(false, true, None, 30));
        assert!(!poll_due(false, true, Some(Duration::from_secs(90)), 30));
        assert!(poll_due(true, true, None, 30));
        assert!(!poll_due(true, true, Some(Duration::from_secs(1)), 30));
        assert!(poll_due(true, true, Some(Duration::from_secs(2)), 30));
        assert!(!poll_due(true, false, Some(Duration::from_secs(20)), 30));
        assert!(poll_due(true, false, Some(Duration::from_secs(30)), 30));
    }
    #[test]
    fn device_interest_is_scoped() {
        let d = Demand {
            all: false,
            ids: BTreeSet::from(["mouse".into()]),
            revision: 1,
            stopping: false,
        };
        assert!(d.wants("mouse"));
        assert!(!d.wants("keyboard"));
    }
}
