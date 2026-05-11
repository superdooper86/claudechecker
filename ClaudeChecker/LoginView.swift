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
        let onAuthenticated: () -> Void
        var didAuthenticate = false
        var urlObservation: NSKeyValueObservation?

        init(onAuthenticated: @escaping () -> Void) {
            self.onAuthenticated = onAuthenticated
        }

        func checkCurrentURL(_ url: String?) {
            guard !didAuthenticate, let url else { return }
            // Ignore navigations to external OAuth providers (Google, etc.) —
            // only consider auth complete when we land back on claude.ai/anthropic.com
            guard url.contains("claude.ai") || url.contains("anthropic.com") else { return }
            if url.contains("/login") || url.contains("/auth") { return }
            didAuthenticate = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                self.onAuthenticated()
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
