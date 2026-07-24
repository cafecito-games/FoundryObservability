// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "FoundryObservabilitySentry",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(
            name: "FoundryObservabilitySentry",
            type: .dynamic,
            targets: ["FoundryObservabilitySentry"]
        ),
    ],
    dependencies: [
        .package(
            url: "https://github.com/getsentry/sentry-cocoa.git",
            exact: "9.23.0"
        ),
    ],
    targets: [
        .target(
            name: "FoundryObservabilitySentry",
            dependencies: [
                .product(name: "Sentry", package: "sentry-cocoa"),
            ],
            path: "Sources/FoundryObservabilitySentry",
            // The Foundry-Swift bridge is compiled by the XcodeGen project. Keeping
            // it out of this mapper-only SwiftPM target avoids requiring binary
            // artifacts just to run deterministic event-mapping tests.
            exclude: ["FoundryObservabilitySentry.swift"],
            swiftSettings: [.swiftLanguageMode(.v6)]
        ),
        .testTarget(
            name: "FoundryObservabilitySentryTests",
            dependencies: [
                "FoundryObservabilitySentry",
                .product(name: "Sentry", package: "sentry-cocoa"),
            ],
            path: "Tests/FoundryObservabilitySentryTests",
            swiftSettings: [.swiftLanguageMode(.v6)]
        ),
    ]
)
