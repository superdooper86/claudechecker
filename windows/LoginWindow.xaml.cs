using Microsoft.Web.WebView2.Core;
using System;
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
            if (uri.Contains("claude.ai") && !uri.Contains("/login"))
            {
                // Likely signed in — enable done button
                var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
                var hasSess = cookies.Any(c => c.Name.Contains("session") || c.Name.Contains("sk-"));
                await Dispatcher.InvokeAsync(() =>
                {
                    DoneButton.IsEnabled = hasSess || uri.Contains("/chats") || uri.Contains("/new");
                    StatusText.Text      = DoneButton.IsEnabled
                        ? "Signed in! Click Done to continue."
                        : "Waiting for sign-in…";
                });
            }
        };
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
        UsageViewModel.SaveCookies(cookies);
        DialogResult = true;
    }
}
