import SwiftUI
import WebKit

@MainActor
class UsageViewModel: ObservableObject {
    static let orgId = "daf626a9-4924-4ff3-ba98-23b523062f8e"
    @Published var limits: [AgentLimit] = []
    @Published var isLoading = false
    @Published var lastUpdated: Date?
    @Published var errorMessage: String?
    @Published var extraUsage: ExtraUsage?
    @Published var prepaidCredits: PrepaidCredits?
    @Published var overageSpendLimit: OverageSpendLimit?
    @Published var planLabel: String = "Claude"
    @Published var isNotAuthenticated: Bool = false
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
    private var cachedOrgId: String?

    init() {
        cachedOrgId = UserDefaults.standard.string(forKey: "claude_org_id") ?? Self.orgId
        let saved = UserDefaults.standard.double(forKey: "refresh_interval")
        refreshInterval = saved > 0 ? saved : 60
        showInMenuBar = UserDefaults.standard.object(forKey: "show_in_menubar") as? Bool ?? true
        loadPlaceholderData()
    }

    func refresh() async {
        isLoading = true
        errorMessage = nil
        isNotAuthenticated = false
        defer { isLoading = false }

        do {
            let orgId = cachedOrgId!
            async let usageFetch        = fetchUsage(orgId: orgId)
            async let prepaidFetch      = fetchPrepaidCredits(orgId: orgId)
            async let overageFetch      = fetchOverageSpendLimit(orgId: orgId)
            let (usage, prepaid, overage) = try await (usageFetch, prepaidFetch, overageFetch)
            limits = buildLimits(from: usage)
            extraUsage = usage.extraUsage
            prepaidCredits = prepaid
            overageSpendLimit = overage
            planLabel = "Pro"
            lastUpdated = Date()

            for i in limits.indices {
                let key = limits[i].window.rawValue
                var history = burnHistoryStore[key] ?? []
                history.append(limits[i].usedPercent)
                if history.count > maxHistorySamples { history.removeFirst() }
                burnHistoryStore[key] = history
                if history.count > 1 { limits[i].burnHistory = history }
            }
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

    // MARK: - Fetch usage

    private func fetchUsage(orgId: String) async throws -> UsageResponse {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/usage")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")

        // Inject cookies from WKWebsiteDataStore (shared with browser/Claude app)
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter { $0.domain.contains("claude.ai") }
        if let header = HTTPCookie.requestHeaderFields(with: claudeCookies)["Cookie"] {
            req.setValue(header, forHTTPHeaderField: "Cookie")
        }

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw AppError.networkError }
        if http.statusCode == 401 || http.statusCode == 403 { throw AppError.notAuthenticated }
        guard http.statusCode == 200 else { throw AppError.networkError }

        return try JSONDecoder().decode(UsageResponse.self, from: data)
    }

    private func fetchPrepaidCredits(orgId: String) async throws -> PrepaidCredits? {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/prepaid/credits")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter { $0.domain.contains("claude.ai") }
        if let header = HTTPCookie.requestHeaderFields(with: claudeCookies)["Cookie"] {
            req.setValue(header, forHTTPHeaderField: "Cookie")
        }
        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return nil }
        return try? JSONDecoder().decode(PrepaidCredits.self, from: data)
    }

    private func fetchOverageSpendLimit(orgId: String) async throws -> OverageSpendLimit? {
        let url = URL(string: "https://claude.ai/api/organizations/\(orgId)/overage_spend_limit")!
        var req = URLRequest(url: url)
        req.setValue("application/json", forHTTPHeaderField: "accept")
        let cookies = await WKWebsiteDataStore.default().httpCookieStore.allCookies()
        let claudeCookies = cookies.filter { $0.domain.contains("claude.ai") }
        if let header = HTTPCookie.requestHeaderFields(with: claudeCookies)["Cookie"] {
            req.setValue(header, forHTTPHeaderField: "Cookie")
        }
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
    static let updateDetected = Notification.Name("updateDetected")
}
