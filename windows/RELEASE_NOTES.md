## What's new in beta.49

### Architecture
- All data (usage, plan, overage, prepaid credits) now fetched via the persistent background WebView2 — the same always-live session as the macOS app, so Cloudflare cookies never expire between refreshes
- HttpClient path kept only as a brief startup fallback before the browser is ready
- Settings file simplified: only stores session signal, burn history, and user preferences — no more cached API responses

### Bug fixes
- Plan label (e.g. "Pro") now updates on every refresh, not just at login
- Extra Usage Credits values now correct (were 100× too large)
- Limit and balance now update live on every refresh
- Refresh interval dropdown now shows "1 min", "2 min" etc. correctly
- ComboBox no longer flashes bright blue when clicked
- Buttons now show a visible pressed state
- Footer "Updated X ago" counter now ticks every second; shows "X min, Y sec ago" past 60 s
- Removed stray sparkline bars from the 5 h and 7 d limit cards
- App window now shown on launch

### UI improvements
- Gauge percentage larger, "used" label removed
- Each limit card now shows an "after reset" or "today HH:MM" badge
- Session Diary card redesigned to match macOS layout (stats row + sparkline, no Claude header)
- Reset date moved inline with time remaining
