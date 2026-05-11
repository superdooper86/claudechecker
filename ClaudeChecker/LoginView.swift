import SwiftUI
import WebKit

// MARK: - Login Web View

struct LoginWebView: NSViewRepresentable {
    let onAuthenticated: () -> Void

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default()

        let webView = WKWebView(frame: .zero, configuration: config)
        webView.navigationDelegate = context.coordinator
        webView.load(URLRequest(url: URL(string: "https://claude.ai/login")!))
        return webView
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}

    func makeCoordinator() -> Coordinator {
        Coordinator(onAuthenticated: onAuthenticated)
    }

    class Coordinator: NSObject, WKNavigationDelegate {
        let onAuthenticated: () -> Void
        var didAuthenticate = false

        init(onAuthenticated: @escaping () -> Void) {
            self.onAuthenticated = onAuthenticated
        }

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            guard !didAuthenticate else { return }
            // Don't fire on the login/auth pages themselves
            if let url = webView.url?.absoluteString,
               url.contains("/login") || url.contains("/auth") { return }

            // Ask the WebView itself whether we're authenticated — it uses its own
            // session (cookies, localStorage, etc.) so we don't need to know the
            // cookie domain or name.
            webView.callAsyncJavaScript(
                "const r = await fetch('/api/bootstrap', {credentials: 'include'}); return r.status;",
                arguments: [:], in: nil, in: .defaultClient
            ) { [weak self] result in
                guard let self, !self.didAuthenticate else { return }
                if case .success(let val) = result, let status = val as? Int, status == 200 {
                    self.didAuthenticate = true
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                        self.onAuthenticated()
                    }
                }
            }
        }
    }
}

// MARK: - Login Sheet View

struct LoginSheetView: View {
    @Binding var isPresented: Bool
    let onDone: () -> Void
    @State private var authenticated = false

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(authenticated ? "✓ Signed in — loading data…" : "Sign in to Claude")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                if authenticated {
                    Button("Done") {
                        isPresented = false
                        onDone()
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.small)
                } else {
                    Button("Cancel") { isPresented = false }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .background(Color(nsColor: .windowBackgroundColor))

            Divider()

            LoginWebView {
                authenticated = true
                // Auto-dismiss and refresh after brief delay
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    isPresented = false
                    onDone()
                }
            }
        }
        .frame(width: 460, height: 560)
    }
}
