use anyhow::Result;
use sd_api::*;
struct Mock {
    descriptor: Descriptor,
    mode: String,
}
impl sd_sdk::Plugin for Mock {
    fn descriptor(&self) -> Descriptor {
        self.descriptor.clone()
    }
    fn poll(&mut self, _: &Device, io: &mut sd_sdk::Client) -> Result<State> {
        use std::io::Write;
        match self.mode.as_str() {
            "recover-once" => {
                self.mode = "ok".into();
                anyhow::bail!("device asleep");
            }
            "crash" => std::process::exit(9),
            "hang" => std::thread::sleep(std::time::Duration::from_secs(60)),
            "malformed" => {
                println!("not-json");
                std::io::stdout().flush()?;
                std::process::exit(0);
            }
            "oversize" => {
                println!("{}", "x".repeat(MAX_FRAME + 1));
                std::io::stdout().flush()?;
                std::process::exit(0);
            }
            "forbidden" => {
                io.query("write.dpi")?;
            }
            "flood" => {
                for _ in 0..100 {
                    write_frame(&mut std::io::stdout(), &PluginMessage::Ready)?;
                }
            }
            _ => {}
        }
        let mut s = State::new();
        s.insert("battery.percent".into(), Reading::available(76, "mock"));
        Ok(s)
    }
}
fn main() -> Result<()> {
    let mode = std::env::args().nth(1).unwrap_or_else(|| "ok".into());
    if mode == "startup-crash" {
        std::process::exit(3);
    }
    if mode == "startup-hang" {
        std::thread::sleep(std::time::Duration::from_secs(60));
    }
    sd_sdk::serve(Mock {
        descriptor: sd_sdk::installed_descriptor()?,
        mode,
    })
}
