//! Only the host links HIDAPI. Device handles never cross plugin IPC.
use anyhow::{Result, bail};
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
        Ok(Handle(self.api.open_path(path)?))
    }
}
pub struct Handle(HidDevice);
impl Handle {
    pub fn execute(&mut self, q: Query) -> Result<Vec<u8>> {
        match q {
            Query::HidFeature {
                request,
                response_len,
                delay_ms,
            } => {
                self.0.send_feature_report(&request)?;
                std::thread::sleep(Duration::from_millis(delay_ms.into()));
                let mut data = vec![0; response_len];
                data[0] = request[0];
                let n = self.0.get_feature_report(&mut data)?;
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
                    if self.0.read_timeout(&mut old, 0)? == 0 {
                        break;
                    }
                }
                let n = self.0.write(&request)?;
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
                        .0
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
                let n = self.0.read_timeout(&mut data, 1000)?;
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
