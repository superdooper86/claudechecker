## What's new in v1.2.1-beta.18

### Bug fixes
- Replaced callAsyncJavaScript fetch approach with WebKit navigation: instead of running `fetch()` in the page's JS context (which was returning 401 because it bypasses the SPA's auth interceptors), each API call now navigates the WebView to the API URL directly. WebKit sends full browser headers and cookies automatically at the HTTP layer, the same way a real browser navigation works. This is more reliable regardless of what server-side auth mechanism claude.ai uses.
- API calls are now sequential to share a single WebView for all navigations
- Added redirect detection: if WebKit follows a 302 to /login, the response is treated as an auth failure rather than returning HTML to the JSON parser

## What's new in v1.2.1-beta.17

### Bug fixes
- Fixed "Not signed in" after login by adopting the login WebView directly for API calls
- Added diagnostic error messages for JS errors and unexpected responses

## What's new in v1.2.1

### Bug fixes
- Fixed WebKit suspending the background WKWebView: anchored in a transparent 1×1 NSWindow
- Fixed login window auto-closing before sign-in completes
- Added /api/organizations as a final fallback for org ID resolution
- Fixed Settings incorrectly showing "Signed in" after a failed refresh
