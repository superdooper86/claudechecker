## What's new in v1.2.1

### Bug fixes
- Fixed the root cause of "Not signed in" errors: URLSession was silently discarding the manually-set Cookie header because `httpShouldHandleCookies` defaults to `true`, which makes URLSession replace it with its own (empty) HTTPCookieStorage — claude.ai session cookies live in WKWebsiteDataStore, not HTTPCookieStorage. Setting `httpShouldHandleCookies = false` ensures the cookies are actually sent.
- Added browser-like request headers (User-Agent, Origin, Referer) matching what Claude's API expects, consistent with the working Windows implementation
- Removed background WKWebView complexity added in beta.12–13 — reverted to simple URLSession approach with correct cookie handling
- Fixed login window auto-closing before the user could sign in — login window loads `/login` so auth is only detected after the actual sign-in redirect
- Fixed login detection for Next.js SPA navigation using KVO on WebView URL (history.pushState doesn't trigger didFinish)
- Added `/api/organizations` as a final fallback for org ID resolution
- Fixed Settings incorrectly showing "Signed in" after a failed refresh
