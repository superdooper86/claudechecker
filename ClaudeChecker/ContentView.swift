import SwiftUI

struct ContentView: View {
    @EnvironmentObject var vm: UsageViewModel
    @EnvironmentObject var updater: UpdateManager
    @State private var showSettings = false
    @State private var showLogin = false
    @State private var showUpdateSheet = false

    var claudeLimits: [AgentLimit] { vm.limits }

    var body: some View {
        ZStack {
            Color(nsColor: .windowBackgroundColor)
                .opacity(0.97)

            VStack(spacing: 0) {
                // Header
                PanelHeader(showSettings: $showSettings)
                    .environmentObject(vm)

                Divider().opacity(0.4)

                // Update success banner — shown after auto-relaunch
                if updater.justUpdated && !showSettings {
                    UpdateSuccessBanner(version: updater.currentVersion) {
                        updater.justUpdated = false
                    }
                }

                // Update available banner
                if updater.updateAvailable && !showSettings {
                    UpdateBanner(showSheet: $showUpdateSheet)
                        .environmentObject(updater)
                }

                if showSettings {
                    SettingsView(showSettings: $showSettings, showUpdateSheet: $showUpdateSheet)
                        .environmentObject(vm)
                        .environmentObject(updater)
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                } else {
                    ScrollView(.vertical, showsIndicators: false) {
                        VStack(spacing: 0) {
                            // Cards grid
                            if vm.limits.isEmpty {
                                EmptyStateView()
                                    .padding(.vertical, 40)
                            } else {
                                VStack(spacing: 1) {
                                    ForEach(vm.limits) { limit in
                                        LimitCard(limit: limit)
                                    }
                                }
                                .background(Color.primary.opacity(0.05))
                                .padding(.vertical, 1)

                                // Extra credits row
                                if let extra = vm.extraUsage, extra.isEnabled {
                                    ExtraUsageRow(extra: extra, prepaid: vm.prepaidCredits, overage: vm.overageSpendLimit)
                                        .padding(.horizontal, 16)
                                        .padding(.top, 12)
                                }

                                // Session Diary
                                DiarySection(limits: vm.limits)
                                    .padding(.horizontal, 16)
                                    .padding(.top, 14)
                                    .padding(.bottom, 8)
                            }

                            if let err = vm.errorMessage {
                                if vm.isNotAuthenticated {
                                    SignInBanner { showLogin = true }
                                        .padding(.horizontal, 16)
                                        .padding(.bottom, 8)
                                } else {
                                    ErrorBanner(message: err)
                                        .padding(.horizontal, 16)
                                        .padding(.bottom, 8)
                                }
                            }
                        }
                    }
                    .transition(.opacity)

                    Divider().opacity(0.3)

                    // Footer
                    FooterBar()
                        .environmentObject(vm)
                }
            }
        }
        .frame(width: 480)
        .animation(.easeInOut(duration: 0.2), value: showSettings)
        .sheet(isPresented: $showLogin) {
            LoginSheetView(isPresented: $showLogin) {
                Task { await vm.refresh() }
            }
        }
        .sheet(isPresented: $showUpdateSheet) {
            UpdateSheet(isPresented: $showUpdateSheet)
                .environmentObject(updater)
        }
        .onChange(of: vm.triggerLogin) { triggered in
            if triggered {
                showLogin = true
                vm.triggerLogin = false
            }
        }
        .task {
            await updater.checkForUpdates()
        }
    }
}

// MARK: - Panel Header

struct PanelHeader: View {
    @EnvironmentObject var vm: UsageViewModel
    @Binding var showSettings: Bool

    var body: some View {
        HStack(spacing: 10) {
            // App icon
            Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                .resizable()
                .frame(width: 24, height: 24)
                .cornerRadius(6)

            Text("ClaudeChecker")
                .font(.system(size: 14, weight: .semibold))

            Spacer()

            HeaderButton(icon: "gearshape") { showSettings.toggle() }
                .background(showSettings ? Color.primary.opacity(0.08) : Color.clear)
                .cornerRadius(6)

            HeaderButton(icon: "xmark") {
                NotificationCenter.default.post(name: .closePopover, object: nil)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
    }
}

struct HeaderButton: View {
    let icon: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 11.5, weight: .regular))
                .foregroundColor(.secondary)
                .frame(width: 26, height: 26)
                .background(Color.primary.opacity(0.001))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(Color.primary.opacity(0.1), lineWidth: 0.5)
        )
    }
}

// MARK: - Limit Card

struct LimitCard: View {
    let limit: AgentLimit
    @EnvironmentObject var vm: UsageViewModel

    var accentColor: Color { .orange }

    var body: some View {
        HStack(alignment: .center, spacing: 16) {

            // Gauge
            GaugeView(percent: limit.usedPercent, color: accentColor)
                .frame(width: 76, height: 76)

            // Centre info block
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    AgentIconView(agent: .claude, size: 14)
                    Text("Claude")
                        .font(.system(size: 13, weight: .semibold))
                    Text("·  \(vm.planLabel)  ·  \(limit.window.label)")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                    Spacer()
                    if limit.isLive { LiveBadge() }
                    Text("Usg: \(limit.usageLabel)")
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(.secondary)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.primary.opacity(0.06))
                        .cornerRadius(4)
                        .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.primary.opacity(0.1), lineWidth: 0.5))
                }

                ProgressBar(value: limit.usedPercent / 100, color: accentColor)
                    .frame(height: 4)

                HStack(spacing: 16) {
                    HStack(spacing: 4) {
                        Image(systemName: "clock")
                            .font(.system(size: 10))
                            .foregroundColor(.secondary)
                        Text(limit.timeRemaining)
                            .font(.system(size: 12, weight: .medium).monospacedDigit())
                    }
                    Text("Resets \(limit.resetDate.formatted(date: .abbreviated, time: .shortened))")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                    Spacer()
                    HStack(spacing: 3) {
                        Image(systemName: "chart.line.uptrend.xyaxis")
                            .font(.system(size: 9))
                            .foregroundColor(.secondary)
                        Text(limit.projectedHit.display)
                            .font(.system(size: 11, weight: .medium))
                            .foregroundColor(.secondary)
                    }
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .background(Color(nsColor: .controlBackgroundColor))
    }
}

// MARK: - Gauge View

struct GaugeView: View {
    let percent: Double
    let color: Color

    var body: some View {
        ZStack {
            // Track
            Circle()
                .trim(from: 0.125, to: 0.875)
                .stroke(Color.primary.opacity(0.07), style: StrokeStyle(lineWidth: 6, lineCap: .round))
                .rotationEffect(.degrees(90))

            // Fill
            Circle()
                .trim(from: 0.125, to: 0.125 + 0.75 * (percent / 100))
                .stroke(color, style: StrokeStyle(lineWidth: 6, lineCap: .round))
                .rotationEffect(.degrees(90))
                .animation(.easeInOut(duration: 0.8), value: percent)

            // Label
            VStack(spacing: 1) {
                Text("\(Int(percent.rounded()))%")
                    .font(.system(size: 17, weight: .bold, design: .monospaced))
                    .minimumScaleFactor(0.7)
                Text("Updated\nnow")
                    .font(.system(size: 7.5))
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
            }
        }
    }
}

// MARK: - Progress Bar

struct ProgressBar: View {
    let value: Double // 0.0–1.0
    let color: Color

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 2)
                    .fill(Color.primary.opacity(0.07))
                RoundedRectangle(cornerRadius: 2)
                    .fill(color)
                    .frame(width: geo.size.width * CGFloat(min(1, max(0, value))))
                    .animation(.easeInOut(duration: 0.6), value: value)
            }
        }
    }
}

// MARK: - Live Badge

struct LiveBadge: View {
    @State private var pulse = false

    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(Color.green)
                .frame(width: 5, height: 5)
                .opacity(pulse ? 0.4 : 1.0)
                .animation(.easeInOut(duration: 1.2).repeatForever(), value: pulse)
                .onAppear { pulse = true }
            Text("Live")
                .font(.system(size: 10, weight: .medium))
                .foregroundColor(.green)
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
        .background(Color.green.opacity(0.1))
        .cornerRadius(4)
        .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.green.opacity(0.2), lineWidth: 0.5))
    }
}

// MARK: - Agent Icon

struct AgentIconView: View {
    let agent: AgentType
    let size: CGFloat

    var body: some View {
        ZStack {
            Circle()
                .fill(Color.orange.opacity(0.15))
                .frame(width: size * 1.4, height: size * 1.4)
            Text("✦")
                .font(.system(size: size * 0.7))
                .foregroundColor(.orange)
        }
    }
}

// MARK: - Session Diary

struct DiarySection: View {
    let limits: [AgentLimit]

    var sparkData: [Double] { limits.first(where: { $0.window == .fiveHour })?.burnHistory ?? [] }
    var burnRate: Double    { limits.first(where: { $0.window == .fiveHour })?.burnRate ?? 0 }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text("Session Diary")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(.secondary)
                Text("quota samples · 5h avg burn rate")
                    .font(.system(size: 10.5))
                    .foregroundColor(Color.secondary.opacity(0.6))
            }

            DiaryRow(
                totalSaved: sparkData.count,
                windowSaved: sparkData.count,
                burnRate: burnRate,
                sparkData: sparkData,
                color: .orange
            )
        }
    }
}

struct DiaryRow: View {
    let totalSaved: Int
    let windowSaved: Int
    let burnRate: Double
    let sparkData: [Double]
    let color: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                AgentIconView(agent: .claude, size: 12)
                Text("Claude")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(color)
                Spacer()
                Text("\(totalSaved) samples · Avg Burn Rate: +\(String(format: "%.1f", burnRate))%/h")
                    .font(.system(size: 10.5))
                    .foregroundColor(.secondary)
            }

            SparklineView(data: sparkData, color: color)
                .frame(height: 28)
                .cornerRadius(3)
        }
    }
}

// MARK: - Sparkline

struct SparklineView: View {
    let data: [Double]
    let color: Color

    private func pt(i: Int, w: CGFloat, h: CGFloat, minV: Double, range: Double) -> CGPoint {
        let x = CGFloat(i) / CGFloat(data.count - 1) * w
        let y = h - CGFloat((data[i] - minV) / range) * (h - 6) - 3
        return CGPoint(x: x, y: y)
    }

    var body: some View {
        GeometryReader { geo in
            if data.count > 1 {
                let w = geo.size.width
                let h = geo.size.height
                let minV = data.min() ?? 0
                let maxV = data.max() ?? 1
                let range = max(maxV - minV, 1)

                ZStack {
                    Path { path in
                        path.move(to: CGPoint(x: 0, y: h))
                        for i in 0..<data.count {
                            path.addLine(to: pt(i: i, w: w, h: h, minV: minV, range: range))
                        }
                        path.addLine(to: CGPoint(x: w, y: h))
                        path.closeSubpath()
                    }
                    .fill(color.opacity(0.1))

                    Path { path in
                        path.move(to: pt(i: 0, w: w, h: h, minV: minV, range: range))
                        for i in 1..<data.count {
                            path.addLine(to: pt(i: i, w: w, h: h, minV: minV, range: range))
                        }
                    }
                    .stroke(color, style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
                }
            } else {
                Color.clear
            }
        }
        .background(Color.primary.opacity(0.03))
    }
}

// MARK: - Footer

struct FooterBar: View {
    @EnvironmentObject var vm: UsageViewModel

    var body: some View {
        HStack {
            if let updated = vm.lastUpdated {
                Text("Updated \(updated, style: .relative) ago")
                    .font(.system(size: 10.5).monospacedDigit())
                    .foregroundColor(Color.secondary.opacity(0.6))
            } else {
                Text("Not yet updated")
                    .font(.system(size: 10.5))
                    .foregroundColor(Color.secondary.opacity(0.5))
            }

            Spacer()

            if vm.isLoading {
                ProgressView()
                    .scaleEffect(0.6)
                    .frame(height: 14)
            }

            Button(action: {
                Task { await vm.refresh() }
            }) {
                HStack(spacing: 4) {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 10))
                    Text("Refresh")
                        .font(.system(size: 11))
                }
                .foregroundColor(.secondary)
                .padding(.horizontal, 8)
                .padding(.vertical, 4)
                .overlay(RoundedRectangle(cornerRadius: 5).stroke(Color.primary.opacity(0.1), lineWidth: 0.5))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
    }
}

// MARK: - Extra Usage Row

struct ExtraUsageRow: View {
    let extra: ExtraUsage
    let prepaid: PrepaidCredits?
    let overage: OverageSpendLimit?

    var cur: String { overage?.currency ?? extra.currency ?? "" }

    var spentCents: Double? { overage?.usedCredits }
    var limitCents: Double? { overage?.monthlyCreditLimit }
    var balanceCents: Double? { prepaid?.amount }

    var pct: Double {
        guard let s = spentCents, let l = limitCents, l > 0 else { return 0 }
        return min(100, max(0, (s / l) * 100))
    }
    var color: Color { pct > 80 ? .red : pct > 50 ? .orange : .green }

    func fmt(_ cents: Double?) -> String {
        guard let cents else { return "—" }
        return String(format: "%.2f", cents / 100)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "creditcard")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                Text("Extra Usage Credits")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(.secondary)
            }
            VStack(spacing: 4) {
                ExtraUsageLine(label: "Spent",   value: "\(cur) \(fmt(spentCents))",   color: color)
                ExtraUsageLine(label: "Limit",   value: limitCents != nil ? "\(cur) \(fmt(limitCents))" : "Unlimited", color: .secondary)
                ExtraUsageLine(label: "Balance", value: "\(cur) \(fmt(balanceCents))", color: .primary)
            }
            if limitCents != nil {
                ProgressBar(value: pct / 100, color: color)
                    .frame(height: 4)
            }
        }
        .padding(12)
        .background(Color.primary.opacity(0.04))
        .cornerRadius(8)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.primary.opacity(0.08), lineWidth: 0.5))
    }
}

struct ExtraUsageLine: View {
    let label: String
    let value: String
    let color: Color
    var body: some View {
        HStack {
            Text(label)
                .font(.system(size: 11))
                .foregroundColor(.secondary)
            Spacer()
            Text(value)
                .font(.system(size: 11.5, weight: .medium).monospacedDigit())
                .foregroundColor(color)
        }
    }
}

// MARK: - Settings View

struct SettingsView: View {
    @EnvironmentObject var vm: UsageViewModel
    @EnvironmentObject var updater: UpdateManager
    @Binding var showSettings: Bool
    @Binding var showUpdateSheet: Bool
    @State private var checkingForUpdates = false

    let refreshOptions: [(label: String, seconds: TimeInterval)] = [
        ("1 min",  60),
        ("2 min",  120),
        ("3 min",  180),
        ("4 min",  240),
        ("5 min",  300),
        ("10 min", 600),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Button(action: { showSettings = false }) {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                        Text("Back")
                    }
                    .font(.system(size: 12))
                    .foregroundColor(.accentColor)
                }
                .buttonStyle(.plain)
                Spacer()
            }

            Text("Settings")
                .font(.system(size: 16, weight: .semibold))

            // Auth section
            VStack(alignment: .leading, spacing: 8) {
                Label("Authentication", systemImage: "person.crop.circle")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.secondary)

                if vm.isSignedIn {
                    VStack(alignment: .leading, spacing: 2) {
                        HStack(spacing: 6) {
                            Circle()
                                .fill(Color.green)
                                .frame(width: 7, height: 7)
                            Text("Signed in to claude.ai")
                                .font(.system(size: 13, weight: .semibold))
                        }
                        if !vm.userEmail.isEmpty {
                            Text(vm.userEmail)
                                .font(.system(size: 11.5))
                                .foregroundColor(.secondary)
                                .padding(.leading, 13)
                        }
                    }
                } else {
                    HStack(spacing: 6) {
                        Circle()
                            .fill(Color.orange)
                            .frame(width: 7, height: 7)
                        Text("Not signed in")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.orange)
                    }
                }

                Text("Your session is stored locally and persists across restarts.")
                    .font(.system(size: 11.5))
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                HStack(spacing: 8) {
                    Button(action: {
                        showSettings = false
                        vm.triggerLogin = true
                    }) {
                        Label(vm.isSignedIn ? "Re-authenticate" : "Sign in", systemImage: "arrow.right.circle")
                            .font(.system(size: 12))
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.small)

                    if vm.isSignedIn {
                        Button(action: {
                            Task { await vm.signOut() }
                        }) {
                            Label("Sign out", systemImage: "rectangle.portrait.and.arrow.right")
                                .font(.system(size: 12))
                        }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                        .tint(.red)
                    }
                }
            }

            Divider()

            // Beta channel
            VStack(alignment: .leading, spacing: 8) {
                Label("Update channel", systemImage: "dot.radiowaves.left.and.right")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.secondary)

                Toggle(isOn: $updater.betaChannel) {
                    VStack(alignment: .leading, spacing: 2) {
                        HStack(spacing: 6) {
                            Text("Beta updates")
                                .font(.system(size: 12))
                            Text("BETA")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(.white)
                                .padding(.horizontal, 5)
                                .padding(.vertical, 2)
                                .background(Color.orange)
                                .cornerRadius(4)
                            if updater.betaAvailable && !updater.betaChannel {
                                Text("v\(updater.latestBetaVersion) available")
                                    .font(.system(size: 10, weight: .medium))
                                    .foregroundColor(.orange)
                            }
                        }
                        Text("Receive early access builds — may be less stable")
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                }
                .toggleStyle(.switch)
                .onChange(of: updater.betaChannel) { _ in
                    Task { await updater.checkForUpdates() }
                }
            }

            Divider()

            // Menubar display
            VStack(alignment: .leading, spacing: 8) {
                Label("Menubar display", systemImage: "menubar.rectangle")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.secondary)

                Toggle(isOn: $vm.showInMenuBar) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Show percentages in menubar")
                            .font(.system(size: 12))
                        Text("Displays 5h and 7d usage inline next to the icon")
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                }
                .toggleStyle(.switch)
            }

            Divider()

            // Refresh interval
            VStack(alignment: .leading, spacing: 8) {
                Label("Auto-refresh interval", systemImage: "clock.arrow.circlepath")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.secondary)

                Picker("", selection: $vm.refreshInterval) {
                    ForEach(refreshOptions, id: \.seconds) { option in
                        Text(option.label).tag(option.seconds)
                    }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }

            Divider()

            // Endpoints info
            VStack(alignment: .leading, spacing: 4) {
                Label("Data source", systemImage: "network")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.secondary)
                VStack(alignment: .leading, spacing: 2) {
                    Text("• /api/bootstrap")
                    Text("• /api/organizations/{id}/usage")
                    Text("• /api/organizations/{id}/overage_spend_limit")
                    Text("• /api/organizations/{id}/prepaid/credits")
                }
                .font(.system(size: 11, design: .monospaced))
                .foregroundColor(Color.secondary.opacity(0.7))
            }

            Spacer()

            Divider()

            // About
            HStack(spacing: 12) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                    .resizable()
                    .frame(width: 36, height: 36)
                    .cornerRadius(8)

                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text("ClaudeChecker")
                            .font(.system(size: 12, weight: .semibold))
                        Text("v\(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0")")
                            .font(.system(size: 11, weight: .medium, design: .monospaced))
                            .foregroundColor(.secondary)
                            .padding(.horizontal, 5)
                            .padding(.vertical, 1)
                            .background(Color.primary.opacity(0.06))
                            .cornerRadius(4)
                        if updater.updateAvailable {
                            Text("v\(updater.latestVersion) available")
                                .font(.system(size: 10, weight: .medium))
                                .foregroundColor(.white)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(Color.blue)
                                .cornerRadius(4)
                        } else if updater.betaAvailable {
                            Text("v\(updater.latestBetaVersion) beta")
                                .font(.system(size: 10, weight: .medium))
                                .foregroundColor(.white)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(Color.orange)
                                .cornerRadius(4)
                        }
                    }
                    Text("Built by James Bone & Claude")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                }

                Spacer()

                Button(action: {
                    if updater.updateAvailable {
                        showSettings = false
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                            showUpdateSheet = true
                        }
                    } else if updater.betaAvailable && !updater.betaChannel {
                        // Tease: explain they need to enable beta
                    } else {
                        checkingForUpdates = true
                        Task {
                            await updater.checkForUpdates()
                            checkingForUpdates = false
                        }
                    }
                }) {
                    if checkingForUpdates {
                        ProgressView().scaleEffect(0.6).frame(width: 16, height: 16)
                    } else if updater.updateAvailable {
                        Text("Install")
                            .font(.system(size: 11))
                    } else if updater.betaAvailable && !updater.betaChannel {
                        Text("Enable beta to install")
                            .font(.system(size: 10))
                            .foregroundColor(.orange)
                    } else {
                        Text("Check")
                            .font(.system(size: 11))
                    }
                }
                .buttonStyle(.bordered)
                .controlSize(.small)
                .disabled(updater.betaAvailable && !updater.updateAvailable && !updater.betaChannel)
            }
            .padding(.top, 4)
        }
        .padding(16)
    }
}

// MARK: - Sign In Banner

struct SignInBanner: View {
    let onSignIn: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "person.crop.circle.badge.exclamationmark")
                .foregroundColor(.orange)
                .font(.system(size: 14))
            VStack(alignment: .leading, spacing: 2) {
                Text("Not signed in")
                    .font(.system(size: 12, weight: .medium))
                Text("Sign in to claude.ai to see your usage.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            }
            Spacer()
            Button("Sign In") { onSignIn() }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .background(Color.orange.opacity(0.08))
        .cornerRadius(8)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.orange.opacity(0.2), lineWidth: 0.5))
    }
}

// MARK: - Error Banner

struct ErrorBanner: View {
    let message: String

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundColor(.orange)
                .font(.system(size: 12))
            Text(message)
                .font(.system(size: 11.5))
                .foregroundColor(.secondary)
            Spacer()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Color.orange.opacity(0.08))
        .cornerRadius(6)
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.orange.opacity(0.2), lineWidth: 0.5))
    }
}

// MARK: - Empty State

struct EmptyStateView: View {
    var body: some View {
        VStack(spacing: 10) {
            Image(systemName: "key.slash")
                .font(.system(size: 28))
                .foregroundColor(.secondary)
            Text("No API key configured")
                .font(.system(size: 13, weight: .medium))
            Text("Open Settings to add your Anthropic API key.")
                .font(.system(size: 11.5))
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: - Color Extension

extension Color {
    init(hex: String) {
        let hex = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        var int: UInt64 = 0
        Scanner(string: hex).scanHexInt64(&int)
        let r = Double((int >> 16) & 0xFF) / 255
        let g = Double((int >> 8) & 0xFF) / 255
        let b = Double(int & 0xFF) / 255
        self.init(red: r, green: g, blue: b)
    }
}

// MARK: - Update Success Banner

struct UpdateSuccessBanner: View {
    let version: String
    let onDismiss: () -> Void
    @State private var timeRemaining = 10
    @State private var timer: Timer? = nil

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "checkmark.circle.fill")
                .foregroundColor(.green)
                .font(.system(size: 16))
            VStack(alignment: .leading, spacing: 1) {
                Text("Updated to v\(version) successfully!")
                    .font(.system(size: 12, weight: .semibold))
                Text("ClaudeChecker is now up to date.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            }
            Spacer()
            Text("\(timeRemaining)s")
                .font(.system(size: 10, design: .monospaced))
                .foregroundColor(Color.secondary.opacity(0.5))
            Button(action: {
                timer?.invalidate()
                onDismiss()
            }) {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .medium))
                    .foregroundColor(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(Color.green.opacity(0.08))
        .overlay(Divider(), alignment: .bottom)
        .onAppear {
            timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in
                if timeRemaining > 1 {
                    timeRemaining -= 1
                } else {
                    timer?.invalidate()
                    onDismiss()
                }
            }
        }
        .onDisappear {
            timer?.invalidate()
        }
    }
}

// MARK: - Update Banner

struct UpdateBanner: View {
    @EnvironmentObject var updater: UpdateManager
    @Binding var showSheet: Bool

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "arrow.down.circle.fill")
                .foregroundColor(.blue)
                .font(.system(size: 16))
            VStack(alignment: .leading, spacing: 1) {
                Text("Update available — v\(updater.latestVersion)")
                    .font(.system(size: 12, weight: .semibold))
                if !updater.releaseNotes.isEmpty {
                    Text(updater.releaseNotes)
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer()
            Button("Update") { showSheet = true }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(Color.blue.opacity(0.08))
        .overlay(Divider(), alignment: .bottom)
    }
}

// MARK: - Update Sheet

struct UpdateSheet: View {
    @EnvironmentObject var updater: UpdateManager
    @Binding var isPresented: Bool

    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Text("Update ClaudeChecker")
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
                if !updater.isDownloading && !updater.updateComplete {
                    Button("Not Now") { isPresented = false }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)

            Divider()

            VStack(spacing: 20) {
                // Icon + version info
                HStack(spacing: 14) {
                    Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                        .resizable()
                        .frame(width: 48, height: 48)
                        .cornerRadius(11)

                    VStack(alignment: .leading, spacing: 4) {
                        HStack(spacing: 8) {
                            Text("v\(updater.currentVersion)")
                                .font(.system(size: 12, design: .monospaced))
                                .foregroundColor(.secondary)
                            Image(systemName: "arrow.right")
                                .font(.system(size: 10))
                                .foregroundColor(.secondary)
                            Text("v\(updater.latestVersion)")
                                .font(.system(size: 13, weight: .semibold, design: .monospaced))
                                .foregroundColor(.blue)
                        }
                        if !updater.releaseNotes.isEmpty {
                            Text(updater.releaseNotes)
                                .font(.system(size: 12))
                                .foregroundColor(.secondary)
                        }
                    }
                    Spacer()
                }

                if updater.updateComplete {
                    // Done state
                    VStack(spacing: 8) {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 32))
                            .foregroundColor(.green)
                        Text(updater.statusMessage)
                            .font(.system(size: 13, weight: .medium))
                    }
                } else if updater.isDownloading {
                    // Progress state
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text(updater.statusMessage)
                                .font(.system(size: 12))
                                .foregroundColor(.secondary)
                            Spacer()
                            Text("\(Int(updater.downloadProgress * 100))%")
                                .font(.system(size: 12, weight: .medium, design: .monospaced))
                                .foregroundColor(.secondary)
                        }
                        ProgressView(value: updater.downloadProgress)
                            .progressViewStyle(.linear)
                            .tint(.blue)
                    }
                } else {
                    // Ready to install
                    VStack(spacing: 10) {
                        if let err = updater.updateError {
                            HStack(spacing: 6) {
                                Image(systemName: "exclamationmark.triangle.fill")
                                    .foregroundColor(.orange)
                                Text(err)
                                    .font(.system(size: 11.5))
                                    .foregroundColor(.secondary)
                            }
                        }
                        Text("ClaudeChecker will download the update, replace itself, and relaunch automatically.")
                            .font(.system(size: 11.5))
                            .foregroundColor(.secondary)
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)

                        Button(action: {
                            Task { await updater.downloadAndInstall() }
                        }) {
                            HStack {
                                Image(systemName: "arrow.down.circle.fill")
                                Text(updater.updateError != nil ? "Try Again" : "Install Update")
                                    .fontWeight(.semibold)
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.large)
                    }
                }
            }
            .padding(20)
        }
        .frame(width: 380)
    }
}
