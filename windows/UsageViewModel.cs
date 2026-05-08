using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeCheckerWindows;

public class UsageViewModel : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private List<AgentLimit> _limits = [];
    private bool _isLoading;
    private string? _errorMessage;
    private bool _isSignedIn;
    private string _userEmail = "";
    private string _planLabel = "Claude";
    private DateTime? _lastUpdated;
    private OverageSpendLimit? _overage;
    private PrepaidCredits? _prepaid;
    private ExtraUsage? _extraUsage;

    private readonly Dictionary<string, List<double>> _burnHistory = [];
    private const int MaxHistorySamples = 24;

    public List<AgentLimit> Limits          { get => _limits;       set => Set(ref _limits, value); }
    public bool IsLoading                   { get => _isLoading;    set => Set(ref _isLoading, value); }
    public string? ErrorMessage             { get => _errorMessage; set => Set(ref _errorMessage, value); }
    public bool IsSignedIn                  { get => _isSignedIn;   set => Set(ref _isSignedIn, value); }
    public string UserEmail                 { get => _userEmail;    set => Set(ref _userEmail, value); }
    public string PlanLabel                 { get => _planLabel;    set => Set(ref _planLabel, value); }
    public DateTime? LastUpdated            { get => _lastUpdated;  set => Set(ref _lastUpdated, value); }
    public OverageSpendLimit? Overage       { get => _overage;      set => Set(ref _overage, value); }
    public PrepaidCredits? Prepaid          { get => _prepaid;      set => Set(ref _prepaid, value); }
    public ExtraUsage? ExtraUsage           { get => _extraUsage;   set => Set(ref _extraUsage, value); }

    private int _refreshInterval = 120;
    public int RefreshInterval
    {
        get => _refreshInterval;
        set
        {
            Set(ref _refreshInterval, value);
            AppSettings.Default.RefreshInterval = value;
            AppSettings.Default.Save();
        }
    }

    private bool _showInTaskbar = true;
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            Set(ref _showInTaskbar, value);
            AppSettings.Default.ShowInTaskbar = value;
            AppSettings.Default.Save();
        }
    }

    public UsageViewModel()
    {
        _refreshInterval = AppSettings.Default.RefreshInterval > 0
            ? AppSettings.Default.RefreshInterval : 120;
        _showInTaskbar = AppSettings.Default.ShowInTaskbar;
        LoadBurnHistory();
        LoadPlaceholders();
    }

    public async Task RefreshAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);

        try
        {
            var cookies = await GetCookiesAsync();

            // Check if we have any persistent proof that the user authenticated
            bool hasCookies    = cookies.Count > 0;
            bool hasCachedAuth = !string.IsNullOrEmpty(AppSettings.Default.Email) ||
                                 !string.IsNullOrEmpty(AppSettings.Default.OrgId);

            if (!hasCookies && !hasCachedAuth)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSignedIn   = false;
                    ErrorMessage = "Not signed in — click Sign In to authenticate.";
                    IsLoading    = false;
                });
                return;
            }

            // HttpClient refresh — WebView2 is NOT used here to avoid spawning a heavy
            // browser process every refresh cycle (causes memory accumulation).
            // SaveAndClose at login time is responsible for fetching live data via WebView2.
            List<AgentLimit> limits    = [];
            OverageSpendLimit? overage = null;
            PrepaidCredits?    prepaid = null;
            ExtraUsage?     extraUsage = null;
            string? email = null, orgId = null, planLabel = null;
            string? refreshError = null;

            if (hasCookies)
            {
                try
                {
                    (limits, overage, prepaid, extraUsage, email, orgId, planLabel, _) =
                        await TryHttpRefreshAsync(cookies);
                }
                catch (Exception ex)
                {
                    refreshError = ex.Message;
                }
            }

            // Fill in any blanks from cached values saved at login time
            if (string.IsNullOrEmpty(email))  email  = AppSettings.Default.Email;
            if (string.IsNullOrEmpty(orgId))  orgId  = AppSettings.Default.OrgId;
            if (limits.Count == 0 && !string.IsNullOrEmpty(AppSettings.Default.UsageJson))
            {
                try
                {
                    var cached = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(
                        AppSettings.Default.UsageJson, JsonOpts);
                    limits = BuildLimits(cached);
                }
                catch { }
            }

            foreach (var limit in limits)
            {
                var key = limit.Window.ToString();
                if (!_burnHistory.ContainsKey(key)) _burnHistory[key] = [];
                _burnHistory[key].Add(limit.UsedPercent);
                if (_burnHistory[key].Count > MaxHistorySamples)
                    _burnHistory[key].RemoveAt(0);
                limit.BurnHistory = [.. _burnHistory[key]];
            }
            SaveBurnHistory();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (limits.Count > 0)
                {
                    Limits    = limits;
                    Overage   = overage;
                    Prepaid   = prepaid;
                    // Only clear ExtraUsage if the API explicitly disables it; keep cached value
                    // if the HTTP endpoint omits the field (it often does for non-browser clients)
                    if (extraUsage != null) ExtraUsage = extraUsage;
                }
                if (!string.IsNullOrEmpty(planLabel)) PlanLabel = planLabel;
                if (!string.IsNullOrEmpty(email)) UserEmail = email;
                IsSignedIn   = true;
                ErrorMessage = refreshError;
                LastUpdated  = DateTime.Now;
                IsLoading    = false;
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ErrorMessage = ex.Message;
                IsLoading    = false;
            });
        }
    }

    private async Task<(List<AgentLimit>, OverageSpendLimit?, PrepaidCredits?, ExtraUsage?, string?, string?, string?, bool)>
        TryHttpRefreshAsync(List<(string Name, string Value, string Domain, string Path)> cookies)
    {
        try
        {
            using var http = BuildClient(cookies);
            var bootstrapResp = await http.GetAsync("https://claude.ai/api/bootstrap");
            if ((int)bootstrapResp.StatusCode is 401 or 403)
                throw new Exception("Session expired — please re-authenticate.");
            if (!bootstrapResp.IsSuccessStatusCode)
                throw new Exception($"Bootstrap failed ({(int)bootstrapResp.StatusCode}).");

            var bootstrapJson = await bootstrapResp.Content.ReadAsStringAsync();
            // Targeted debug: log org property names + capabilities value so we can see
            // exactly what the HttpClient bootstrap response contains
            try
            {
                using var dbgDoc = System.Text.Json.JsonDocument.Parse(bootstrapJson);
                var dbgRoot = dbgDoc.RootElement;
                var dbg = $"len:{bootstrapJson.Length}";
                if (dbgRoot.TryGetProperty("account", out var dbgAcct) &&
                    dbgAcct.TryGetProperty("memberships", out var dbgMems) &&
                    dbgMems.GetArrayLength() > 0 &&
                    dbgMems[0].TryGetProperty("organization", out var dbgOrg))
                {
                    var keys = string.Join(",", dbgOrg.EnumerateObject().Select(p => p.Name));
                    dbg += $"|org_keys:{keys}";
                    if (dbgOrg.TryGetProperty("capabilities", out var dbgCaps))
                        dbg += $"|caps:{dbgCaps.GetRawText()}";
                    else
                        dbg += "|caps:MISSING";
                }
                else
                {
                    dbg += "|no_memberships";
                }
                AppSettings.Default.DebugInfo = dbg;
                AppSettings.Default.Save();
            }
            catch { /* debug only — never block refresh */ }
            var (email, orgId, planLabel) = ParseBootstrap(bootstrapJson);

            if (string.IsNullOrEmpty(orgId))
                orgId = await FetchOrgIdFromListAsync(http);
            if (string.IsNullOrEmpty(orgId) && !string.IsNullOrEmpty(AppSettings.Default.OrgId))
                orgId = AppSettings.Default.OrgId;

            if (string.IsNullOrEmpty(orgId))
                return ([], null, null, null, email, orgId, planLabel, true);

            AppSettings.Default.OrgId = orgId;
            AppSettings.Default.Save();

            var usageUrl  = $"https://claude.ai/api/organizations/{orgId}/usage";
            var ut  = http.GetAsync(usageUrl);
            var ot  = FetchAsync<OverageSpendLimit>(http, $"https://claude.ai/api/organizations/{orgId}/overage_spend_limit");
            var pt  = FetchAsync<PrepaidCredits>(http, $"https://claude.ai/api/organizations/{orgId}/prepaid/credits");
            // Fetch org details as a fallback source for capabilities (bootstrap omits them via HttpClient)
            var odt = http.GetAsync($"https://claude.ai/api/organizations/{orgId}");
            await Task.WhenAll(ut, ot, pt, odt);

            var usageResp = ut.Result;
            if (!usageResp.IsSuccessStatusCode)
                throw new Exception($"Usage fetch failed ({(int)usageResp.StatusCode}).");

            var usageJson = await usageResp.Content.ReadAsStringAsync();
            var usage     = JsonSerializer.Deserialize<UsageResponse>(usageJson, JsonOpts);

            // Try org endpoint for capabilities if bootstrap didn't return them
            if (string.IsNullOrEmpty(planLabel) && odt.Result.IsSuccessStatusCode)
            {
                try
                {
                    var orgJson = await odt.Result.Content.ReadAsStringAsync();
                    using var orgDoc = JsonDocument.Parse(orgJson);
                    if (orgDoc.RootElement.TryGetProperty("capabilities", out var orgCaps) &&
                        orgCaps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cap in orgCaps.EnumerateArray())
                        {
                            var s = cap.GetString() ?? "";
                            if (s.StartsWith("claude_", StringComparison.OrdinalIgnoreCase))
                            {
                                var name = s.Substring("claude_".Length);
                                if (name.Length > 0)
                                    planLabel = char.ToUpper(name[0]) + name.Substring(1).ToLower();
                                break;
                            }
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            // Persist fresh usage so cache reflects live data
            AppSettings.Default.UsageJson = usageJson;
            if (!string.IsNullOrEmpty(planLabel)) AppSettings.Default.PlanLabel = planLabel;
            if (ot.Result != null) AppSettings.Default.OverageJson = JsonSerializer.Serialize(ot.Result);
            if (pt.Result != null) AppSettings.Default.PrepaidJson = JsonSerializer.Serialize(pt.Result);
            AppSettings.Default.Save();

            return (BuildLimits(usage), ot.Result, pt.Result, usage?.ExtraUsage, email, orgId, planLabel, true);
        }
        catch { throw; }
    }

    private static async Task<(List<AgentLimit>, string?, string?)> TryWebView2RefreshAsync()
    {
        try
        {
            const string script = @"(async()=>{try{
                const h={headers:{accept:'application/json'}};
                const b=await(await fetch('/api/bootstrap',h)).json();
                let id=b?.account?.memberships?.[0]?.organization?.uuid
                        ||b?.memberships?.[0]?.organization?.uuid||b?.organizations?.[0]?.uuid
                        ||b?.default_organization?.uuid||null;
                const em=b?.account?.email_address||b?.account?.email||b?.email||null;
                if(!id){
                    try{const ol=await(await fetch('/api/organizations',h)).json();
                        if(Array.isArray(ol)&&ol.length>0)id=ol[0]?.uuid||null;}catch(e2){}
                }
                if(!id){
                    let pu=null;
                    try{pu=await(await fetch('/api/usage',h)).json();}catch(e3){}
                    window.chrome.webview.postMessage({email:em,orgId:null,usage:(pu&&!pu.error?pu:null)});
                    return;
                }
                const u=await(await fetch('/api/organizations/'+id+'/usage',h)).json();
                window.chrome.webview.postMessage({email:em,orgId:id,usage:u});
            }catch(ex){window.chrome.webview.postMessage(null);}})();";

            var resultJson = await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var host = new WebViewFetchWindow();
                host.Show();
                try   { return await host.FetchAsync("https://claude.ai", script); }
                finally { host.Close(); }
            }).Task.Unwrap();

            if (resultJson == null || resultJson == "null") return ([], null, null);

            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            string? email = root.TryGetProperty("email", out var em) && em.ValueKind == System.Text.Json.JsonValueKind.String
                ? em.GetString() : null;
            string? orgId = root.TryGetProperty("orgId", out var oi) && oi.ValueKind == System.Text.Json.JsonValueKind.String
                ? oi.GetString() : null;

            List<AgentLimit> limits = [];
            if (root.TryGetProperty("usage", out var us) && us.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var usage = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(
                    us.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                limits = BuildLimits(usage);

                // Cache for next time
                AppSettings.Default.UsageJson = us.GetRawText();
                if (!string.IsNullOrEmpty(orgId)) AppSettings.Default.OrgId = orgId;
                if (!string.IsNullOrEmpty(email)) AppSettings.Default.Email = email;
                AppSettings.Default.Save();
            }

            return (limits, email, orgId);
        }
        catch { return ([], null, null); }
    }

    public async Task LoadFromCacheAsync()
    {
        var email  = AppSettings.Default.Email;
        var orgId  = AppSettings.Default.OrgId;
        var cookie = AppSettings.Default.CookieStore;

        bool isAuth = !string.IsNullOrEmpty(email) || !string.IsNullOrEmpty(orgId)
                   || !string.IsNullOrEmpty(cookie);
        if (!isAuth) return;

        List<AgentLimit> limits = [];
        ExtraUsage?     extraUsage = null;
        OverageSpendLimit? overage = null;
        PrepaidCredits?    prepaid = null;

        if (!string.IsNullOrEmpty(AppSettings.Default.UsageJson))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<UsageResponse>(
                    AppSettings.Default.UsageJson, JsonOpts);
                limits     = BuildLimits(cached);
                extraUsage = cached?.ExtraUsage;
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(AppSettings.Default.OverageJson))
        {
            try { overage = JsonSerializer.Deserialize<OverageSpendLimit>(AppSettings.Default.OverageJson, JsonOpts); }
            catch { }
        }
        if (!string.IsNullOrEmpty(AppSettings.Default.PrepaidJson))
        {
            try { prepaid = JsonSerializer.Deserialize<PrepaidCredits>(AppSettings.Default.PrepaidJson, JsonOpts); }
            catch { }
        }

        // Attach persisted burn history to cached limits
        foreach (var limit in limits)
        {
            var key = limit.Window.ToString();
            if (_burnHistory.TryGetValue(key, out var hist) && hist.Count > 0)
                limit.BurnHistory = [.. hist];
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!string.IsNullOrEmpty(email))                   UserEmail = email;
            if (!string.IsNullOrEmpty(AppSettings.Default.PlanLabel)) PlanLabel = AppSettings.Default.PlanLabel;
            Limits       = limits.Count > 0 ? limits : LoadPlaceholderLimits();
            Overage      = overage;
            Prepaid      = prepaid;
            ExtraUsage   = extraUsage;
            IsSignedIn   = true;
            ErrorMessage = null;
            LastUpdated  = limits.Count > 0 ? DateTime.Now : LastUpdated;
        });
    }

    public async Task SignOutAsync()
    {
        AppSettings.Default.CookieStore = "";
        AppSettings.Default.OrgId       = "";
        AppSettings.Default.Email       = "";
        AppSettings.Default.UsageJson   = "";
        AppSettings.Default.Save();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsSignedIn   = false;
            UserEmail    = "";
            ErrorMessage = "Signed out.";
            Limits       = [];
        });
    }

    public static async Task<List<(string Name, string Value, string Domain, string Path)>> GetCookiesAsync()
    {
        var raw = AppSettings.Default.CookieStore;
        if (string.IsNullOrEmpty(raw)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CookieEntry>>(raw)
                ?.Select(c => (c.Name, c.Value, c.Domain, c.Path))
                .ToList() ?? [];
        }
        catch { return []; }
    }

    public static void SaveCookies(IEnumerable<CoreWebView2Cookie> cookies)
    {
        var entries = cookies
            .Where(c => c.Domain.Contains("claude.ai") || c.Domain.Contains("anthropic.com"))
            .Select(c => new CookieEntry { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path })
            .ToList();
        AppSettings.Default.CookieStore = JsonSerializer.Serialize(entries);
        AppSettings.Default.Save();
    }

    private static HttpClient BuildClient(List<(string Name, string Value, string Domain, string Path)> cookies)
    {
        var handler = new HttpClientHandler { UseCookies = false };
        var http    = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("accept", "application/json");
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Add("Origin",           "https://claude.ai");
        http.DefaultRequestHeaders.Add("Referer",          "https://claude.ai/");
        http.DefaultRequestHeaders.Add("sec-fetch-dest",   "empty");
        http.DefaultRequestHeaders.Add("sec-fetch-mode",   "cors");
        http.DefaultRequestHeaders.Add("sec-fetch-site",   "same-origin");
        http.DefaultRequestHeaders.Add("sec-ch-ua",        "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\"");
        http.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        http.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        http.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        return http;
    }

    private static async Task<T?> FetchAsync<T>(HttpClient http, string url) where T : class
    {
        try
        {
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch { return null; }
    }

    // Parse email and org ID out of bootstrap JSON (multiple fallback paths for org ID)
    private static (string? Email, string? OrgId, string? PlanLabel) ParseBootstrap(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? email = null;
            string? orgId = null;

            if (root.TryGetProperty("account", out var acct) &&
                acct.TryGetProperty("email_address", out var em))
                email = em.GetString();

            // Path 1: account.memberships[0].organization.uuid  (Claude.ai personal/pro accounts)
            if (root.TryGetProperty("account", out var acctNode) &&
                acctNode.TryGetProperty("memberships", out var mems) && mems.GetArrayLength() > 0)
            {
                var first = mems[0];
                if (first.TryGetProperty("organization", out var org) &&
                    org.TryGetProperty("uuid", out var uuid))
                    orgId = uuid.GetString();
            }

            // Path 2: memberships[0].organization.uuid  (root-level, older API shape)
            if (string.IsNullOrEmpty(orgId) &&
                root.TryGetProperty("memberships", out var rootMems) && rootMems.GetArrayLength() > 0)
            {
                var first = rootMems[0];
                if (first.TryGetProperty("organization", out var org) &&
                    org.TryGetProperty("uuid", out var uuid))
                    orgId = uuid.GetString();
            }

            // Path 3: organizations[0].uuid  (flat list on root)
            if (string.IsNullOrEmpty(orgId) &&
                root.TryGetProperty("organizations", out var orgs) && orgs.GetArrayLength() > 0)
            {
                if (orgs[0].TryGetProperty("uuid", out var uuid))
                    orgId = uuid.GetString();
            }

            string? planLabel = null;
            if (root.TryGetProperty("account", out var acctPlan) &&
                acctPlan.TryGetProperty("memberships", out var plMems) && plMems.GetArrayLength() > 0 &&
                plMems[0].TryGetProperty("organization", out var plOrg) &&
                plOrg.TryGetProperty("capabilities", out var caps) &&
                caps.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var cap in caps.EnumerateArray())
                {
                    var s = cap.GetString() ?? "";
                    if (s.StartsWith("claude_", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = s.Substring("claude_".Length);
                        if (name.Length > 0)
                            planLabel = char.ToUpper(name[0]) + name.Substring(1).ToLower();
                        break;
                    }
                }
            }
            return (email, orgId, planLabel);
        }
        catch { return (null, null, null); }
    }

    private static async Task<string?> FetchOrgIdFromListAsync(HttpClient http)
    {
        try
        {
            var resp = await http.GetAsync("https://claude.ai/api/organizations");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.TryGetProperty("uuid", out var uuid))
                    return uuid.GetString();
            }
            return null;
        }
        catch { return null; }
    }

    private static List<AgentLimit> BuildLimits(UsageResponse? usage)
    {
        if (usage == null) return [];
        var now    = DateTime.Now;
        var result = new List<AgentLimit>();

        if (usage.FiveHour != null)
        {
            var reset = ParseDate(usage.FiveHour.ResetsAt, now);
            var pct   = Math.Clamp(usage.FiveHour.Utilization, 0, 100);
            result.Add(new AgentLimit
            {
                Window        = WindowKind.FiveHour,
                UsedPercent   = pct,
                TimeRemaining = TimeLeft(reset, now),
                ResetDate     = reset,
                BurnRate      = pct / 5.0,
                IsLive        = true,
            });
        }

        if (usage.SevenDay != null)
        {
            var reset = ParseDate(usage.SevenDay.ResetsAt, now);
            var pct   = Math.Clamp(usage.SevenDay.Utilization, 0, 100);
            result.Add(new AgentLimit
            {
                Window        = WindowKind.SevenDay,
                UsedPercent   = pct,
                TimeRemaining = TimeLeft(reset, now),
                ResetDate     = reset,
                BurnRate      = pct / (7 * 24.0),
                IsLive        = true,
            });
        }

        return result;
    }

    private static DateTime ParseDate(string? s, DateTime fallback)
    {
        if (s == null) return fallback.AddHours(1);
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d.ToLocalTime() : fallback.AddHours(1);
    }

    private static string TimeLeft(DateTime reset, DateTime now)
    {
        var diff = reset - now;
        if (diff <= TimeSpan.Zero) return "resetting...";
        if (diff.TotalDays >= 1)
            return $"{(int)diff.TotalDays}d {diff.Hours}h";
        return $"{diff.Hours}h {diff.Minutes}m";
    }

    private static List<AgentLimit> LoadPlaceholderLimits()
    {
        var now = DateTime.Now;
        return
        [
            new() { Window = WindowKind.FiveHour, UsedPercent = 0, TimeRemaining = "—", ResetDate = now.AddHours(1) },
            new() { Window = WindowKind.SevenDay,  UsedPercent = 0, TimeRemaining = "—", ResetDate = now.AddDays(7) },
        ];
    }

    private void LoadPlaceholders() => Limits = LoadPlaceholderLimits();

    private void LoadBurnHistory()
    {
        try
        {
            var raw = AppSettings.Default.BurnHistory;
            if (string.IsNullOrEmpty(raw)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<double>>>(raw);
            if (saved != null)
                foreach (var kv in saved) _burnHistory[kv.Key] = kv.Value;
        }
        catch { }
    }

    private void SaveBurnHistory()
    {
        AppSettings.Default.BurnHistory = JsonSerializer.Serialize(_burnHistory);
        AppSettings.Default.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private class CookieEntry
    {
        public string Name   { get; set; } = "";
        public string Value  { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path   { get; set; } = "/";
    }
}
