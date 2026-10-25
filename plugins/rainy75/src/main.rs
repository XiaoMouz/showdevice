use anyhow::Result;
use sd_api::*;
struct Rainy75 {
    descriptor: Descriptor,
}
impl sd_sdk::Plugin for Rainy75 {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        // Require a response, rather than equating receiver presence with an awake keyboard.
        let version = sd_plugin_rainy75::version(&io.query("version")?)?;
        let mut state = State::from([(
            "keyboard.via_version".into(),
            Reading::available(version, "rainy75.via"),
        )]);
        for (query, id, key) in [
            ("brightness", 1, "lighting.brightness"),
            ("effect", 2, "lighting.effect_id"),
        ] {
            let reading = io
                .query(query)
                .ok()
                .and_then(|bytes| sd_plugin_rainy75::lighting(&bytes, id).ok())
                .map(|value| Reading::available(value, "rainy75.via"))
                .unwrap_or_else(|| Reading::unknown("rainy75.via"));
            state.insert(key.into(), reading);
        }
        Ok(state)
    }
}
fn main() -> Result<()> {
    sd_sdk::serve(Rainy75 {
        descriptor: sd_sdk::installed_descriptor()?,
    })
}
