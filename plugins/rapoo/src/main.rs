use anyhow::Result;
use sd_api::*;
struct Rapoo {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for Rapoo {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        // Passive reports may be seconds apart; never send legacy VT3 commands.
        for _ in 0..4 {
            if let Ok(bytes) = io.query("status")
                && bytes.first() == Some(&7)
            {
                return sd_plugin_rapoo::parse(&bytes);
            }
        }
        Ok(State::from([(
            "battery.percent".into(),
            Reading::unknown("rapoo.nrf54l"),
        )]))
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(Rapoo {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
