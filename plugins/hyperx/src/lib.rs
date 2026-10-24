use anyhow::{Result, bail};
pub fn parse(bytes: &[u8], command: u8) -> Result<Option<u8>> {
    if bytes.len() < 7 || bytes[0] != 0x0c || bytes[5] != command {
        bail!("invalid Cloud III S response");
    }
    if bytes[6] == 0xff {
        return Ok(None);
    }
    if command == 6 && bytes[6] > 100 {
        bail!("invalid battery percentage");
    }
    Ok(Some(bytes[6]))
}
#[cfg(test)]
mod tests {
    #[test]
    fn sentinel_is_unknown() {
        assert_eq!(super::parse(&[12, 2, 3, 1, 0, 6, 255], 6).unwrap(), None);
    }
    #[test]
    fn rejects_invalid() {
        assert!(super::parse(&[12, 2, 3], 6).is_err());
        assert!(super::parse(&[12, 2, 3, 1, 0, 6, 101], 6).is_err());
    }
}
