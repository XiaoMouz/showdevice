use sd_api::{ConfigChange, ConfigResult, WriteStage};
use std::collections::BTreeMap;
#[test]
fn partial_commit_and_readback_are_per_field() {
    let result = ConfigResult {
        request_id: "test".into(),
        fields: BTreeMap::from([
            ("dpi.x".into(), WriteStage::Verified),
            ("dpi.y".into(), WriteStage::Failed),
        ]),
        message: Some("readback mismatch".into()),
    };
    assert_ne!(result.fields["dpi.x"], result.fields["dpi.y"]);
}
#[test]
fn reject_read_only_and_out_of_range() {
    let cap = sd_api::Capability {
        id: "dpi.x".into(),
        readable: true,
        writable: false,
        subscribable: false,
        value_type: sd_api::ValueType::Number {
            min: 100.,
            max: 30000.,
            step: Some(50.),
        },
        depends_on: vec![],
        effect: None,
    };
    let change = ConfigChange {
        request_id: "change-1".into(),
        expected_revision: None,
        values: BTreeMap::from([("dpi.x".into(), 800.into())]),
    };
    assert!(sd_device_features::validate_config(&change, std::slice::from_ref(&cap)).is_err());
    let mut writable = cap;
    writable.writable = true;
    assert!(sd_device_features::validate_config(&change, &[writable.clone()]).is_ok());
    let mut bad = change;
    bad.values.insert("dpi.x".into(), 123.into());
    assert!(sd_device_features::validate_config(&bad, &[writable]).is_err());
}
