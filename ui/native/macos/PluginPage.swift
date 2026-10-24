import AppKit
enum PluginPage {
    static func condition(_ condition: Any?, state: [String: Any]) -> Bool {
        guard let c = condition as? [String: Any] else { return true }
        guard let op = c["op"] as? String, op != "host_writes_enabled",
              let key = c["binding"] as? String, let r = state[key] as? [String: Any],
              r["availability"] as? String == "available" else { return false }
        if op == "available" { return true }
        return op == "equals" && (r["value"] as? NSObject) == (c["value"] as? NSObject)
    }
    static func value(_ state: [String: Any], _ key: String?) -> String {
        guard let key = key, let r = state[key] as? [String: Any] else { return "Waiting for device" }
        let availability = r["availability"] as? String ?? "unknown"
        if availability == "unknown" { return "Unknown" }
        if availability == "offline" { return "Device offline" }
        if availability == "unsupported" { return "Not supported" }
        var text = String(describing: r["value"] ?? "Unknown")
        if availability == "stale" { text = "Last known: " + text }
        if let ms = r["updated_at_ms"] as? Double {
            let formatter = DateFormatter(); formatter.timeStyle = .medium
            text += " · " + formatter.string(from: Date(timeIntervalSince1970: ms/1000))
        }
        return text
    }
    static func render(_ model: [String: Any], into stack: NSStackView) {
        if let error = model["error"] as? String { stack.addArrangedSubview(NSTextField(wrappingLabelWithString: "Reading unavailable: " + error)) }
        let state = model["state"] as? [String: Any] ?? [:]
        let caps = model["capabilities"] as? [[String: Any]] ?? []
        for page in model["pages"] as? [[String: Any]] ?? [] {
            let title = NSTextField(labelWithString: page["title"] as? String ?? "")
            title.font = .systemFont(ofSize: 20, weight: .semibold); stack.addArrangedSubview(title)
            for node in page["nodes"] as? [[String: Any]] ?? [] { add(node, state: state, caps: caps, into: stack) }
        }
    }
    static func add(_ node: [String: Any], state: [String: Any], caps: [[String: Any]], into stack: NSStackView) {
        guard condition(node["visible_when"], state: state) else { return }
        let label = node["label"] as? String ?? ""
        let component = node["component"] as? String ?? "status"
        let binding = node["binding"] as? String
        let title = NSTextField(labelWithString: label); title.font = .systemFont(ofSize: 13, weight: .semibold)
        if component != "toggle" && component != "button" { stack.addArrangedSubview(title) }
        if component == "group" {
            for child in node["children"] as? [[String: Any]] ?? [] { add(child, state: state, caps: caps, into: stack) }; return
        }
        let type = caps.first { $0["id"] as? String == binding }?["value_type"] as? [String: Any] ?? [:]
        let control: NSView
        switch component {
        case "toggle": let c = NSButton(checkboxWithTitle: label, target: nil, action: nil); c.isEnabled = false; control = c
        case "select": let c = NSPopUpButton(); c.addItems(withTitles: type["values"] as? [String] ?? []); c.isEnabled = false; control = c
        case "number": let c = NSTextField(string: value(state, binding)); c.isEnabled = false; control = c
        case "slider": let c = NSSlider(value: 0, minValue: type["min"] as? Double ?? 0, maxValue: type["max"] as? Double ?? 100, target: nil, action: nil); c.isEnabled = false; control = c
        case "button": let c = NSButton(title: label, target: nil, action: nil); c.isEnabled = false; control = c
        default: control = NSTextField(wrappingLabelWithString: value(state, binding))
        }
        control.setAccessibilityLabel(label); stack.addArrangedSubview(control)
    }
}

