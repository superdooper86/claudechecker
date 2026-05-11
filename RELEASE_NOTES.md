## What's new in v1.2.1

### Bug fixes
- Fixed "Not signed in" showing incorrectly on launch when the session was already active
- Sign-in state is now detected immediately from stored cookies on startup, before the first data refresh completes
- Fixed login window auto-closing before the user could sign in — the login window now correctly loads the `/login` page so it only detects auth after the actual sign-in redirect
- Fixed "No API key configured" showing after signing out — now correctly shows "Not signed in" with a prompt to sign in
- Added `/api/organizations` as a final fallback for org ID resolution when the bootstrap API response doesn't include it
- Fixed usage data not loading after sign-in — API requests now include required browser-like headers (Origin, Referer, User-Agent) that Claude's usage endpoints require
