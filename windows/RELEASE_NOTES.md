## What's new in beta.30

- Fix: removed WebView2 from periodic refresh — it was spawning a full Chromium process every 2 minutes and not cleaning up, causing ~1.5GB memory growth
- Fix: added Origin, Referer, sec-fetch-* and sec-ch-ua headers to HttpClient so the API accepts requests as browser fetches
- Periodic refresh now uses HttpClient only, falling back to data cached at login time
- To get limits showing: sign out and sign back in once so SaveAndClose can fetch usage with the corrected org ID path
