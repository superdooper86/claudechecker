import SwiftUI
import WebKit

struct DiagnosticsView: View {
    @EnvironmentObject var vm: UsageViewModel
    @Binding var isPresented: Bool
    @State private var liveAllCookies: [HTTPCookie] = []
    @State private var loadingCookies = false
    @State private var copied = false

    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Text("Diagnostics")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                Button("Copy All") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(fullDiagText, forType: .string)
                    copied = true
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { copied = false }
                }
                .buttonStyle(.bordered)
                .controlSize(.small)
                if copied {
                    Text("Copied! (IDs redacted)")
                        .font(.system(size: 11))
                        .foregroundColor(.green)
                }
                Button("Close") { isPresented = false }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .background(Color(nsColor: .windowBackgroundColor))

            Divider()

            Text("Email and org IDs are redacted when copied.")
                .font(.system(size: 10))
                .foregroundColor(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 14)
                .padding(.vertical, 6)

            Divider()

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {

                    // App info
                    DiagSection(title: "App") {
                        DiagRow("Version", Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "?")
                        DiagRow("Build",   Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "?")
                        DiagRow("Signed in", vm.isSignedIn ? "Yes" : "No")
                        DiagRow("Org ID",  UserDefaults.standard.string(forKey: "claude_org_id") ?? "(none)")
                        DiagRow("lastActiveOrg", vm.diagLastActiveOrg.isEmpty ? "(not read)" : vm.diagLastActiveOrg)
                        DiagRow("Error",   vm.errorMessage ?? "(none)")
                    }

                    Divider()

                    // Last request
                    DiagSection(title: "Last Request") {
                        DiagRow("Path",    vm.diagLastPath.isEmpty ? "(none)" : vm.diagLastPath)
                        DiagRow("Status",  vm.diagLastStatus == 0 ? "(none)" : "\(vm.diagLastStatus)")
                        DiagRow("Error",   vm.diagLastError.isEmpty ? "(none)" : vm.diagLastError)
                        if let t = vm.diagLastFetch {
                            DiagRow("Time", t.formatted(date: .omitted, time: .standard))
                        }
                    }

                    Divider()

                    // Cookie summary
                    DiagSection(title: "Cookie Store") {
                        DiagRow("Total cookies",       "\(vm.diagCookieCount)")
                        DiagRow("claude.ai cookies",   "\(vm.diagClaudeCookieCount)")
                        DiagRow("All domains", vm.diagCookieDomains.isEmpty
                            ? "(none — never fetched)"
                            : vm.diagCookieDomains.joined(separator: ", "))
                    }

                    Divider()

                    // Live cookie list
                    DiagSection(title: "Live Cookie Names (WKWebsiteDataStore.default)") {
                        if loadingCookies {
                            ProgressView().scaleEffect(0.7)
                        } else if liveAllCookies.isEmpty {
                            Text("No cookies found")
                                .font(.system(size: 11))
                                .foregroundColor(.secondary)
                        } else {
                            let claudeCookies = liveAllCookies.filter {
                                $0.domain.contains("claude.ai") || $0.domain.contains("anthropic.com")
                            }
                            let otherCookies = liveAllCookies.filter {
                                !$0.domain.contains("claude.ai") && !$0.domain.contains("anthropic.com")
                            }
                            if !claudeCookies.isEmpty {
                                Text("claude.ai / anthropic.com (\(claudeCookies.count))")
                                    .font(.system(size: 10, weight: .medium))
                                    .foregroundColor(.orange)
                                ForEach(claudeCookies, id: \.name) { c in
                                    Text("• \(c.domain)  \(c.name)")
                                        .font(.system(size: 10, design: .monospaced))
                                        .foregroundColor(.secondary)
                                }
                            }
                            if !otherCookies.isEmpty {
                                Text("Other domains (\(otherCookies.count))")
                                    .font(.system(size: 10, weight: .medium))
                                    .foregroundColor(.secondary)
                                    .padding(.top, 4)
                                ForEach(otherCookies.prefix(10), id: \.name) { c in
                                    Text("• \(c.domain)  \(c.name)")
                                        .font(.system(size: 10, design: .monospaced))
                                        .foregroundColor(Color.secondary.opacity(0.6))
                                }
                                if otherCookies.count > 10 {
                                    Text("… and \(otherCookies.count - 10) more")
                                        .font(.system(size: 10))
                                        .foregroundColor(Color.secondary.opacity(0.5))
                                }
                            }
                        }

                        Button("Refresh cookies") { loadLiveCookies() }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                            .padding(.top, 4)
                    }
                }
                .padding(14)
            }
        }
        .frame(width: 480, height: 560)
        .onAppear { loadLiveCookies() }
    }

    private func loadLiveCookies() {
        loadingCookies = true
        WKWebsiteDataStore.default().httpCookieStore.getAllCookies { cookies in
            DispatchQueue.main.async {
                liveAllCookies = cookies.sorted { $0.domain < $1.domain }
                loadingCookies = false
            }
        }
    }

    private var fullDiagText: String {
        var lines: [String] = []
        lines.append("=== ClaudeChecker Diagnostics ===")
        lines.append("Version: \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "?") (\(Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "?"))")
        lines.append("Signed in: \(vm.isSignedIn ? "Yes" : "No")")
        lines.append("Error: \(vm.errorMessage ?? "(none)")")
        lines.append("")
        lines.append("Last path: \(vm.diagLastPath)")
        lines.append("Last status: \(vm.diagLastStatus)")
        lines.append("Last error: \(vm.diagLastError)")
        lines.append("Total cookies: \(vm.diagCookieCount)")
        lines.append("Claude cookies: \(vm.diagClaudeCookieCount)")
        lines.append("Domains: \(vm.diagCookieDomains.joined(separator: ", "))")
        lines.append("")
        lines.append("")
        lines.append("Live cookies:")
        for c in liveAllCookies {
            lines.append("  \(c.domain)  \(c.name)  httpOnly=\(c.isHTTPOnly)  secure=\(c.isSecure)")
        }
        return redactUUIDs(lines.joined(separator: "\n"))
    }

    private func redactUUIDs(_ text: String) -> String {
        let pattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return text }
        let range = NSRange(text.startIndex..., in: text)
        return regex.stringByReplacingMatches(in: text, range: range, withTemplate: "****")
    }
}

// MARK: - Helpers

private struct DiagSection<Content: View>: View {
    let title: String
    @ViewBuilder let content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.secondary)
                .textCase(.uppercase)
            content()
        }
    }
}

private struct DiagRow: View {
    let label: String
    let value: String

    init(_ label: String, _ value: String) {
        self.label = label
        self.value = value
    }

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Text(label)
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .frame(width: 100, alignment: .leading)
            Text(value)
                .font(.system(size: 11, design: .monospaced))
                .foregroundColor(.primary)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
