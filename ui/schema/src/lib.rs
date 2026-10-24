use sd_api::*;
#[cfg(test)]
mod tests;
use std::collections::{BTreeMap, BTreeSet};
pub const COMPONENTS: &[&str] = &[
    "group", "status", "toggle", "select", "number", "slider", "button", "result",
];
pub fn validate(descriptor: &Descriptor) -> Result<Vec<UiPage>, String> {
    if serde_json::to_vec(descriptor)
        .map_err(|e| e.to_string())?
        .len()
        > 16_384
    {
        return Err("descriptor byte budget exceeded".into());
    }
    if descriptor.capabilities.len() > 64 || descriptor.pages.len() > 8 {
        return Err("descriptor too large".into());
    }
    let mut caps = BTreeMap::new();
    for cap in &descriptor.capabilities {
        if cap.id.is_empty() || cap.id.len() > 96 || caps.insert(cap.id.clone(), cap).is_some() {
            return Err("invalid/duplicate capability".into());
        }
        match &cap.value_type {
            ValueType::Number { min, max, step }
                if !min.is_finite()
                    || !max.is_finite()
                    || min > max
                    || step.is_some_and(|s| !s.is_finite() || s <= 0.) =>
            {
                return Err("invalid numeric range".into());
            }
            ValueType::Choice { values }
                if values.is_empty()
                    || values.len() > 64
                    || values.iter().any(|v| v.len() > 256) =>
            {
                return Err("invalid choice values".into());
            }
            _ => {}
        }
    }
    for c in caps.values() {
        if c.depends_on.iter().any(|d| !caps.contains_key(d)) {
            return Err("unknown capability dependency".into());
        }
    }
    let mut ids = BTreeSet::new();
    let mut count = 0;
    let mut pages = Vec::new();
    for page in &descriptor.pages {
        if page.id.is_empty()
            || page.id.len() > 96
            || page.title.len() > 256
            || !ids.insert(page.id.clone())
        {
            return Err("invalid page".into());
        }
        let mut unsupported = false;
        fn walk(
            nodes: &[UiNode],
            depth: usize,
            count: &mut usize,
            ids: &mut BTreeSet<String>,
            caps: &BTreeMap<String, &Capability>,
            unsupported: &mut bool,
        ) -> Result<(), String> {
            if depth > 8 {
                return Err("UI depth limit".into());
            }
            for n in nodes {
                *count += 1;
                if *count > 256 {
                    return Err("UI node limit".into());
                }
                if n.id.is_empty()
                    || n.id.len() > 96
                    || n.label.len() > 512
                    || !ids.insert(n.id.clone())
                {
                    return Err("invalid node id/label".into());
                }
                if !COMPONENTS.contains(&n.component.as_str()) || n.version != 1 {
                    *unsupported = true;
                }
                if n.binding.as_ref().is_some_and(|b| !caps.contains_key(b)) {
                    return Err("unknown UI binding".into());
                }
                if n.action
                    .as_ref()
                    .is_some_and(|a| !caps.get(a).is_some_and(|c| c.writable))
                {
                    return Err("action is not a writable capability".into());
                }
                for cond in [&n.visible_when, &n.enabled_when].into_iter().flatten() {
                    let b = match cond {
                        Condition::Available { binding } | Condition::Equals { binding, .. } => {
                            Some(binding)
                        }
                        Condition::HostWritesEnabled => None,
                    };
                    if b.is_some_and(|b| !caps.contains_key(b)) {
                        return Err("unknown condition binding".into());
                    }
                }
                if n.component != "group" && !n.children.is_empty() {
                    return Err("only groups can have children".into());
                }
                walk(&n.children, depth + 1, count, ids, caps, unsupported)?;
            }
            Ok(())
        }
        walk(
            &page.nodes,
            1,
            &mut count,
            &mut ids,
            &caps,
            &mut unsupported,
        )?;
        if unsupported {
            if page.optional {
                continue;
            }
            return Err("required UI component unavailable".into());
        }
        pages.push(page.clone());
    }
    Ok(pages)
}
pub fn condition(c: &Option<Condition>, state: &State, writes: bool) -> bool {
    match c {
        None => true,
        Some(Condition::HostWritesEnabled) => writes,
        Some(Condition::Available { binding }) => state
            .get(binding)
            .is_some_and(|r| r.availability == Availability::Available),
        Some(Condition::Equals { binding, value }) => state.get(binding).is_some_and(|r| {
            r.availability == Availability::Available && r.value.as_ref() == Some(value)
        }),
    }
}
pub fn text(reading: Option<&Reading>) -> String {
    match reading {
        None => "Waiting for device".into(),
        Some(r) => match r.availability {
            Availability::Unknown => "Unknown".into(),
            Availability::Unsupported => "Not supported".into(),
            Availability::Offline => "Device offline".into(),
            Availability::Stale => format!(
                "Last known: {}",
                r.value.as_ref().map(|v| v.to_string()).unwrap_or_default()
            ),
            Availability::Available => r
                .value
                .as_ref()
                .map(|v| {
                    v.as_str()
                        .map(str::to_string)
                        .unwrap_or_else(|| v.to_string())
                })
                .unwrap_or_else(|| "Unknown".into()),
        },
    }
}
