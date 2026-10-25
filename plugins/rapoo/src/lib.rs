use anyhow::{Result, ensure};
use sd_api::*;
// Independent implementation of Nordic status facts; see protocol.json.
pub fn parse(bytes: &[u8]) -> Result<State> {
    ensure!(
        bytes.len() >= 9 && bytes[0] == 7,
        "invalid Rapoo status report"
    );
    let battery = match bytes[8] {
        percent @ 0..=100 => Reading::available(percent, "rapoo.nrf54l"),
        0x7f | 0xff => Reading::unknown("rapoo.nrf54l"),
        _ => anyhow::bail!("invalid Rapoo battery level"),
    };
    Ok(State::from([("battery.percent".into(), battery)]))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn status_layout_and_unknown_values() {
        // Synthetic vectors. The sensor/protocol bitfield is not a command.
        for flags in [0x20, 0x21, 0x30] {
            let mut packet = [7, flags, 0, 0x20, 3, 0x20, 3, 1, 73];
            assert_eq!(
                parse(&packet).unwrap()["battery.percent"].value,
                Some(73.into())
            );
            for sentinel in [127, 255] {
                packet[8] = sentinel;
                assert!(parse(&packet).unwrap()["battery.percent"].value.is_none());
            }
            packet[8] = 101;
            assert!(parse(&packet).is_err());
        }
    }
    #[test]
    fn rejects_truncated_and_old_generation_reports() {
        for length in 0..9 {
            assert!(parse(&[7; 9][..length]).is_err());
        }
        assert!(parse(&[0xbb; 64]).is_err());
        assert!(parse(&[0; 64]).is_err());
    }
}
