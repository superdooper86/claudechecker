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

    // WebView used for JS-based API calls (avoids URLSession 403 fingerprinting issues)
    private var apiWebView: WKWebView?

    // Diagnostics
    @Published var diagCookieCount: Int = 0
    @Published var diagClaudeCookieCount: Int = 0
    @Published var diagCookieDomains: [String] = []
    @Published var diagLastPath: String = ""
    @Published var diagLastStatus: Int = 0
    @Published var diagLastError: String = ""
    @Published var diagLastActiveOrg: String = ""   // lastActiveOrg cookie value from JS
    @Published var diagLastFetch: Date? = nil

    init() {
        let saved = UserDefaults.standard.double(forKey: "refresh_interval")
        refreshInterval = saved > 0 ? saved : 60
        showInMenuBar = UserDefaults.standard.object(forKey: "show_in_menubar") as? Bool ?? true
        if let saved = UserDefaults.standard.object(forKey: "burn_history") as? [String: [Double]] {
            burnHistoryStore = saved
        }
        loadPlaceholderData()
        Task { await checkInitialSignInState() }
    }

    // Called by AppDelegate once the primer WebView has loaded claude.ai.
    // Sets it as the API WebView and triggers the first data fetch.
    func adoptPrimerWebView(_ wv: WKWebView) async {
        apiWebView = wv
        await refresh()
    }

    // Called after login: store the login WebView (already on claude.ai) so jsFetch can use it.
    func adoptAndRefresh(_ loginWebView: WKWebView) async {
        for _ in 0..<30 {
            let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
            if cookies.contains(where: { $0.domain.contains("claude.ai") }) { break }
            try? await Task.sleep(nanoseconds: 300_000_000)
        }
        apiWebView = loginWebView
        await refresh()
    }

    // Runs a fetch() inside the live claude.ai WebView — same browser context as the frontend,
    // so no CORS/fingerprinting issues that URLSession hits.
    private func jsFetch(_ path: String) async throws -> (Int, String) {
        guard let wv = apiWebView else { throw AppError.networkError }
        let js = """
        const r = await fetch('\(path)', {
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
        });
        const body = await r.text();
        return {status: r.status, body: body};
        """
        let result: Any? = try await withCheckedThrowingContinuation { cont in
            wv.callAsyncJavaScript(js, arguments: [:], in: nil, in: .defaultClient) { res in
                switch res {
                case .success(let val): cont.resume(returning: val)
                case .failure(let err): cont.resume(throwing: err)
                }
            }
        }
        guard let dict = result as? [String: Any],
              let status = dict["status"] as? Int,
              let body = dict["body"] as? String else {
            throw AppError.networkError
        }
        diagLastPath = path
        diagLastFetch = Date()
        diagLastStatus = status
        diagLastError = status != 200 ? "JS HTTP \(status)" : ""
        return (status, body)
    }

    // Use JS fetch when apiWebView is ready on claude.ai, otherwise fall back to URLSession.
    private func apiFetch(_ path: String) async throws -> (Int, String) {
        if let wv = apiWebView, wv.url?.host?.hasSuffix("claude.ai") == true {
            return try await jsFetch(path)
        }
        return try await urlFetch(path)
    }

    // Fetches an API path via URLSession with browser-like headers.
    // Claude.ai's API requires sec-fetch-*, Origin, Referer, and a browser User-Agent
    // to avoid 401 — matching how the Windows version (HttpClient) handles this.
    private func urlFetch(_ path: String) async throws -> (Int, String) {
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter {
            $0.domain.contains("claude.ai") || $0.domain.contains("anthropic.com")
        }

        // Record cookie diagnostics
        diagCookieCount = cookies.count
        diagClaudeCookieCount = claudeCookies.count
        diagCookieDomains = Array(Set(cookies.map { $0.domain })).sorted()
        diagLastPath = path
        diagLastFetch = Date()
        diagLastError = ""

        guard !claudeCookies.isEmpty else {
            let msg = "No claude.ai cookies (\(cookies.count) total in store)"
            diagLastError = msg
            throw AppError.detail(msg)
        }

        var req = URLRequest(url: URL(string: "https://claude.ai\(path)")!)
        req.httpShouldHandleCookies = false
        // Build cookie header manually (same format as Windows HttpClient) to avoid
        // any encoding differences from HTTPCookie.requestHeaderFields(with:)
        let cookieHeader = claudeCookies.map { "\($0.name)=\($0.value)" }.joined(separator: "; ")
        req.setValue(cookieHeader, forHTTPHeaderField: "Cookie")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.setValue("https://claude.ai", forHTTPHeaderField: "Origin")
        req.setValue("https://claude.ai/", forHTTPHeaderField: "Referer")
        req.setValue("empty",       forHTTPHeaderField: "sec-fetch-dest")
        req.setValue("cors",        forHTTPHeaderField: "sec-fetch-mode")
        req.setValue("same-origin", forHTTPHeaderField: "sec-fetch-site")
        req.setValue(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            forHTTPHeaderField: "User-Agent")
        req.setValue(
            "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"",
            forHTTPHeaderField: "sec-ch-ua")
        req.setValue("?0",        forHTTPHeaderField: "sec-ch-ua-mobile")
        req.setValue("\"macOS\"", forHTTPHeaderField: "sec-ch-ua-platform")
        req.setValue("web",       forHTTPHeaderField: "anthropic-client-platform")

        let session = URLSession(configuration: .ephemeral)
        do {
            let (data, response) = try await session.data(for: req)
            guard let http = response as? HTTPURLResponse else { throw AppError.networkError }
            let body = String(data: data, encoding: .utf8) ?? ""
            diagLastStatus = http.statusCode
            if http.statusCode != 200 {
                diagLastError = "HTTP \(http.statusCode)"
            }
            return (http.statusCode, body)
        } catch let e as AppError { throw e }
        catch {
            let msg = error.localizedDescription
            diagLastError = msg
            throw AppError.detail(msg)
        }
    }

    private func checkInitialSignInState() async {
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        guard cookies.contains(where: { $0.domain.contains("claude.ai") }) else { return }
        isSignedIn = true
        // AppDelegate's primer WebView will call adoptPrimerWebView once it loads,
        // which triggers the first real data fetch.
    }

    func signOut() async {
        let store = WKWebsiteDataStore.default()
        let types = WKWebsiteDataStore.allWebsiteDataTypes()
        let records = await store.dataRecords(ofTypes: types)
        let claudeRecords = records.filter { $0.displayName.contains("claude.ai") || $0.displayName.contains("anthropic.com") }
        await store.removeData(ofTypes: types, for: claudeRecords)
        UserDefaults.standard.removeObject(forKey: "claude_org_id")
        apiWebView = nil
        isSignedIn = false
        isNotAuthenticated = true
        lastUpdated = nil
        userEmail = ""
        limits = []
        extraUsage = nil
        prepaidCredits = nil
        overageSpendLimit = nil
    }

    func refresh() async {
        isLoading = true
        errorMessage = nil
        isNotAuthenticated = false
        defer { isLoading = false }

        do {
            // Read lastActiveOrg cookie from JS — most reliable org source since it's
            // set by the claude.ai frontend to whichever org is currently active.
            var cookieOrgId: String? = nil
            if let wv = apiWebView, wv.url?.host?.hasSuffix("claude.ai") == true {
                let js = """
                return document.cookie.split(';')
                    .map(c => c.trim().split('='))
                    .filter(p => p[0] === 'lastActiveOrg')
                    .map(p => p.slice(1).join('='))[0] || null;
                """
                let raw: Any? = try? await withCheckedThrowingContinuation { cont in
                    wv.callAsyncJavaScript(js, arguments: [:], in: nil, in: .defaultClient) { r in
                        cont.resume(returning: (try? r.get()) ?? nil)
                    }
                }
                if let v = raw as? String, !v.isEmpty {
                    cookieOrgId = v
                    diagLastActiveOrg = v
                }
            }

            let (fetchedOrgId, fetchedEmail, fetchedPlan) = try await fetchBootstrap(preferredOrgId: cookieOrgId)

            let orgId: String
            if let id = cookieOrgId ?? fetchedOrgId {
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
            extraUsage        = usage.extraUsage
            prepaidCredits    = prepaid
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

    private func fetchBootstrap(preferredOrgId: String? = nil) async throws -> (orgId: String?, email: String?, planLabel: String?) {
        let (status, body) = try await apiFetch("/api/bootstrap")
        if status == 401 || status == 403 {
            throw AppError.detail("HTTP \(status) — \(body.prefix(120))")
        }
        guard status == 200 else { throw AppError.detail("HTTP \(status): \(body.prefix(80))") }
        guard body.trimmingCharacters(in: .whitespacesAndNewlines).hasPrefix("{") else {
            throw AppError.detail("Non-JSON response (HTML?): \(body.prefix(80))")
        }
        guard let data = body.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return (nil, nil, nil)
        }

        let account  = json["account"] as? [String: Any]
        let memberships = (account?["memberships"] ?? json["memberships"]) as? [[String: Any]]
        let allOrgs = memberships?.compactMap { $0["organization"] as? [String: Any] } ?? []
        let firstOrg = allOrgs.first

        var orgId: String? = firstOrg?["uuid"] as? String
        if orgId == nil {
            orgId = (json["organizations"] as? [[String: Any]])?.first?["uuid"] as? String
        }
        if orgId == nil {
            if let (orgsStatus, orgsBody) = try? await apiFetch("/api/organizations"),
               orgsStatus == 200,
               let orgsData = orgsBody.data(using: .utf8),
               let orgs = try? JSONSerialization.jsonObject(with: orgsData) as? [[String: Any]] {
                orgId = orgs.first?["uuid"] as? String
            }
        }

        let email = account?["email_address"] as? String

        // Read capabilities from the preferred (active) org, falling back to first org.
        let capOrg = preferredOrgId.flatMap { id in allOrgs.first(where: { $0["uuid"] as? String == id }) } ?? firstOrg
        var planLabel: String? = nil
        if let caps = capOrg?["capabilities"] as? [String],
           let cap = caps.first(where: { $0.hasPrefix("claude_") }) {
            let name = String(cap.dropFirst("claude_".count))
            planLabel = name.prefix(1).uppercased() + name.dropFirst().lowercased()
        }

        return (orgId, email, planLabel)
    }

    // MARK: - Fetch usage

    private func fetchUsage(orgId: String) async throws -> UsageResponse {
        let (status, body) = try await apiFetch("/api/organizations/\(orgId)/usage")
        if status == 401 || status == 403 { throw AppError.detail("usage \(status): \(body.prefix(200))") }
        guard status == 200 else { throw AppError.networkError }
        guard let data = body.data(using: .utf8) else { throw AppError.networkError }
        return try JSONDecoder().decode(UsageResponse.self, from: data)
    }

    private func fetchPrepaidCredits(orgId: String) async throws -> PrepaidCredits? {
        guard let (status, body) = try? await apiFetch("/api/organizations/\(orgId)/prepaid/credits"),
              status == 200,
              let data = body.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(PrepaidCredits.self, from: data)
    }

    private func fetchOverageSpendLimit(orgId: String) async throws -> OverageSpendLimit? {
        guard let (status, body) = try? await apiFetch("/api/organizations/\(orgId)/overage_spend_limit"),
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
