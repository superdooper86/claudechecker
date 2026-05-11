## What's new in v1.2.1-beta.17

### Bug fixes
- Fixed "Not signed in" after login by adopting the login WebView directly for API calls — the login WebView is proven-authenticated (user just completed sign-in in it), so reusing it eliminates the problem where a separately-loaded background WebView might not have the full auth context (localStorage tokens, Service Worker state) that claude.ai requires
- Added diagnostic error messages: JS errors, WebView URL, and unexpected response types are now surfaced in the error banner to aid future debugging

## What's new in v1.2.1

### Bug fixes
- Fixed the root cause of "Not signed in" after being clearly signed in: claude.ai's auth requires credentials beyond plain HTTP cookies (localStorage tokens, Service Worker state, etc.) that URLSession cannot access. All API calls now run via callAsyncJavaScript inside a background WKWebView, using the same fetch path the page itself uses — credentials are included automatically.
- Fixed WebKit suspending the background WKWebView: a WKWebView with no window is throttled/suspended by macOS, preventing JS execution. The background WebView is now anchored in a transparent 1×1 NSWindow, keeping it active.
- Fixed login window auto-closing before sign-in completes — login window loads `/login` and only detects auth when back on claude.ai (not on OAuth provider redirects)
- Added `/api/organizations` as a final fallback for org ID resolution
- Fixed Settings incorrectly showing "Signed in" after a failed refresh
