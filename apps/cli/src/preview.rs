//! Explicit demo mode only; never enumerate or open physical hardware here.
use sd_api::*;
use sd_runtime::Broker;

pub fn devices() -> Vec<Device> {
    if std::env::var("SHOWDEVICE_DEMO_PROFILE").as_deref() != Ok("new-devices") {
        return vec![crate::demo_device()];
    }
    [
        ("Rapoo VT3 Pro V2（模拟）", 0x24ae, 0x14a5, 0xff00, 2),
        (
            "WOBKEY Rainy75 标准版（模拟）",
            0x320f,
            0x5055,
            0xff60,
            0x61,
        ),
    ]
    .into_iter()
    .map(|(name, vendor_id, product_id, usage_page, usage)| Device {
        id: format!("simulation:{vendor_id:04x}:{product_id:04x}"),
        name: name.into(),
        vendor_id,
        product_id,
        usage_page,
        usage,
        interface: 1,
        feature_report_ids: vec![],
        firmware: None,
        connection: if vendor_id == 0x24ae {
            Connection::Receiver
        } else {
            Connection::Usb
        },
    })
    .collect()
}

pub fn broker(vendor: u16) -> Broker {
    Broker::new(move |query| match (vendor, query) {
        (0x24ae, Query::HidInput { .. }) => Ok(vec![7, 0x21, 0, 0, 0, 0, 0, 1, 73]),
        (0x320f, Query::HidOutput { request, .. }) => {
            let mut reply = vec![0; 32];
            match request.as_slice() {
                [0, 1, ..] => reply[..3].copy_from_slice(&[1, 0, 11]),
                [0, 8, 3, 1, ..] => reply[..4].copy_from_slice(&[8, 3, 1, 6]),
                [0, 8, 3, 2, ..] => reply[..4].copy_from_slice(&[8, 3, 2, 17]),
                _ => return Err("Unsupported simulated query".into()),
            }
            Ok(reply)
        }
        (65535, _) => Ok(vec![]),
        _ => Err("Unsupported simulated device".into()),
    })
}
