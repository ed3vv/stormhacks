// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SphericalWorld",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "SphericalWorld",
            resources: [.copy("World.metal")]
        )
    ]
)
