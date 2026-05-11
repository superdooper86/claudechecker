## What's new in beta.62

### Installer
- App now ships as a proper Windows installer (no more zip extract)
- Installs to user AppData — no admin rights required
- Auto-update now downloads and silently runs the new installer, then relaunches
- Desktop shortcut creation is optional during install

### Bug fixes
- Fixed app crash on launch (startup DllNotFoundException caused by single-file publish; all DLLs now installed alongside the exe)
- Fixed clicking Copy in Diagnostics crashing the app (CLIPBRD_E_CANT_OPEN)
- Fixed Sign In — Done button now always clickable; session detection no longer depends on specific cookie names that may have changed

### Diagnostics
- Org IDs are now redacted when copying diagnostics text
- Removed stale Cookie Store section (always showed 0)
- Fixed missing fields in copied diagnostics output
- Privacy note added below diagnostics header
