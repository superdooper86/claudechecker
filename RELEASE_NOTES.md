## What's new in v1.2.1

### Bug fixes
- Fixed the root cause of "Not signed in" after being clearly signed in: claude.ai's auth requires credentials beyond plain HTTP cookies (localStorage tokens, Service Worker state, etc.) that URLSession cannot access. All API calls now run via callAsyncJavaScript inside a background WKWebView, using the same fetch path the page itself uses — credentials are included automatically.
- Fixed WebKit suspending the background WKWebView: a WKWebView with no window is throttled/suspended by macOS, preventing JS execution. The background WebView is now anchored in a transparent 1×1 NSWindow, keeping it active.
- Fixed login window auto-closing before sign-in completes — login window loads `/login` and only detects auth when back on claude.ai (not on OAuth provider redirects)
- Added `/api/organizations` as a final fallback for org ID resolution
- Fixed Settings incorrectly showing "Signed in" after a failed refresh
