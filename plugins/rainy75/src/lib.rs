use anyhow::{Result, ensure};
// Unnumbered VIA input reports do not include a leading zero in HIDAPI.
pub fn version(bytes: &[u8]) -> Result<u16> {
    ensure!(
        bytes.len() == 32 && bytes[0] == 1,
        "invalid Rainy75 VIA response"
    );
    let version = u16::from_be_bytes([bytes[1], bytes[2]]);
    ensure!(
        (9..=12).contains(&version),
        "unsupported Rainy75 VIA version"
    );
    Ok(version)
}
pub fn lighting(bytes: &[u8], id: u8) -> Result<u8> {
    ensure!(
        bytes.len() == 32 && bytes[..3] == [8, 3, id],
        "invalid Rainy75 lighting response"
    );
    let max = match id {
        1 => 9,
        2 => 18,
        _ => anyhow::bail!("unsupported lighting field"),
    };
    ensure!(bytes[3] <= max, "invalid Rainy75 lighting value");
    Ok(bytes[3])
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn validates_reply_identity_and_range() {
        let mut reply = [0; 32];
        reply[..3].copy_from_slice(&[1, 0, 11]);
        assert_eq!(version(&reply).unwrap(), 11);
        assert!(version(&reply[..3]).is_err());
        reply[..4].copy_from_slice(&[8, 3, 1, 9]);
        assert_eq!(lighting(&reply, 1).unwrap(), 9);
        assert!(lighting(&reply, 2).is_err());
        reply[3] = 10;
        assert!(lighting(&reply, 1).is_err());
        reply[..4].copy_from_slice(&[1, 3, 9, 1]);
        assert!(lighting(&reply, 1).is_err());
        assert!(version(&reply).is_err());
    }
}
