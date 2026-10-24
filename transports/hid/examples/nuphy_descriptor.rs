fn main() -> anyhow::Result<()> {
    let api = hidapi::HidApi::new()?;
    for d in api
        .device_list()
        .filter(|d| d.product_string() == Some("NuPhy Halo96") && d.usage_page() == 0xff00)
    {
        let h = api.open_path(d.path())?;
        let mut buf = [0; 4096];
        let n = h.get_report_descriptor(&mut buf)?;
        println!("{} {:02x?}", d.path().to_string_lossy(), &buf[..n]);
    }
    Ok(())
}
