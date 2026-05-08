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
            // CookieStore being non-empty is our signal that the user has signed in.
            if (string.IsNullOrEmpty(AppSettings.Default.CookieStore))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSignedIn   = false;
                    ErrorMessage = "Not signed in — click Sign In to authenticate.";
                    IsLoading    = false;
                });
                return;
            }

            List<AgentLimit> limits    = [];
            OverageSpendLimit? overage = null;
            PrepaidCredits?    prepaid = null;
            ExtraUsage?     extraUsage = null;
            string? email = null, orgId = null, planLabel = null;
            string? refreshError = null;

            // Primary: persistent background WebView2 — always-live cookies, same as macOS.
            // Fallback: HttpClient with saved cookies, only if the browser isn't ready yet
            // (the brief window while InitAsync is still navigating at startup).
            if (App.BackgroundBrowser.IsReady)
            {
                try
                {
                    (limits, overage, prepaid, extraUsage, email, orgId, planLabel) =
                        await TryBrowserRefreshAsync();
                }
                catch (Exception ex)
                {
                    refreshError = ex.Message;
                }
            }
            else
            {
                var cookies = await GetCookiesAsync();
                if (cookies.Count > 0)
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
                    if (extraUsage != null) ExtraUsage = extraUsage;
                }
                if (!string.IsNullOrEmpty(planLabel)) PlanLabel = planLabel;
                if (!string.IsNullOrEmpty(email))     UserEmail = email;
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

    // ── Browser-based refresh (primary) ───────────────────────────────────────
    // Runs JS on the persistent claude.ai page. Same live cookies as macOS WKWebView.

    private async Task<(List<AgentLimit>, OverageSpendLimit?, PrepaidCredits?, ExtraUsage?, string?, string?, string?)>
        TryBrowserRefreshAsync()
    {
        const string script = @"(async()=>{try{
            const h={headers:{accept:'application/json'}};
            const b=await(await fetch('/api/bootstrap',h)).json();
            if(b?.error_type==='authentication_error'){window.chrome.webview.postMessage({authError:true});return;}
            const org0=b?.account?.memberships?.[0]?.organization;
            let id=org0?.uuid||b?.memberships?.[0]?.organization?.uuid||b?.organizations?.[0]?.uuid||null;
            const email=b?.account?.email_address||b?.account?.email||null;
            const caps=org0?.capabilities||[];
            const capStr=caps.find(c=>typeof c==='string'&&c.startsWith('claude_'))||null;
            const planLabel=capStr?(capStr.slice(7,8).toUpperCase()+capStr.slice(8).toLowerCase()):null;
            if(!id){
                try{const ol=await(await fetch('/api/organizations',h)).json();
                    if(Array.isArray(ol)&&ol.length>0)id=ol[0]?.uuid||null;}catch(e2){}
            }
            if(!id){window.chrome.webview.postMessage({noOrg:true,email});return;}
            const [u,ov,pp]=await Promise.all([
                fetch('/api/organizations/'+id+'/usage',h).then(r=>r.json()),
                fetch('/api/organizations/'+id+'/overage_spend_limit',h).then(r=>r.ok?r.json():null).catch(()=>null),
                fetch('/api/organizations/'+id+'/prepaid/credits',h).then(r=>r.ok?r.json():null).catch(()=>null)
            ]);
            window.chrome.webview.postMessage({email,orgId:id,planLabel,usage:u,overage:ov,prepaid:pp});
        }catch(ex){window.chrome.webview.postMessage({error:String(ex)});}})()";

        var json = await Application.Current.Dispatcher.InvokeAsync(
            () => App.BackgroundBrowser.RunScriptAsync(script)).Task.Unwrap();

        if (string.IsNullOrEmpty(json) || json == "null")
            throw new Exception("Browser refresh returned no data.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("authError", out _))
            throw new Exception("Session expired — please re-authenticate.");
        if (root.TryGetProperty("error", out var errEl))
            throw new Exception("Browser script error: " + errEl.GetString());
        if (root.TryGetProperty("noOrg", out _))
            throw new Exception("Could not determine organization ID.");

        string? email     = root.TryGetProperty("email",     out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() : null;
        string? orgId     = root.TryGetProperty("orgId",     out var oi) && oi.ValueKind == JsonValueKind.String ? oi.GetString() : null;
        string? planLabel = root.TryGetProperty("planLabel", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() : null;

        UsageResponse?     usage       = null;
        OverageSpendLimit? overage     = null;
        PrepaidCredits?    prepaid     = null;

        if (root.TryGetProperty("usage",   out var us) && us.ValueKind == JsonValueKind.Object)
            usage   = JsonSerializer.Deserialize<UsageResponse>(us.GetRawText(), JsonOpts);
        if (root.TryGetProperty("overage", out var ov) && ov.ValueKind == JsonValueKind.Object)
            overage = JsonSerializer.Deserialize<OverageSpendLimit>(ov.GetRawText(), JsonOpts);
        if (root.TryGetProperty("prepaid", out var pp) && pp.ValueKind == JsonValueKind.Object)
            prepaid = JsonSerializer.Deserialize<PrepaidCredits>(pp.GetRawText(), JsonOpts);

        return (BuildLimits(usage), overage, prepaid, usage?.ExtraUsage, email, orgId, planLabel);
    }

    // ── HttpClient refresh (fallback — before browser is ready) ──────────────

    private async Task<(List<AgentLimit>, OverageSpendLimit?, PrepaidCredits?, ExtraUsage?, string?, string?, string?, bool)>
        TryHttpRefreshAsync(List<(string Name, string Value, string Domain, string Path)> cookies)
    {
        using var http = BuildClient(cookies);
        var bootstrapResp = await http.GetAsync("https://claude.ai/api/bootstrap");
        if ((int)bootstrapResp.StatusCode is 401 or 403)
            throw new Exception("Session expired — please re-authenticate.");
        if (!bootstrapResp.IsSuccessStatusCode)
            throw new Exception($"Bootstrap failed ({(int)bootstrapResp.StatusCode}).");

        var bootstrapJson = await bootstrapResp.Content.ReadAsStringAsync();
        var (email, orgId, planLabel) = ParseBootstrap(bootstrapJson);

        if (string.IsNullOrEmpty(orgId))
            orgId = await FetchOrgIdFromListAsync(http);

        if (string.IsNullOrEmpty(orgId))
            return ([], null, null, null, email, orgId, planLabel, true);

        var ut = http.GetAsync($"https://claude.ai/api/organizations/{orgId}/usage");
        var ot = FetchAsync<OverageSpendLimit>(http, $"https://claude.ai/api/organizations/{orgId}/overage_spend_limit");
        var pt = FetchAsync<PrepaidCredits>(http, $"https://claude.ai/api/organizations/{orgId}/prepaid/credits");
        await Task.WhenAll(ut, ot, pt);

        var usageResp = ut.Result;
        if (!usageResp.IsSuccessStatusCode)
            throw new Exception($"Usage fetch failed ({(int)usageResp.StatusCode}).");

        var usageJson = await usageResp.Content.ReadAsStringAsync();
        var usage     = JsonSerializer.Deserialize<UsageResponse>(usageJson, JsonOpts);

        return (BuildLimits(usage), ot.Result, pt.Result, usage?.ExtraUsage, email, orgId, planLabel, true);
    }

    // ── Startup cache ─────────────────────────────────────────────────────────
    // Called before the browser is ready. Sets sign-in state and shows placeholders
    // with any previously accumulated burn history so the popup isn't blank.

    public async Task LoadFromCacheAsync()
    {
        if (string.IsNullOrEmpty(AppSettings.Default.CookieStore)) return;

        var placeholders = LoadPlaceholderLimits();
        foreach (var limit in placeholders)
        {
            var key = limit.Window.ToString();
            if (_burnHistory.TryGetValue(key, out var hist) && hist.Count > 0)
                limit.BurnHistory = [.. hist];
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Limits     = placeholders;
            IsSignedIn = true;
        });
    }

    public async Task SignOutAsync()
    {
        AppSettings.Default.CookieStore = "";
        AppSettings.Default.Save();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsSignedIn   = false;
            UserEmail    = "";
            PlanLabel    = "Claude";
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
        http.DefaultRequestHeaders.Add("Origin",              "https://claude.ai");
        http.DefaultRequestHeaders.Add("Referer",             "https://claude.ai/");
        http.DefaultRequestHeaders.Add("sec-fetch-dest",      "empty");
        http.DefaultRequestHeaders.Add("sec-fetch-mode",      "cors");
        http.DefaultRequestHeaders.Add("sec-fetch-site",      "same-origin");
        http.DefaultRequestHeaders.Add("sec-ch-ua",           "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\"");
        http.DefaultRequestHeaders.Add("sec-ch-ua-mobile",    "?0");
        http.DefaultRequestHeaders.Add("sec-ch-ua-platform",  "\"Windows\"");
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

            if (root.TryGetProperty("account", out var acctNode) &&
                acctNode.TryGetProperty("memberships", out var mems) && mems.GetArrayLength() > 0)
            {
                var first = mems[0];
                if (first.TryGetProperty("organization", out var org) &&
                    org.TryGetProperty("uuid", out var uuid))
                    orgId = uuid.GetString();
            }

            if (string.IsNullOrEmpty(orgId) &&
                root.TryGetProperty("memberships", out var rootMems) && rootMems.GetArrayLength() > 0)
            {
                var first = rootMems[0];
                if (first.TryGetProperty("organization", out var org) &&
                    org.TryGetProperty("uuid", out var uuid))
                    orgId = uuid.GetString();
            }

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
                caps.ValueKind == JsonValueKind.Array)
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
