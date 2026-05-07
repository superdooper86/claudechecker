import Foundation
import AppKit

// MARK: - Version Info

struct VersionInfo: Codable {
    let version: String
    let url: String
    let notes: String?
}

enum UpdateError: LocalizedError {
    case downloadFailed
    case unzipFailed
    case appNotFound
    case replaceFailed(String)

    var errorDescription: String? {
        switch self {
        case .downloadFailed:       return "Download failed. Check your connection."
        case .unzipFailed:          return "Could not unpack the update."
        case .appNotFound:          return "Could not find ClaudeChecker.app in the update."
        case .replaceFailed(let r): return "Install failed: \(r)"
        }
    }
}

// MARK: - Update Manager

@MainActor
class UpdateManager: ObservableObject {

    static let versionURL      = "https://raw.githubusercontent.com/superdooper86/claudechecker/refs/heads/main/version.json"
    static let betaVersionURL  = "https://raw.githubusercontent.com/superdooper86/claudechecker/refs/heads/main/version-beta.json"

    @Published var updateAvailable = false
    @Published var latestVersion = ""
    @Published var releaseNotes = ""
    @Published var downloadURL = ""
    @Published var betaAvailable = false
    @Published var latestBetaVersion = ""
    @Published var isDownloading = false
    @Published var downloadProgress: Double = 0
    @Published var statusMessage = ""
    @Published var updateError: String? = nil
    @Published var updateComplete = false
    @Published var justUpdated = false

    private var notifiedVersion: String = ""

    @Published var betaChannel: Bool = false {
        didSet {
            UserDefaults.standard.set(betaChannel, forKey: "beta_channel")
        }
    }

    var currentVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0"
    }

    init() {
        betaChannel = UserDefaults.standard.bool(forKey: "beta_channel")
    }

    // MARK: - Check

    func checkForUpdates() async {
        print("[UpdateManager] Checking for updates... current=\(currentVersion)")
        async let stableResult = fetchVersion(from: Self.versionURL)
        async let betaResult = fetchVersion(from: Self.betaVersionURL)

        if let stable = await stableResult {
            print("[UpdateManager] Stable remote=\(stable.version), isNewer=\(isNewer(stable.version, than: currentVersion))")
            if isNewer(stable.version, than: currentVersion) {
                latestVersion = stable.version
                releaseNotes = stable.notes ?? ""
                downloadURL = stable.url
                updateAvailable = true
                print("[UpdateManager] updateAvailable set to true")
                if notifiedVersion != stable.version {
                    notifiedVersion = stable.version
                    NotificationCenter.default.post(name: .updateDetected, object: stable.version)
                }
            } else {
                updateAvailable = false
                latestVersion = ""
            }
        } else {
            print("[UpdateManager] Stable fetch returned nil")
            updateAvailable = false
            latestVersion = ""
        }

        if let beta = await betaResult {
            print("[UpdateManager] Beta remote=\(beta.version)")
            if isNewer(beta.version, than: currentVersion) {
                latestBetaVersion = beta.version
                betaAvailable = true
                if betaChannel && isNewer(beta.version, than: latestVersion) {
                    latestVersion = beta.version
                    releaseNotes = beta.notes ?? ""
                    downloadURL = beta.url
                    updateAvailable = true
                    if notifiedVersion != beta.version {
                        notifiedVersion = beta.version
                        NotificationCenter.default.post(name: .updateDetected, object: beta.version)
                    }
                }
            } else {
                betaAvailable = false
                latestBetaVersion = ""
            }
        } else {
            print("[UpdateManager] Beta fetch returned nil")
            betaAvailable = false
            latestBetaVersion = ""
        }
    }

    private func fetchVersion(from urlString: String) async -> VersionInfo? {
        // Add timestamp to bust GitHub's 5-minute CDN cache
        let cacheBusted = urlString + "?t=\(Int(Date().timeIntervalSince1970))"
        guard let url = URL(string: cacheBusted) else {
            print("[UpdateManager] Invalid URL: \(urlString)")
            return nil
        }
        do {
            var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalAndRemoteCacheData)
            request.timeoutInterval = 10
            let (data, response) = try await URLSession.shared.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? -1
            print("[UpdateManager] Fetch \(urlString) -> HTTP \(status)")
            guard status == 200 else { return nil }
            let info = try JSONDecoder().decode(VersionInfo.self, from: data)
            print("[UpdateManager] Decoded version: \(info.version)")
            return info
        } catch {
            print("[UpdateManager] Fetch error: \(error)")
            return nil
        }
    }

    // MARK: - Download & Install

    func downloadAndInstall() async {
        guard let src = URL(string: downloadURL) else { return }
        isDownloading = true
        updateError = nil
        updateComplete = false

        do {
            // 1. Set up temp dir
            let tempDir = FileManager.default.temporaryDirectory
                .appendingPathComponent("CCUpdate_\(UUID().uuidString)")
            try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
            let zipURL = tempDir.appendingPathComponent("ClaudeChecker.zip")

            // 2. Download with progress
            statusMessage = "Downloading…"
            let (asyncBytes, response) = try await URLSession.shared.bytes(from: src)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else {
                throw UpdateError.downloadFailed
            }

            let total = Double(response.expectedContentLength)
            var received: Double = 0
            var buffer = Data()
            if total > 0 { buffer.reserveCapacity(Int(total)) }

            for try await byte in asyncBytes {
                buffer.append(byte)
                received += 1
                if Int(received) % 50_000 == 0 && total > 0 {
                    downloadProgress = (received / total) * 0.75
                }
            }
            try buffer.write(to: zipURL)
            downloadProgress = 0.75

            // 3. Unzip
            statusMessage = "Unpacking…"
            let unzipDir = tempDir.appendingPathComponent("unzipped")
            try FileManager.default.createDirectory(at: unzipDir, withIntermediateDirectories: true)

            let unzip = Process()
            unzip.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
            unzip.arguments = ["-q", "-o", zipURL.path, "-d", unzipDir.path]
            try unzip.run()
            unzip.waitUntilExit()
            guard unzip.terminationStatus == 0 else { throw UpdateError.unzipFailed }
            downloadProgress = 0.85

            // 4. Find the .app
            guard let newApp = findApp(in: unzipDir) else { throw UpdateError.appNotFound }

            // 5. Determine install target — same location as currently running app
            let currentApp = Bundle.main.bundleURL
            let installTarget = currentApp

            statusMessage = "Installing…"
            downloadProgress = 0.9

            // 6. Write installer script to home dir (persists after app quits)
            let homeDir = FileManager.default.homeDirectoryForCurrentUser
            let scriptURL = homeDir.appendingPathComponent(".claudechecker_update.sh")
            let logURL = homeDir.appendingPathComponent(".claudechecker_update.log")

            let newAppPath = newApp.path
            let targetPath = installTarget.path

            let script = """
            #!/bin/bash
            exec > "\(logURL.path)" 2>&1
            echo "=== ClaudeChecker Updater $(date) ==="
            echo "Source: \(newAppPath)"
            echo "Target: \(targetPath)"

            # Wait for the old ClaudeChecker process to fully exit
            for i in $(seq 1 20); do
                if ! pgrep -x "ClaudeChecker" > /dev/null 2>&1; then
                    echo "Process exited after ${i}s"
                    break
                fi
                sleep 1
            done

            if [ ! -d "\(newAppPath)" ]; then
                echo "ERROR: source not found"; exit 1
            fi

            echo "Replacing app..."
            rm -rf "\(targetPath)"
            /usr/bin/ditto "\(newAppPath)" "\(targetPath)"

            if [ ! -d "\(targetPath)" ]; then
                echo "ERROR: ditto failed"; exit 1
            fi

            chmod -R 755 "\(targetPath)"
            xattr -cr "\(targetPath)" 2>/dev/null || true

            sleep 1

            echo "Relaunching..."
            # Write flag file so app knows it just updated
            touch "$HOME/.claudechecker_just_updated"
            open "\(targetPath)"

            sleep 5
            rm -rf "\(tempDir.path)"
            rm -f "\(scriptURL.path)"
            echo "Done."
            """

            try script.write(to: scriptURL, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes(
                [.posixPermissions: NSNumber(value: Int16(0o755))],
                ofItemAtPath: scriptURL.path)

            downloadProgress = 1.0
            statusMessage = "Installed! Relaunching…"
            updateComplete = true

            try await Task.sleep(nanoseconds: 800_000_000)

            do {
                let launcher = Process()
                launcher.executableURL = URL(fileURLWithPath: "/bin/sh")
                launcher.arguments = ["-c", "nohup /bin/bash '\(scriptURL.path)' &"]
                launcher.standardInput = FileHandle.nullDevice
                launcher.standardOutput = FileHandle.nullDevice
                launcher.standardError = FileHandle.nullDevice
                try launcher.run()
            } catch {
                let fallback = Process()
                fallback.executableURL = URL(fileURLWithPath: "/usr/bin/open")
                fallback.arguments = [scriptURL.path]
                try? fallback.run()
            }

            // Terminate — belt and braces approach
            DispatchQueue.main.async {
                NSApp.terminate(nil)
                // If NSApp.terminate gets blocked by a delegate, exit() guarantees quit
                DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
                    exit(0)
                }
            }

        } catch let e as UpdateError {
            updateError = e.localizedDescription
            isDownloading = false
            statusMessage = ""
            // Log path for debugging: ~/.claudechecker_update.log
        } catch {
            updateError = error.localizedDescription
            isDownloading = false
            statusMessage = ""
        }
    }

    // MARK: - Helpers

    private func findApp(in dir: URL) -> URL? {
        // Check direct children first
        if let contents = try? FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: [.isDirectoryKey]) {
            if let app = contents.first(where: { $0.pathExtension == "app" }) { return app }
        }
        // Then recurse
        guard let enumerator = FileManager.default.enumerator(
            at: dir, includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]) else { return nil }
        for case let url as URL in enumerator where url.pathExtension == "app" {
            return url
        }
        return nil
    }

    private func isNewer(_ version: String, than current: String) -> Bool {
        let toInts = { (v: String) in v.split(separator: ".").compactMap { Int($0) } }
        let a = toInts(version), b = toInts(current)
        for i in 0..<max(a.count, b.count) {
            let av = i < a.count ? a[i] : 0
            let bv = i < b.count ? b[i] : 0
            if av != bv { return av > bv }
        }
        return false
    }
}
