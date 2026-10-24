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
    Stop,
}
enum Control {
    Refresh,
    Stop,
}
struct Actor {
    generation: u64,
    control: SyncSender<Control>,
    thread: Option<std::thread::JoinHandle<()>>,
}
fn worker(
    package: Package,
    device: Device,
    generation: u64,
    events: SyncSender<Event>,
    control: Receiver<Control>,
    demo: bool,
) {
    let mut view = DeviceView {
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
    let started = (|| -> Result<(Session, Broker)> {
        let broker = if demo {
            Broker::new(|_| Ok(vec![]))
        } else {
            let inventory = sd_hid::Inventory::enumerate()?;
            let mut handle = inventory.open(&device.id)?;
            Broker::new(move |q| handle.execute(q).map_err(|e| e.to_string()))
        };
        Ok((Session::launch(&package, &device)?, broker))
    })();
    let (mut session, mut broker) = match started {
        Ok(v) => v,
        Err(e) => {
            view.error = Some(format!("{e}. Reconnect the device to retry."));
            let _ = events.send(Event::Update {
                generation,
                view: Box::new(view),
            });
            return;
        }
    };
    loop {
        match session.poll(&mut broker) {
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
        match control.recv_timeout(Duration::from_secs(30)) {
            Ok(Control::Stop) | Err(mpsc::RecvTimeoutError::Disconnected) => return,
            _ => {}
        }
    }
}
pub fn run(root: &Path, demo: bool) -> Result<()> {
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
            match reader.by_ref().take(128).read_line(&mut line) {
                Ok(0) | Err(_) => {
                    let _ = commands.send(Event::Stop);
                    return;
                }
                Ok(_) if line.trim() == "refresh" => {
                    let _ = commands.try_send(Event::Refresh);
                }
                _ => {}
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
                vec![demo_device()]
            } else {
                match sd_hid::Inventory::enumerate() {
                    Ok(i) => i.devices,
                    Err(e) => {
                        diagnostics = vec![format!("Device enumeration failed: {e}")];
                        vec![]
                    }
                }
            };
            let current: BTreeSet<_> = devices.iter().map(|d| d.id.clone()).collect();
            actors.retain(|id, a| {
                if current.contains(id) {
                    return true;
                }
                let _ = a.control.try_send(Control::Stop);
                if let Some(v) = views.get_mut(id) {
                    for r in v.state.values_mut() {
                        r.availability = Availability::Offline;
                    }
                    v.error = Some("Device disconnected".into());
                }
                dirty = true;
                false
            });
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
                let thread =
                    std::thread::spawn(move || worker(package, device, g, events, jobs, demo));
                actors.insert(
                    id,
                    Actor {
                        generation: g,
                        control,
                        thread: Some(thread),
                    },
                );
            }
            if views.len() > 16 {
                views.retain(|id, _| current.contains(id));
                dirty = true;
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
            Ok(Event::Update { generation, view }) => {
                if actors
                    .get(&view.device.id)
                    .is_some_and(|a| a.generation == generation)
                {
                    views.insert(view.device.id.clone(), *view);
                    if views.len() > 16 {
                        views.retain(|id, _| actors.contains_key(id));
                    }
                    dirty = true;
                }
            }
            Ok(Event::Refresh) => {
                if refresh_at.elapsed() >= Duration::from_secs(2) {
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
