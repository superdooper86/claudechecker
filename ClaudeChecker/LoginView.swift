import SwiftUI
import WebKit

// MARK: - Login Web View

struct LoginWebView: NSViewRepresentable {
    let onAuthenticated: (WKWebView) -> Void

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default()

        let webView = WKWebView(frame: .zero, configuration: config)
        webView.navigationDelegate = context.coordinator
        context.coordinator.webView = webView

        // KVO on url catches SPA pushState navigations that don't fire didFinish
        context.coordinator.urlObservation = webView.observe(\.url, options: [.new]) { [weak coordinator = context.coordinator] wv, _ in
            coordinator?.checkCurrentURL(wv.url?.absoluteString)
        }

        webView.load(URLRequest(url: URL(string: "https://claude.ai/login")!))
        return webView
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}

    func makeCoordinator() -> Coordinator {
        Coordinator(onAuthenticated: onAuthenticated)
    }

    class Coordinator: NSObject, WKNavigationDelegate {
        let onAuthenticated: (WKWebView) -> Void
        weak var webView: WKWebView?
        var didAuthenticate = false
        var urlObservation: NSKeyValueObservation?

        init(onAuthenticated: @escaping (WKWebView) -> Void) {
            self.onAuthenticated = onAuthenticated
        }

        func checkCurrentURL(_ url: String?) {
            guard !didAuthenticate, let url, let wv = webView else { return }
            // Ignore navigations to external OAuth providers (Google, etc.) —
            // only consider auth complete when we land back on claude.ai/anthropic.com
            guard url.contains("claude.ai") || url.contains("anthropic.com") else { return }
            if url.contains("/login") || url.contains("/auth") { return }
            didAuthenticate = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                self.onAuthenticated(wv)
            }
        }

        // Covers full cross-document navigations
        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            checkCurrentURL(webView.url?.absoluteString)
        }
    }
}

// MARK: - Login Sheet View

struct LoginSheetView: View {
    @Binding var isPresented: Bool
    let onDone: (WKWebView) -> Void
    @State private var authenticated = false

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(authenticated ? "✓ Signed in — loading data…" : "Sign in to Claude")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                if authenticated {
                    Button("Done") { isPresented = false }
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

            LoginWebView { webView in
                authenticated = true
                // Adopt the authenticated WebView immediately (before sheet tears it down),
                // then auto-dismiss after a moment so the user sees confirmation.
                onDone(webView)
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    isPresented = false
                }
            }
        }
        .frame(width: 460, height: 560)
    }
}
