using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeCheckerWindows;

// Invisible 1×1 window that hosts a WebView2 for authenticated API calls.
// Uses the same user data folder as LoginWindow so the session is shared.
internal sealed class WebViewFetchWindow : Window
{
    private readonly WebView2 _wv = new();

    public WebViewFetchWindow()
    {
        Width          = 1;
        Height         = 1;
        Left           = -9999;
        Top            = -9999;
        ShowInTaskbar  = false;
        WindowStyle    = WindowStyle.None;
        AllowsTransparency = true;
        Opacity        = 0;
        Content        = _wv;
    }

    public async Task<string?> FetchAsync(string navigateUrl, string script, int timeoutMs = 20000)
    {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder:
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeChecker", "WebView2"));

        await _wv.EnsureCoreWebView2Async(env);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Use WebMessageReceived so the script can post back asynchronously without
        // relying on Promise-awaiting support in the WebView2 runtime version.
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
}
