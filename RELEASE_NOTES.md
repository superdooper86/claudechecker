## What's new in v1.2.0

### Bug fixes
- App now works for all users — org ID is fetched dynamically from the API instead of being hardcoded
- Plan name (e.g. Pro, Max) now updates correctly on every refresh

### Improvements
- Plan name is read from the API rather than hardcoded
- Bootstrap API call consolidated — org ID, email, and plan name fetched in a single request per refresh
- Main panel no longer scrolls — popover auto-sizes to fit content
- Session Diary card redesigned — sample count and avg burn rate shown left/right above the sparkline, Claude header removed
