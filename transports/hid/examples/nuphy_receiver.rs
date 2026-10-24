//! Read-only inspection of the locally observed Halo96 receiver.
//! No SET_REPORT/output writes, pairing, keyboard input capture or firmware commands.
fn main() -> anyhow::Result<()> {
    let api = hidapi::HidApi::new()?;
    for d in api.device_list().filter(|d| {
        d.vendor_id() == 0x05ac
            && d.product_id() == 0x0256
            && d.product_string() == Some("2.4G Wireless Receiver")
            && d.usage_page() >= 0xff00
    }) {
        let h = api.open_path(d.path())?;
        let mut descriptor = [0; 4096];
        let n = h.get_report_descriptor(&mut descriptor)?;
        println!(
            "usage={:04x}:{:04x} descriptor={:02x?}",
            d.usage_page(),
            d.usage(),
            &descriptor[..n]
        );
        if d.usage_page() == 0xff02 && d.usage() == 2 {
            let expected = [
                0x06, 0x02, 0xff, 0x09, 0x02, 0xa1, 0x01, 0x85, 0x06, 0x09, 0x02, 0x15, 0x00, 0x26,
                0xff, 0x00, 0x75, 0x08, 0x95, 0x07, 0xb1, 0x02, 0xc0,
            ];
            anyhow::ensure!(
                descriptor[..n] == expected,
                "unexpected receiver feature descriptor"
            );
            for _ in 0..3 {
                let mut report = [0; 8];
                report[0] = 6;
                match h.get_feature_report(&mut report) {
                    Ok(n) => println!("GET_REPORT 6: {:02x?}", &report[..n]),
                    Err(e) => println!("GET_REPORT 6 error: {e}"),
                }
                std::thread::sleep(std::time::Duration::from_millis(100));
            }
        } else if d.usage_page() == 0xff01 && d.usage() == 0 {
            for _ in 0..3 {
                let mut report = [0; 8];
                match h.read_timeout(&mut report, 1000) {
                    Ok(0) => println!("vendor input: no report in 1000 ms"),
                    Ok(n) => println!("vendor input: {:02x?}", &report[..n]),
                    Err(e) => println!("vendor input error: {e}"),
                }
            }
        }
    }
    Ok(())
}
