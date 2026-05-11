import SwiftUI
import WebKit

@MainActor
class UsageViewModel: ObservableObject {
    @Published var limits: [AgentLimit] = []
    @Published var isLoading = false
    @Published var lastUpdated: Date?
    @Published var errorMessage: String?
    @Published var extraUsage: ExtraUsage?
    @Published var prepaidCredits: PrepaidCredits?
    @Published var overageSpendLimit: OverageSpendLimit?
    @Published var planLabel: String = "Claude"
    @Published var isNotAuthenticated: Bool = false
    @Published var isSignedIn: Bool = false
    @Published var userEmail: String = ""
    @Published var triggerLogin: Bool = false
    @Published var showInMenuBar: Bool = true {
        didSet { UserDefaults.standard.set(showInMenuBar, forKey: "show_in_menubar") }
    }
    @Published var refreshInterval: TimeInterval = 60 {
        didSet {
            UserDefaults.standard.set(refreshInterval, forKey: "refresh_interval")
            NotificationCenter.default.post(name: .refreshIntervalChanged, object: refreshInterval)
        }
    }

    private var burnHistoryStore: [String: [Double]] = [:]
    private let maxHistorySamples = 24
    private var previousPercents: [String: Double] = [:]
    private var firedThresholds: [String: Set<Int>] = [:]

    // Background WKWebView for API calls via callAsyncJavaScript.
    // Hosted in a hidden NSWindow — without a window WebKit suspends the WebView
    // and JS execution stops working, breaking callAsyncJavaScript.
    private var apiWebView: WKWebView?
    private var apiDelegate: APIWebViewDelegate?
    private var apiWindow: NSWindow?
    private var apiWebViewLoaded = false
    private var apiReadyContinuations: [CheckedContinuation<Void, Never>] = []

    init() {
        let saved = UserDefaults.standard.double(forKey: "refresh_interval")
        refreshInterval = saved > 0 ? saved : 60
        showInMenuBar = UserDefaults.standard.object(forKey: "show_in_menubar") as? Bool ?? true
        if let saved = UserDefaults.standard.object(forKey: "burn_history") as? [String: [Double]] {
            burnHistoryStore = saved
        }
        loadPlaceholderData()
        setupAPIWebView()
        Task { await checkInitialSignInState() }
    }

    // MARK: - Background API WebView

    private func setupAPIWebView() {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default()
        let wv = WKWebView(frame: NSRect(x: 0, y: 0, width: 1, height: 1), configuration: config)
        let del = APIWebViewDelegate()
        del.onNavigationEnd = { [weak self] in
            guard let self else { return }
            self.apiWebViewLoaded = true
            self.resumeAPIReadyContinuations()
        }
        wv.navigationDelegate = del
        apiWebView = wv
        apiDelegate = del

        // A WKWebView with no window is suspended by macOS — JS execution won't run.
        // Hosting it in a 1×1 transparent window keeps WebKit's process alive.
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1, height: 1),
            styleMask: .borderless,
            backing: .buffered,
            defer: false)
        window.alphaValue = 0.0
        window.ignoresMouseEvents = true
        window.isReleasedWhenClosed = false
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        window.contentView?.addSubview(wv)
        window.orderFrontRegardless()
        apiWindow = window

        wv.load(URLRequest(url: URL(string: "https://claude.ai")!))
    }

    private func resumeAPIReadyContinuations() {
        let pending = apiReadyContinuations
        apiReadyContinuations.removeAll()
        pending.forEach { $0.resume() }
    }

    private func waitForAPIWebViewReady() async {
        guard !apiWebViewLoaded else { return }
        await withCheckedContinuation { cont in
            apiReadyContinuations.append(cont)
        }
    }

    // Called after login: adopts the proven-authenticated login WebView for all API calls.
    // This avoids the background WebView potentially missing auth tokens that only exist
    // in the login WebView's JS context (localStorage, Service Workers, etc.).
    func adoptAndRefresh(_ loginWebView: WKWebView) async {
        loginWebView.removeFromSuperview()
        loginWebView.frame = NSRect(x: 0, y: 0, width: 1, height: 1)
        apiWindow?.contentView?.addSubview(loginWebView)
        apiWebView = loginWebView
        apiWebViewLoaded = true
        resumeAPIReadyContinuations()
        try? await Task.sleep(nanoseconds: 400_000_000)
        await refresh()
    }

    // Executes a same-origin fetch inside the background WebView's page context.
    // This includes all credentials the page has (cookies, localStorage tokens, etc.),
    // which URLSession cannot access — hence using callAsyncJavaScript instead.
    private func webViewFetch(_ path: String) async throws -> (statusCode: Int, body: String) {
        guard let wv = apiWebView else { throw AppError.detail("no webview") }
        await waitForAPIWebViewReady()
        let js = "const r = await fetch(path, {credentials:'include'}); return {s: r.status, b: await r.text()};"
        do {
            let result = try await wv.callAsyncJavaScript(js, arguments: ["path": path], in: nil, in: .page)
            guard let d = result as? [String: Any],
                  let s = (d["s"] as? NSNumber)?.intValue,
                  let b = d["b"] as? String else {
                throw AppError.detail("bad result: \(String(describing: result).prefix(120))")
            }
            return (s, b)
        } catch let e as AppError { throw e }
        catch {
            let loc = wv.url?.absoluteString ?? "?"
            throw AppError.detail("JS err @ \(loc): \(error.localizedDescription.prefix(100))")
        }
    }

    private func checkInitialSignInState() async {
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        if !cookies.isEmpty { isSignedIn = true }
    }

    func signOut() async {
        let store = WKWebsiteDataStore.default()
        let types = WKWebsiteDataStore.allWebsiteDataTypes()
        let records = await store.dataRecords(ofTypes: types)
        let claudeRecords = records.filter { $0.displayName.contains("claude.ai") || $0.displayName.contains("anthropic.com") }
        await store.removeData(ofTypes: types, for: claudeRecords)
        UserDefaults.standard.removeObject(forKey: "claude_org_id")
        isSignedIn = false
        isNotAuthenticated = true
        lastUpdated = nil
        userEmail = ""
        limits = []
        extraUsage = nil
        prepaidCredits = nil
        overageSpendLimit = nil
        apiWebViewLoaded = false
        apiWebView?.load(URLRequest(url: URL(string: "https://claude.ai")!))
    }

    func refresh() async {
        isLoading = true
        errorMessage = nil
        isNotAuthenticated = false
        defer { isLoading = false }

        do {
            let (fetchedOrgId, fetchedEmail, fetchedPlan) = try await fetchBootstrap()

            let orgId: String
            if let id = fetchedOrgId {
                UserDefaults.standard.set(id, forKey: "claude_org_id")
                orgId = id
            } else if let cached = UserDefaults.standard.string(forKey: "claude_org_id") {
                orgId = cached
            } else {
                throw AppError.notAuthenticated
            }

            if let email = fetchedEmail { userEmail = email }
            if let plan = fetchedPlan   { planLabel = plan }

            async let usageFetch   = fetchUsage(orgId: orgId)
            async let prepaidFetch = fetchPrepaidCredits(orgId: orgId)
            async let overageFetch = fetchOverageSpendLimit(orgId: orgId)
            let (usage, prepaid, overage) = try await (usageFetch, prepaidFetch, overageFetch)
            limits = buildLimits(from: usage)
            extraUsage = usage.extraUsage
            prepaidCredits = prepaid
            overageSpendLimit = overage
            lastUpdated = Date()
            isSignedIn = true

            for i in limits.indices {
                let key = limits[i].window.rawValue
                var history = burnHistoryStore[key] ?? []
                history.append(limits[i].usedPercent)
                if history.count > maxHistorySamples { history.removeFirst() }
                burnHistoryStore[key] = history
                limits[i].burnHistory = history
            }
            UserDefaults.standard.set(burnHistoryStore, forKey: "burn_history")
            checkLimitNotifications(for: limits)
        } catch AppError.notAuthenticated {
            isNotAuthenticated = true
            isSignedIn = false
            errorMessage = "Not signed in"
        } catch let error as DecodingError {
            switch error {
            case .keyNotFound(let key, let ctx):
                errorMessage = "Missing key '\(key.stringValue)' at \(ctx.codingPath.map(\.stringValue).joined(separator: "."))"
            case .typeMismatch(let type, let ctx):
                errorMessage = "Type mismatch (\(type)) at \(ctx.codingPath.map(\.stringValue).joined(separator: "."))"
            case .valueNotFound(let type, let ctx):
                errorMessage = "Value not found (\(type)) at \(ctx.codingPath.map(\.stringValue).joined(separator: "."))"
            default:
                errorMessage = error.localizedDescription
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Bootstrap

    private func fetchBootstrap() async throws -> (orgId: String?, email: String?, planLabel: String?) {
        let (status, body) = try await webViewFetch("/api/bootstrap")
        if status == 401 || status == 403 { throw AppError.notAuthenticated }
        guard status == 200 else { throw AppError.networkError }
        guard let data = body.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return (nil, nil, nil)
        }

        let account  = json["account"] as? [String: Any]
        let memberships = (account?["memberships"] ?? json["memberships"]) as? [[String: Any]]
        let firstOrg = memberships?.first?["organization"] as? [String: Any]

        var orgId: String? = firstOrg?["uuid"] as? String
        if orgId == nil {
            orgId = (json["organizations"] as? [[String: Any]])?.first?["uuid"] as? String
        }
        if orgId == nil {
            if let (orgsStatus, orgsBody) = try? await webViewFetch("/api/organizations"),
               orgsStatus == 200,
               let orgsData = orgsBody.data(using: .utf8),
               let orgs = try? JSONSerialization.jsonObject(with: orgsData) as? [[String: Any]] {
                orgId = orgs.first?["uuid"] as? String
            }
        }

        let email = account?["email_address"] as? String

        var planLabel: String? = nil
        if let caps = firstOrg?["capabilities"] as? [String],
           let cap = caps.first(where: { $0.hasPrefix("claude_") }) {
            let name = String(cap.dropFirst("claude_".count))
            planLabel = name.prefix(1).uppercased() + name.dropFirst().lowercased()
        }

        return (orgId, email, planLabel)
    }

    // MARK: - Fetch usage

    private func fetchUsage(orgId: String) async throws -> UsageResponse {
        let (status, body) = try await webViewFetch("/api/organizations/\(orgId)/usage")
        if status == 401 || status == 403 { throw AppError.notAuthenticated }
        guard status == 200 else { throw AppError.networkError }
        guard let data = body.data(using: .utf8) else { throw AppError.networkError }
        return try JSONDecoder().decode(UsageResponse.self, from: data)
    }

    private func fetchPrepaidCredits(orgId: String) async throws -> PrepaidCredits? {
        guard let (status, body) = try? await webViewFetch("/api/organizations/\(orgId)/prepaid/credits"),
              status == 200,
              let data = body.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(PrepaidCredits.self, from: data)
    }

    private func fetchOverageSpendLimit(orgId: String) async throws -> OverageSpendLimit? {
        guard let (status, body) = try? await webViewFetch("/api/organizations/\(orgId)/overage_spend_limit"),
              status == 200,
              let data = body.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(OverageSpendLimit.self, from: data)
    }

    // MARK: - Build limits

    private func buildLimits(from usage: UsageResponse) -> [AgentLimit] {
        let now = Date()
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]

        func parseDate(_ str: String?) -> Date {
            guard let str else { return now.addingTimeInterval(3600) }
            return iso.date(from: str) ?? now.addingTimeInterval(3600)
        }

        func timeLeft(until reset: Date) -> String {
            let diff = max(0, reset.timeIntervalSince(now))
            if diff == 0 { return "resetting..." }
            let h = Int(diff / 3600)
            let m = Int((diff.truncatingRemainder(dividingBy: 3600)) / 60)
            if h >= 24 { return "\(h/24)d \(h%24)h" }
            return "\(h)h \(m)m"
        }

        func projected(pct: Double, windowHours: Double, reset: Date) -> ProjectedHit {
            guard pct > 0 else { return .afterReset }
            let rate = pct / windowHours
            guard rate > 0 else { return .afterReset }
            let hit = now.addingTimeInterval(((100 - pct) / rate) * 3600)
            return hit > reset ? .afterReset : .at(hit)
        }

        var result: [AgentLimit] = []

        if let fh = usage.fiveHour {
            let reset = parseDate(fh.resetsAt)
            let pct = min(100, max(0, fh.utilization))
            result.append(AgentLimit(
                agent: .claude, window: .fiveHour,
                usedPercent: pct,
                timeRemaining: timeLeft(until: reset),
                resetDate: reset,
                projectedHit: projected(pct: pct, windowHours: 5, reset: reset),
                burnRate: pct / 5.0,
                burnHistory: burnHistoryStore["5h"] ?? [pct],
                isLive: true
            ))
        }

        if let sd = usage.sevenDay {
            let reset = parseDate(sd.resetsAt)
            let pct = min(100, max(0, sd.utilization))
            result.append(AgentLimit(
                agent: .claude, window: .sevenDay,
                usedPercent: pct,
                timeRemaining: timeLeft(until: reset),
                resetDate: reset,
                projectedHit: projected(pct: pct, windowHours: 7*24, reset: reset),
                burnRate: pct / (7*24.0),
                burnHistory: burnHistoryStore["7d"] ?? [pct],
                isLive: true
            ))
        }

        return result
    }

    // MARK: - Limit notifications

    private func checkLimitNotifications(for limits: [AgentLimit]) {
        let thresholds = [80, 95]
        for limit in limits {
            let key = limit.window.rawValue
            let curr = limit.usedPercent
            let prev = previousPercents[key]
            var fired = firedThresholds[key] ?? []
            for t in thresholds {
                let threshold = Double(t)
                guard curr >= threshold, prev ?? threshold < threshold, !fired.contains(t) else { continue }
                fired.insert(t)
                NotificationCenter.default.post(
                    name: .limitWarning,
                    object: nil,
                    userInfo: ["windowName": limit.window.displayName, "percent": t]
                )
            }
            if let prev, prev > 50, curr < 20 {
                fired.removeAll()
                NotificationCenter.default.post(
                    name: .limitReset,
                    object: nil,
                    userInfo: ["windowName": limit.window.displayName]
                )
            }
            firedThresholds[key] = fired
            previousPercents[key] = curr
        }
    }

    private func loadPlaceholderData() {
        let now = Date()
        limits = [
            AgentLimit(agent: .claude, window: .fiveHour,
                usedPercent: 0, timeRemaining: "—",
                resetDate: now.addingTimeInterval(3600),
                projectedHit: .afterReset, burnRate: 0,
                burnHistory: [], isLive: false),
            AgentLimit(agent: .claude, window: .sevenDay,
                usedPercent: 0, timeRemaining: "—",
                resetDate: now.addingTimeInterval(7*24*3600),
                projectedHit: .afterReset, burnRate: 0,
                burnHistory: [], isLive: false),
        ]
    }
}

// MARK: - API WebView Delegate

private class APIWebViewDelegate: NSObject, WKNavigationDelegate {
    var onNavigationEnd: (() -> Void)?

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        onNavigationEnd?()
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        onNavigationEnd?()
    }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        onNavigationEnd?()
    }
}

enum AppError: LocalizedError {
    case notAuthenticated
    case networkError
    case detail(String)
    var errorDescription: String? {
        switch self {
        case .notAuthenticated:   return "Not signed into claude.ai — open claude.ai in your browser first."
        case .networkError:       return "Network error fetching usage data."
        case .detail(let msg):    return msg
        }
    }
}

extension Notification.Name {
    static let refreshIntervalChanged = Notification.Name("refreshIntervalChanged")
    static let closePopover = Notification.Name("closePopover")
    static let showPopover = Notification.Name("showPopover")
    static let popoverWillOpen = Notification.Name("popoverWillOpen")
    static let updateDetected = Notification.Name("updateDetected")
    static let limitWarning = Notification.Name("limitWarning")
    static let limitReset = Notification.Name("limitReset")
    static let openUpdateSheet = Notification.Name("openUpdateSheet")
}
