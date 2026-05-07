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
    let monthlyLimit: Double?
    let usedCredits: Double?
    let utilization: Double?
    let currency: String?
    enum CodingKeys: String, CodingKey {
        case isEnabled = "is_enabled"
        case monthlyLimit = "monthly_limit"
        case usedCredits = "used_credits"
        case utilization
        case currency
    }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        isEnabled    = (try? c.decodeIfPresent(Bool.self,   forKey: .isEnabled)) ?? false
        monthlyLimit = try? c.decodeIfPresent(Double.self,  forKey: .monthlyLimit)
        usedCredits  = try? c.decodeIfPresent(Double.self,  forKey: .usedCredits)
        utilization  = try? c.decodeIfPresent(Double.self,  forKey: .utilization)
        currency     = try? c.decodeIfPresent(String.self,  forKey: .currency)
    }
}

// (Bootstrap models removed — org ID is configured directly)
