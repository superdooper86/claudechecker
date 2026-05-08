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

        var tcs = new TaskCompletionSource<string?>();

        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = async (_, e) =>
        {
            _wv.CoreWebView2.NavigationCompleted -= handler;
            if (!e.IsSuccess) { tcs.TrySetResult(null); return; }
            try
            {
                var result = await _wv.CoreWebView2.ExecuteScriptAsync(script);
                tcs.TrySetResult(result);
            }
            catch { tcs.TrySetResult(null); }
        };

        _wv.CoreWebView2.NavigationCompleted += handler;
        _wv.CoreWebView2.Navigate(navigateUrl);

        _ = Task.Delay(timeoutMs).ContinueWith(_ => tcs.TrySetResult(null));

        return await tcs.Task;
    }
}
