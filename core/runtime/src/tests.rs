use super::*;
#[test]
fn artwork_is_optional_local_and_scoped_to_package() {
    let mut p = package();
    p.directory = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../plugins/nuphy");
    assert!(p.product_image().is_none());
    p.descriptor.product_image = Some("assets/product-trimmed.png".into());
    assert!(p.product_image().is_some());
    for invalid in [
        "missing.jpg",
        "https://example.com/image.jpg",
        "../hyperx/assets/product-trimmed.png",
        "plugin.json",
    ] {
        p.descriptor.product_image = Some(invalid.into());
        assert!(p.product_image().is_none(), "{invalid}");
    }
}
fn device() -> Device {
    Device {
        feature_report_ids: vec![],
        id: "test".into(),
        name: "Test".into(),
        vendor_id: 65535,
        product_id: 1,
        interface: 0,
        usage_page: 0,
        usage: 0,
        connection: Connection::Receiver,
        firmware: None,
    }
}
fn package() -> Package {
    Package {
        directory: PathBuf::from("."),
        descriptor: serde_json::from_str(include_str!("../../../plugins/mock/plugin.json"))
            .unwrap(),
        permissions: Permissions {
            queries: BTreeMap::new(),
        },
    }
}
#[test]
fn ambiguous_match_rejected() {
    assert!(select(&[package(), package()], &device()).is_err());
}
#[test]
fn exact_match_beats_vendor_family() {
    let a = package();
    let mut b = package();
    b.descriptor.manifest.id = "family".into();
    b.descriptor.manifest.matches[0].product_id = None;
    let p = [b, a];
    assert_eq!(
        select(&p, &device())
            .unwrap()
            .unwrap()
            .descriptor
            .manifest
            .id,
        "org.showdevice.mock"
    );
}
#[test]
fn backend_panic_is_contained() {
    let mut b = Broker::new(|_| panic!("fake transport panic"));
    assert!(
        b.query(
            Query::HidInput {
                response_len: 1,
                latest: false
            },
            Duration::from_millis(100)
        )
        .is_err()
    );
}
#[test]
fn timed_out_transport_is_retired() {
    let mut b = Broker::new(|_| {
        thread::sleep(Duration::from_millis(80));
        Ok(vec![])
    });
    assert!(
        b.query(
            Query::HidInput {
                response_len: 1,
                latest: false
            },
            Duration::from_millis(10)
        )
        .is_err()
    );
    assert!(
        b.query(
            Query::HidInput {
                response_len: 1,
                latest: false
            },
            Duration::from_millis(100)
        )
        .is_err()
    );
}
#[test]
fn oversized_response_cannot_enter_core() {
    let mut b = Broker::new(|_| Ok(vec![0; 1025]));
    assert!(
        b.query(
            Query::HidInput {
                response_len: 1,
                latest: false
            },
            Duration::from_millis(100)
        )
        .is_err()
    );
}
