use anyhow::{Result, bail, ensure};
use sd_api::*;

// Protocol facts: Sony's Linux hid-playstation driver. No output reports are sent.
fn crc32(bytes: impl Iterator<Item = u8>) -> u32 {
    let mut crc = !0u32;
    for byte in bytes {
        crc ^= byte as u32;
        for _ in 0..8 {
            crc = (crc >> 1) ^ (0xedb88320 & 0u32.wrapping_sub(crc & 1));
        }
    }
    !crc
}
pub fn parse(data: &[u8], bluetooth: bool) -> Result<State> {
    let mut state = State::new();
    state.insert(
        "connection.mode".into(),
        Reading::available(if bluetooth { "Bluetooth" } else { "USB" }, "dualsense"),
    );
    let status = if bluetooth {
        if data.first() == Some(&1) && data.len() == 10 {
            for key in ["battery.percent", "battery.charging", "battery.status"] {
                state.insert(key.into(), Reading::unknown("dualsense.basic_report"));
            }
            return Ok(state);
        }
        ensure!(
            data.len() == 78 && data[0] == 0x31,
            "invalid DualSense Bluetooth report"
        );
        let expected = u32::from_le_bytes(data[74..78].try_into()?);
        ensure!(
            crc32(std::iter::once(0xa1).chain(data[..74].iter().copied())) == expected,
            "DualSense response checksum mismatch"
        );
        data[54]
    } else {
        ensure!(
            data.len() == 64 && data[0] == 1,
            "invalid DualSense USB report"
        );
        data[53]
    };
    let level = status & 15;
    let mode = status >> 4;
    let (percent, charging, label) = match mode {
        0 | 1 => {
            ensure!(level <= 10, "invalid DualSense battery level");
            (
                Some((level * 10 + 5).min(100)),
                Some(mode == 1),
                if mode == 1 { "Charging" } else { "Discharging" },
            )
        }
        2 => (Some(100), Some(false), "Fully charged"),
        10 => (None, Some(false), "Voltage or temperature error"),
        11 => (None, Some(false), "Temperature error"),
        15 => (None, None, "Charging error"),
        _ => bail!("invalid DualSense charging status"),
    };
    let mut battery = percent
        .map(|p| Reading::available(p, "dualsense"))
        .unwrap_or_else(|| Reading::unknown("dualsense"));
    battery.precision = Some("Estimated from 10% battery bands".into());
    state.insert("battery.percent".into(), battery);
    state.insert(
        "battery.charging".into(),
        charging
            .map(|p| Reading::available(p, "dualsense"))
            .unwrap_or_else(|| Reading::unknown("dualsense")),
    );
    state.insert(
        "battery.status".into(),
        Reading::available(label, "dualsense"),
    );
    Ok(state)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn usb(status: u8) -> Vec<u8> {
        let mut b = vec![0; 64];
        b[0] = 1;
        b[53] = status;
        b
    }
    #[test]
    fn battery_states() {
        for (raw, value, charging) in [
            (0x04, 45, false),
            (0x17, 75, true),
            (0x0a, 100, false),
            (0x20, 100, false),
        ] {
            let s = parse(&usb(raw), false).unwrap();
            assert_eq!(s["battery.percent"].value, Some(serde_json::json!(value)));
            assert_eq!(
                s["battery.charging"].value,
                Some(serde_json::json!(charging))
            );
        }
    }
    #[test]
    fn errors_are_not_empty_batteries() {
        for raw in [0xa0, 0xb0, 0xf0] {
            assert!(
                parse(&usb(raw), false).unwrap()["battery.percent"]
                    .value
                    .is_none()
            );
        }
    }
    #[test]
    fn malformed_reports() {
        assert!(parse(&[], false).is_err());
        assert!(parse(&usb(0x0f), false).is_err());
        assert!(parse(&usb(0x30), false).is_err());
        let mut b = usb(4);
        b[0] = 2;
        assert!(parse(&b, false).is_err());
    }
    #[test]
    fn bluetooth_crc_and_offsets() {
        let mut b = vec![0; 78];
        b[0] = 0x31;
        b[54] = 0x16;
        let crc = crc32(std::iter::once(0xa1).chain(b[..74].iter().copied()));
        b[74..].copy_from_slice(&crc.to_le_bytes());
        assert_eq!(
            parse(&b, true).unwrap()["battery.percent"].value,
            Some(serde_json::json!(65))
        );
        b[20] ^= 1;
        assert!(parse(&b, true).is_err());
    }
    #[test]
    fn basic_bluetooth_is_unknown() {
        let mut b = vec![0; 10];
        b[0] = 1;
        assert!(parse(&b, true).unwrap()["battery.percent"].value.is_none());
    }
}
