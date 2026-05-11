using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeCheckerWindows;

// Invisible 1×1 window that hosts a WebView2 for authenticated API calls.
// Uses the same user data folder as LoginWindow so the session (including
// cf_clearance) is always shared and fresh — equivalent to macOS's WKWebsiteDataStore.
//
// Two usage modes:
//   • Persistent (App.BackgroundBrowser): created once at startup, navigates to
//     claude.ai once, then scripts run directly on the live page every refresh.
//     No re-navigation = no memory accumulation.
//   • One-shot (LoginWindow): created, used, closed — same as before.
public sealed class WebViewFetchWindow : Window
{
    private readonly WebView2 _wv = new();
    private bool _initialized;
    private bool _readyForScript; // true after first navigation to claude.ai completes

    private static readonly string UserDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeChecker", "WebView2");

    public bool IsReady => _readyForScript;

    public WebViewFetchWindow()
    {
        Width              = 1;
        Height             = 1;
        Left               = -9999;
        Top                = -9999;
        ShowInTaskbar      = false;
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Opacity            = 0;
        Content            = _wv;
    }

    // ── Persistent-mode init ──────────────────────────────────────────────────

    // Call once at startup. Navigates to claude.ai so the session/cookies are
    // established and cf_clearance is fresh. Subsequent RunScriptAsync calls
    // skip navigation and just execute JS on the live page.
    public async Task InitAsync()
    {
        await EnsureInitAsync();

        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? h = null;
        h = (_, e) => { _wv.CoreWebView2.NavigationCompleted -= h; navDone.TrySetResult(e.IsSuccess); };
        _wv.CoreWebView2.NavigationCompleted += h;
        _wv.CoreWebView2.Navigate("https://claude.ai");

        // Wait up to 15 s for initial navigation
        await Task.WhenAny(navDone.Task, Task.Delay(15000));
        _readyForScript = navDone.Task.IsCompletedSuccessfully && navDone.Task.Result;
    }

    // Run a JS script on the already-loaded claude.ai page.
    // The script must call window.chrome.webview.postMessage(result).
    public async Task<string?> RunScriptAsync(string script, int timeoutMs = 10000)
    {
        if (!_readyForScript) return null;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2WebMessageReceivedEventArgs>? msgHandler = null;
        msgHandler = (_, args) =>
        {
            _wv.CoreWebView2.WebMessageReceived -= msgHandler;
            tcs.TrySetResult(args.WebMessageAsJson);
        };
        _wv.CoreWebView2.WebMessageReceived += msgHandler;

        try { await _wv.CoreWebView2.ExecuteScriptAsync(script); }
        catch
        {
            _wv.CoreWebView2.WebMessageReceived -= msgHandler;
            return null;
        }

        _ = Task.Delay(timeoutMs).ContinueWith(_ =>
        {
            _wv.CoreWebView2.WebMessageReceived -= msgHandler;
            tcs.TrySetResult(null);
        });

        return await tcs.Task;
    }

    // ── One-shot mode (LoginWindow) ───────────────────────────────────────────

    public async Task<string?> FetchAsync(string navigateUrl, string script, int timeoutMs = 20000)
    {
        await EnsureInitAsync();

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<CoreWebView2WebMessageReceivedEventArgs>? msgHandler = null;
        msgHandler = (_, args) =>
        {
            _wv.CoreWebView2.WebMessageReceived -= msgHandler;
            tcs.TrySetResult(args.WebMessageAsJson);
        };
        _wv.CoreWebView2.WebMessageReceived += msgHandler;

        EventHandler<CoreWebView2NavigationCompletedEventArgs>? navHandler = null;
        navHandler = async (_, e) =>
        {
            _wv.CoreWebView2.NavigationCompleted -= navHandler;
            if (!e.IsSuccess) { tcs.TrySetResult(null); return; }
            try   { await _wv.CoreWebView2.ExecuteScriptAsync(script); }
            catch { tcs.TrySetResult(null); }
        };
        _wv.CoreWebView2.NavigationCompleted += navHandler;
        _wv.CoreWebView2.Navigate(navigateUrl);

        _ = Task.Delay(timeoutMs).ContinueWith(_ =>
        {
            _wv.CoreWebView2.WebMessageReceived -= msgHandler;
            tcs.TrySetResult(null);
        });

        return await tcs.Task;
    }

    // Re-navigate to claude.ai with the current session (call after sign-in).
    public async Task ReloadAsync()
    {
        if (_wv.CoreWebView2 == null) { await InitAsync(); return; }
        _readyForScript = false;
        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? h = null;
        h = (_, e) => { _wv.CoreWebView2.NavigationCompleted -= h; navDone.TrySetResult(e.IsSuccess); };
        _wv.CoreWebView2.NavigationCompleted += h;
        _wv.CoreWebView2.Navigate("https://claude.ai");
        await Task.WhenAny(navDone.Task, Task.Delay(15000));
        _readyForScript = navDone.Task.IsCompletedSuccessfully && navDone.Task.Result;
    }

    // ── Shared init ───────────────────────────────────────────────────────────

    private int _initGuard;

    private async Task EnsureInitAsync()
    {
        if (_initialized) return;
        if (Interlocked.Exchange(ref _initGuard, 1) != 0) return;

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
        await _wv.EnsureCoreWebView2Async(env);
        _initialized = true;
    }
}
