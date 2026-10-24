//! Device features: pure validation, state freshness and future config outcomes.
use sd_api::*;
pub fn validate_value(value: &serde_json::Value, kind: &ValueType) -> bool {
    match kind {
        ValueType::Boolean => value.is_boolean(),
        ValueType::Text => value.as_str().is_some_and(|s| s.len() <= 4096),
        ValueType::Choice { values } => value
            .as_str()
            .is_some_and(|s| values.iter().any(|v| v == s)),
        ValueType::Number { min, max, step } => value.as_f64().is_some_and(|n| {
            n.is_finite()
                && n >= *min
                && n <= *max
                && step.is_none_or(|s| {
                    let v = (n - min) / s;
                    (v - v.round()).abs() < 1e-6
                })
        }),
    }
}
pub fn validate_state(state: &State, caps: &[Capability]) -> Result<(), String> {
    if state.len() > 64 {
        return Err("too many readings".into());
    }
    for (id, r) in state {
        let cap = caps
            .iter()
            .find(|c| &c.id == id && c.readable)
            .ok_or("undeclared reading")?;
        if r.source.len() > 96 || r.precision.as_ref().is_some_and(|p| p.len() > 128) {
            return Err("reading text limit".into());
        }
        if r.availability == Availability::Available
            && (r.updated_at_ms.is_none()
                || r.value
                    .as_ref()
                    .is_none_or(|v| !validate_value(v, &cap.value_type)))
        {
            return Err("invalid reading value".into());
        }
        if r.updated_at_ms.is_some_and(|t| t > now_ms() + 60_000) {
            return Err("timestamp in future".into());
        }
    }
    Ok(())
}
pub fn mark_stale(state: &mut State) {
    for r in state.values_mut() {
        if r.value.is_some() {
            r.availability = Availability::Stale;
        }
    }
}
pub fn validate_config(change: &ConfigChange, caps: &[Capability]) -> Result<(), String> {
    if change.request_id.is_empty() || change.values.is_empty() || change.values.len() > 64 {
        return Err("invalid change".into());
    }
    for (id, v) in &change.values {
        let c = caps
            .iter()
            .find(|c| c.id == *id && c.writable)
            .ok_or("unsupported write")?;
        if !validate_value(v, &c.value_type) {
            return Err(format!("invalid value for {id}"));
        }
    }
    Ok(())
}
/// Once sent, losing contact cannot establish whether a write took effect.
pub fn disconnected(stage: WriteStage) -> WriteStage {
    match stage {
        WriteStage::Sent | WriteStage::Acknowledged => WriteStage::Uncertain,
        other => other,
    }
}
pub fn verify(expected: &serde_json::Value, actual: &serde_json::Value) -> WriteStage {
    if expected == actual {
        WriteStage::Verified
    } else {
        WriteStage::Failed
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn write_disconnect_is_uncertain() {
        assert_eq!(
            disconnected(WriteStage::Acknowledged),
            WriteStage::Uncertain
        );
    }
    #[test]
    fn mismatch_is_not_verified() {
        assert_eq!(verify(&800.into(), &1600.into()), WriteStage::Failed);
    }
    #[test]
    fn invalid_numbers_rejected() {
        assert!(!validate_value(
            &101.into(),
            &ValueType::Number {
                min: 0.,
                max: 100.,
                step: None
            }
        ));
    }
}
