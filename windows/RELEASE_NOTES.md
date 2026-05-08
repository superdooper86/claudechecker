## What's new in beta.26

- Fixed: script results now use window.chrome.webview.postMessage instead of async IIFE return value — older WebView2 runtimes don't await Promises from ExecuteScriptAsync, so data was never received
- Debug text in Settings will now always show what happened during sign-in
