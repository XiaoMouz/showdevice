# ShowDevice

## Windows 开发

需要 Rust MSVC、Visual Studio C++ Build Tools/Windows SDK、.NET 10 SDK。构建时包管理器需要获取依赖；运行时没有网络功能。

```powershell
cargo test --workspace
cargo build --workspace
cargo run -p showdevice -- package .
cargo run -p showdevice -- self-test dist/plugins
dotnet build apps/windows/ShowDevice.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
& apps/windows/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/ShowDevice.exe
```

也可以执行 `./scripts/build.ps1`。重建发布目录前请关闭正在运行的 ShowDevice，避免 Windows 可执行文件锁定。

### CLI

```powershell
./dist/showdevice.exe enumerate
./dist/showdevice.exe snapshot dist/plugins
./dist/showdevice.exe watch dist/plugins
./dist/showdevice.exe demo dist/plugins
./dist/showdevice.exe validate dist/plugins/razer
./dist/showdevice.exe schemas core/api/schema
./dist/showdevice.exe new-plugin plugins/my-device
```

`demo` 明确显示模拟设备；普通模式不回退到模拟数据。`watch` 输出 JSON Lines，通过 stdin 接收 `refresh`；stdin 关闭即退出。每个设备有独立工作线程，30 秒轮询，5 秒检查拔插，手动刷新至少间隔 2 秒。无变化时不推送 UI 帧。

桌面宿主可以通过 `SHOWDEVICE_HOME` 指定包含 `dist` 的项目目录。仅开发演示可设 `SHOWDEVICE_DEMO=1`。

## macOS / Linux

macOS：先 `cargo build --workspace && cargo run -p showdevice -- package .`，再 `sh apps/macos/build.sh`。输出 `dist/ShowDevice.app`。

Linux：安装 HID/GTK 开发运行依赖（例如 Debian/Ubuntu 的 `libudev-dev libgtk-4-dev python3-gi gir1.2-gtk-4.0`），构建及打包 Rust 后运行 `python3 apps/linux/main.py`。系统可能要求为已确认的 HID 设备配置访问权限；程序不自动修改权限或加载驱动。

## 扩展

本地包由 `plugin.json`、`permissions.json` 和可执行文件组成。复制到 `dist/plugins/<package>` 后重启宿主；必须先运行校验器。
