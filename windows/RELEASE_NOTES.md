## What's new in beta.21

- Fixed: clicking Sign In now auto-closes as soon as you are detected as signed in (no need to click Done)
- Fixed: login now fetches and caches your data directly via the browser session (no more authentication failures)
- Fixed: periodic refresh falls back to browser-session API calls when HttpClient is rejected
- Fixed: cached usage from login is shown immediately while refreshing
