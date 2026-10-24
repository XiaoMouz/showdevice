#[test]
fn replay_valid_and_wrong_command() {
    let f: serde_json::Value =
        serde_json::from_str(include_str!("../../../tests/fixtures/hyperx-battery.json")).unwrap();
    let b: Vec<u8> = serde_json::from_value(f["response"].clone()).unwrap();
    assert_eq!(sd_plugin_hyperx::parse(&b, 6).unwrap(), Some(88));
    assert!(sd_plugin_hyperx::parse(&b, 72).is_err());
}
