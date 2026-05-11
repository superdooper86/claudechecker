## What's new in v1.2.1

### Bug fixes
- Fixed usage data not loading after sign-in — API calls now run inside a persistent background WebView using the page's own fetch(), so all credentials (cookies, localStorage tokens, etc.) are included automatically
- Fixed "Not signed in" showing after login — the background WebView is now reloaded after sign-in to pick up the new session before the first data refresh
- Fixed "Not signed in" showing incorrectly on launch when the session was already active
- Sign-in state is now detected immediately from stored cookies on startup, before the first data refresh completes
- Fixed login window auto-closing before the user could sign in — the login window now correctly loads the `/login` page so it only detects auth after the actual sign-in redirect
- Fixed "No API key configured" showing after signing out — now correctly shows "Not signed in" with a prompt to sign in
- Added `/api/organizations` as a final fallback for org ID resolution when the bootstrap API response doesn't include it
- Fixed Settings incorrectly showing "Signed in" after a failed refresh — sign-in state now resets when authentication fails
- Rewrote login detection to use KVO on the WebView URL — correctly detects auth for Next.js SPA navigation (history.pushState) that doesn't trigger didFinish
