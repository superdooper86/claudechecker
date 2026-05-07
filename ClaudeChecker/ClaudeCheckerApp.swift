import SwiftUI
import WebKit
import Combine

@main
struct ClaudeCheckerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    var body: some Scene {
        Settings { EmptyView() }
    }
}

@MainActor
class AppDelegate: NSObject, NSApplicationDelegate {
    var statusItem: NSStatusItem?
    var popover: NSPopover?
    var usageViewModel = UsageViewModel()
    var updateManager = UpdateManager()
    var refreshTimer: Timer?
    var cookiePrimerView: WKWebView?
    var cancellables = Set<AnyCancellable>()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        // Hidden WKWebView to prime the shared cookie store
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default()
        cookiePrimerView = WKWebView(frame: .zero, configuration: config)
        cookiePrimerView?.load(URLRequest(url: URL(string: "https://claude.ai/api/organizations/\(UsageViewModel.orgId)/usage")!))

        // Status item
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem?.button {
            setMenubarIcon(button: button)
            button.action = #selector(handleClick)
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            button.target = self
        }

        // Popover
        popover = NSPopover()
        popover?.contentSize = NSSize(width: 480, height: 640)
        popover?.behavior = .transient
        popover?.animates = true
        popover?.contentViewController = NSHostingController(
            rootView: ContentView()
                .environmentObject(usageViewModel)
                .environmentObject(updateManager)
        )

        // React to limits or menubar toggle changes
        usageViewModel.$limits
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateStatusItem() }
            .store(in: &cancellables)

        usageViewModel.$showInMenuBar
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateStatusItem() }
            .store(in: &cancellables)

        // Refresh timer
        scheduleTimer(interval: usageViewModel.refreshInterval)

        NotificationCenter.default.addObserver(forName: .showPopover, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor [weak self] in self?.showPopover() }
        }

        NotificationCenter.default.addObserver(forName: .updateDetected, object: nil, queue: .main) { [weak self] note in
            Task { @MainActor [weak self] in
                guard let self else { return }
                let version = note.object as? String ?? ""
                let notes = self.updateManager.releaseNotes
                UpdateNotificationWindowController.showUpdate(
                    version: version,
                    notes: notes,
                    near: self.statusItem
                )
            }
        }

        NotificationCenter.default.addObserver(forName: .limitWarning, object: nil, queue: .main) { [weak self] note in
            Task { @MainActor [weak self] in
                guard let self, let info = note.userInfo,
                      let windowName = info["windowName"] as? String,
                      let percent = info["percent"] as? Int else { return }
                UpdateNotificationWindowController.showLimitWarning(windowName: windowName, percent: percent, near: self.statusItem)
            }
        }

        NotificationCenter.default.addObserver(forName: .limitReset, object: nil, queue: .main) { [weak self] note in
            Task { @MainActor [weak self] in
                guard let self, let windowName = note.userInfo?["windowName"] as? String else { return }
                UpdateNotificationWindowController.showLimitReset(windowName: windowName, near: self.statusItem)
            }
        }

        NotificationCenter.default.addObserver(forName: .closePopover, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.popover?.performClose(nil)
            }
        }

        NotificationCenter.default.addObserver(forName: .refreshIntervalChanged, object: nil, queue: .main) { [weak self] note in
            if let interval = note.object as? TimeInterval {
                Task { @MainActor [weak self] in
                    self?.scheduleTimer(interval: interval)
                }
            }
        }

        // Auto-show compact notification if relaunched after an update
        let flagFile = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".claudechecker_just_updated")
        if FileManager.default.fileExists(atPath: flagFile.path) {
            try? FileManager.default.removeItem(at: flagFile)
            updateManager.justUpdated = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
                let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
                UpdateNotificationWindowController.show(
                    version: version,
                    notes: "ClaudeChecker is now up to date.",
                    near: self?.statusItem
                )
            }
        }

        // Initial fetch after cookie primer + update check
        Task {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            await usageViewModel.refresh()
            await updateManager.checkForUpdates()
        }
    }

    private func setMenubarIcon(button: NSStatusBarButton) {
        // Use template image so macOS auto-adapts to dark/light menu bar
        if let img = NSImage(named: "MenubarTemplate") {
            img.isTemplate = true
            img.size = NSSize(width: 18, height: 18)
            button.image = img
            button.imageScaling = .scaleProportionallyDown
        }
    }

    func scheduleTimer(interval: TimeInterval) {
        refreshTimer?.invalidate()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            Task {
                await self?.usageViewModel.refresh()
                await self?.updateManager.checkForUpdates()
            }
        }
    }

    func updateStatusItem() {
        guard let button = statusItem?.button else { return }
        let limits = usageViewModel.limits
        let show = usageViewModel.showInMenuBar && limits.first?.isLive == true

        if show {
            let fh = limits.first(where: { $0.window == .fiveHour })
            let sd = limits.first(where: { $0.window == .sevenDay })
            let fhStr = fh.map { "\(Int($0.usedPercent.rounded()))%" } ?? "—"
            let sdStr = sd.map { "\(Int($0.usedPercent.rounded()))%" } ?? "—"
            button.image = nil
            button.attributedTitle = NSAttributedString(string: "")
            button.title = "◔ \(fhStr)  \(sdStr)"
            button.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        } else {
            button.title = ""
            button.attributedTitle = NSAttributedString(string: "")
            button.font = NSFont.systemFont(ofSize: 12)
            setMenubarIcon(button: button)
        }
    }

    @objc func handleClick() {
        guard let event = NSApp.currentEvent else { return }

        if event.type == .rightMouseUp {
            let menu = NSMenu()
            let showItem = NSMenuItem(title: "Show ClaudeChecker", action: #selector(showPopover), keyEquivalent: "")
            showItem.target = self
            menu.addItem(showItem)
            menu.addItem(.separator())
            let quitItem = NSMenuItem(title: "Quit", action: #selector(quitApp), keyEquivalent: "q")
            quitItem.target = self
            menu.addItem(quitItem)
            statusItem?.menu = menu
            statusItem?.button?.performClick(nil)
            DispatchQueue.main.async { self.statusItem?.menu = nil }
        } else {
            togglePopover()
        }
    }

    @objc func showPopover() {
        guard let button = statusItem?.button, let popover else { return }
        if !popover.isShown {
            NotificationCenter.default.post(name: .popoverWillOpen, object: nil)
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    func togglePopover() {
        guard let button = statusItem?.button, let popover else { return }
        if popover.isShown {
            popover.performClose(nil)
        } else {
            NotificationCenter.default.post(name: .popoverWillOpen, object: nil)
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    @objc func quitApp() {
        NSApp.terminate(nil)
    }

    func applicationWillTerminate(_ notification: Notification) {
        refreshTimer?.invalidate()
    }

}
