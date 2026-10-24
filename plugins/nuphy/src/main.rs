//! Deliberately no device match until model/firmware-specific transport is verified.
use anyhow::Result;
use sd_api::*;
struct NuPhy {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for NuPhy {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, _: &mut sd_sdk::Client) -> Result<State> {
        let mut s = State::new();
        let mut r = Reading::unknown("nuphy");
        r.availability = Availability::Unsupported;
        s.insert("battery.percent".into(), r);
        Ok(s)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(NuPhy {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
