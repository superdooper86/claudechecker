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

    private var cachedOrgId: String?
    private var burnHistoryStore: [String: [Double]] = [:]
    private let maxHistorySamples = 24
    private var previousPercents: [String: Double] = [:]
    private var firedThresholds: [String: Set<Int>] = [:]

    init() {
        cachedOrgId = UserDefaults.standard.string(forKey: "claude_org_id")
        let saved = UserDefaults.standard.double(forKey: "refresh_interval")
        refreshInterval = saved > 0 ? saved : 60
        showInMenuBar = UserDefaults.standard.object(forKey: "show_in_menubar") as? Bool ?? true
        if let saved = UserDefaults.standard.object(forKey: "burn_history") as? [String: [Double]] {
            burnHistoryStore = saved
        }
        loadPlaceholderData()
    }

    func signOut() async {
        let store = WKWebsiteDataStore.default()
        let types = WKWebsiteDataStore.allWebsiteDataTypes()
        let records = await store.dataRecords(ofTypes: types)
        let claudeRecords = records.filter { $0.displayName.contains("claude.ai") }
        await store.removeData(ofTypes: types, for: claudeRecords)
        cachedOrgId = nil
        UserDefaults.standard.removeObject(forKey: "claude_org_id")
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
            // Fetch org ID dynamically if not cached
            if cachedOrgId == nil {
                let (fetchedOrgId, fetchedPlan) = try await fetchOrgId()
                cachedOrgId = fetchedOrgId
                if let id = cachedOrgId {
                    UserDefaults.standard.set(id, forKey: "claude_org_id")
                }
                if let plan = fetchedPlan { planLabel = plan }
            }
            guard let orgId = cachedOrgId else {
                throw AppError.notAuthenticated
            }

            async let usageFetch   = fetchUsage(orgId: orgId)
            async let prepaidFetch = fetchPrepaidCredits(orgId: orgId)
            async let overageFetch = fetchOverageSpendLimit(orgId: orgId)
            async let emailFetch   = fetchUserEmail()
            let (usage, prepaid, overage, emailResult) = try await (usageFetch, prepaidFetch, overageFetch, emailFetch)
            if let email = emailResult.email { userEmail = email }
            if let plan = emailResult.planLabel { planLabel = plan }
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

    // MARK: - Fetch org ID from bootstrap

    private func fetchOrgId() async throws -> (orgId: String?, planLabel: String?) {
        let url = URL(string: "https://claude.ai/api/bootstrap")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter { $0.domain.contains("claude.ai") }
        if claudeCookies.isEmpty { throw AppError.notAuthenticated }
        if let header = HTTPCookie.requestHeaderFields(with: claudeCookies)["Cookie"] {
            req.setValue(header, forHTTPHeaderField: "Cookie")
        }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw AppError.networkError }
        if http.statusCode == 401 || http.statusCode == 403 { throw AppError.notAuthenticated }
        guard http.statusCode == 200 else { throw AppError.networkError }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return (nil, nil) }

        func planFromOrg(_ org: [String: Any]?) -> String? {
            guard let caps = org?["capabilities"] as? [String] else { return nil }
            guard let cap = caps.first(where: { $0.hasPrefix("claude_") }) else { return nil }
            let name = String(cap.dropFirst("claude_".count))
            return name.prefix(1).uppercased() + name.dropFirst().lowercased()
        }

        // account.memberships[0].organization.uuid
        if let account = json["account"] as? [String: Any],
           let memberships = account["memberships"] as? [[String: Any]],
           let org = memberships.first?["organization"] as? [String: Any],
           let uuid = org["uuid"] as? String {
            return (uuid, planFromOrg(org))
        }
        // root memberships (older API shape)
        if let memberships = json["memberships"] as? [[String: Any]],
           let org = memberships.first?["organization"] as? [String: Any],
           let uuid = org["uuid"] as? String {
            return (uuid, planFromOrg(org))
        }
        // root organizations array
        if let orgs = json["organizations"] as? [[String: Any]],
           let uuid = orgs.first?["uuid"] as? String {
            return (uuid, planFromOrg(orgs.first))
        }
        return (nil, nil)
    }

    // MARK: - Fetch usage

    private func claudeCookieHeader() async -> String? {
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter { $0.domain.contains("claude.ai") }
        return HTTPCookie.requestHeaderFields(with: claudeCookies)["Cookie"]
    }

    private func fetchUsage(orgId: String) async throws -> UsageResponse {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/usage")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        if let cookie = await claudeCookieHeader() { req.setValue(cookie, forHTTPHeaderField: "Cookie") }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw AppError.networkError }
        if http.statusCode == 401 || http.statusCode == 403 { throw AppError.notAuthenticated }
        guard http.statusCode == 200 else { throw AppError.networkError }
        return try JSONDecoder().decode(UsageResponse.self, from: data)
    }

    private func fetchUserEmail() async throws -> (email: String?, planLabel: String?) {
        let url = URL(string: "https://claude.ai/api/bootstrap")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        if let cookie = await claudeCookieHeader() { req.setValue(cookie, forHTTPHeaderField: "Cookie") }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return (nil, nil) }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let account = json["account"] as? [String: Any] else { return (nil, nil) }
        let email = account["email_address"] as? String
        var planLabel: String? = nil
        if let memberships = account["memberships"] as? [[String: Any]],
           let org = memberships.first?["organization"] as? [String: Any],
           let caps = org["capabilities"] as? [String],
           let cap = caps.first(where: { $0.hasPrefix("claude_") }) {
            let name = String(cap.dropFirst("claude_".count))
            planLabel = name.prefix(1).uppercased() + name.dropFirst().lowercased()
        }
        return (email, planLabel)
    }

    private func fetchPrepaidCredits(orgId: String) async throws -> PrepaidCredits? {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/prepaid/credits")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        if let cookie = await claudeCookieHeader() { req.setValue(cookie, forHTTPHeaderField: "Cookie") }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return nil }
        return try? JSONDecoder().decode(PrepaidCredits.self, from: data)
    }

    private func fetchOverageSpendLimit(orgId: String) async throws -> OverageSpendLimit? {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/overage_spend_limit")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        if let cookie = await claudeCookieHeader() { req.setValue(cookie, forHTTPHeaderField: "Cookie") }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return nil }
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
    var errorDescription: String? {
        switch self {
        case .notAuthenticated: return "Not signed into claude.ai — open claude.ai in your browser first."
        case .networkError:     return "Network error fetching usage data."
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
