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

final class AppDelegate: NSObject, NSApplicationDelegate, WKNavigationDelegate, NSWindowDelegate {

    var window: NSWindow!
    var web: WKWebView!
    var errorHtml: String?
    var lastURL: URL?
    var showingError = false
    var fullscreenItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()

        if let url = Bundle.main.url(forResource: "error", withExtension: "html"),
           let html = try? String(contentsOf: url, encoding: .utf8) {
            errorHtml = html
        }

        let frame = NSRect(x: 0, y: 0, width: 1100, height: 760)
        window = NSWindow(contentRect: frame,
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered,
                          defer: false)
        window.title = appTitle
        window.center()
        window.setFrameAutosaveName("QizhouPavilionMain")
        window.delegate = self

        let cfg = WKWebViewConfiguration()
        // 往页面里插「更改域名」入口（不改网页源码）
        if let injectUrl = Bundle.main.url(forResource: "inject", withExtension: "js"),
           let injectSource = try? String(contentsOf: injectUrl, encoding: .utf8) {
            let script = WKUserScript(source: injectSource,
                                      injectionTime: .atDocumentEnd,
                                      forMainFrameOnly: true)
            cfg.userContentController.addUserScript(script)
        }
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
        openURL(url)
    }

    /// 统一走这里，记住最后一次要打开的地址，打不开时「重试」用它。
    func openURL(_ url: URL) {
        lastURL = url
        showingError = false
        web.load(URLRequest(url: url))
    }

    /// 打不开时换成 App 自己的提示页，不留 WebKit 那一片空白
    func showErrorPage() {
        guard !showingError, let html = errorHtml else { return }
        showingError = true
        web.loadHTMLString(html, baseURL: URL(string: "https://qizhouge.app/"))
    }

    /// 取消（比如菜单回传的 zqnavgo:）不算加载失败
    func isCancelled(_ error: Error) -> Bool {
        let ns = error as NSError
        return ns.domain == NSURLErrorDomain && ns.code == NSURLErrorCancelled
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!,
                 withError error: Error) {
        if isCancelled(error) { return }
        showErrorPage()
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        if isCancelled(error) { return }
        showErrorPage()
    }

    /// 窗口进出全屏时同步菜单文案
    func windowDidEnterFullScreen(_ notification: Notification) {
        fullscreenItem?.title = "退出全屏"
    }

    func windowDidExitFullScreen(_ notification: Notification) {
        fullscreenItem?.title = "全屏"
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    /// 长按菜单走 zqnavgo:<动作名>，WebKit 默认会当未知协议忽略，这里拦下来
    func webView(_ webView: WKWebView,
                 decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if let url = navigationAction.request.url, url.scheme == "zqnavgo" {
            decisionHandler(.cancel)
            let prefix = "zqnavgo:"
            let raw = url.absoluteString
            let body = raw.hasPrefix(prefix) ? String(raw.dropFirst(prefix.count)) : raw
            let text = body.removingPercentEncoding ?? body
            switch text {
            case "change": changeDomain(nil)
            case "home": goHome(nil)
            case "reload": reloadPage(nil)
            case "retry": retryLast(nil)
            case "back": goBack(nil)
            case "forward": goForward(nil)
            case "fullscreen": toggleFullscreen(nil)
            default: break
            }
            return
        }
        decisionHandler(.allow)
    }

    @objc func reloadPage(_ sender: Any?) {
        if showingError {
            retryLast(sender)
            return
        }
        web?.reload()
    }

    @objc func retryLast(_ sender: Any?) {
        guard let url = lastURL else { return }
        openURL(url)
    }

    @objc func goBack(_ sender: Any?) {
        if web?.canGoBack == true { web?.goBack() }
    }

    @objc func goForward(_ sender: Any?) {
        if web?.canGoForward == true { web?.goForward() }
    }

    @objc func toggleFullscreen(_ sender: Any?) {
        window?.toggleFullScreen(sender)
    }

    /// 回导航站首页
    @objc func goHome(_ sender: Any?) {
        guard let home = loadSavedURL(), let url = normalize(home) else { return }
        openURL(url)
    }

    @objc func changeDomain(_ sender: Any?) {
        guard let url = promptURL() else { return }
        saveURL(url.absoluteString)
        openURL(url)
    }

    private func buildMenu() {
        let mainMenu = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()

        appMenu.addItem(withTitle: "关于 \(appTitle)",
                        action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)),
                        keyEquivalent: "")
        appMenu.addItem(NSMenuItem.separator())

        // 与 Windows 右键菜单、手机长按菜单完全一致的六项，且不挂快捷键
        let entries: [(String, Selector)] = [
            ("刷新", #selector(reloadPage(_:))),
            ("全屏", #selector(toggleFullscreen(_:))),
            ("更换域名", #selector(changeDomain(_:))),
            ("后退上一页", #selector(goBack(_:))),
            ("前进下一页", #selector(goForward(_:))),
            ("返回主页面", #selector(goHome(_:)))
        ]
        for (title, action) in entries {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = self
            if action == #selector(toggleFullscreen(_:)) { fullscreenItem = item }
            appMenu.addItem(item)
        }

        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "隐藏 \(appTitle)",
                        action: #selector(NSApplication.hide(_:)),
                        keyEquivalent: "")
        appMenu.addItem(withTitle: "退出",
                        action: #selector(NSApplication.terminate(_:)),
                        keyEquivalent: "")

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
