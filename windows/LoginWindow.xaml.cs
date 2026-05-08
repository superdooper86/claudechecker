using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeCheckerWindows;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitBrowserAsync();
    }

    private async Task InitBrowserAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder:
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeChecker", "WebView2"));

        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Navigate("https://claude.ai");

        Browser.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            var uri = Browser.CoreWebView2.Source;
            if (!uri.Contains("claude.ai")) return;

            var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
            var signedIn = cookies.Any(c =>
                c.Name.Equals("sessionKey", StringComparison.OrdinalIgnoreCase) ||
                c.Name.StartsWith("sk-ant", StringComparison.OrdinalIgnoreCase));

            await Dispatcher.InvokeAsync(() =>
            {
                DoneButton.IsEnabled = !uri.Contains("/login") && !uri.Contains("/signin");
                StatusText.Text = signedIn
                    ? "Signed in — click Done to continue."
                    : "Complete sign-in, then click Done.";
            });

            // Auto-close once clearly on the main app page
            if (signedIn && (uri.Contains("/chats") || uri.Contains("/new") || uri == "https://claude.ai/"))
            {
                await SaveAndClose(cookies);
            }
        };
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
        await SaveAndClose(cookies);
    }

    private async Task SaveAndClose(IReadOnlyList<CoreWebView2Cookie> cookies)
    {
        UsageViewModel.SaveCookies(cookies);
        await Dispatcher.InvokeAsync(() => DialogResult = true);
    }
}
