# ShowDevice

跨平台、离线、只读的设备状态工具。Rust 共享核心，独立进程插件，系统原生 UI。公共契约只有一个来源：`core/api`。

## 当前状态

- Windows：Rust 工作区及 WinUI 3 已构建；已用 Viper V2 Pro 与 Cloud III S Wireless 读取真实电量，Viper 还读取 X/Y DPI。
- macOS：AppKit 源码及构建脚本已提供；本机无法验证，不标记为已支持。
- Linux：GTK 4 原生宿主及 HID 后端已提供；本机无法验证，不标记为已支持。
- 雷柏：被动电量报文解析原型；没有确认具体型号，匹配清单为空，默认不查询任何雷柏设备。
- NuPhy Halo96 V1：独立插件已实测通过 USB 读取背光、去抖、Win/Mac 键位模式、USB/2.4 GHz/蓝牙选择模式和蓝牙通道 1–3。当前 USB 协议没有验证电量读取，电量显示“不支持”；不匹配 Halo96 V2 或其他型号。
- BLE：提供可选的 WinRT/CoreBluetooth/BlueZ 后端、受限特征读取及订阅；Windows 编译通过。尚未接入已验证的设备配置，宿主不启用 BLE 插件能力，不能声称支持蓝牙设备。
- 配置下发：契约及模拟测试，宿主无真实写入入口。编辑控件保持禁用。
- 应用功能：提醒去重、有限历史和偏好默认值作为独立库提供；通知投递、持久化设置与托盘尚未接入。

这些边界是刻意显式的：有源码或接口不等于已经完成平台/设备兼容验证。

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

- [架构与安全边界](docs/architecture.md)
- [插件开发流程](docs/plugin-development.md)
- [兼容性与验证记录](docs/verification.md)
- [协议来源](docs/protocols.md)
- [Halo96 V1 插件与实测范围](docs/nuphy-halo96.md)
- [UI 组件契约](docs/ui.md)

本地包由 `plugin.json`、`permissions.json` 和可执行文件组成。复制到 `dist/plugins/<package>` 后重启宿主；必须先运行校验器。插件是可信本地代码，进程隔离不是恶意代码沙箱。


