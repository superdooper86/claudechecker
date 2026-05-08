## What's new in beta.20

- Fixed: app now correctly shows you as signed in after login
- Fixed: org ID is fetched from API rather than hardcoded; tries multiple JSON paths
- Fixed: auth check now uses HTTP 200 from bootstrap (not whether org ID was found)
- Added browser User-Agent header so API responds correctly
