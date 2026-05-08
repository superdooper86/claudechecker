## What's new in beta.23

- About section now shows "vX.X.X available" label when an update is available (not just button label change)
- Sign-in: fixed app showing signed out after login window closes — cookies and cached auth now correctly set IsSignedIn=true
- Sign-in: added brief delay before refresh so WebView2 user data folder is fully released by LoginWindow before reuse
- Wider cookie domain filter (anthropic.com included alongside claude.ai)
- Broader JS paths for email and org ID in bootstrap response
