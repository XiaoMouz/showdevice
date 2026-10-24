//! Canonical public contracts. No platform types cross this boundary.
#[cfg(test)]
mod contract_tests;
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};
use std::{
    collections::BTreeMap,
    io::{self, BufRead, Write},
};
pub const API_VERSION: &str = "1.0.0";
pub const WIRE_VERSION: u16 = 1;
pub const MAX_FRAME: usize = 65_536;
pub const MAX_APP_FRAME: usize = 2_097_152;
pub const HOST_CAPABILITIES: &[&str] = &[
    "hid.query.v1",
    "state.snapshot.v1",
    "ui.declarative.v1",
    "hid.feature-response-id.v1",
    "hid.match-reports.v1",
];

#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum Connection {
    Usb,
    Receiver,
    Ble,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Device {
    #[serde(default)]
    pub feature_report_ids: Vec<u8>,
    pub id: String,
    pub name: String,
    pub vendor_id: u16,
    pub product_id: u16,
    pub interface: i32,
    pub usage_page: u16,
    pub usage: u16,
    pub connection: Connection,
    #[serde(default)]
    pub firmware: Option<String>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct DeviceMatch {
    #[serde(default)]
    pub name: Option<String>,
    #[serde(default)]
    pub feature_report_ids: Vec<u8>,
    pub vendor_id: u16,
    #[serde(default)]
    pub product_id: Option<u16>,
    #[serde(default)]
    pub interface: Option<i32>,
    #[serde(default)]
    pub usage_page: Option<u16>,
    #[serde(default)]
    pub usage: Option<u16>,
    #[serde(default)]
    pub connection: Option<Connection>,
    #[serde(default)]
    pub firmware: Option<String>,
}
impl DeviceMatch {
    pub fn score(&self, d: &Device) -> Option<u16> {
        if self.vendor_id != d.vendor_id
            || self.name.as_ref().is_some_and(|v| v != &d.name)
            || !self
                .feature_report_ids
                .iter()
                .all(|id| d.feature_report_ids.contains(id))
            || self.product_id.is_some_and(|v| v != d.product_id)
            || self.interface.is_some_and(|v| v != d.interface)
            || self.usage_page.is_some_and(|v| v != d.usage_page)
            || self.usage.is_some_and(|v| v != d.usage)
            || self.connection.as_ref().is_some_and(|v| v != &d.connection)
            || self
                .firmware
                .as_ref()
                .is_some_and(|v| d.firmware.as_ref() != Some(v))
        {
            return None;
        }
        Some(
            (if self.product_id.is_some() { 100 } else { 0 })
                + u16::from(self.interface.is_some())
                + u16::from(self.usage_page.is_some())
                + u16::from(self.usage.is_some())
                + u16::from(self.connection.is_some())
                + u16::from(self.firmware.is_some())
                + u16::from(self.name.is_some())
                + u16::from(!self.feature_report_ids.is_empty()),
        )
    }
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Manifest {
    pub id: String,
    pub version: String,
    pub api: String,
    pub wire: u16,
    pub executable: String,
    #[serde(default)]
    pub args: Vec<String>,
    #[serde(default)]
    pub required: Vec<String>,
    pub matches: Vec<DeviceMatch>,
}
impl Manifest {
    pub fn validate(&self) -> Result<(), String> {
        if self.id.is_empty()
            || self.id.len() > 96
            || !self
                .id
                .bytes()
                .all(|c| c.is_ascii_alphanumeric() || b".-_".contains(&c))
        {
            return Err("invalid plugin id".into());
        }
        semver::Version::parse(&self.version).map_err(|e| e.to_string())?;
        let req = semver::VersionReq::parse(&self.api).map_err(|e| e.to_string())?;
        if !req.matches(&semver::Version::parse(API_VERSION).unwrap()) || self.wire != WIRE_VERSION
        {
            return Err("incompatible API or wire version".into());
        }
        if self
            .required
            .iter()
            .any(|c| !HOST_CAPABILITIES.contains(&c.as_str()))
        {
            return Err("missing required host capability".into());
        }
        if self.matches.len() > 128 || self.args.len() > 16 {
            return Err("manifest limit exceeded".into());
        }
        let path = std::path::Path::new(&self.executable);
        if path.as_os_str().is_empty()
            || path
                .components()
                .any(|c| !matches!(c, std::path::Component::Normal(_)))
        {
            return Err("executable must be relative inside package".into());
        }
        Ok(())
    }
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum Availability {
    Available,
    Unknown,
    Stale,
    Unsupported,
    Offline,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Reading {
    pub availability: Availability,
    pub value: Option<serde_json::Value>,
    pub updated_at_ms: Option<u64>,
    pub precision: Option<String>,
    pub source: String,
}
impl Reading {
    pub fn available(value: impl Into<serde_json::Value>, source: &str) -> Self {
        Self {
            availability: Availability::Available,
            value: Some(value.into()),
            updated_at_ms: Some(now_ms()),
            precision: None,
            source: source.into(),
        }
    }
    pub fn unknown(source: &str) -> Self {
        Self {
            availability: Availability::Unknown,
            value: None,
            updated_at_ms: None,
            precision: None,
            source: source.into(),
        }
    }
}
pub fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}
pub type State = BTreeMap<String, Reading>;

#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ValueType {
    Number {
        min: f64,
        max: f64,
        step: Option<f64>,
    },
    Boolean,
    Choice {
        values: Vec<String>,
    },
    Text,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Capability {
    pub id: String,
    pub readable: bool,
    pub writable: bool,
    pub subscribable: bool,
    pub value_type: ValueType,
    #[serde(default)]
    pub depends_on: Vec<String>,
    #[serde(default)]
    pub effect: Option<String>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[serde(tag = "op", rename_all = "snake_case")]
pub enum Condition {
    Available {
        binding: String,
    },
    Equals {
        binding: String,
        value: serde_json::Value,
    },
    HostWritesEnabled,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct UiNode {
    pub id: String,
    pub component: String,
    pub version: u16,
    pub label: String,
    #[serde(default)]
    pub binding: Option<String>,
    #[serde(default)]
    pub action: Option<String>,
    #[serde(default)]
    pub visible_when: Option<Condition>,
    #[serde(default)]
    pub enabled_when: Option<Condition>,
    #[serde(default)]
    pub children: Vec<UiNode>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct UiPage {
    pub id: String,
    pub title: String,
    #[serde(default)]
    pub optional: bool,
    pub nodes: Vec<UiNode>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Descriptor {
    #[serde(default)]
    pub device_kind: DeviceKind,
    pub manifest: Manifest,
    pub capabilities: Vec<Capability>,
    pub pages: Vec<UiPage>,
}

#[derive(Debug, Clone, Copy, Default, Serialize, Deserialize, JsonSchema)]
#[serde(rename_all = "snake_case")]
pub enum DeviceKind {
    Keyboard,
    Mouse,
    Headset,
    #[default]
    #[serde(other)]
    Other,
}

/// Trusted local packages supply read-query recipes. Plugins invoke names, not
/// arbitrary transfers. Configuration writes are not part of the host protocol.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema, PartialEq)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum Query {
    HidFeature {
        #[serde(default)]
        response_report_id: Option<u8>,
        request: Vec<u8>,
        response_len: usize,
        delay_ms: u16,
    },
    HidOutput {
        request: Vec<u8>,
        response_len: usize,
        report_id: u8,
        command_index: usize,
        command: u8,
    },
    HidInput {
        response_len: usize,
    },
    BleRead {
        service: String,
        characteristic: String,
    },
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct Permissions {
    pub queries: BTreeMap<String, Query>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum HostMessage {
    Hello {
        api: String,
        wire: u16,
        capabilities: Vec<String>,
    },
    Activate {
        device: Device,
    },
    Poll {
        request_id: u64,
    },
    IoResult {
        request_id: u64,
        data: Vec<u8>,
        error: Option<String>,
    },
    Shutdown,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum PluginMessage {
    Hello { descriptor: Descriptor },
    Ready,
    Query { request_id: u64, name: String },
    Snapshot { request_id: u64, state: State },
    Error { request_id: u64, message: String },
}
// Future configuration contract. HostMessage intentionally has no write command.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct ConfigSupport {
    pub atomic: bool,
    pub rollback: bool,
    pub safe_retry: bool,
    pub readback: bool,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct ConfigChange {
    pub request_id: String,
    pub expected_revision: Option<String>,
    pub values: BTreeMap<String, serde_json::Value>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum WriteStage {
    ValidationRejected,
    Sent,
    Acknowledged,
    Verified,
    Failed,
    Uncertain,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct ConfigResult {
    pub request_id: String,
    pub fields: BTreeMap<String, WriteStage>,
    pub message: Option<String>,
}
/// Future extension only. These are NOT accepted by the v1 HostMessage endpoint.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[serde(tag = "operation", rename_all = "snake_case")]
pub enum ConfigRequest {
    Read {
        fields: Vec<String>,
    },
    Validate {
        change: ConfigChange,
    },
    Commit {
        change: ConfigChange,
    },
    Verify {
        request_id: String,
        expected: BTreeMap<String, serde_json::Value>,
    },
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct DeviceView {
    #[serde(default)]
    pub device_kind: DeviceKind,
    pub device: Device,
    pub plugin: String,
    pub state: State,
    pub pages: Vec<UiPage>,
    pub capabilities: Vec<Capability>,
    pub error: Option<String>,
}
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
pub struct AppSnapshot {
    pub api: String,
    pub generated_at_ms: u64,
    pub writes_enabled: bool,
    pub devices: Vec<DeviceView>,
    pub diagnostics: Vec<String>,
}

/// Bounded JSON Lines over inherited local OS pipes.
pub fn read_frame<T: serde::de::DeserializeOwned>(reader: &mut impl BufRead) -> io::Result<T> {
    let mut bytes = Vec::new();
    loop {
        let chunk = reader.fill_buf()?;
        if chunk.is_empty() {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "pipe closed"));
        }
        let end = chunk.iter().position(|b| *b == b'\n');
        let n = end.map_or(chunk.len(), |i| i + 1);
        if bytes.len() + n > MAX_FRAME {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                "frame too large",
            ));
        }
        bytes.extend_from_slice(&chunk[..n]);
        reader.consume(n);
        if end.is_some() {
            return serde_json::from_slice(&bytes)
                .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e));
        }
    }
}
pub fn write_frame<T: Serialize>(writer: &mut impl Write, value: &T) -> io::Result<()> {
    write_limited(writer, value, MAX_FRAME)
}
pub fn write_app_frame(writer: &mut impl Write, value: &AppSnapshot) -> io::Result<()> {
    write_limited(writer, value, MAX_APP_FRAME)
}
fn write_limited<T: Serialize>(writer: &mut impl Write, value: &T, limit: usize) -> io::Result<()> {
    let mut bytes = serde_json::to_vec(value)?;
    if bytes.len() + 1 > limit {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "frame too large",
        ));
    }
    bytes.push(b'\n');
    writer.write_all(&bytes)?;
    writer.flush()
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bounded_frames() {
        assert!(
            read_frame::<serde_json::Value>(&mut io::Cursor::new(vec![b'x'; MAX_FRAME + 1]))
                .is_err()
        );
    }
    #[test]
    fn additive_fields() {
        let x: HostMessage = serde_json::from_str(
            r#"{"type":"hello","api":"1.0.0","wire":1,"capabilities":[],"future":true}"#,
        )
        .unwrap();
        assert!(matches!(x, HostMessage::Hello { .. }));
    }
    #[test]
    fn unknown_is_not_zero() {
        assert!(Reading::unknown("test").value.is_none());
    }
}
