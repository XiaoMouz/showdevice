//! Minimal process-plugin SDK; no device handles or network libraries.
use anyhow::{Result, bail};
use sd_api::*;
use std::io::{self, BufReader};
pub trait Plugin {
    fn descriptor(&self) -> Descriptor;
    fn poll(&mut self, device: &Device, io: &mut Client) -> Result<State>;
}
pub struct Client {
    input: BufReader<io::Stdin>,
    output: io::Stdout,
    next: u64,
}
impl Client {
    pub fn query(&mut self, name: &str) -> Result<Vec<u8>> {
        self.next += 1;
        let id = self.next;
        write_frame(
            &mut self.output,
            &PluginMessage::Query {
                request_id: id,
                name: name.into(),
            },
        )?;
        match read_frame::<HostMessage>(&mut self.input)? {
            HostMessage::IoResult {
                request_id,
                data,
                error,
            } if request_id == id => {
                if let Some(e) = error {
                    bail!("{e}");
                }
                Ok(data)
            }
            _ => bail!("invalid host I/O response"),
        }
    }
}
pub fn serve(mut plugin: impl Plugin) -> Result<()> {
    let mut io = Client {
        input: BufReader::new(io::stdin()),
        output: io::stdout(),
        next: 0,
    };
    if !matches!(
        read_frame::<HostMessage>(&mut io.input)?,
        HostMessage::Hello {
            wire: WIRE_VERSION,
            ..
        }
    ) {
        bail!("unsupported handshake");
    }
    write_frame(
        &mut io.output,
        &PluginMessage::Hello {
            descriptor: plugin.descriptor(),
        },
    )?;
    let HostMessage::Activate { device } = read_frame(&mut io.input)? else {
        bail!("expected activation");
    };
    write_frame(&mut io.output, &PluginMessage::Ready)?;
    loop {
        let message = match read_frame::<HostMessage>(&mut io.input) {
            Ok(m) => m,
            Err(e) if e.kind() == io::ErrorKind::UnexpectedEof => return Ok(()),
            Err(e) => return Err(e.into()),
        };
        match message {
            HostMessage::Poll { request_id } => {
                let result = match plugin.poll(&device, &mut io) {
                    Ok(state) => PluginMessage::Snapshot { request_id, state },
                    Err(e) => PluginMessage::Error {
                        request_id,
                        message: e.to_string().chars().take(1024).collect(),
                    },
                };
                write_frame(&mut io.output, &result)?;
            }
            HostMessage::Shutdown => return Ok(()),
            _ => bail!("unexpected host message"),
        }
    }
}
pub fn installed_descriptor() -> Result<Descriptor> {
    let bytes = std::fs::read("plugin.json")?;
    if bytes.len() > MAX_FRAME {
        bail!("descriptor too large");
    }
    Ok(serde_json::from_slice(&bytes)?)
}
pub fn number(id: &str, min: f64, max: f64) -> Capability {
    Capability {
        id: id.into(),
        readable: true,
        writable: false,
        subscribable: false,
        value_type: ValueType::Number {
            min,
            max,
            step: None,
        },
        depends_on: vec![],
        effect: None,
    }
}
pub fn boolean(id: &str) -> Capability {
    Capability {
        id: id.into(),
        readable: true,
        writable: false,
        subscribable: false,
        value_type: ValueType::Boolean,
        depends_on: vec![],
        effect: None,
    }
}
pub fn status(id: &str, label: &str) -> UiNode {
    UiNode {
        id: id.replace('.', "_"),
        component: "status".into(),
        version: 1,
        label: label.into(),
        binding: Some(id.into()),
        action: None,
        visible_when: None,
        enabled_when: None,
        children: vec![],
    }
}
