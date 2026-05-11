## What's new in v1.3.0

### Multi-org support
- App now uses the `lastActiveOrg` cookie to identify which organisation is active, matching exactly what the claude.ai frontend does. Previously the app always picked the first org returned by bootstrap, which is the personal "Individual Org" for users with multiple organisations — causing persistent 403 errors on all usage endpoints.
- Plan label (e.g. **Max**, **Pro**, **Team**) now reads capabilities from the active org, not the first membership.

### Reliable API access via WebView
- All API calls now run through `WKWebView.callAsyncJavaScript`, making real same-origin browser requests instead of URLSession. Claude.ai's org-specific endpoints reject native HTTP clients (403) even with correct cookies and headers; running inside the browser context passes all server-side checks.
- A background WebView is created at startup when session cookies are detected, so usage data loads immediately without requiring a manual sign-in.

### Diagnostics
- New **Diagnostics** panel in Settings shows cookie store, last API request/response, bootstrap body, and the `lastActiveOrg` cookie value — making it easy to report issues.
