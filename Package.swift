// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ClaudeChecker",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "ClaudeChecker", targets: ["ClaudeChecker"])
    ],
    targets: [
        .executableTarget(
            name: "ClaudeChecker",
            path: "ClaudeChecker",
            resources: [
                .process("Info.plist")
            ]
        )
    ]
)
