//! Application features consume normalized state, never vendor packets.
use sd_api::*;
use std::collections::{BTreeMap, VecDeque};
pub struct Preferences {
    pub low_battery_percent: f64,
    pub poll_seconds: u64,
    pub history_limit: usize,
}
impl Default for Preferences {
    fn default() -> Self {
        Self {
            low_battery_percent: 20.,
            poll_seconds: 30,
            history_limit: 120,
        }
    }
}
#[derive(Default)]
pub struct LowBattery {
    warned: BTreeMap<String, bool>,
}
impl LowBattery {
    pub fn update(&mut self, device: &str, state: &State, threshold: f64) -> bool {
        let Some(r) = state.get("battery.percent") else {
            return false;
        };
        if r.availability != Availability::Available {
            return false;
        }
        let Some(n) = r.value.as_ref().and_then(|v| v.as_f64()) else {
            return false;
        };
        if state.get("battery.charging").is_some_and(|r| {
            r.value == Some(true.into()) && r.availability == Availability::Available
        }) {
            self.warned.insert(device.into(), false);
            return false;
        }
        let was = *self.warned.get(device).unwrap_or(&false);
        if n > threshold + 5. {
            self.warned.insert(device.into(), false);
            return false;
        }
        if n <= threshold && !was {
            self.warned.insert(device.into(), true);
            return true;
        }
        false
    }
}
pub struct History {
    limit: usize,
    samples: VecDeque<(u64, State)>,
}
impl History {
    pub fn new(limit: usize) -> Self {
        Self {
            limit: limit.min(10_000),
            samples: VecDeque::new(),
        }
    }
    pub fn push(&mut self, state: State) {
        if self.limit == 0 {
            return;
        }
        while self.samples.len() >= self.limit {
            self.samples.pop_front();
        }
        self.samples.push_back((now_ms(), state));
    }
    pub fn samples(&self) -> &VecDeque<(u64, State)> {
        &self.samples
    }
}
