use crate::*;
#[test]
fn halo96_match_requires_model_and_request_collection() {
    let descriptor: Descriptor =
        serde_json::from_str(include_str!("../../../plugins/nuphy/plugin.json")).unwrap();
    let mut device: Device = serde_json::from_value(serde_json::json!({
        "id":"test", "name":"NuPhy Halo96", "vendor_id":1452, "product_id":591,
        "interface":1, "usage_page":65280, "usage":1, "connection":"usb", "feature_report_ids":[5]
    }))
    .unwrap();
    let matcher = &descriptor.manifest.matches[0];
    assert!(matcher.score(&device).is_some());
    device.name = "NuPhy Halo75".into();
    assert!(matcher.score(&device).is_none());
    device.name = "NuPhy Halo96".into();
    device.feature_report_ids = vec![6];
    assert!(matcher.score(&device).is_none());
    device.feature_report_ids.clear();
    assert!(matcher.score(&device).is_none());
}
fn manifest() -> Manifest {
    Manifest {
        id: "org.test.device".into(),
        version: "7.4.2".into(),
        api: ">=1.0.0, <2.0.0".into(),
        wire: 1,
        executable: "plugin".into(),
        args: vec![],
        required: vec![],
        matches: vec![],
    }
}
#[test]
fn plugin_version_is_not_api_version() {
    assert!(manifest().validate().is_ok());
}
#[test]
fn missing_required_capability_rejected() {
    let mut m = manifest();
    m.required.push("network.http".into());
    assert!(m.validate().is_err());
}
#[test]
fn incompatible_api_rejected() {
    let mut m = manifest();
    m.api = ">=2.0.0".into();
    assert!(m.validate().is_err());
}
#[test]
fn package_escape_rejected() {
    let mut m = manifest();
    m.executable = "../escape".into();
    assert!(m.validate().is_err());
}
#[test]
fn future_write_is_not_live_command() {
    assert!(
        serde_json::from_str::<HostMessage>(
            r#"{"type":"commit","request_id":"x","values":{"dpi.x":800}}"#
        )
        .is_err()
    );
}
#[test]
fn oversized_frame_without_newline_is_bounded() {
    let bytes = vec![b' '; MAX_FRAME + 20];
    assert!(read_frame::<HostMessage>(&mut std::io::Cursor::new(bytes)).is_err());
}
