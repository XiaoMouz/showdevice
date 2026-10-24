use anyhow::{Result, bail};
use sd_api::*;
struct Rapoo {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for Rapoo {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        let mut s = State::new();
        match io.query("status") {
            Ok(b) => {
                let payload = if b.first() == Some(&7) {
                    &b[1..]
                } else {
                    &b[..]
                };
                if payload.len() < 8 || payload[0] != 0x20 || payload[7] > 100 {
                    bail!("invalid Rapoo status");
                }
                s.insert(
                    "battery.percent".into(),
                    Reading::available(payload[7], "rapoo"),
                );
            }
            Err(_) => {
                s.insert("battery.percent".into(), Reading::unknown("rapoo"));
            }
        }
        Ok(s)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(Rapoo {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
