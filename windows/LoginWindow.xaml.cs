using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeChecker", "WebView2"));

        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Navigate("https://claude.ai");

        Browser.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess || _closing) return;
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

            // Auto-close as soon as we detect sign-in on any claude.ai page
            if (signedIn && !uri.Contains("/login") && !uri.Contains("/signin"))
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

        UsageViewModel.SaveCookies(cookies);

        // Fetch bootstrap + usage from within WebView2 (already authenticated, no header issues)
        try
        {
            const string script = @"(async()=>{try{
                const b=await(await fetch('/api/bootstrap',{headers:{accept:'application/json'}})).json();
                const id=b?.memberships?.[0]?.organization?.uuid||b?.organizations?.[0]?.uuid||null;
                const e=b?.account?.email_address||null;
                if(!id)return{email:e,orgId:null,usage:null};
                const u=await(await fetch('/api/organizations/'+id+'/usage',{headers:{accept:'application/json'}})).json();
                return{email:e,orgId:id,usage:u};
            }catch(ex){return{error:String(ex)};}})()";

            var json = await Browser.CoreWebView2.ExecuteScriptAsync(script);
            if (json != "null" && !string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                    AppSettings.Default.Email = em.GetString() ?? "";

                if (root.TryGetProperty("orgId", out var oi) && oi.ValueKind == JsonValueKind.String)
                    AppSettings.Default.OrgId = oi.GetString() ?? "";

                if (root.TryGetProperty("usage", out var us) && us.ValueKind == JsonValueKind.Object)
                    AppSettings.Default.UsageJson = us.GetRawText();

                AppSettings.Default.Save();
            }
        }
        catch { }

        await Dispatcher.InvokeAsync(() => DialogResult = true);
    }
}
