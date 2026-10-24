import AppKit
import WebKit

let appTitle = "琪舟阁"
let configDirName = "QizhouPavilion"

func configFile() -> URL {
    let base = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/\(configDirName)", isDirectory: true)
    try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
    return base.appendingPathComponent("home.txt")
}

func loadSavedURL() -> String? {
    guard let text = try? String(contentsOf: configFile(), encoding: .utf8) else { return nil }
    let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
    return trimmed.isEmpty ? nil : trimmed
}

func saveURL(_ url: String) {
    try? url.write(to: configFile(), atomically: true, encoding: .utf8)
}

func normalize(_ raw: String) -> URL? {
    var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
    if s.isEmpty { return nil }
    if !s.hasPrefix("http://") && !s.hasPrefix("https://") { s = "https://" + s }
    while s.count > 8 && s.hasSuffix("/") { s.removeLast() }
    guard s.contains(".") else { return nil }
    return URL(string: s)
}

func promptURL() -> URL? {
    let alert = NSAlert()
    alert.messageText = appTitle
    alert.informativeText = "输入你部署的导航站域名"
    alert.addButton(withTitle: "进入")
    alert.addButton(withTitle: "取消")

    let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24))
    field.placeholderString = "例如 nav.example.com"
    field.stringValue = loadSavedURL() ?? ""
    alert.accessoryView = field
    alert.window.initialFirstResponder = field

    guard alert.runModal() == .alertFirstButtonReturn else { return nil }
    return normalize(field.stringValue)
}

final class AppDelegate: NSObject, NSApplicationDelegate, WKNavigationDelegate {

    var window: NSWindow!
    var web: WKWebView!

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()

        let frame = NSRect(x: 0, y: 0, width: 1100, height: 760)
        window = NSWindow(contentRect: frame,
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered,
                          defer: false)
        window.title = appTitle
        window.center()
        window.setFrameAutosaveName("QizhouPavilionMain")

        let cfg = WKWebViewConfiguration()
        web = WKWebView(frame: frame, configuration: cfg)
        web.autoresizingMask = [.width, .height]
        web.allowsBackForwardNavigationGestures = true
        web.navigationDelegate = self
        window.contentView?.addSubview(web)
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        var target: URL?
        if CommandLine.arguments.count > 1 {
            target = normalize(CommandLine.arguments[1])
        }
        if target == nil, let saved = loadSavedURL() {
            target = normalize(saved)
        }
        if target == nil {
            target = promptURL()
            if let t = target { saveURL(t.absoluteString) }
        }
        guard let url = target else {
            NSApp.terminate(nil)
            return
        }
        web.load(URLRequest(url: url))
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    /// 网页侧边栏的「更改域名」走 zqnavgo:change，WebKit 默认会当未知协议忽略，这里拦下来
    func webView(_ webView: WKWebView,
                 decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if let url = navigationAction.request.url, url.scheme == "zqnavgo" {
            decisionHandler(.cancel)
            let prefix = "zqnavgo:"
            let raw = url.absoluteString
            let body = raw.hasPrefix(prefix) ? String(raw.dropFirst(prefix.count)) : raw
            let text = body.removingPercentEncoding ?? body
            if text == "change" { changeDomain(nil) }
            return
        }
        decisionHandler(.allow)
    }

    @objc func reloadPage(_ sender: Any?) {
        web?.reload()
    }

    @objc func goBack(_ sender: Any?) {
        if web?.canGoBack == true { web?.goBack() }
    }

    @objc func changeDomain(_ sender: Any?) {
        guard let url = promptURL() else { return }
        saveURL(url.absoluteString)
        web.load(URLRequest(url: url))
    }

    private func buildMenu() {
        let mainMenu = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()

        appMenu.addItem(withTitle: "关于 \(appTitle)",
                        action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)),
                        keyEquivalent: "")
        appMenu.addItem(NSMenuItem.separator())

        let reloadItem = NSMenuItem(title: "刷新", action: #selector(reloadPage(_:)), keyEquivalent: "r")
        reloadItem.target = self
        appMenu.addItem(reloadItem)

        let backItem = NSMenuItem(title: "后退", action: #selector(goBack(_:)), keyEquivalent: "[")
        backItem.target = self
        appMenu.addItem(backItem)

        let domainItem = NSMenuItem(title: "更换域名", action: #selector(changeDomain(_:)), keyEquivalent: "l")
        domainItem.target = self
        appMenu.addItem(domainItem)

        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "隐藏 \(appTitle)",
                        action: #selector(NSApplication.hide(_:)),
                        keyEquivalent: "h")
        appMenu.addItem(withTitle: "退出",
                        action: #selector(NSApplication.terminate(_:)),
                        keyEquivalent: "q")

        appItem.submenu = appMenu
        mainMenu.addItem(appItem)
        NSApp.mainMenu = mainMenu
    }
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.setActivationPolicy(.regular)
application.run()
