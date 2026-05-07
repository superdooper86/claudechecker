import Foundation

// MARK: - Models

struct AgentLimit: Identifiable {
    let id = UUID()
    var agent: AgentType
    var window: WindowType
    var usedPercent: Double
    var timeRemaining: String
    var resetDate: Date
    var projectedHit: ProjectedHit
    var burnRate: Double
    var burnHistory: [Double]
    var isLive: Bool

    var usageLabel: String {
        switch usedPercent {
        case ..<30: return "low"
        case 30..<70: return "med"
        default: return "high"
        }
    }
}

enum AgentType: String {
    case claude = "Claude"
}

enum WindowType: String {
    case fiveHour = "5h"
    case sevenDay = "7d"
    var label: String { rawValue }
    var displayName: String {
        switch self {
        case .fiveHour: return "5-hour"
        case .sevenDay: return "7-day"
        }
    }
}

enum ProjectedHit {
    case afterReset
    case at(Date)

    var display: String {
        switch self {
        case .afterReset: return "after reset"
        case .at(let date):
            let fmt = DateFormatter()
            let cal = Calendar.current
            if cal.isDateInToday(date) { fmt.dateFormat = "'today' h:mm a" }
            else if cal.isDateInTomorrow(date) { fmt.dateFormat = "'tmw' h:mm a" }
            else { fmt.dateFormat = "MMM d, h:mm a" }
            return fmt.string(from: date)
        }
    }
}

// MARK: - API Response — /api/organizations/{id}/usage

struct UsageResponse: Decodable {
    let fiveHour: UsageWindow?
    let sevenDay: UsageWindow?
    let extraUsage: ExtraUsage?

    enum CodingKeys: String, CodingKey {
        case fiveHour = "five_hour"
        case sevenDay = "seven_day"
        case extraUsage = "extra_usage"
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        fiveHour   = try c.decodeIfPresent(UsageWindow.self, forKey: .fiveHour)
        sevenDay   = try c.decodeIfPresent(UsageWindow.self, forKey: .sevenDay)
        extraUsage = try c.decodeIfPresent(ExtraUsage.self,  forKey: .extraUsage)
    }
}

struct UsageWindow: Codable {
    let utilization: Double
    let resetsAt: String?
    enum CodingKeys: String, CodingKey {
        case utilization
        case resetsAt = "resets_at"
    }
}

struct ExtraUsage: Codable {
    let isEnabled: Bool
    let currency: String?
    enum CodingKeys: String, CodingKey {
        case isEnabled = "is_enabled"
        case currency
    }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        isEnabled = (try? c.decodeIfPresent(Bool.self,  forKey: .isEnabled)) ?? false
        currency  = try? c.decodeIfPresent(String.self, forKey: .currency)
    }
}

struct PrepaidCredits: Decodable {
    let amount: Double?
    let currency: String?
    enum CodingKeys: String, CodingKey { case amount, currency }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        amount   = try? c.decodeIfPresent(Double.self, forKey: .amount)
        currency = try? c.decodeIfPresent(String.self, forKey: .currency)
    }
}

struct OverageSpendLimit: Decodable {
    let isEnabled: Bool
    let usedCredits: Double?
    let monthlyCreditLimit: Double?
    let currency: String?
    enum CodingKeys: String, CodingKey {
        case isEnabled = "is_enabled"
        case usedCredits = "used_credits"
        case monthlyCreditLimit = "monthly_credit_limit"
        case currency
    }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        isEnabled          = (try? c.decodeIfPresent(Bool.self,   forKey: .isEnabled)) ?? false
        usedCredits        = try? c.decodeIfPresent(Double.self,  forKey: .usedCredits)
        monthlyCreditLimit = try? c.decodeIfPresent(Double.self,  forKey: .monthlyCreditLimit)
        currency           = try? c.decodeIfPresent(String.self,  forKey: .currency)
    }
}

// (Bootstrap models removed — org ID is configured directly)
