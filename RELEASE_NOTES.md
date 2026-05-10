## What's new in v1.2.1

### Bug fixes
- Fixed "Not signed in" showing incorrectly on launch when the session was already active
- Sign-in state is now detected immediately from stored cookies on startup, before the first data refresh completes
- Fixed Re-authenticate (and Sign In) not detecting an existing session — the login window now loads `claude.ai` directly so already-signed-in users are detected correctly and the window auto-dismisses
- Fixed "No API key configured" showing after signing out — now correctly shows "Not signed in" with a prompt to sign in
- Added `/api/organizations` as a final fallback for org ID resolution when the bootstrap API response doesn't include it
- Session cookie detection now checks both `sessionKey` and `__Secure-next-auth.session-token` cookie names
