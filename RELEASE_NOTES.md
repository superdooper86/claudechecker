## What's new in v1.2.1

### Bug fixes
- Fixed premature login detection when signing in with Google/OAuth: the login window was closing the moment the browser navigated to the OAuth provider (e.g. accounts.google.com), before the sign-in actually completed and before any session cookie was set. Auth is now only detected when the browser returns to claude.ai/anthropic.com at a non-login page.
- Fixed the root cause of "Not signed in" errors: URLSession was silently discarding the manually-set Cookie header because `httpShouldHandleCookies` defaults to `true`, which makes URLSession replace it with its own (empty) HTTPCookieStorage. Setting `httpShouldHandleCookies = false` ensures cookies from WKWebsiteDataStore are actually sent.
- Added browser-like request headers (User-Agent, Origin, Referer) matching what Claude's API expects
- Fixed login window auto-closing before the user could sign in — login window loads `/login` so detection only fires after the actual sign-in redirect
- Fixed login detection for Next.js SPA navigation using KVO on WebView URL
- Added `/api/organizations` as a final fallback for org ID resolution
- Fixed Settings incorrectly showing "Signed in" after a failed refresh
