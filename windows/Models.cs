using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClaudeCheckerWindows;

public enum WindowKind { FiveHour, SevenDay }

public class AgentLimit
{
    public WindowKind Window { get; init; }
    public double UsedPercent { get; set; }
    public string TimeRemaining { get; set; } = "";
    public DateTime ResetDate { get; set; }
    public double BurnRate { get; set; }
    public List<double> BurnHistory { get; set; } = [];
    public bool IsLive { get; set; }
    public string UsageLabel => UsedPercent switch { < 33 => "low", < 66 => "med", _ => "high" };
    public string WindowLabel => Window == WindowKind.FiveHour ? "5 HOUR LIMIT" : "7 DAY LIMIT";
}

public class UsageResponse
{
    [JsonPropertyName("five_hour")]   public WindowData?  FiveHour   { get; set; }
    [JsonPropertyName("seven_day")]   public WindowData?  SevenDay   { get; set; }
    [JsonPropertyName("extra_usage")] public ExtraUsage?  ExtraUsage { get; set; }
}

public class WindowData
{
    [JsonPropertyName("utilization")] public double Utilization { get; set; }
    [JsonPropertyName("resets_at")]   public string? ResetsAt   { get; set; }
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")] public bool    IsEnabled { get; set; }
    [JsonPropertyName("currency")]   public string? Currency  { get; set; }
}

public class BootstrapResponse
{
    [JsonPropertyName("account")] public AccountInfo? Account { get; set; }
}

public class AccountInfo
{
    [JsonPropertyName("email_address")] public string? EmailAddress { get; set; }
}

public class OverageSpendLimit
{
    [JsonPropertyName("monthly_credit_limit")] public double? MonthlyCreditLimit { get; set; }
    [JsonPropertyName("used_credits")]         public double? UsedCredits         { get; set; }
    [JsonPropertyName("currency")]             public string? Currency            { get; set; }
}

public class PrepaidCredits
{
    [JsonPropertyName("amount")]   public double? Amount   { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
}

public class VersionInfo
{
    [JsonPropertyName("version")] public string  Version { get; set; } = "";
    [JsonPropertyName("url")]     public string  Url     { get; set; } = "";
    [JsonPropertyName("notes")]   public string? Notes   { get; set; }
}
