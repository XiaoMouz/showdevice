use anyhow::{Context, Result, bail};
use sd_api::*;
use sd_runtime::{Broker, Package, Session};
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
    time::{Duration, Instant},
};
mod live;
fn default_plugins() -> PathBuf {
    let exe = std::env::current_exe().unwrap_or_default();
    let sibling = exe.parent().unwrap_or(Path::new(".")).join("plugins");
    if sibling.is_dir() {
        sibling
    } else {
        PathBuf::from("dist/plugins")
    }
}
fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    match args.first().map(String::as_str).unwrap_or("help") {
        "enumerate" => {
            println!(
                "{}",
                serde_json::to_string_pretty(&sd_hid::Inventory::enumerate()?.devices)?
            );
        }
        "probe" => {
            let p = Package::load(Path::new(
                args.get(1)
                    .context("probe <package> <device-index> <query>")?,
            ))?;
            let index: usize = args.get(2).context("device index required")?.parse()?;
            let name = args.get(3).context("query name required")?;
            let inv = sd_hid::Inventory::enumerate()?;
            let d = inv.devices.get(index).context("invalid device index")?;
            if !p
                .descriptor
                .manifest
                .matches
                .iter()
                .any(|m| m.product_id == Some(d.product_id) && m.score(d).is_some())
            {
                bail!("device is outside plugin's exact device match");
            }
            let recipe = p
                .permissions
                .queries
                .get(name)
                .context("unknown approved query")?
                .clone();
            let mut handle = inv.open(&d.id)?;
            let result = Broker::new(move |q| handle.execute(q).map_err(|e| e.to_string()))
                .query(recipe, Duration::from_secs(4));
            println!(
                "{} {:04x}:{:04x} {}",
                d.name,
                d.usage_page,
                d.usage,
                match result {
                    Ok(v) => format!("{:02x?}", v),
                    Err(e) => e.to_string(),
                }
            );
        }
        "package" => package(args.get(1).map(Path::new).unwrap_or(Path::new(".")))?,
        "validate" => {
            let p = Package::load(Path::new(
                args.get(1).context("validate <package-directory>")?,
            ))?;
            println!(
                "Valid: {} {}",
                p.descriptor.manifest.id, p.descriptor.manifest.version
            );
        }
        "schema" => {
            println!(
                "{}",
                serde_json::to_string_pretty(&schemars::schema_for!(AppSnapshot))?
            );
        }
        "schemas" => schemas(Path::new(
            args.get(1).context("schemas <output-directory>")?,
        ))?,
        "new-plugin" => new_plugin(Path::new(
            args.get(1).context("new-plugin <new-directory>")?,
        ))?,
        "self-test" => self_test(
            args.get(1)
                .map(PathBuf::from)
                .unwrap_or_else(default_plugins),
        )?,
        "snapshot" | "watch" | "demo" => {
            let root = args
                .get(1)
                .map(PathBuf::from)
                .unwrap_or_else(default_plugins);
            let demo = args[0] == "demo";
            let watch = args[0] == "watch" || demo;
            if watch {
                live::run(&root, demo)?;
            } else {
                monitor(&root, false, demo)?;
            }
        }
        _ => println!(
            "ShowDevice (local, read-only)\n  enumerate\n  package [repo]\n  validate <package>\n  snapshot [plugins]\n  watch [plugins]   JSON Lines over stdout; stdin 'refresh' requests a refresh\n  demo [plugins]    explicit simulated device\n  self-test [plugins]\n  schema\n  schemas <directory>\n  new-plugin <new-directory>"
        ),
    }
    Ok(())
}
fn package(root: &Path) -> Result<()> {
    let target = std::env::current_exe()?
        .parent()
        .context("executable directory")?
        .to_owned();
    let dest = root.join("dist/plugins");
    std::fs::create_dir_all(&dest)?;
    for brand in ["razer", "hyperx", "rapoo", "nuphy", "sony", "mock"] {
        let source = root.join("plugins").join(brand);
        let mut d: Descriptor =
            serde_json::from_slice(&std::fs::read(source.join("plugin.json"))?)?;
        let filename = format!("sd-plugin-{brand}{}", std::env::consts::EXE_SUFFIX);
        d.manifest.executable = filename.clone();
        let out = dest.join(brand);
        std::fs::create_dir_all(&out)?;
        if let Some(ref image) = d.product_image {
            let relative = Path::new(image);
            anyhow::ensure!(
                relative
                    .components()
                    .all(|c| matches!(c, std::path::Component::Normal(_))),
                "invalid artwork path"
            );
            let source_image = source.join(relative);
            if source_image.is_file() {
                let destination = out.join(relative);
                std::fs::create_dir_all(destination.parent().context("artwork directory")?)?;
                std::fs::copy(source_image, destination)?;
            }
        }
        std::fs::write(out.join("plugin.json"), serde_json::to_vec_pretty(&d)?)?;
        std::fs::copy(
            source.join("permissions.json"),
            out.join("permissions.json"),
        )?;
        std::fs::copy(target.join(filename), out.join(&d.manifest.executable))?;
        Package::load(&out)?;
    }
    std::fs::copy(
        std::env::current_exe()?,
        root.join(format!("dist/showdevice{}", std::env::consts::EXE_SUFFIX)),
    )?;
    println!("Packaged into {}", dest.display());
    Ok(())
}
fn schemas(out: &Path) -> Result<()> {
    std::fs::create_dir_all(out)?;
    for (name, schema) in [
        ("app-snapshot", schemars::schema_for!(AppSnapshot)),
        ("app-command", schemars::schema_for!(AppCommand)),
        ("poll-preferences", schemars::schema_for!(PollPreferences)),
        ("surface-template", schemars::schema_for!(SurfaceTemplate)),
        (
            "device-settings-extension",
            schemars::schema_for!(DeviceSettingsExtension),
        ),
        (
            "device-settings-change",
            schemars::schema_for!(DeviceSettingsChange),
        ),
        ("descriptor", schemars::schema_for!(Descriptor)),
        ("host-message", schemars::schema_for!(HostMessage)),
        ("plugin-message", schemars::schema_for!(PluginMessage)),
        ("permissions", schemars::schema_for!(Permissions)),
        ("config-change", schemars::schema_for!(ConfigChange)),
        ("config-result", schemars::schema_for!(ConfigResult)),
        ("config-request", schemars::schema_for!(ConfigRequest)),
    ] {
        std::fs::write(
            out.join(format!("{name}.json")),
            serde_json::to_vec_pretty(&schema)?,
        )?;
    }
    Ok(())
}
fn demo_device() -> Device {
    Device {
        feature_report_ids: vec![],
        id: "demo:mock".into(),
        name: "Example device (simulated)".into(),
        vendor_id: 65535,
        product_id: 1,
        interface: 0,
        usage_page: 0,
        usage: 0,
        connection: Connection::Receiver,
        firmware: None,
    }
}
struct Active {
    session: Session,
    broker: Broker,
    view: DeviceView,
    last: Instant,
}
fn monitor(root: &Path, watch: bool, demo: bool) -> Result<()> {
    let (packages, mut diagnostics) = sd_runtime::discover(root);
    if demo {
        diagnostics.push("SIMULATION — no hardware is queried".into());
    }
    let mut active: BTreeMap<String, Active> = BTreeMap::new();
    let mut views: BTreeMap<String, DeviceView> = BTreeMap::new();
    let mut retry: BTreeMap<String, Instant> = BTreeMap::new();
    let (tx, rx) = std::sync::mpsc::sync_channel(1);
    if watch {
        std::thread::spawn(move || {
            use std::io::BufRead;
            for line in std::io::stdin().lock().lines() {
                match line {
                    Ok(l) if l.trim() == "refresh" => {
                        let _ = tx.try_send(());
                    }
                    Ok(_) => {}
                    Err(_) => break,
                }
            }
        });
    }
    let mut first = true;
    let mut refreshed = Instant::now() - Duration::from_secs(60);
    loop {
        let manual = rx.try_recv().is_ok() && refreshed.elapsed() >= Duration::from_secs(2);
        if manual {
            refreshed = Instant::now();
        }
        let inventory = if demo {
            None
        } else {
            Some(sd_hid::Inventory::enumerate()?)
        };
        let devices = if demo {
            vec![demo_device()]
        } else {
            inventory.as_ref().unwrap().devices.clone()
        };
        let current: std::collections::BTreeSet<_> = devices.iter().map(|d| d.id.clone()).collect();
        active.retain(|id, _| current.contains(id));
        retry.retain(|id, _| current.contains(id));
        views.retain(|id, _| current.contains(id));
        let mut errors = diagnostics.clone();
        for device in devices {
            let package = match sd_runtime::select(&packages, &device) {
                Ok(Some(p)) => p,
                Ok(None) => continue,
                Err(e) => {
                    errors.push(e.to_string());
                    continue;
                }
            };
            if !active.contains_key(&device.id) {
                if retry
                    .get(&device.id)
                    .is_some_and(|t| t.elapsed() < Duration::from_secs(60))
                    && !manual
                {
                    continue;
                }
                let started = (|| -> Result<Active> {
                    let broker = if demo {
                        Broker::new(|_| Ok(vec![]))
                    } else {
                        let mut handle = inventory.as_ref().unwrap().open(&device.id)?;
                        Broker::new(move |q| handle.execute(q).map_err(|e| e.to_string()))
                    };
                    let session = Session::launch(package, &device)?;
                    let state = package
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
                        .collect();
                    let view = DeviceView {
                        surface_templates: package.descriptor.surface_templates.clone(),
                        product_image: package.product_image(),
                        device_kind: package.descriptor.device_kind,
                        device: device.clone(),
                        plugin: package.descriptor.manifest.id.clone(),
                        state,
                        pages: session.descriptor.pages.clone(),
                        capabilities: session.descriptor.capabilities.clone(),
                        error: None,
                    };
                    Ok(Active {
                        session,
                        broker,
                        view,
                        last: Instant::now() - Duration::from_secs(60),
                    })
                })();
                match started {
                    Ok(a) => {
                        active.insert(device.id.clone(), a);
                    }
                    Err(e) => {
                        retry.insert(device.id.clone(), Instant::now());
                        views
                            .entry(device.id.clone())
                            .or_insert(DeviceView {
                                surface_templates: vec![],
                                product_image: package.product_image(),
                                device_kind: package.descriptor.device_kind,
                                device: device.clone(),
                                plugin: package.descriptor.manifest.id.clone(),
                                state: State::new(),
                                pages: vec![],
                                capabilities: vec![],
                                error: None,
                            })
                            .error = Some(e.to_string());
                        continue;
                    }
                }
            }
            let a = active.get_mut(&device.id).unwrap();
            if a.last.elapsed() >= Duration::from_secs(30) || manual {
                match a.session.poll(&mut a.broker) {
                    Ok(s) => {
                        sd_device_features_mark_stale(&mut a.view.state);
                        a.view.state.extend(s);
                        a.view.error = None;
                    }
                    Err(e) => {
                        sd_device_features_mark_stale(&mut a.view.state);
                        a.view.error = Some(e.to_string());
                        retry.insert(device.id.clone(), Instant::now());
                    }
                }
                a.last = Instant::now();
                views.insert(device.id.clone(), a.view.clone());
            }
        }
        active.retain(|_, a| a.session.active);
        let snapshot = AppSnapshot {
            api: API_VERSION.into(),
            generated_at_ms: now_ms(),
            writes_enabled: false,
            devices: views.values().cloned().collect(),
            diagnostics: errors,
        };
        // Host snapshots have their own bounded output budget.
        let value = serde_json::to_string(&snapshot)?;
        if value.len() + 1 > MAX_APP_FRAME {
            bail!("app snapshot exceeds frame budget");
        }
        if first || manual || watch {
            println!("{value}");
            use std::io::Write;
            std::io::stdout().flush()?;
        }
        if !watch {
            break;
        }
        first = false;
        std::thread::sleep(Duration::from_secs(2));
    }
    Ok(())
}
fn sd_device_features_mark_stale(state: &mut State) {
    for r in state.values_mut() {
        if r.value.is_some() {
            r.availability = Availability::Stale;
        }
    }
}
fn self_test(root: PathBuf) -> Result<()> {
    let original = Package::load(&root.join("mock"))?;
    let temp = std::env::temp_dir().join(format!("showdevice-test-{}", std::process::id()));
    std::fs::create_dir_all(&temp)?;
    let result = (|| -> Result<()> {
        let exe = &original.descriptor.manifest.executable;
        std::fs::copy(original.directory.join(exe), temp.join(exe))?;
        std::fs::copy(
            original.directory.join("permissions.json"),
            temp.join("permissions.json"),
        )?;
        for mode in [
            "ok",
            "startup-crash",
            "startup-hang",
            "crash",
            "hang",
            "malformed",
            "oversize",
            "forbidden",
            "flood",
        ] {
            let mut d = original.descriptor.clone();
            d.manifest.args = vec![mode.into()];
            // Different plugin version is intentionally compatible.
            d.manifest.version = "9.8.7".into();
            std::fs::write(temp.join("plugin.json"), serde_json::to_vec(&d)?)?;
            let p = Package::load(&temp)?;
            let launch =
                Session::launch_with_timeout(&p, &demo_device(), Duration::from_millis(500));
            let result = match launch {
                Ok(mut s) => s.poll_with_timeout(
                    &mut Broker::new(|_| Ok(vec![])),
                    Duration::from_millis(500),
                ),
                Err(e) => Err(e),
            };
            if (mode == "ok") != result.is_ok() {
                bail!("isolation test failed: {mode}: {result:?}");
            }
            println!("PASS {mode}");
            // A fresh healthy process must still activate after each failure.
            let mut healthy = Session::launch(&original, &demo_device())?;
            assert!(
                healthy
                    .poll(&mut Broker::new(|_| Ok(vec![])))?
                    .contains_key("battery.percent")
            );
        }
        let mut d = original.descriptor.clone();
        d.manifest.args = vec!["hang".into()];
        std::fs::write(temp.join("plugin.json"), serde_json::to_vec(&d)?)?;
        let p = Package::load(&temp)?;
        let mut hanging = Session::launch(&p, &demo_device())?;
        let hung = std::thread::spawn(move || {
            hanging.poll_with_timeout(&mut Broker::new(|_| Ok(vec![])), Duration::from_secs(2))
        });
        let started = Instant::now();
        let mut healthy = Session::launch(&original, &demo_device())?;
        healthy.poll(&mut Broker::new(|_| Ok(vec![])))?;
        if started.elapsed() > Duration::from_secs(1) {
            bail!("healthy plugin blocked behind hung plugin");
        }
        assert!(hung.join().unwrap().is_err());
        println!("PASS concurrent isolation");
        d.manifest.args = vec!["recover-once".into()];
        std::fs::write(temp.join("plugin.json"), serde_json::to_vec(&d)?)?;
        let p = Package::load(&temp)?;
        let mut recovering = Session::launch(&p, &demo_device())?;
        let mut broker = Broker::new(|_| Ok(vec![]));
        assert!(recovering.poll(&mut broker).is_err());
        assert!(recovering.active);
        assert!(
            recovering
                .poll(&mut broker)?
                .contains_key("battery.percent")
        );
        println!("PASS transient device recovery");
        Ok(())
    })();
    // Exact process-specific temporary directory created above.
    let _ = std::fs::remove_dir_all(&temp);
    result
}
fn new_plugin(path: &Path) -> Result<()> {
    if path.exists() {
        bail!("destination already exists");
    }
    std::fs::create_dir_all(path.join("src"))?;
    std::fs::write(
        path.join("README.md"),
        "Add this package to the workspace. Start with no device matches. Add verified read-only query recipes, capabilities, fixtures and UI nodes; validate with showdevice validate. See docs/plugin-development.md.\n",
    )?;
    std::fs::write(
        path.join("Cargo.toml"),
        "[package]\nname=\"sd-plugin-example\"\nversion=\"0.1.0\"\nedition=\"2024\"\n[dependencies]\nsd-api.workspace=true\nsd-sdk.workspace=true\nanyhow.workspace=true\n",
    )?;
    std::fs::write(
        path.join("src/main.rs"),
        "use sd_api::*;\nstruct Example;\nimpl sd_sdk::Plugin for Example {\nfn descriptor(&self)->Descriptor {sd_sdk::installed_descriptor().expect(\"descriptor\")}\nfn poll(&mut self,_:&Device,_:&mut sd_sdk::Client)->anyhow::Result<State>{Ok(State::new())}\n}\nfn main()->anyhow::Result<()>{sd_sdk::serve(Example)}\n",
    )?;
    let d = Descriptor {
        surface_templates: vec![],
        product_image: None,
        device_kind: DeviceKind::Other,
        manifest: Manifest {
            id: "org.example.device".into(),
            version: "0.1.0".into(),
            api: ">=1.0.0, <2.0.0".into(),
            wire: 1,
            executable: format!("sd-plugin-example{}", std::env::consts::EXE_SUFFIX),
            args: vec![],
            required: vec![],
            matches: vec![],
        },
        capabilities: vec![],
        pages: vec![],
    };
    std::fs::write(path.join("plugin.json"), serde_json::to_vec_pretty(&d)?)?;
    std::fs::write(path.join("permissions.json"), "{\"queries\":{}}\n")?;
    println!("Created {}", path.display());
    Ok(())
}
