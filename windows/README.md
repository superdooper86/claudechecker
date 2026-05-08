# ClaudeChecker for Windows

A native Windows port of ClaudeChecker — a system tray app for monitoring your Claude AI usage limits in real time.

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

## Building

```powershell
cd windows
dotnet build -c Release
```

The output is a single `ClaudeChecker.exe` in `bin\Release\net8.0-windows\`.

> **Assets:** You need to supply `Assets/icon.ico` and `Assets/icon.png` (128×128) before building.
> Copy them from the macOS project's `ClaudeChecker/Assets.xcassets/AppIcon.appiconset/`.

## Features

- System tray icon — left-click to show/hide the popup
- 5-hour and 7-day usage gauges with burn rate sparklines
- Cookie-based auth via embedded WebView2 browser (sign in once, session persists)
- Auto-refresh on a configurable interval (1–10 min)
- Update checking against the same `version.json` / `version-beta.json` as the macOS app
- Beta channel toggle
- Settings panel (auth, refresh interval, update channel)

## Architecture

| File | Purpose |
|---|---|
| `App.xaml.cs` | Tray icon, popup lifecycle, refresh timer |
| `PopupWindow.xaml/cs` | Main UI — limit cards, settings, update sheet |
| `LoginWindow.xaml/cs` | WebView2 browser for claude.ai auth |
| `UsageViewModel.cs` | Data fetching, cookie handling, state |
| `UpdateManager.cs` | Version checking, download and install |
| `Models.cs` | Data models |
| `Controls/GaugeControl.cs` | Custom circular gauge |
| `Controls/SparklineControl.cs` | Burn history sparkline |

## Notes

- Cookies are stored in user settings (via `Properties.Settings`), not the WebView2 profile, so they survive WebView2 cache clears.
- Updates download a `.zip`, extract, and run a `.bat` script that replaces the exe after the app exits.
- The popup positions itself in the bottom-right corner near the system tray.
