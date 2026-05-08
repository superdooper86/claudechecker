## What's new in beta.24

- Sign-in now shows email and data immediately after login (loads from cache set during login flow)
- Background refresh runs 3 seconds after login — avoids WebView2 user data folder lock race
- Added /api/organizations and personal /api/usage fallbacks when bootstrap has no org ID
- Broader JS paths for email and org ID
