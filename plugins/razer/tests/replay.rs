#[test]
fn replay_valid_and_corrupted_reports() {
    for source in [
        include_str!("../../../tests/fixtures/razer-battery.json"),
        include_str!("../../../tests/fixtures/razer-dpi.json"),
    ] {
        let f: serde_json::Value = serde_json::from_str(source).unwrap();
        let mut b: Vec<u8> = serde_json::from_value(f["response"].clone()).unwrap();
        let class = f["class"].as_u64().unwrap() as u8;
        let command = f["command"].as_u64().unwrap() as u8;
        assert!(sd_plugin_razer::parse(&b, class, command).is_ok());
        b[10] ^= 1;
        assert!(sd_plugin_razer::parse(&b, class, command).is_err());
    }
}
