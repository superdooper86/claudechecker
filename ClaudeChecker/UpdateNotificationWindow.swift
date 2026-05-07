import SwiftUI
import AppKit

// MARK: - Compact update notification window (shown on relaunch after update)

class UpdateNotificationWindowController: NSWindowController {
    static var shared: UpdateNotificationWindowController?

    // Called after successful install + relaunch
    static func show(version: String, notes: String, near statusItem: NSStatusItem?) {
        showWindow(version: version, subtitle: notes.isEmpty ? "ClaudeChecker is up to date." : notes, near: statusItem, isUpdate: false)
    }

    // Called when a new update is detected while running
    static func showUpdate(version: String, notes: String, near statusItem: NSStatusItem?) {
        showWindow(version: version, subtitle: notes.isEmpty ? "Tap to update now." : notes, near: statusItem, isUpdate: true)
    }

    private static func showWindow(version: String, subtitle: String, near statusItem: NSStatusItem?, isUpdate: Bool) {
        shared?.close()

        let content = UpdateNotificationView(version: version, subtitle: subtitle, isUpdate: isUpdate) {
            shared?.close()
            shared = nil
        }

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

        if let button = statusItem?.button,
           let buttonWindow = button.window,
           let screen = buttonWindow.screen {
            let buttonFrame = buttonWindow.convertToScreen(button.frame)
            let x = buttonFrame.midX - 170
            let y = buttonFrame.minY - 80 - 8
            let clampedX = max(8, min(x, screen.frame.maxX - 348))
            window.setFrameOrigin(NSPoint(x: clampedX, y: y))
        } else {
            window.center()
        }

        let controller = UpdateNotificationWindowController(window: window)
        shared = controller
        controller.showWindow(nil)

        // Auto-dismiss after 8 seconds
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.3
                window.animator().alphaValue = 0
            } completionHandler: {
                controller.close()
                if shared === controller { shared = nil }
            }
        }
    }
}

// MARK: - Notification View

struct UpdateNotificationView: View {
    let version: String
    let subtitle: String
    let isUpdate: Bool
    let onDismiss: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            ZStack(alignment: .bottomTrailing) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                    .resizable()
                    .frame(width: 44, height: 44)
                    .cornerRadius(10)

                Image(systemName: isUpdate ? "arrow.down.circle.fill" : "checkmark.circle.fill")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(isUpdate ? .blue : .green)
                    .background(Color(nsColor: .windowBackgroundColor).clipShape(Circle()))
                    .offset(x: 4, y: 4)
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(isUpdate ? "Update available — v\(version)" : "Updated to v\(version) ✓")
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
