//! Local plugin processes, negotiated contracts and bounded device RPC.
#[cfg(test)]
mod tests;
use anyhow::{Context, Result, anyhow, bail};
use sd_api::*;
use std::{
    collections::BTreeMap,
    io::BufReader,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::mpsc::{self, Receiver, SyncSender},
    thread,
    time::{Duration, Instant},
};
pub const CALL_TIMEOUT: Duration = Duration::from_secs(4);
pub const POLL_TIMEOUT: Duration = Duration::from_secs(12);
const MAX_QUERIES: usize = 16;
#[derive(Debug)]
pub struct DeviceUnavailable(pub String);
impl std::fmt::Display for DeviceUnavailable {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}", self.0)
    }
}
impl std::error::Error for DeviceUnavailable {}
#[derive(Clone)]
pub struct Package {
    pub directory: PathBuf,
    pub descriptor: Descriptor,
    pub permissions: Permissions,
}
impl Package {
    /// Artwork is optional: invalid/missing resources fall back without disabling a device.
    pub fn product_image(&self) -> Option<String> {
        let relative = Path::new(self.descriptor.product_image.as_deref()?);
        if relative
            .components()
            .any(|c| !matches!(c, std::path::Component::Normal(_)))
        {
            return None;
        }
        let path = self.directory.join(relative).canonicalize().ok()?;
        let root = self.directory.canonicalize().ok()?;
        if !path.starts_with(root)
            || !path.is_file()
            || path.metadata().ok()?.len() > 4 * 1024 * 1024
        {
            return None;
        }
        match path.extension()?.to_str()?.to_ascii_lowercase().as_str() {
            "png" | "jpg" | "jpeg" => Some(path.to_string_lossy().into_owned()),
            _ => None,
        }
    }
    pub fn load(directory: &Path) -> Result<Self> {
        let directory = directory.canonicalize()?;
        fn bounded<T: serde::de::DeserializeOwned>(p: &Path) -> Result<T> {
            if p.metadata()?.len() > MAX_FRAME as u64 {
                bail!("package metadata too large");
            }
            Ok(serde_json::from_slice(&std::fs::read(p)?)?)
        }
        let descriptor: Descriptor = bounded(&directory.join("plugin.json"))?;
        descriptor.manifest.validate().map_err(|e| anyhow!(e))?;
        sd_ui_schema::validate(&descriptor).map_err(|e| anyhow!(e))?;
        let permissions: Permissions = bounded(&directory.join("permissions.json"))?;
        validate_permissions(&permissions)?;
        let executable = directory
            .join(&descriptor.manifest.executable)
            .canonicalize()?;
        if !executable.starts_with(&directory) || !executable.is_file() {
            bail!("executable escapes package");
        }
        Ok(Self {
            directory,
            descriptor,
            permissions,
        })
    }
}
pub fn validate_permissions(p: &Permissions) -> Result<()> {
    if p.queries.len() > 32 {
        bail!("too many queries");
    }
    for (name, q) in &p.queries {
        if name.is_empty() || name.len() > 96 {
            bail!("invalid query name");
        }
        match q {
            Query::HidFeature {
                request,
                response_len,
                delay_ms,
                ..
            } => {
                if request.is_empty()
                    || request.len() > 1024
                    || *response_len == 0
                    || *response_len > 4096
                    || *delay_ms > 100
                {
                    bail!("invalid feature query");
                }
            }
            Query::HidOutput {
                request,
                response_len,
                command_index,
                ..
            } => {
                if request.is_empty()
                    || request.len() > 1024
                    || *response_len == 0
                    || *response_len > 1024
                    || *command_index >= *response_len
                {
                    bail!("invalid output query");
                }
            }
            Query::HidInput { response_len, .. } => {
                if *response_len == 0 || *response_len > 1024 {
                    bail!("invalid input query");
                }
            }
            Query::BleRead { .. } => {
                bail!("BLE plugin capability is not enabled in this host release")
            }
        }
    }
    Ok(())
}
/// One worker per device; one in-flight call, one queued job at most.
/// A timed-out backend is permanently retired; no retries on a stuck handle.
pub struct Broker {
    tx: SyncSender<Query>,
    rx: Receiver<Result<Vec<u8>, String>>,
    healthy: bool,
    io_failed: bool,
}
impl Broker {
    pub fn new<F>(mut execute: F) -> Self
    where
        F: FnMut(Query) -> Result<Vec<u8>, String> + Send + 'static,
    {
        let (tx, jobs) = mpsc::sync_channel(1);
        let (results, rx) = mpsc::sync_channel(1);
        thread::spawn(move || {
            while let Ok(q) = jobs.recv() {
                let value = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| execute(q)))
                    .unwrap_or_else(|_| Err("transport panic".into()));
                if results.send(value).is_err() {
                    break;
                }
            }
        });
        Self {
            tx,
            rx,
            healthy: true,
            io_failed: false,
        }
    }
    pub fn query(&mut self, q: Query, timeout: Duration) -> Result<Vec<u8>> {
        let limit = match &q {
            Query::HidFeature { response_len, .. }
            | Query::HidOutput { response_len, .. }
            | Query::HidInput { response_len, .. } => (*response_len).min(4096),
            Query::BleRead { .. } => 1024,
        };
        if !self.healthy {
            bail!("transport retired");
        }
        self.tx.try_send(q).map_err(|_| anyhow!("transport busy"))?;
        match self.rx.recv_timeout(timeout) {
            Ok(Ok(v)) => {
                if v.len() > limit {
                    self.healthy = false;
                    bail!("oversized transport result");
                }
                Ok(v)
            }
            Ok(Err(e)) => {
                self.io_failed = true;
                Err(anyhow!(e))
            }
            Err(_) => {
                self.healthy = false;
                Err(anyhow!("transport timeout; handle retired"))
            }
        }
    }
    pub fn is_healthy(&self) -> bool {
        self.healthy
    }
    /// Only completed I/O errors permit replacing a handle; never a timeout.
    pub fn needs_reopen(&self) -> bool {
        self.healthy && self.io_failed
    }
}
pub struct Session {
    child: Child,
    tx: Option<SyncSender<HostMessage>>,
    rx: Receiver<Result<PluginMessage, String>>,
    pub descriptor: Descriptor,
    permissions: Permissions,
    sequence: u64,
    pub active: bool,
}
impl Session {
    pub fn launch(package: &Package, device: &Device) -> Result<Self> {
        Self::launch_with_timeout(package, device, CALL_TIMEOUT)
    }
    pub fn launch_with_timeout(
        package: &Package,
        device: &Device,
        timeout: Duration,
    ) -> Result<Self> {
        package
            .descriptor
            .manifest
            .validate()
            .map_err(|e| anyhow!(e))?;
        if !package
            .descriptor
            .manifest
            .matches
            .iter()
            .any(|m| m.score(device).is_some())
        {
            bail!("device does not match plugin");
        }
        let exe = package
            .directory
            .join(&package.descriptor.manifest.executable);
        let mut cmd = Command::new(exe);
        cmd.args(&package.descriptor.manifest.args)
            .current_dir(&package.directory)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            cmd.creation_flags(0x08000000);
        }
        let mut child = cmd.spawn().context("start plugin")?;
        let mut input = child.stdin.take().context("stdin pipe")?;
        let output = child.stdout.take().context("stdout pipe")?;
        let (tx, outbox) = mpsc::sync_channel(1);
        thread::spawn(move || {
            while let Ok(msg) = outbox.recv() {
                if write_frame(&mut input, &msg).is_err() {
                    break;
                }
            }
        });
        let (inbox, rx) = mpsc::sync_channel(4);
        thread::spawn(move || {
            let mut reader = BufReader::new(output);
            loop {
                let msg = read_frame::<PluginMessage>(&mut reader).map_err(|e| e.to_string());
                let failed = msg.is_err();
                // Flooding terminates the reader; no unbounded queue.
                if inbox.try_send(msg).is_err() || failed {
                    break;
                }
            }
        });
        let mut session = Self {
            child,
            tx: Some(tx),
            rx,
            descriptor: package.descriptor.clone(),
            permissions: package.permissions.clone(),
            sequence: 0,
            active: false,
        };
        let result = (|| {
            session.send(HostMessage::Hello {
                api: API_VERSION.into(),
                wire: WIRE_VERSION,
                capabilities: HOST_CAPABILITIES.iter().map(|s| s.to_string()).collect(),
            })?;
            let PluginMessage::Hello { descriptor } = session.receive(timeout)? else {
                bail!("expected plugin handshake");
            };
            descriptor.manifest.validate().map_err(|e| anyhow!(e))?;
            // An executable cannot silently widen its installed contract.
            if serde_json::to_value(&descriptor)? != serde_json::to_value(&package.descriptor)? {
                bail!("handshake differs from installed descriptor");
            }
            session.descriptor.pages =
                sd_ui_schema::validate(&descriptor).map_err(|e| anyhow!(e))?;
            session.send(HostMessage::Activate {
                device: device.clone(),
            })?;
            if !matches!(session.receive(timeout)?, PluginMessage::Ready) {
                bail!("activation rejected");
            }
            session.active = true;
            Ok(())
        })();
        if let Err(e) = result {
            session.stop();
            return Err(e);
        }
        Ok(session)
    }
    fn send(&self, msg: HostMessage) -> Result<()> {
        self.tx
            .as_ref()
            .ok_or_else(|| anyhow!("plugin stopped"))?
            .try_send(msg)
            .map_err(|_| anyhow!("plugin input queue unavailable"))
    }
    fn receive(&self, timeout: Duration) -> Result<PluginMessage> {
        self.rx
            .recv_timeout(timeout)
            .map_err(|_| anyhow!("plugin timeout or closed pipe"))?
            .map_err(|e| anyhow!(e))
    }
    pub fn poll(&mut self, broker: &mut Broker) -> Result<State> {
        self.poll_with_timeout(broker, POLL_TIMEOUT)
    }
    pub fn poll_with_timeout(&mut self, broker: &mut Broker, timeout: Duration) -> Result<State> {
        let result = self.poll_inner(broker, timeout);
        if result
            .as_ref()
            .is_err_and(|e| e.downcast_ref::<DeviceUnavailable>().is_none())
        {
            self.stop();
        }
        result
    }
    fn poll_inner(&mut self, broker: &mut Broker, timeout: Duration) -> Result<State> {
        if !self.active {
            bail!("plugin is not active");
        }
        self.sequence += 1;
        let id = self.sequence;
        self.send(HostMessage::Poll { request_id: id })?;
        let deadline = Instant::now() + timeout;
        let mut queries = 0;
        let mut previous_io = 0;
        loop {
            let remaining = deadline
                .checked_duration_since(Instant::now())
                .ok_or_else(|| anyhow!("poll deadline exceeded"))?;
            match self.receive(remaining)? {
                PluginMessage::Query { request_id, name } => {
                    queries += 1;
                    if queries > MAX_QUERIES || request_id <= previous_io {
                        bail!("query limit or invalid sequence");
                    }
                    previous_io = request_id;
                    let recipe = self
                        .permissions
                        .queries
                        .get(&name)
                        .ok_or_else(|| anyhow!("query not permitted: {name}"))?
                        .clone();
                    let remaining = deadline
                        .checked_duration_since(Instant::now())
                        .ok_or_else(|| anyhow!("poll deadline exceeded"))?;
                    let result = broker.query(recipe, remaining.min(CALL_TIMEOUT));
                    let (data, error) = match result {
                        Ok(v) => (v, None),
                        Err(e) => (Vec::new(), Some(e.to_string())),
                    };
                    self.send(HostMessage::IoResult {
                        request_id,
                        data,
                        error,
                    })?;
                }
                PluginMessage::Snapshot { request_id, state } if request_id == id => {
                    sd_device_features::validate_state(&state, &self.descriptor.capabilities)
                        .map_err(|e| anyhow!(e))?;
                    return Ok(state);
                }
                PluginMessage::Error {
                    request_id,
                    message,
                } if request_id == id => {
                    return Err(DeviceUnavailable(message.chars().take(1024).collect()).into());
                }
                _ => bail!("unexpected plugin message or response id"),
            }
        }
    }
    pub fn stop(&mut self) {
        self.active = false;
        self.tx.take();
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}
impl Drop for Session {
    fn drop(&mut self) {
        self.stop();
    }
}
pub fn select<'a>(packages: &'a [Package], device: &Device) -> Result<Option<&'a Package>> {
    let mut best = None;
    let mut score = None;
    let mut conflict = false;
    for p in packages {
        let candidate = p
            .descriptor
            .manifest
            .matches
            .iter()
            .filter_map(|m| m.score(device))
            .max();
        if let Some(n) = candidate {
            if score.is_none_or(|s| n > s) {
                best = Some(p);
                score = Some(n);
                conflict = false;
            } else if score == Some(n) {
                conflict = true;
            }
        }
    }
    if conflict {
        bail!("ambiguous plugin match for {}", device.name);
    }
    Ok(best)
}
pub fn discover(root: &Path) -> (Vec<Package>, Vec<String>) {
    let mut packages = Vec::new();
    let mut errors = Vec::new();
    let mut ids = BTreeMap::new();
    let Ok(entries) = std::fs::read_dir(root) else {
        return (
            packages,
            vec![format!("Plugin directory unavailable: {}", root.display())],
        );
    };
    for entry in entries.flatten().take(128) {
        if !entry.path().join("plugin.json").is_file() {
            continue;
        }
        match Package::load(&entry.path()) {
            Ok(p) => {
                let id = p.descriptor.manifest.id.clone();
                *ids.entry(id).or_insert(0) += 1;
                packages.push(p);
            }
            Err(e) => errors.push(
                format!("{}: {e}", entry.file_name().to_string_lossy())
                    .chars()
                    .take(512)
                    .collect(),
            ),
        }
    }
    packages.retain(|p| {
        if ids[&p.descriptor.manifest.id] > 1 {
            errors.push(format!("duplicate plugin id: {}", p.descriptor.manifest.id));
            false
        } else {
            true
        }
    });
    (packages, errors)
}
