//! Halo96 V1 read-only BYK916 plugin; no keyboard handles in this process.
use anyhow::Result;
use sd_api::*;
struct NuPhy {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for NuPhy {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, device: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        anyhow::ensure!(device.name == "NuPhy Halo96", "unsupported NuPhy model");
        sd_plugin_nuphy::parse(&io.query("lighting")?)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(NuPhy {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
