use super::*;
#[test]
fn surface_templates_require_valid_pages_and_capabilities() {
    let mut d = descriptor();
    d.surface_templates.push(SurfaceTemplate {
        surface: SurfaceTarget::Tray,
        page_id: d.pages[0].id.clone(),
        style: "mouse.dpi.v1".into(),
        optional: false,
    });
    assert!(validate(&d).is_ok());
    d.surface_templates[0].page_id = "missing".into();
    assert!(validate(&d).is_err());
    d.surface_templates[0].optional = true;
    assert!(validate(&d).is_ok());
    d.surface_templates.push(d.surface_templates[0].clone());
    assert!(validate(&d).is_err());
}
#[test]
fn future_surface_components_require_explicit_optional_fallback() {
    let mut d = descriptor();
    d.surface_templates.push(SurfaceTemplate {
        surface: SurfaceTarget::Desktop,
        page_id: d.pages[0].id.clone(),
        style: "audio.waveform.v2".into(),
        optional: false,
    });
    assert!(validate(&d).is_err());
    d.surface_templates[0].optional = true;
    assert!(validate(&d).is_ok());
}
fn descriptor() -> Descriptor {
    serde_json::from_str(include_str!("../../../plugins/mock/plugin.json")).unwrap()
}
#[test]
fn missing_required_component_rejected() {
    let mut d = descriptor();
    d.pages[0].nodes[0].component = "keyboard-layout".into();
    assert!(validate(&d).is_err());
}
#[test]
fn optional_component_can_be_omitted() {
    let mut d = descriptor();
    d.pages[0].optional = true;
    d.pages[0].nodes[0].component = "keyboard-layout".into();
    assert!(validate(&d).unwrap().is_empty());
}
#[test]
fn invalid_optional_page_is_not_silently_accepted() {
    let mut d = descriptor();
    d.pages[0].optional = true;
    d.pages[0].nodes[0].binding = Some("nonexistent".into());
    assert!(validate(&d).is_err());
}
#[test]
fn excessive_depth_rejected() {
    let mut d = descriptor();
    let mut n = d.pages[0].nodes[0].clone();
    for i in 0..9 {
        n = UiNode {
            id: format!("g{i}"),
            component: "group".into(),
            version: 1,
            label: "Group".into(),
            binding: None,
            action: None,
            visible_when: None,
            enabled_when: None,
            children: vec![n],
        };
    }
    d.pages[0].nodes = vec![n];
    assert!(validate(&d).is_err());
}
#[test]
fn duplicate_ids_rejected() {
    let mut d = descriptor();
    let n = d.pages[0].nodes[0].clone();
    d.pages[0].nodes.push(n);
    assert!(validate(&d).is_err());
}
#[test]
fn readonly_host_never_enables_writes() {
    assert!(!condition(
        &Some(Condition::HostWritesEnabled),
        &State::new(),
        false
    ));
}
#[test]
fn stale_value_is_not_fresh_condition() {
    let mut s = State::new();
    let mut r = Reading::available(76, "mock");
    r.availability = Availability::Stale;
    s.insert("battery.percent".into(), r);
    assert!(!condition(
        &Some(Condition::Available {
            binding: "battery.percent".into()
        }),
        &s,
        false
    ));
}
