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

        UsageViewModel.SaveCookies(cookies);

        try
        {
            // Use WebMessageReceived so the async script can post back without relying
            // on Promise-awaiting support in the WebView2 runtime version.
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2WebMessageReceivedEventArgs>? msgHandler = null;
            msgHandler = (_, args) =>
            {
                Browser.CoreWebView2.WebMessageReceived -= msgHandler;
                tcs.TrySetResult(args.WebMessageAsJson);
            };
            Browser.CoreWebView2.WebMessageReceived += msgHandler;

            const string script = @"(async()=>{try{
                const h={headers:{accept:'application/json'}};
                const b=await(await fetch('/api/bootstrap',h)).json();
                const bkeys=Object.keys(b||{}).join(',');
                const akeys=Object.keys(b?.account||{}).join(',');
                let id=b?.memberships?.[0]?.organization?.uuid||b?.organizations?.[0]?.uuid
                        ||b?.default_organization?.uuid||null;
                const e=b?.account?.email_address||b?.account?.email||b?.email||null;
                let orgSrc='bootstrap';
                if(!id){
                    try{const ol=await(await fetch('/api/organizations',h)).json();
                        if(Array.isArray(ol)&&ol.length>0){id=ol[0]?.uuid||null;orgSrc='orgs-list';}}catch(e2){}
                }
                if(!id){
                    let pu=null;
                    try{pu=await(await fetch('/api/usage',h)).json();}catch(e3){}
                    window.chrome.webview.postMessage({email:e,orgId:null,usage:(pu&&!pu.error?pu:null),debug:'no-org|bkeys:'+bkeys+'|akeys:'+akeys});
                    return;
                }
                const u=await(await fetch('/api/organizations/'+id+'/usage',h)).json();
                window.chrome.webview.postMessage({email:e,orgId:id,usage:u,debug:'orgSrc:'+orgSrc+'|bkeys:'+bkeys+'|akeys:'+akeys});
            }catch(ex){window.chrome.webview.postMessage({error:String(ex)});}})()";

            await Browser.CoreWebView2.ExecuteScriptAsync(script);

            // Wait up to 15 s for the script to post its message
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(15000));
            var json = completed == tcs.Task ? tcs.Task.Result : null;

            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                    AppSettings.Default.Email = em.GetString() ?? "";

                if (root.TryGetProperty("orgId", out var oi) && oi.ValueKind == JsonValueKind.String)
                    AppSettings.Default.OrgId = oi.GetString() ?? "";

                if (root.TryGetProperty("usage", out var us) && us.ValueKind == JsonValueKind.Object)
                    AppSettings.Default.UsageJson = us.GetRawText();

                if (root.TryGetProperty("debug", out var dbg) && dbg.ValueKind == JsonValueKind.String)
                    AppSettings.Default.DebugInfo = dbg.GetString() ?? "";
                else if (root.TryGetProperty("error", out var err))
                    AppSettings.Default.DebugInfo = "JS error: " + err.GetRawText();
                else
                    AppSettings.Default.DebugInfo = "timeout or empty message";

                AppSettings.Default.Save();
            }
            else
            {
                AppSettings.Default.DebugInfo = json == null ? "script timeout (15s)" : "empty message";
                AppSettings.Default.Save();
            }
        }
        catch (Exception ex)
        {
            AppSettings.Default.DebugInfo = "exception: " + ex.Message;
            AppSettings.Default.Save();
        }

        await Dispatcher.InvokeAsync(() => DialogResult = true);
    }
}
