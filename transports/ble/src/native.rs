//! Optional native BLE adapters: WinRT, CoreBluetooth and BlueZ through btleplug.
//! Not advertised to plugins before a BLE device profile is verified.
use crate::{BATTERY_LEVEL, BATTERY_SERVICE, BleBackend};
use anyhow::{Context, Result, bail};
use btleplug::{
    api::{Central, Characteristic, Manager as _, Peripheral as _, ScanFilter},
    platform::{Adapter, Manager, Peripheral},
};
use futures_util::StreamExt;
use sd_api::{Connection, Device};
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::mpsc::{self, Receiver, SyncSender},
    time::Duration,
};
use tokio::{runtime::Runtime, task::JoinHandle};
use uuid::Uuid;
const DEADLINE: Duration = Duration::from_secs(5);
type Key = (String, String, String);
#[derive(Debug)]
pub struct Notification {
    pub device_id: String,
    pub characteristic: String,
    pub data: Vec<u8>,
}
pub struct NativeBle {
    runtime: Runtime,
    adapters: Vec<Adapter>,
    peripherals: BTreeMap<String, Peripheral>,
    allowed: BTreeSet<(String, String)>,
    subscriptions: BTreeMap<Key, JoinHandle<()>>,
    tx: SyncSender<Notification>,
    rx: Receiver<Notification>,
}
impl NativeBle {
    pub fn battery_only() -> Result<Self> {
        Self::new([(BATTERY_SERVICE.into(), BATTERY_LEVEL.into())].into())
    }
    pub fn new(allowed: BTreeSet<(String, String)>) -> Result<Self> {
        if allowed.len() > 32 {
            bail!("BLE characteristic permission limit");
        }
        for (s, c) in &allowed {
            Uuid::parse_str(s)?;
            Uuid::parse_str(c)?;
        }
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(1)
            .enable_all()
            .build()?;
        let adapters = runtime.block_on(async {
            tokio::time::timeout(DEADLINE, async { Manager::new().await?.adapters().await }).await
        })??;
        let (tx, rx) = mpsc::sync_channel(32);
        Ok(Self {
            runtime,
            adapters,
            peripherals: BTreeMap::new(),
            allowed,
            subscriptions: BTreeMap::new(),
            tx,
            rx,
        })
    }
    /// Explicit discovery only. Construction/enumeration never start scanning.
    pub fn scan(&self, duration: Duration) -> Result<()> {
        let duration = duration.min(Duration::from_secs(10));
        let services: Vec<Uuid> = self
            .allowed
            .iter()
            .map(|(s, _)| Uuid::parse_str(s).unwrap())
            .collect();
        self.runtime.block_on(async {
            let mut started = Vec::new();
            let mut failure = None;
            for adapter in &self.adapters {
                match tokio::time::timeout(
                    DEADLINE,
                    adapter.start_scan(ScanFilter {
                        services: services.clone(),
                    }),
                )
                .await
                {
                    Ok(Ok(())) => started.push(adapter),
                    Ok(Err(e)) => {
                        failure = Some(anyhow::anyhow!(e));
                        break;
                    }
                    Err(e) => {
                        failure = Some(anyhow::anyhow!(e));
                        break;
                    }
                }
            }
            if failure.is_none() {
                tokio::time::sleep(duration).await;
            }
            // Cleanup adapters already started even if a later adapter failed.
            for a in started {
                let _ = tokio::time::timeout(DEADLINE, a.stop_scan()).await;
            }
            if let Some(e) = failure {
                return Err(e);
            }
            Ok(())
        })
    }
    fn target(
        &self,
        id: &str,
        service: &str,
        characteristic: &str,
    ) -> Result<(Peripheral, Characteristic)> {
        if !self
            .allowed
            .contains(&(service.into(), characteristic.into()))
        {
            bail!("BLE characteristic not permitted");
        }
        let p = self
            .peripherals
            .get(id)
            .context("unknown BLE device")?
            .clone();
        let s = Uuid::parse_str(service)?;
        let c = Uuid::parse_str(characteristic)?;
        self.runtime.block_on(async {
            tokio::time::timeout(DEADLINE, async {
                if !p.is_connected().await? {
                    p.connect().await?;
                }
                p.discover_services().await?;
                let characteristic = p
                    .characteristics()
                    .into_iter()
                    .find(|v| v.uuid == c && v.service_uuid == s)
                    .context("BLE characteristic unavailable")?;
                Ok((p, characteristic))
            })
            .await?
        })
    }
    pub fn next_notification(&self) -> Option<Notification> {
        self.rx.try_recv().ok()
    }
}
impl BleBackend for NativeBle {
    fn enumerate(&mut self) -> Result<Vec<Device>, String> {
        let result = self
            .runtime
            .block_on(async {
                tokio::time::timeout(DEADLINE, async {
                    let mut found = Vec::new();
                    for a in &self.adapters {
                        for p in a.peripherals().await? {
                            let properties = p.properties().await?;
                            found.push((
                                p,
                                properties
                                    .and_then(|v| v.local_name)
                                    .unwrap_or_else(|| "BLE device".into()),
                            ));
                            if found.len() >= 128 {
                                return Ok::<_, anyhow::Error>(found);
                            }
                        }
                    }
                    Ok(found)
                })
                .await?
            })
            .map_err(|e: anyhow::Error| e.to_string())?;
        let mut devices = Vec::new();
        for (p, name) in result {
            let id = p.id().to_string();
            devices.push(Device {
                id: id.clone(),
                name,
                vendor_id: 0,
                product_id: 0,
                interface: -1,
                usage_page: 0,
                usage: 0,
                connection: Connection::Ble,
                firmware: None,
            });
            self.peripherals.insert(id, p);
        }
        self.peripherals
            .retain(|id, _| devices.iter().any(|d| &d.id == id));
        Ok(devices)
    }
    fn read(&mut self, id: &str, service: &str, characteristic: &str) -> Result<Vec<u8>, String> {
        let (p, c) = self
            .target(id, service, characteristic)
            .map_err(|e| e.to_string())?;
        let value = self
            .runtime
            .block_on(async { tokio::time::timeout(DEADLINE, p.read(&c)).await })
            .map_err(|e| e.to_string())?
            .map_err(|e| e.to_string())?;
        if value.len() > 1024 {
            return Err("BLE value too large".into());
        }
        Ok(value)
    }
    fn subscribe(&mut self, id: &str, service: &str, characteristic: &str) -> Result<(), String> {
        let key = (id.into(), service.into(), characteristic.into());
        if self.subscriptions.contains_key(&key) {
            return Ok(());
        }
        if self.subscriptions.len() >= 16 {
            return Err("BLE subscription limit".into());
        }
        let (p, c) = self
            .target(id, service, characteristic)
            .map_err(|e| e.to_string())?;
        let mut notifications = self
            .runtime
            .block_on(async {
                tokio::time::timeout(DEADLINE, async {
                    let stream = p.notifications().await?;
                    p.subscribe(&c).await?;
                    Ok::<_, anyhow::Error>(stream)
                })
                .await?
            })
            .map_err(|e: anyhow::Error| e.to_string())?;
        let tx = self.tx.clone();
        let id = id.to_string();
        let characteristic = characteristic.to_string();
        let task = self.runtime.spawn(async move {
            while let Some(n) = notifications.next().await {
                if n.uuid == c.uuid && n.value.len() <= 1024 {
                    let _ = tx.try_send(Notification {
                        device_id: id.clone(),
                        characteristic: characteristic.clone(),
                        data: n.value,
                    });
                }
            }
        });
        self.subscriptions.insert(key, task);
        Ok(())
    }
    fn disconnect(&mut self, id: &str) {
        self.subscriptions.retain(|key, task| {
            if key.0 == id {
                task.abort();
                false
            } else {
                true
            }
        });
        if let Some(p) = self.peripherals.remove(id) {
            let _ = self
                .runtime
                .block_on(async { tokio::time::timeout(DEADLINE, p.disconnect()).await });
        }
    }
}
impl Drop for NativeBle {
    fn drop(&mut self) {
        for task in self.subscriptions.values() {
            task.abort();
        }
        // Drop cannot hang on unavailable adapters.
        self.runtime.block_on(async {
            let _ = tokio::time::timeout(Duration::from_secs(2), async {
                for a in &self.adapters {
                    let _ = a.stop_scan().await;
                }
                for p in self.peripherals.values() {
                    let _ = p.disconnect().await;
                }
            })
            .await;
        });
    }
}
