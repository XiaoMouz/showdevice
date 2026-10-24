use anyhow::{Result, bail};
pub fn parse(bytes: &[u8], class: u8, command: u8) -> Result<&[u8]> {
    let b = if bytes.len() == 91 && bytes[0] == 0 {
        &bytes[1..]
    } else {
        bytes
    };
    if b.len() != 90 || b[0] != 2 || b[1] != 0x1f || b[6] != class || b[7] != command {
        bail!("invalid Razer response header/status");
    }
    if b[2..88].iter().fold(0, |a, x| a ^ x) != b[88] {
        bail!("Razer response checksum mismatch");
    }
    let n = b[5] as usize;
    if n > 80 {
        bail!("invalid Razer payload length");
    }
    Ok(&b[8..8 + n])
}
#[cfg(test)]
mod tests {
    #[test]
    fn rejects_short_and_wrong_checksum() {
        assert!(super::parse(&[0; 3], 7, 0x80).is_err());
        let mut b = [0; 90];
        b[0] = 2;
        b[1] = 0x1f;
        b[6] = 7;
        b[7] = 0x80;
        assert!(super::parse(&b, 7, 0x80).is_err());
    }
}
