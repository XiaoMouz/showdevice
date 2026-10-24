import AppKit
import Foundation

final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var list: NSStackView!
    var detail: NSStackView!
    var status: NSTextField!
    var process: Process?
    let input = Pipe()
    var devices: [[String: Any]] = []
    var selected = 0
    var signature = ""
    var lastRefresh = Date.distantPast
    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        let item = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Quit ShowDevice", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        item.submenu = appMenu; menu.addItem(item); NSApp.mainMenu = menu
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 600),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "ShowDevice"; window.center()
        let root = NSStackView(); root.orientation = .vertical; root.alignment = .leading; root.spacing = 20
        root.edgeInsets = NSEdgeInsets(top: 24, left: 24, bottom: 24, right: 24)
        let heading = NSTextField(labelWithString: "Devices"); heading.font = .systemFont(ofSize: 28, weight: .semibold)
        status = NSTextField(wrappingLabelWithString: "Loading…")
        let refresh = NSButton(title: "Refresh", target: self, action: #selector(refreshDevices))
        let header = NSStackView(views: [heading, refresh]); header.spacing = 24
        root.addArrangedSubview(header); root.addArrangedSubview(status)
        list = NSStackView(); list.orientation = .vertical; list.alignment = .leading; list.spacing = 8
        detail = NSStackView(); detail.orientation = .vertical; detail.alignment = .leading; detail.spacing = 16
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true
        scroll.documentView = detail
        detail.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([detail.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            detail.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            detail.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor)])
        let body = NSStackView(views: [list, scroll]); body.spacing = 24; body.alignment = .top
        list.widthAnchor.constraint(equalToConstant: 250).isActive = true
        root.addArrangedSubview(body)
        window.contentView = root
        body.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 350).isActive = true
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        start()
    }
    @objc func refreshDevices() {
        guard Date().timeIntervalSince(lastRefresh) >= 2 else { return }
        lastRefresh = Date()
        try? input.fileHandleForWriting.write(contentsOf: Data("refresh\n".utf8))
    }
    func start() {
        let environment = ProcessInfo.processInfo.environment
        let base = environment["SHOWDEVICE_HOME"].map { URL(fileURLWithPath: $0).appendingPathComponent("dist") }
            ?? Bundle.main.executableURL!.deletingLastPathComponent()
        let p = Process(); p.executableURL = base.appendingPathComponent("showdevice")
        p.arguments = [environment["SHOWDEVICE_DEMO"] == "1" ? "demo" : "watch", base.appendingPathComponent("plugins").path]
        p.standardInput = input
        let output = Pipe(); p.standardOutput = output
        p.standardError = FileHandle.nullDevice
        do { try p.run(); process = p } catch { status.stringValue = "Unable to start local core: \(error.localizedDescription)"; return }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var buffer = Data()
            while true {
                let chunk = output.fileHandleForReading.availableData
                if chunk.isEmpty { break }
                buffer.append(chunk)
                while let end = buffer.firstIndex(of: 10) {
                    let line = buffer.prefix(upTo: end); buffer.removeSubrange(...end)
                    if line.count > 2097152 { p.terminate(); return }
                    guard let snapshot = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { p.terminate(); return }
                    DispatchQueue.main.async { self?.apply(snapshot) }
                }
                if buffer.count > 2097152 { p.terminate(); return }
            }
            DispatchQueue.main.async { self?.status.isHidden = false; self?.status.stringValue = "Local core stopped. Reopen the app to reconnect." }
        }
    }
    func apply(_ snapshot: [String: Any]) {
        let next = snapshot["devices"] as? [[String: Any]] ?? []
        let bytes = (try? JSONSerialization.data(withJSONObject: [next, snapshot["diagnostics"] ?? []], options: [.sortedKeys])) ?? Data()
        let key = bytes.base64EncodedString()
        if key == signature { return }; signature = key
        devices = next
        for v in list.arrangedSubviews { list.removeArrangedSubview(v); v.removeFromSuperview() }
        let diagnostics = snapshot["diagnostics"] as? [String] ?? []
        status.stringValue = diagnostics.joined(separator: "\n")
        status.isHidden = diagnostics.isEmpty
        for (index, model) in next.enumerated() {
            let name = (model["device"] as? [String: Any])?["name"] as? String ?? "Device"
            let button = NSButton(title: name, target: self, action: #selector(selectDevice(_:))); button.tag = index
            button.setAccessibilityLabel(name); list.addArrangedSubview(button)
        }
        selected = min(selected, max(0, next.count-1)); render()
    }
    @objc func selectDevice(_ sender: NSButton) { selected = sender.tag; render() }
    func render() {
        for v in detail.arrangedSubviews { detail.removeArrangedSubview(v); v.removeFromSuperview() }
        guard devices.indices.contains(selected) else { detail.addArrangedSubview(NSTextField(wrappingLabelWithString: "Connect a supported USB device or receiver, then refresh.")); return }
        PluginPage.render(devices[selected], into: detail)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ notification: Notification) { try? input.fileHandleForWriting.close(); process?.terminate() }
}
let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()

