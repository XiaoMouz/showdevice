use anyhow::Result;
use sd_api::*;
struct HyperX {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for HyperX {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        let mut s = State::new();
        let battery = io.query("battery")?;
        let n = sd_plugin_hyperx::parse(&battery, 6)?;
        s.insert(
            "battery.percent".into(),
            n.map(|v| Reading::available(v, "hyperx"))
                .unwrap_or_else(|| Reading::unknown("hyperx")),
        );
        // Charging codes vary with firmware; expose only validated boolean codes.
        if let Ok(bytes) = io.query("charging")
            && let Some(v) = sd_plugin_hyperx::parse(&bytes, 0x48)?
            && v <= 1
        {
            s.insert(
                "battery.charging".into(),
                Reading::available(v == 1, "hyperx"),
            );
        }
        Ok(s)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(HyperX {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
