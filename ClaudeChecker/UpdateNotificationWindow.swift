import SwiftUI
import AppKit

// MARK: - Compact update notification window (shown on relaunch after update)

class UpdateNotificationWindowController: NSWindowController {
    static var shared: UpdateNotificationWindowController?
    static var sharedLimit: UpdateNotificationWindowController?

    // Called after successful install + relaunch
    static func show(version: String, notes: String, near statusItem: NSStatusItem?) {
        showWindow(version: version, subtitle: notes.isEmpty ? "ClaudeChecker is up to date." : notes, near: statusItem, isUpdate: false)
    }

    // Called when a new update is detected while running
    static func showUpdate(version: String, notes: String, near statusItem: NSStatusItem?) {
        showWindow(version: version, subtitle: notes.isEmpty ? "Tap to update now." : notes, near: statusItem, isUpdate: true)
    }

    static func showLimitWarning(windowName: String, percent: Int, near statusItem: NSStatusItem?) {
        let icon   = percent >= 95 ? "exclamationmark.circle.fill" : "exclamationmark.triangle.fill"
        let color  = percent >= 95 ? Color.red : Color.orange
        let title  = "Claude \(windowName) limit at \(percent)%"
        let body   = percent >= 95 ? "Almost out of quota — usage is critically high." : "Approaching your quota limit."
        showLimitWindow(title: title, subtitle: body, badgeIcon: icon, badgeColor: color, near: statusItem)
    }

    static func showLimitReset(windowName: String, near statusItem: NSStatusItem?) {
        showLimitWindow(
            title: "Claude \(windowName) limit reset",
            subtitle: "Your quota has been reset — you're good to go.",
            badgeIcon: "arrow.clockwise.circle.fill",
            badgeColor: .green,
            near: statusItem
        )
    }

    private static func showLimitWindow(title: String, subtitle: String, badgeIcon: String, badgeColor: Color, near statusItem: NSStatusItem?) {
        sharedLimit?.close()
        let content = UpdateNotificationView(
            version: "", subtitle: subtitle, isUpdate: false,
            titleText: title, badgeIcon: badgeIcon, badgeColor: badgeColor
        ) {
            sharedLimit?.close()
            sharedLimit = nil
        }
        let controller = makeWindow(content: content)
        position(window: controller.window!, near: statusItem, existingWindow: shared?.window)
        sharedLimit = controller
        controller.showWindow(nil)
        autoDismiss(controller: controller, slot: .limit)
    }

    private static func showWindow(version: String, subtitle: String, near statusItem: NSStatusItem?, isUpdate: Bool) {
        shared?.close()
        let content = UpdateNotificationView(version: version, subtitle: subtitle, isUpdate: isUpdate) {
            shared?.close()
            shared = nil
        }
        let controller = makeWindow(content: content)
        position(window: controller.window!, near: statusItem, existingWindow: nil)
        shared = controller
        controller.showWindow(nil)
        autoDismiss(controller: controller, slot: .update)
    }

    private static func makeWindow<V: View>(content: V) -> UpdateNotificationWindowController {
        let hosting = NSHostingController(rootView: content)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 80),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        window.isOpaque = false
        window.backgroundColor = .clear
        window.level = .statusBar
        window.hasShadow = false
        window.contentViewController = hosting
        return UpdateNotificationWindowController(window: window)
    }

    private static func position(window: NSWindow, near statusItem: NSStatusItem?, existingWindow: NSWindow?) {
        if let button = statusItem?.button,
           let buttonWindow = button.window,
           let screen = buttonWindow.screen {
            let buttonFrame = buttonWindow.convertToScreen(button.frame)
            let x = buttonFrame.midX - 170
            // Stack below the update notification if it's showing
            let yOffset: CGFloat = existingWindow != nil ? 80 + 8 + 8 : 8
            let y = buttonFrame.minY - 80 - yOffset
            let clampedX = max(8, min(x, screen.frame.maxX - 348))
            window.setFrameOrigin(NSPoint(x: clampedX, y: y))
        } else {
            window.center()
        }
    }

    private enum NotificationSlot { case update, limit }

    private static func autoDismiss(controller: UpdateNotificationWindowController, slot: NotificationSlot) {
        let window = controller.window!
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.3
                window.animator().alphaValue = 0
            } completionHandler: {
                controller.close()
                switch slot {
                case .update: if shared === controller { shared = nil }
                case .limit:  if sharedLimit === controller { sharedLimit = nil }
                }
            }
        }
    }
}

// MARK: - Notification View

struct UpdateNotificationView: View {
    let version: String
    let subtitle: String
    let isUpdate: Bool
    var titleText: String? = nil
    var badgeIcon: String? = nil
    var badgeColor: Color? = nil
    let onDismiss: () -> Void

    private var effectiveTitle: String {
        titleText ?? (isUpdate ? "Update available — v\(version)" : "Updated to v\(version) ✓")
    }
    private var effectiveBadgeIcon: String {
        badgeIcon ?? (isUpdate ? "arrow.down.circle.fill" : "checkmark.circle.fill")
    }
    private var effectiveBadgeColor: Color {
        badgeColor ?? (isUpdate ? .blue : .green)
    }

    var body: some View {
        HStack(spacing: 12) {
            ZStack(alignment: .bottomTrailing) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                    .resizable()
                    .frame(width: 44, height: 44)
                    .cornerRadius(10)

                Image(systemName: effectiveBadgeIcon)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(effectiveBadgeColor)
                    .background(Color(nsColor: .windowBackgroundColor).clipShape(Circle()))
                    .offset(x: 4, y: 4)
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(effectiveTitle)
                    .font(.system(size: 13, weight: .semibold))
                Text(subtitle)
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .lineLimit(1)
            }

            Spacer()

            Button(action: onDismiss) {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .medium))
                    .foregroundColor(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 14)
        .frame(width: 340, height: 80)
        .background(
            RoundedRectangle(cornerRadius: 13)
                .fill(Color(nsColor: .windowBackgroundColor))
                .shadow(color: .black.opacity(0.25), radius: 16, x: 0, y: 6)
        )
    }
}
