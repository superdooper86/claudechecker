## What's new in v1.1.2

### Cleaner usage cards
Removed the redundant "5h" / "7d" labels from each card row — the section headers already make it clear which limit is which.

### Beta update support
The app now correctly detects and compares beta versions (e.g. v1.1.2-beta.2 > v1.1.2-beta.1) when the beta channel is enabled in Settings. Stable releases also correctly supersede beta builds of the same version — so users on a beta are prompted to install the final release.

### Fixed crash when enabling beta channel
Enabling the beta toggle could crash the app if no stable update was available. This is now fixed.

### Fixed settings panel truncation
The app name and version number no longer get cut off when an update badge is displayed in Settings.
