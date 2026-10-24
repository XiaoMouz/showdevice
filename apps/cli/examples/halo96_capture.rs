//! Capture only the installed Halo96 lighting read query for manual A/B tests.
//! Labels are operator annotations, never evidence of automatic mode detection.
use anyhow::{Context, Result, ensure};
use sd_runtime::{Broker, Package};
use std::{path::Path, time::Duration};
fn main() -> Result<()> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    let label = args
        .first()
        .context("halo96_capture <label> <new-output.json> [plugin-directory]")?;
    ensure!(label.len() <= 128, "label too long");
    let output = args.get(1).context("new output path required")?;
    let package = Package::load(Path::new(
        args.get(2)
            .map(String::as_str)
            .unwrap_or("dist/plugins/nuphy"),
    ))?;
    ensure!(
        package.descriptor.manifest.id == "org.showdevice.nuphy",
        "not the NuPhy package"
    );
    let inventory = sd_hid::Inventory::enumerate()?;
    let matches: Vec<_> = inventory
        .devices
        .iter()
        .filter(|d| {
            package
                .descriptor
                .manifest
                .matches
                .iter()
                .any(|m| m.score(d).is_some())
        })
        .collect();
    ensure!(
        matches.len() == 1,
        "expected exactly one matching Halo96, found {}",
        matches.len()
    );
    let device = matches[0];
    let query = package
        .permissions
        .queries
        .get("lighting")
        .context("missing lighting query")?
        .clone();
    let mut handle = inventory.open(&device.id)?;
    let mut broker = Broker::new(move |q| handle.execute(q).map_err(|e| format!("{e:#}")));
    let mut reports = Vec::new();
    for _ in 0..3 {
        reports.push(broker.query(query.clone(), Duration::from_secs(4))?);
        std::thread::sleep(Duration::from_millis(200));
    }
    let stable = reports.windows(2).all(|p| p[0] == p[1]);
    let capture = serde_json::json!({"label":label,"label_source":"operator annotation; not auto-detected", "captured_at_ms":sd_api::now_ms(),"device":device,"query":"lighting","reports":reports,"stable":stable});
    let file = std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(output)?;
    serde_json::to_writer_pretty(file, &capture)?;
    println!("Captured three read responses; stable={stable}; {output}");
    Ok(())
}
