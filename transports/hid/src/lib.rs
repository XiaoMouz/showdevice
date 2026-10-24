//! Only the host links HIDAPI. Device handles never cross plugin IPC.
use anyhow::{Context, Result, bail};
use hidapi::{HidApi, HidDevice};
use sd_api::*;
use std::{
    collections::BTreeMap,
    ffi::CString,
    time::{Duration, Instant},
};
pub struct Inventory {
    pub devices: Vec<Device>,
    paths: BTreeMap<String, CString>,
    api: HidApi,
}
impl Inventory {
    pub fn enumerate() -> Result<Self> {
        let api = HidApi::new()?;
        let mut devices = Vec::new();
        let mut paths = BTreeMap::new();
        for d in api.device_list() {
            let id = d.path().to_string_lossy().into_owned();
            paths.insert(id.clone(), d.path().to_owned());
            devices.push(Device {
                // Vendor-defined collections may share usage IDs; reports identify
                // the correct endpoint without sending device commands.
                feature_report_ids: if d.usage_page() >= 0xff00 {
                    api.open_path(d.path())
                        .ok()
                        .and_then(|h| {
                            let mut bytes = [0; 4096];
                            h.get_report_descriptor(&mut bytes)
                                .ok()
                                .and_then(|n| feature_ids(&bytes[..n]).ok())
                        })
                        .unwrap_or_default()
                } else {
                    vec![]
                },
                id,
                name: d.product_string().unwrap_or("HID device").into(),
                vendor_id: d.vendor_id(),
                product_id: d.product_id(),
                interface: d.interface_number(),
                usage_page: d.usage_page(),
                usage: d.usage(),
                connection: match d.bus_type() {
                    hidapi::BusType::Bluetooth => Connection::Ble,
                    _ if (d.vendor_id() == 0x1532 && d.product_id() == 0x00a6)
                        || (d.vendor_id() == 0x03f0
                            && [0x02cc, 0x06be].contains(&d.product_id())) =>
                    {
                        Connection::Receiver
                    }
                    _ => Connection::Usb,
                },
                firmware: None,
            });
        }
        Ok(Self {
            devices,
            paths,
            api,
        })
    }
    pub fn open(&self, id: &str) -> Result<Handle> {
        let path = self
            .paths
            .get(id)
            .ok_or_else(|| anyhow::anyhow!("unknown host device"))?;
        let primary = self.api.open_path(path)?;
        #[cfg(windows)]
        let mut peers = BTreeMap::new();
        #[cfg(not(windows))]
        let peers = BTreeMap::new();
        // Windows splits one HID interface into top-level collection handles.
        // Only sibling collections of this exact physical interface may route a
        // response report. Linux/macOS generally expose both IDs on one handle.
        #[cfg(windows)]
        if let Some(group) = collection_group(id) {
            let selected = self.devices.iter().find(|d| d.id == id).unwrap();
            for d in &self.devices {
                if d.id != id
                    && collection_group(&d.id).as_ref() == Some(&group)
                    && d.vendor_id == selected.vendor_id
                    && d.product_id == selected.product_id
                    && d.name == selected.name
                    && d.interface == selected.interface
                {
                    for report in &d.feature_report_ids {
                        if peers.contains_key(report) {
                            bail!("ambiguous sibling report");
                        }
                        peers.insert(*report, self.api.open_path(&self.paths[&d.id])?);
                    }
                }
            }
        }
        Ok(Handle { primary, peers })
    }
}
pub struct Handle {
    primary: HidDevice,
    peers: BTreeMap<u8, HidDevice>,
}
#[cfg(any(windows, test))]
fn collection_group(path: &str) -> Option<String> {
    let lower = path.to_ascii_lowercase();
    let parts: Vec<_> = lower.split('#').collect();
    if parts.len() != 4 || !parts[0].ends_with("hid") {
        return None;
    }
    let (interface, collection) = parts[1].rsplit_once("&col")?;
    if collection.len() != 2 || !collection.bytes().all(|b| b.is_ascii_hexdigit()) {
        return None;
    }
    let (parent, child) = parts[2].rsplit_once('&')?;
    if child.len() != 4 || !child.bytes().all(|b| b.is_ascii_hexdigit()) {
        return None;
    }
    Some(format!("{interface}#{parent}#{}", parts[3]))
}
/// Parse HID short/global items, including Report ID push/pop scoping.
fn feature_ids(bytes: &[u8]) -> Result<Vec<u8>> {
    let mut ids = std::collections::BTreeSet::new();
    let mut stack = Vec::new();
    let mut report = 0;
    let mut offset = 0;
    while offset < bytes.len() {
        let tag = bytes[offset];
        offset += 1;
        if tag == 0xfe {
            if offset + 2 > bytes.len() {
                bail!("truncated HID long item");
            }
            let n = bytes[offset] as usize;
            offset += 2;
            if offset + n > bytes.len() {
                bail!("truncated HID long data");
            }
            offset += n;
            continue;
        }
        let n = match tag & 3 {
            3 => 4,
            x => x as usize,
        };
        if offset + n > bytes.len() {
            bail!("truncated HID item");
        }
        match tag & 0xfc {
            0x84 => {
                if n != 1 || bytes[offset] == 0 {
                    bail!("invalid report ID");
                }
                report = bytes[offset];
            }
            0xa4 => {
                if stack.len() >= 32 {
                    bail!("HID scope limit");
                }
                stack.push(report);
            }
            0xb4 => {
                report = stack
                    .pop()
                    .ok_or_else(|| anyhow::anyhow!("invalid HID pop"))?;
            }
            0xb0 => {
                ids.insert(report);
            }
            _ => {}
        }
        offset += n;
    }
    Ok(ids.into_iter().collect())
}

impl Handle {
    pub fn execute(&mut self, q: Query) -> Result<Vec<u8>> {
        match q {
            Query::HidFeature {
                request,
                response_len,
                delay_ms,
                response_report_id,
            } => {
                self.primary
                    .send_feature_report(&request)
                    .context("send feature read request")?;
                std::thread::sleep(Duration::from_millis(delay_ms.into()));
                let mut data = vec![0; response_len];
                data[0] = response_report_id.unwrap_or(request[0]);
                let reader = self.peers.get(&data[0]).unwrap_or(&self.primary);
                let n = reader
                    .get_feature_report(&mut data)
                    .context("receive feature response")?;
                data.truncate(n);
                Ok(data)
            }
            Query::HidOutput {
                request,
                response_len,
                report_id,
                command_index,
                command,
            } => {
                // Drain old replies without blocking; bounded to avoid noisy devices.
                let mut old = vec![0; response_len];
                for _ in 0..16 {
                    if self.primary.read_timeout(&mut old, 0)? == 0 {
                        break;
                    }
                }
                let n = self.primary.write(&request)?;
                if n != request.len() {
                    bail!("short HID write");
                }
                let deadline = Instant::now() + Duration::from_millis(1000);
                loop {
                    let remaining = deadline.saturating_duration_since(Instant::now());
                    if remaining.is_zero() {
                        bail!("HID response timeout");
                    }
                    let mut data = vec![0; response_len];
                    let n = self
                        .primary
                        .read_timeout(&mut data, remaining.as_millis().min(1000) as i32)?;
                    data.truncate(n);
                    if data.first() == Some(&report_id) && data.get(command_index) == Some(&command)
                    {
                        return Ok(data);
                    }
                }
            }
            Query::HidInput { response_len } => {
                let mut data = vec![0; response_len];
                let n = self.primary.read_timeout(&mut data, 1000)?;
                if n == 0 {
                    bail!("waiting for device report");
                }
                data.truncate(n);
                Ok(data)
            }
            Query::BleRead { .. } => bail!("BLE request cannot use a HID handle"),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn siblings_must_share_physical_interface() {
        let a = r"\\?\HID#VID_05AC&PID_024F&MI_01&Col05#8&2fc80f7b&0&0004#{hid}";
        let b = a.replace("Col05", "Col06").replace("0004", "0005");
        assert_eq!(collection_group(a), collection_group(&b));
        assert_ne!(
            collection_group(a),
            collection_group(&b.replace("2fc80f7b", "another"))
        );
        assert_ne!(
            collection_group(a),
            collection_group(&b.replace("MI_01", "MI_02"))
        );
        assert_eq!(collection_group("/dev/hidraw1"), None);
    }
    #[test]
    fn feature_reports_are_scoped_and_not_input_reports() {
        assert_eq!(
            feature_ids(&[
                0x85, 5, 0xb1, 2, 0xa4, 0x85, 6, 0xb1, 2, 0xb4, 0xb1, 2, 0x85, 7, 0x81, 2
            ])
            .unwrap(),
            vec![5, 6]
        );
        assert!(feature_ids(&[0x85]).is_err());
        assert!(feature_ids(&[0xb4]).is_err());
    }
}
