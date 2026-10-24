//! Independently implemented parser for the documented Nuga/BYK916 read reply.
//! USB battery reporting is not established for Halo96 V1.
use anyhow::{Result, ensure};
use sd_api::*;
pub fn parse(report: &[u8]) -> Result<State> {
    ensure!(
        (262..=1032).contains(&report.len()),
        "invalid Halo96 response length"
    );
    ensure!(
        report[..3] == [6, 0x83, 0xb6] && report[14..16] == [0x5a, 0xa5],
        "invalid Halo96 response header"
    );
    let params = &report[16..139];
    ensure!(
        params[..2] == [3, 3] && params[6] == 0x20 && params[121..123] == [0x5a, 0xa5],
        "invalid Halo96 parameters"
    );
    ensure!(
        params == &report[139..262],
        "Halo96 parameter copies disagree"
    );
    let mode = match params[5] {
        0 => "Off",
        1 => "Static",
        2 => "Respire",
        3 => "Rainbow",
        4 => "Flash away",
        6 => "Rainbow wheel",
        7 => "Ripples shining",
        8 => "Stars twinkle",
        11 => "Neon stream",
        12 => "Reaction",
        13 => "Sine wave",
        15 => "Rotating windmill",
        16 => "Colorful waterfall",
        17 => "Blossoming",
        18 => "Custom",
        _ => "Unknown effect",
    };
    let mut state = State::new();
    state.insert(
        "lighting.effect".into(),
        Reading::available(mode, "nuphy.byk916"),
    );
    state.insert(
        "keyboard.debounce_ms".into(),
        Reading::available(params[2], "nuphy.byk916"),
    );
    let mut battery = Reading::unknown("nuphy.byk916");
    battery.availability = Availability::Unsupported;
    state.insert("battery.percent".into(), battery);
    // Halo96 V1 A/B captures: header byte 8 mirrors parameter byte 95.
    // Unknown values/disagreement must not be interpreted as Windows.
    let os_mode = match (report[8], params[95]) {
        (0, 0) => Reading::available("Windows", "nuphy.byk916"),
        (1, 1) => Reading::available("macOS", "nuphy.byk916"),
        _ => Reading::unknown("nuphy.byk916"),
    };
    state.insert("keyboard.os_mode".into(), os_mode);
    // User-confirmed USB / receiver / BT1 / BT2 / BT3 captures.
    // These are selected modes, not link establishment or host transport.
    let connection = match params[8] {
        0 => Reading::available("USB", "nuphy.byk916"),
        1 => Reading::available("2.4 GHz", "nuphy.byk916"),
        2 => Reading::available("Bluetooth", "nuphy.byk916"),
        _ => Reading::unknown("nuphy.byk916"),
    };
    let mut channel = Reading::unknown("nuphy.byk916");
    match params[8] {
        2 if (1..=3).contains(&params[7]) => {
            channel = Reading::available(params[7], "nuphy.byk916");
        }
        0 | 1 => channel.availability = Availability::Unsupported,
        _ => {}
    }
    state.insert("keyboard.connection_mode".into(), connection);
    state.insert("keyboard.bluetooth_channel".into(), channel);
    Ok(state)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture() -> Vec<u8> {
        serde_json::from_str::<serde_json::Value>(include_str!(
            "../../../tests/fixtures/nuphy-halo96-lighting.json"
        ))
        .unwrap()["report"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| x.as_u64().unwrap() as u8)
            .collect()
    }
    #[test]
    fn captured_halo96_reply() {
        let state = parse(&fixture()).unwrap();
        assert_eq!(state["lighting.effect"].value, Some("Stars twinkle".into()));
        assert_eq!(state["keyboard.debounce_ms"].value, Some(4.into()));
        assert_eq!(
            state["battery.percent"].availability,
            Availability::Unsupported
        );
        assert_eq!(state["battery.percent"].value, None);
    }
    #[test]
    fn reject_truncation_wrong_command_and_mismatching_copies() {
        let mut r = fixture();
        for end in 0..262 {
            assert!(parse(&r[..end]).is_err());
        }
        r[1] = 3;
        assert!(parse(&r).is_err());
        r = fixture();
        r[144] ^= 1;
        assert!(parse(&r).is_err());
    }
    #[test]
    fn os_mode_requires_agreement_and_known_values() {
        let mut r = fixture();
        r[8] = 0;
        r[111] = 0;
        r[234] = 0;
        assert_eq!(
            parse(&r).unwrap()["keyboard.os_mode"].value,
            Some("Windows".into())
        );
        r[8] = 1;
        r[111] = 1;
        r[234] = 1;
        assert_eq!(
            parse(&r).unwrap()["keyboard.os_mode"].value,
            Some("macOS".into())
        );
        r[8] = 0;
        assert_eq!(
            parse(&r).unwrap()["keyboard.os_mode"].availability,
            Availability::Unknown
        );
        r[8] = 2;
        r[111] = 2;
        r[234] = 2;
        assert_eq!(parse(&r).unwrap()["keyboard.os_mode"].value, None);
    }
    #[test]
    fn replay_live_os_switch_roundtrip() {
        for data in [
            include_str!("../../../tests/fixtures/nuphy-halo96-windows.json"),
            include_str!("../../../tests/fixtures/nuphy-halo96-mac.json"),
            include_str!("../../../tests/fixtures/nuphy-halo96-windows-return.json"),
        ] {
            let fixture: serde_json::Value = serde_json::from_str(data).unwrap();
            let report: Vec<u8> = serde_json::from_value(fixture["report"].clone()).unwrap();
            assert_eq!(
                parse(&report).unwrap()["keyboard.os_mode"].value.as_ref(),
                Some(&fixture["mode"])
            );
        }
    }
    #[test]
    fn replay_live_connection_modes_and_channels() {
        for (data, mode, channel) in [
            (
                include_str!("../../../tests/fixtures/nuphy-halo96-bluetooth-1.json"),
                "Bluetooth",
                Some(1),
            ),
            (
                include_str!("../../../tests/fixtures/nuphy-halo96-bluetooth-2.json"),
                "Bluetooth",
                Some(2),
            ),
            (
                include_str!("../../../tests/fixtures/nuphy-halo96-bluetooth-3.json"),
                "Bluetooth",
                Some(3),
            ),
            (
                include_str!("../../../tests/fixtures/nuphy-halo96-receiver.json"),
                "2.4 GHz",
                None,
            ),
            (
                include_str!("../../../tests/fixtures/nuphy-halo96-usb.json"),
                "USB",
                None,
            ),
        ] {
            let fixture: serde_json::Value = serde_json::from_str(data).unwrap();
            let report: Vec<u8> = serde_json::from_value(fixture["report"].clone()).unwrap();
            let state = parse(&report).unwrap();
            assert_eq!(state["keyboard.connection_mode"].value, Some(mode.into()));
            assert_eq!(
                state["keyboard.bluetooth_channel"].value,
                channel.map(Into::into)
            );
            if channel.is_none() {
                assert_eq!(
                    state["keyboard.bluetooth_channel"].availability,
                    Availability::Unsupported
                );
            }
        }
    }
    #[test]
    fn mode_and_channel_are_not_guessed() {
        let mut r = fixture();
        r[24] = 99;
        r[147] = 99;
        let state = parse(&r).unwrap();
        assert_eq!(
            state["keyboard.connection_mode"].availability,
            Availability::Unknown
        );
        assert_eq!(state["keyboard.bluetooth_channel"].value, None);
        for invalid in [0, 4, 255] {
            r[24] = 2;
            r[147] = 2;
            r[23] = invalid;
            r[146] = invalid;
            assert_eq!(
                parse(&r).unwrap()["keyboard.bluetooth_channel"].availability,
                Availability::Unknown
            );
        }
        // Remembered wireless channel in wired mode must never look active.
        r[24] = 0;
        r[147] = 0;
        r[23] = 3;
        r[146] = 3;
        assert_eq!(parse(&r).unwrap()["keyboard.bluetooth_channel"].value, None);
        r[147] = 1;
        assert!(parse(&r).is_err());
    }
}
