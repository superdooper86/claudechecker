using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Windows;

namespace ClaudeCheckerWindows;

public class UpdateManager : INotifyPropertyChanged
{
    private const string StableUrl = "https://raw.githubusercontent.com/superdooper86/claudechecker/refs/heads/main/version-windows.json";
    private const string BetaUrl   = "https://raw.githubusercontent.com/superdooper86/claudechecker/refs/heads/main/version-windows-beta.json";

    private bool   _updateAvailable;
    private string _latestVersion  = "";
    private string _releaseNotes   = "";
    private string _downloadUrl    = "";
    private bool   _betaAvailable;
    private string _latestBetaVersion = "";
    private bool   _isDownloading;
    private double _downloadProgress;
    private string _statusMessage  = "";
    private string? _updateError;
    private bool   _updateComplete;

    public bool   UpdateAvailable      { get => _updateAvailable;     set => Set(ref _updateAvailable, value); }
    public string LatestVersion        { get => _latestVersion;       set => Set(ref _latestVersion, value); }
    public string ReleaseNotes         { get => _releaseNotes;        set => Set(ref _releaseNotes, value); }
    public string DownloadUrl          { get => _downloadUrl;         set => Set(ref _downloadUrl, value); }
    public bool   BetaAvailable        { get => _betaAvailable;       set => Set(ref _betaAvailable, value); }
    public string LatestBetaVersion    { get => _latestBetaVersion;   set => Set(ref _latestBetaVersion, value); }
    public bool   IsDownloading        { get => _isDownloading;       set => Set(ref _isDownloading, value); }
    public double DownloadProgress     { get => _downloadProgress;    set => Set(ref _downloadProgress, value); }
    public string StatusMessage        { get => _statusMessage;       set => Set(ref _statusMessage, value); }
    public string? UpdateError         { get => _updateError;         set => Set(ref _updateError, value); }
    public bool   UpdateComplete       { get => _updateComplete;      set => Set(ref _updateComplete, value); }

    private bool _betaChannel;
    public bool BetaChannel
    {
        get => _betaChannel;
        set
        {
            Set(ref _betaChannel, value);
            AppSettings.Default.BetaChannel = value;
            AppSettings.Default.Save();
        }
    }

    public string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly()
              .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
              ?.InformationalVersion.Split('+')[0]
              ?? "1.0.0";

    private bool IsPreReleaseBuild => CurrentVersion.Contains('-');

    public UpdateManager()
    {
        _betaChannel = AppSettings.Default.BetaChannel;
    }

    public async Task CheckForUpdatesAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

        var stable = await FetchVersionAsync(http, StableUrl);
        var beta   = await FetchVersionAsync(http, BetaUrl);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (stable != null && IsNewer(stable.Version, CurrentVersion))
            {
                LatestVersion   = stable.Version;
                ReleaseNotes    = stable.Notes ?? "";
                DownloadUrl     = stable.Url;
                UpdateAvailable = true;
            }
            else
            {
                UpdateAvailable = false;
                LatestVersion   = "";
            }

            if (beta != null && IsNewer(beta.Version, CurrentVersion))
            {
                LatestBetaVersion = beta.Version;
                BetaAvailable     = true;

                // Show beta update if user opted in, or if they're already running a beta
                if ((BetaChannel || IsPreReleaseBuild) && IsNewer(beta.Version, LatestVersion))
                {
                    LatestVersion   = beta.Version;
                    ReleaseNotes    = beta.Notes ?? "";
                    DownloadUrl     = beta.Url;
                    UpdateAvailable = true;
                }
            }
            else
            {
                BetaAvailable     = false;
                LatestBetaVersion = "";
            }
        });
    }

    private static async Task<VersionInfo?> FetchVersionAsync(HttpClient http, string url)
    {
        try
        {
            var bust = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var json = await http.GetStringAsync(bust);
            return JsonSerializer.Deserialize<VersionInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public async Task DownloadAndInstallAsync()
    {
        if (string.IsNullOrEmpty(DownloadUrl)) return;

        IsDownloading  = true;
        UpdateError    = null;
        UpdateComplete = false;
        StatusMessage  = "Downloading…";

        try
        {
            var tmpDir  = Path.Combine(Path.GetTempPath(), $"CCUpdate_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            var zipPath = Path.Combine(tmpDir, "ClaudeChecker.zip");

            using var http = new HttpClient();
            var response   = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total    = response.Content.Headers.ContentLength ?? 0;
            var received = 0L;

            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var file   = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    received += read;
                    if (total > 0)
                        DownloadProgress = (double)received / total * 0.8;
                }
            }

            StatusMessage    = "Unpacking…";
            DownloadProgress = 0.85;

            var extractDir = Path.Combine(tmpDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            // Find the installer exe
            var newExe = FindExe(extractDir);
            if (newExe == null) throw new Exception("ClaudeChecker.exe not found in update package.");

            DownloadProgress = 0.95;
            StatusMessage    = "Installing…";

            // Write a batch script to replace the exe after we exit
            var currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            var script = $"""
                @echo off
                timeout /t 2 /nobreak > nul
                copy /Y "{newExe}" "{currentExe}"
                start "" "{currentExe}"
                rmdir /S /Q "{tmpDir}"
                """;

            var scriptPath = Path.Combine(Path.GetTempPath(), "claudechecker_update.bat");
            await File.WriteAllTextAsync(scriptPath, script);

            DownloadProgress = 1.0;
            StatusMessage    = "Installed! Relaunching…";
            UpdateComplete   = true;

            await Task.Delay(800);

            Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/C \"{scriptPath}\"",
                CreateNoWindow  = true,
                UseShellExecute = false,
            });

            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
        catch (Exception ex)
        {
            UpdateError   = ex.Message;
            IsDownloading = false;
            StatusMessage = "";
        }
    }

    private static string? FindExe(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "ClaudeChecker.exe", SearchOption.AllDirectories))
            return f;
        return null;
    }

    public static bool IsNewer(string version, string current)
    {
        static (int[] Base, int[]? Pre) Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return ([], null);
            var halves = v.Split('-', 2);
            var baseP  = Array.ConvertAll(halves[0].Split('.'), p => int.TryParse(p, out var n) ? n : 0);
            int[]? pre = halves.Length > 1
                ? Array.ConvertAll(halves[1].Split('.'), p => int.TryParse(p, out var n) ? n : 0)
                : null;
            return (baseP, pre);
        }

        var (ab, ap) = Parse(version);
        var (bb, bp) = Parse(current);

        for (var i = 0; i < Math.Max(ab.Length, bb.Length); i++)
        {
            var av = i < ab.Length ? ab[i] : 0;
            var bv = i < bb.Length ? bb[i] : 0;
            if (av != bv) return av > bv;
        }

        if (ap == null && bp != null) return true;
        if (ap != null && bp == null) return false;
        if (ap != null && bp != null)
        {
            for (var i = 0; i < Math.Max(ap.Length, bp.Length); i++)
            {
                var av = i < ap.Length ? ap[i] : 0;
                var bv = i < bp.Length ? bp[i] : 0;
                if (av != bv) return av > bv;
            }
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Application.Current.Dispatcher.InvokeAsync(()
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
}
