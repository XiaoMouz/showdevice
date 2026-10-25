use anyhow::Result;
use sd_api::*;
struct DualSense {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for DualSense {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, device: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        sd_plugin_sony::parse(
            &io.query("status")?,
            matches!(device.connection, Connection::Ble),
        )
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(DualSense {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
