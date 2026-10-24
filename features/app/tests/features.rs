use sd_api::*;
use sd_app_features::*;
fn state(level: u8) -> State {
    State::from([("battery.percent".into(), Reading::available(level, "test"))])
}
#[test]
fn low_battery_notifications_have_hysteresis() {
    let mut alerts = LowBattery::default();
    assert!(alerts.update("mouse", &state(19), 20.));
    assert!(!alerts.update("mouse", &state(18), 20.));
    assert!(!alerts.update("mouse", &state(26), 20.));
    assert!(alerts.update("mouse", &state(20), 20.));
}
#[test]
fn stale_does_not_alert() {
    let mut s = state(10);
    s.get_mut("battery.percent").unwrap().availability = Availability::Stale;
    assert!(!LowBattery::default().update("mouse", &s, 20.));
}
#[test]
fn history_is_bounded() {
    let mut h = History::new(2);
    for n in 0..10 {
        h.push(state(n));
    }
    assert_eq!(h.samples().len(), 2);
}
