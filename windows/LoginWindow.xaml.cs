using Microsoft.Web.WebView2.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeCheckerWindows;

public partial class LoginWindow : Window
{
    private bool _closing;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitBrowserAsync();
    }

    private async Task InitBrowserAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder:
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "ClaudeChecker", "WebView2"));

        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Navigate("https://claude.ai");

        Browser.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess || _closing) return;
            var uri = Browser.CoreWebView2.Source;
            if (!uri.Contains("claude.ai")) return;

            var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
            var signedIn = System.Linq.Enumerable.Any(cookies, c =>
                c.Name.Equals("sessionKey", System.StringComparison.OrdinalIgnoreCase) ||
                c.Name.StartsWith("sk-ant",  System.StringComparison.OrdinalIgnoreCase));

            await Dispatcher.InvokeAsync(() =>
            {
                DoneButton.IsEnabled = !uri.Contains("/login") && !uri.Contains("/signin");
                StatusText.Text = signedIn
                    ? "Signed in — click Done to continue."
                    : "Complete sign-in, then click Done.";
            });

            if (!uri.Contains("/login") && !uri.Contains("/signin") && signedIn)
                await SaveAndClose(cookies);
        };
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
        await SaveAndClose(cookies);
    }

    private async Task SaveAndClose(IReadOnlyList<CoreWebView2Cookie> cookies)
    {
        if (_closing) return;
        _closing = true;

        // Persist cookies so the app knows the user is signed in on next startup.
        // All live data is fetched by the background WebView2 on the next refresh.
        UsageViewModel.SaveCookies(cookies);

        await Dispatcher.InvokeAsync(() => DialogResult = true);
    }
}
