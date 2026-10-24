use anyhow::{Result, anyhow};
use sd_api::*;
struct Razer {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for Razer {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        let mut state = State::new();
        for (name, class, command) in [
            ("battery", 7, 0x80),
            ("charging", 7, 0x84),
            ("dpi", 4, 0x85),
        ] {
            let values = io
                .query(name)
                .and_then(|b| Ok(sd_plugin_razer::parse(&b, class, command)?.to_vec()));
            match (name, values) {
                ("battery", Ok(v)) if v.len() >= 2 => {
                    state.insert(
                        "battery.percent".into(),
                        Reading::available((f64::from(v[1]) * 100. / 255.).round(), "razer"),
                    );
                }
                ("charging", Ok(v)) if v.len() >= 2 && v[1] <= 1 => {
                    state.insert(
                        "battery.charging".into(),
                        Reading::available(v[1] == 1, "razer"),
                    );
                }
                ("dpi", Ok(v)) if v.len() >= 5 => {
                    state.insert(
                        "dpi.x".into(),
                        Reading::available(u16::from_be_bytes([v[1], v[2]]), "razer"),
                    );
                    state.insert(
                        "dpi.y".into(),
                        Reading::available(u16::from_be_bytes([v[3], v[4]]), "razer"),
                    );
                }
                _ => {}
            }
        }
        if state.is_empty() {
            return Err(anyhow!(
                "No valid Razer replies; device may be asleep or interface unavailable"
            ));
        }
        Ok(state)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(Razer {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
