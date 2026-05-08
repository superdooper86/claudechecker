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
    private static readonly string OrgId = "daf626a9-4924-4ff3-ba98-23b523062f8e";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private List<AgentLimit> _limits = [];
    private bool _isLoading;
    private string? _errorMessage;
    private bool _isSignedIn;
    private string _userEmail = "";
    private string _planLabel = "Pro";
    private DateTime? _lastUpdated;
    private OverageSpendLimit? _overage;
    private PrepaidCredits? _prepaid;

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

    private int _refreshInterval = 120;
    public int RefreshInterval
    {
        get => _refreshInterval;
        set
        {
            Set(ref _refreshInterval, value);
            Properties.Settings.Default.RefreshInterval = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _showInTaskbar = true;
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            Set(ref _showInTaskbar, value);
            Properties.Settings.Default.ShowInTaskbar = value;
            Properties.Settings.Default.Save();
        }
    }

    public UsageViewModel()
    {
        _refreshInterval = Properties.Settings.Default.RefreshInterval > 0
            ? Properties.Settings.Default.RefreshInterval : 120;
        _showInTaskbar = Properties.Settings.Default.ShowInTaskbar;
        LoadBurnHistory();
        LoadPlaceholders();
    }

    public async Task RefreshAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);

        try
        {
            var cookies = await GetCookiesAsync();
            if (cookies.Count == 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSignedIn = false;
                    ErrorMessage = "Not signed in — click Sign In to authenticate.";
                    IsLoading = false;
                });
                return;
            }

            using var http = BuildClient(cookies);

            var usageTask   = FetchAsync<UsageResponse>(http, $"https://claude.ai/api/organizations/{OrgId}/usage");
            var overageTask = FetchAsync<OverageSpendLimit>(http, $"https://claude.ai/api/organizations/{OrgId}/overage_spend_limit");
            var prepaidTask = FetchAsync<PrepaidCredits>(http, $"https://claude.ai/api/organizations/{OrgId}/prepaid/credits");
            var emailTask   = FetchEmailAsync(http);

            await Task.WhenAll(usageTask, overageTask, prepaidTask, emailTask);

            var usage  = usageTask.Result;
            var limits = BuildLimits(usage);

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
                Limits        = limits;
                Overage       = overageTask.Result;
                Prepaid       = prepaidTask.Result;
                UserEmail     = emailTask.Result ?? UserEmail;
                IsSignedIn    = true;
                ErrorMessage  = null;
                LastUpdated   = DateTime.Now;
                IsLoading     = false;
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

    public async Task SignOutAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync();
        var dataManager = env.CreateCoreWebView2CookieManager();
        // Clear via settings — simplest approach is to delete the stored cookies
        Properties.Settings.Default.CookieStore = "";
        Properties.Settings.Default.Save();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsSignedIn   = false;
            UserEmail    = "";
            ErrorMessage = "Signed out.";
            Limits       = [];
        });
    }

    // Cookie store: we persist cookies as JSON in settings after login
    public static async Task<List<(string Name, string Value, string Domain, string Path)>> GetCookiesAsync()
    {
        var raw = Properties.Settings.Default.CookieStore;
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
            .Where(c => c.Domain.Contains("claude.ai"))
            .Select(c => new CookieEntry { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path })
            .ToList();
        Properties.Settings.Default.CookieStore = JsonSerializer.Serialize(entries);
        Properties.Settings.Default.Save();
    }

    private static HttpClient BuildClient(List<(string Name, string Value, string Domain, string Path)> cookies)
    {
        var handler = new HttpClientHandler { UseCookies = false };
        var http    = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("accept", "application/json");
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

    private static async Task<string?> FetchEmailAsync(HttpClient http)
    {
        try
        {
            var resp = await http.GetAsync("https://claude.ai/api/bootstrap");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var boot = JsonSerializer.Deserialize<BootstrapResponse>(json, JsonOpts);
            return boot?.Account?.EmailAddress;
        }
        catch { return null; }
    }

    private List<AgentLimit> BuildLimits(UsageResponse? usage)
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

    private void LoadPlaceholders()
    {
        var now = DateTime.Now;
        Limits =
        [
            new() { Window = WindowKind.FiveHour, UsedPercent = 0, TimeRemaining = "—", ResetDate = now.AddHours(1) },
            new() { Window = WindowKind.SevenDay,  UsedPercent = 0, TimeRemaining = "—", ResetDate = now.AddDays(7) },
        ];
    }

    private void LoadBurnHistory()
    {
        try
        {
            var raw = Properties.Settings.Default.BurnHistory;
            if (string.IsNullOrEmpty(raw)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<double>>>(raw);
            if (saved != null)
                foreach (var kv in saved) _burnHistory[kv.Key] = kv.Value;
        }
        catch { }
    }

    private void SaveBurnHistory()
    {
        Properties.Settings.Default.BurnHistory = JsonSerializer.Serialize(_burnHistory);
        Properties.Settings.Default.Save();
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
