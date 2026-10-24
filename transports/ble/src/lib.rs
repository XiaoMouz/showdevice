//! BLE backend extension boundary. Not advertised by the v1 runtime until a
//! platform backend has passed permission/connection/subscription tests.
use sd_api::Device;
#[cfg(feature = "native")]
pub mod native;
pub trait BleBackend: Send {
    fn enumerate(&mut self) -> Result<Vec<Device>, String>;
    fn read(
        &mut self,
        device_id: &str,
        service: &str,
        characteristic: &str,
    ) -> Result<Vec<u8>, String>;
    fn subscribe(
        &mut self,
        device_id: &str,
        service: &str,
        characteristic: &str,
    ) -> Result<(), String>;
    fn disconnect(&mut self, device_id: &str);
}
pub const BATTERY_SERVICE: &str = "0000180f-0000-1000-8000-00805f9b34fb";
pub const BATTERY_LEVEL: &str = "00002a19-0000-1000-8000-00805f9b34fb";
pub fn parse_battery(bytes: &[u8]) -> Result<u8, String> {
    match bytes {
        [n] if *n <= 100 => Ok(*n),
        _ => Err("invalid BLE battery level".into()),
    }
}
#[cfg(test)]
mod tests {
    #[test]
    fn validates_level() {
        assert_eq!(super::parse_battery(&[76]), Ok(76));
        assert!(super::parse_battery(&[255]).is_err());
    }
}
