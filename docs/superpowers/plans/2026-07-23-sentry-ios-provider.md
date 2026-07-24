# Sentry iOS Observability Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional `FoundryObservabilitySentry` addon with a tested FoundryScript provider and an iOS Foundry-Swift/Sentry Cocoa extension.

**Architecture:** Keep `FoundryObservability` provider-neutral. Add a sibling addon whose FoundryScript provider forwards normalized dictionaries to a `SentryObservabilityBridge` native class. Build the native class as an iOS device/simulator xcframework; link the prebuilt FoundrySwift artifacts from `Foundry-Swift-Binary` and keep the shared FoundrySwift runtime supplied by its sibling addon.

**Tech Stack:** FoundryScript, Foundry testlib, Swift 6, Foundry-Swift-Binary `0.1.0-alpha.2`, Sentry Cocoa `9.23.0`, XcodeGen, XCTest, Task, shell contract tests.

---

## File map

Create:

- `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs` and its UID.
- `addons/FoundryObservabilitySentry/plugin.cfg`, `export_plugin.fs` and its UID.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry.foundryextension`.
- `addons/FoundryObservabilitySentry/bin/ios/.gitkeep`.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Package.swift`.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/project.yml`.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`.
- `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`.
- `test_project/tests/observability-sentry.test.fs` and its test bridge support file.
- `scripts/test-sentry-ios-build-contract`.

Modify:

- `scripts/test-project`, `scripts/test-foundry-script`, `Taskfile.yml`, `.gitignore`.
- `scripts/package-addon`, `scripts/test-package`.
- `README.md`, `BUILD.md`, `docs/API.md`, `CHANGELOG.md`.

Generate and commit XcodeGen project metadata and SwiftPM resolution under the
native project, matching the tracked AuthenticationKit migration layout.

---

### Task 1: Define the failing FoundryScript provider contract

**Files:**
- Create: `test_project/tests/observability-sentry.test.fs`
- Create: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Modify: `scripts/test-project`

- [ ] **Step 1: Write a recording bridge and tests first**

The support bridge must expose `configure(Dictionary)`, `isAvailable()`,
`capture(Dictionary)`, `flush(int)`, and `shutdown()`, recording deep copies,
timeouts, and shutdown count. The provider tests must cover the following exact
behaviors:

```foundryscript
var bridge := FakeSentryBridge.new()
var provider := SentryObservabilityProvider.new(p_bridge = bridge)
Expect.that(provider.provider_name()).to_equal(&"sentry")
Expect.that(provider.configure(ObservabilityConfig.new(
		p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
Expect.that(provider.capture(ObservabilityEvent.new(
		p_kind = &"message", p_message = "hello", p_timestamp_msec = 1234,
	))).to_equal("sentry:1")
Expect.that(bridge.configured_payload["environment"]).to_equal("")
Expect.that(bridge.captured_payloads[0]["timestamp_msec"]).to_equal(1234)
Expect.that(provider.flush(321)).to_equal(Error.OK)
Expect.that(bridge.flush_timeouts).to_equal([321])
provider.shutdown()
provider.shutdown()
Expect.that(bridge.shutdown_count).to_equal(1)
```

Also test that an enabled provider with no DSN returns `Error.FAILED`, an
enabled provider with no bridge returns `Error.FAILED`, and a disabled provider
without a bridge returns `Error.OK`, unavailable, and an empty capture ID.

- [ ] **Step 2: Materialize the test addon during project tests**

Extend `scripts/test-project` with a `sentry_addon` variable. Its cleanup must
remove a materialized directory and restore a symlink to
`../../addons/FoundryObservabilitySentry`; before running Foundry, copy the
source directory when the path is a symlink. Keep the existing core-addon
cleanup behavior unchanged.

- [ ] **Step 3: Run the red test**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: project import fails because `SentryObservabilityProvider` does not
exist. This verifies the test is red for the intended missing feature.

- [ ] **Step 4: Commit the red test**

```sh
git add test_project/tests/observability-sentry.test.fs test_project/tests/support/fake_sentry_bridge.notest.fs scripts/test-project
git commit -m "test: define Sentry provider contract"
```

---

### Task 2: Implement the FoundryScript provider and addon metadata

**Files:**
- Create: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Create: `addons/FoundryObservabilitySentry/plugin.cfg`
- Create: `addons/FoundryObservabilitySentry/export_plugin.fs`
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry.foundryextension`
- Create: `addons/FoundryObservabilitySentry/bin/ios/.gitkeep`

- [ ] **Step 1: Implement the provider minimally**

Use this behavior and method shape:

```foundryscript
namespace foundry.observability.sentry

import foundry.observability

class_name SentryObservabilityProvider
extends RefCounted
uses ObservabilityProvider

const _NATIVE_CLASS: String = "SentryObservabilityBridge"
var _bridge: Object = null
var _enabled: bool = false
var _shutdown: bool = false

func _init(p_bridge: Object? = null) -> void:
	_bridge = p_bridge

func provider_name() -> StringName:
	return &"sentry"

func is_available() -> bool:
	var bridge: Object = _resolve_bridge()
	return _enabled and not _shutdown and bridge != null \
		and bridge.has_method("isAvailable") and bool(bridge.call("isAvailable"))

func configure(config: ObservabilityConfig) -> int:
	var options: Dictionary = config.provider_options()
	var dsn: String = str(options.get("dsn", ""))
	if config.enabled and dsn.is_empty():
		return Error.FAILED
	var bridge: Object = _resolve_bridge()
	if config.enabled and bridge == null:
		return Error.FAILED
	_enabled = false
	_shutdown = false
	if bridge == null:
		return Error.OK
	var payload: Dictionary = {
			"enabled": config.enabled, "dsn": dsn,
			"environment": config.environment, "release": config.release,
			"dist": config.dist,
			"global_attributes": config.global_attributes(),
			"provider_options": options,
		}
	var result: int = int(bridge.call("configure", payload))
	if result == Error.OK:
		_enabled = config.enabled
	return result

func capture(event: ObservabilityEvent) -> String:
	if event == null or not _enabled or _shutdown:
		return ""
	var bridge: Object = _resolve_bridge()
	if bridge == null or not is_available():
		return ""
	var payload: Dictionary = {
			"kind": String(event.kind()), "level": event.level(),
			"message": event.message(), "source": String(event.source()),
			"timestamp_msec": event.timestamp_msec(),
			"attributes": event.attributes(),
		}
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		payload["exception"] = {
				"type_name": exception.type_name(),
				"message": exception.message(),
				"stack_trace": exception.stack_trace(),
				"attributes": exception.attributes(),
			}
	return str(bridge.call("capture", payload))

func flush(timeout_msec: int = 2000) -> int:
	var bridge: Object = _resolve_bridge()
	if bridge == null or not _enabled or _shutdown:
		return Error.OK
	return int(bridge.call("flush", timeout_msec))

func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	_enabled = false
	var bridge: Object = _resolve_bridge()
	if bridge != null and bridge.has_method("shutdown"):
		bridge.call("shutdown")

func _resolve_bridge() -> Object:
	if _bridge != null:
		return _bridge
	if not ClassDB.class_exists(_NATIVE_CLASS) or not ClassDB.can_instantiate(_NATIVE_CLASS):
		return null
	_bridge = ClassDB.instantiate(_NATIVE_CLASS)
	return _bridge
```

Keep the provider independent of Sentry and FoundrySwift. Preserve deep copies
from the core accessors, and use explicit conversions for dynamic calls so the
strict FoundryScript warning gate stays clean.

- [ ] **Step 2: Add metadata**

`plugin.cfg` must name the addon `FoundryObservabilitySentry`, use version
`0.1.0`, and reference `export_plugin.fs`. The descriptor must use entry symbol
`foundry_observability_sentry_entry_point` and reference
`res://addons/FoundryObservabilitySentry/bin/ios/FoundryObservabilitySentry.xcframework`
for `ios.debug`, `ios.release`, `ios.simulator.debug`, and
`ios.simulator.release`. Its dependencies for those four variants must be
empty maps; it must not mention `FoundrySwift`.

The export plugin must register an `IOSExportPlugin` that returns only the same
Sentry xcframework from `_get_ios_frameworks()` and supports only iOS.

- [ ] **Step 3: Generate UIDs and run the green FoundryScript tests**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: existing core tests and the new provider tests pass. Run
`scripts/test-foundry-script` to verify strict linting.

- [ ] **Step 4: Commit the provider**

```sh
git add addons/FoundryObservabilitySentry test_project/tests scripts/test-foundry-script
git commit -m "feat: add Sentry observability provider adapter"
```

---

### Task 3: Add the Swift package and failing mapper tests

**Files:**
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Package.swift`
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`

- [ ] **Step 1: Add `Package.swift`**

Use Swift 6, iOS 17/macOS 14. The SwiftPM manifest is intentionally a
mapper-test package: it depends on Sentry Cocoa but excludes the native bridge
source, because the Foundry-Swift binary package uses local artifact paths and
would otherwise make clean mapper tests fail before compilation. The XcodeGen
native project pins Foundry-Swift-Binary `0.1.0-alpha.2` and compiles the bridge
against its prebuilt framework and macro artifact.

```swift
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "FoundryObservabilitySentry",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [.library(name: "FoundryObservabilitySentry", type: .dynamic, targets: ["FoundryObservabilitySentry"])],
    dependencies: [
        .package(url: "https://github.com/getsentry/sentry-cocoa.git", exact: "9.23.0"),
    ],
    targets: [
        .target(name: "FoundryObservabilitySentry", dependencies: [
            .product(name: "Sentry", package: "sentry-cocoa"),
        ], path: "Sources/FoundryObservabilitySentry", exclude: ["FoundryObservabilitySentry.swift"], swiftSettings: [.swiftLanguageMode(.v6)]),
        .testTarget(name: "FoundryObservabilitySentryTests", dependencies: [
            "FoundryObservabilitySentry", .product(name: "Sentry", package: "sentry-cocoa"),
        ], path: "Tests/FoundryObservabilitySentryTests", swiftSettings: [.swiftLanguageMode(.v6)]),
    ]
)
```

- [ ] **Step 2: Write mapper tests before implementing mapper helpers**

Test `sentryLevel(for:)`, `mergedExtras(global:event:kind:source:timestampMsec:)`,
and `sentryTimeoutSeconds(milliseconds:)` for all six levels, unknown-level
fallback, event-over-global precedence, reserved `foundry.*` keys, and
millisecond conversion:

```swift
import Sentry
import XCTest
@testable import FoundryObservabilitySentry

final class SentryEventMapperTests: XCTestCase {
    func testMapsLevels() {
        XCTAssertEqual(sentryLevel(for: 10), .debug)
        XCTAssertEqual(sentryLevel(for: 20), .debug)
        XCTAssertEqual(sentryLevel(for: 30), .info)
        XCTAssertEqual(sentryLevel(for: 40), .warning)
        XCTAssertEqual(sentryLevel(for: 50), .error)
        XCTAssertEqual(sentryLevel(for: 60), .fatal)
        XCTAssertEqual(sentryLevel(for: 35), .error)
    }

    func testEventExtrasOverrideGlobalExtrasAndPreserveMetadata() {
        let result = mergedExtras(global: ["shared": "global", "build": 42], event: ["shared": "event"], kind: "log", source: "combat", timestampMsec: 1234)
        XCTAssertEqual(result["shared"] as? String, "event")
        XCTAssertEqual(result["build"] as? Int, 42)
        XCTAssertEqual(result["foundry.kind"] as? String, "log")
        XCTAssertEqual(result["foundry.source"] as? String, "combat")
        XCTAssertEqual(result["foundry.timestamp_msec"] as? Int64, 1234)
    }

    func testConvertsTimeoutMillisecondsToSeconds() {
        XCTAssertEqual(sentryTimeoutSeconds(milliseconds: 321), 0.321, accuracy: 0.0001)
    }
}
```

- [ ] **Step 3: Run the red Swift test**

Run:

```sh
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test --filter SentryEventMapperTests
```

Expected: compilation fails because the mapper helpers do not exist yet.

---

### Task 4: Implement Swift mapping and the Foundry bridge

**Files:**
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`

- [ ] **Step 1: Implement pure translation helpers**

Map TRACE/DEBUG to `.debug`, INFO to `.info`, WARN to `.warning`, ERROR to
`.error`, FATAL to `.fatal`, and unknown values to `.error`. Merge global then
event extras, write reserved `foundry.kind`, `foundry.source`, and
`foundry.timestamp_msec` keys after caller values, and convert milliseconds to
`Double(milliseconds) / 1000.0`. Add exception type/message/stack metadata and
construct a `SentryEvent` with `message`, `level`, `logger`, `extra`, and one
`SentryException` when an exception payload is present. Unsupported Variant
values must be omitted instead of crashing the native extension.

- [ ] **Step 2: Run Swift tests green**

```sh
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test --filter SentryEventMapperTests
```

Expected: PASS.

- [ ] **Step 3: Add the Foundry-Swift bridge**

Create a `RefCounted` class with `@Callable` methods `configure(VariantDictionary)`,
`isAvailable()`, `capture(VariantDictionary)`, `flush(Int)`, and `shutdown()`;
register it with:

```swift
import FoundrySwift
import Sentry

#initFoundryExtension(
    cdecl: "foundry_observability_sentry_entry_point",
    types: [SentryObservabilityBridge.self]
)
```

`configure` must reject an empty DSN, call `SentrySDK.start`, set DSN,
environment, release name, dist, and provider debug option, and retain global
attributes. Disabled configuration returns native success without starting the
SDK. `capture` converts the payload, builds the mapped event, calls
`SentrySDK.capture(event:)`, and returns `sentryIdString`. `flush` converts the
timeout and calls `SentrySDK.flush`; `shutdown` calls `SentrySDK.close` once and
clears state. Use the Sentry Cocoa `Options` properties `dsn`, `environment`,
`releaseName`, `dist`, `debug`, and `enabled`; keep the bridge boundary's
success/failure values `0` and `1` for Foundry `Error.OK`/`Error.FAILED`.

- [ ] **Step 4: Run all Swift tests and commit**

```sh
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test
cd ../../../..
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry
git commit -m "feat: bridge observability events to Sentry Cocoa"
```

Expected: all XCTest cases pass and the commit contains no build output.

---

### Task 5: Add XcodeGen metadata, build task, and export contracts

**Files:**
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/project.yml`
- Create: `scripts/test-sentry-ios-build-contract`
- Modify: `Taskfile.yml`
- Modify: `.gitignore`
- Generate: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj/`

- [ ] **Step 1: Add `project.yml`**

Define framework target `FoundryObservabilitySentry` for iOS and macOS with
deployment targets 17.0/14.0, Swift 6, `BUILD_LIBRARY_FOR_DISTRIBUTION = YES`,
`SKIP_INSTALL = NO`, `DEFINES_MODULE = YES`, `GENERATE_INFOPLIST_FILE = YES`,
and `MARKETING_VERSION = "0.1.0"`. Depend on FoundrySwift and Sentry package
products. Add iOS and macOS schemes; the macOS scheme also runs the XCTest
target.

- [ ] **Step 2: Generate and resolve the project**

```sh
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
xcodegen generate
FOUNDRY_SENTRY_NATIVE_DIR="$PWD" FOUNDRY_SENTRY_DERIVED_DATA="../../../.build/sentry-xcodebuild" ../../../scripts/prepare-foundryswift-binary
xcodebuild -resolvePackageDependencies -scheme FoundryObservabilitySentry_macOS -derivedDataPath ../../../.build/sentry-xcodebuild
```

Expected: generated project, schemes, and Package.resolved are present and the
project contains the generated Info.plist setting. The preparation step stages
the published Foundry-Swift binary artifacts; it does not compile Foundry-Swift
from source.

- [ ] **Step 3: Add `ios:sentry` to `Taskfile.yml`**

The task must build `generic/platform=iOS` and `generic/platform=iOS Simulator`
Release frameworks, set `ARCHS=arm64 CODE_SIGNING_ALLOWED=NO` for the simulator,
then run `xcodebuild -create-xcframework` into
`addons/FoundryObservabilitySentry/bin/ios/FoundryObservabilitySentry.xcframework`.
It must pass `-skipPackagePluginValidation -skipMacroValidation` and use a
stable derived-data directory under `.build`.

- [ ] **Step 4: Add `scripts/test-sentry-ios-build-contract`**

Fail unless the Taskfile has both iOS destinations and simulator signing
disabled; `project.yml` and generated `project.pbxproj` contain
`GENERATE_INFOPLIST_FILE = YES`; source contains the entry symbol and
`#initFoundryExtension`; the descriptor has all four iOS variants, empty
dependency maps, and no `FoundrySwift`; and the export plugin references only
the Sentry xcframework. End with:

```sh
echo "Sentry iOS build contract tests passed"
```

- [ ] **Step 5: Ignore generated native output and run the contract**

Ignore the Sentry xcframework directory, `.build`, `.swiftpm`, `DerivedData`,
and Xcode user data. Run:

```sh
scripts/test-sentry-ios-build-contract
```

Expected: `Sentry iOS build contract tests passed`.

- [ ] **Step 6: Commit packaging metadata**

```sh
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry/project.yml addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj Taskfile.yml scripts/test-sentry-ios-build-contract .gitignore
git commit -m "build: package Sentry provider as an iOS xcframework"
```

---

### Task 6: Integrate repository gates, archives, and documentation

**Files:**
- Modify: `scripts/test-foundry-script`, `scripts/test-project`, `scripts/package-addon`, `scripts/test-package`, `Taskfile.yml`.
- Modify: `README.md`, `BUILD.md`, `docs/API.md`, `CHANGELOG.md`.

- [ ] **Step 1: Extend validation gates**

Lint the Sentry addon with the strict FoundryScript gate, ensure project-test
cleanup restores both addon symlinks, and add the static Sentry contract to the
repository test dependencies without requiring Xcode compilation.

- [ ] **Step 2: Extend archives**

Keep the existing `FoundryObservability-<version>.zip` core archive unchanged
and add `FoundryObservabilitySentry-<version>.zip` containing the Sentry runtime
script, plugin metadata, `.foundryextension`, UIDs, and any built xcframework.
Exclude SwiftPM sources/tests, `.build`, `.foundry`, Xcode state, and repository
state. Update `scripts/test-package` to assert both archives and every tracked
FoundryScript UID companion.

- [ ] **Step 3: Document installation and configuration**

Update README/API/build docs to explain installing the core addon, the sibling
FoundrySwift addon, and the Sentry addon. Add this provider configuration:

```foundryscript
import foundry.observability
import foundry.observability.sentry

var provider := SentryObservabilityProvider.new()
var config := ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "production",
		p_release = "1.0.0",
		p_dist = "ios",
		p_provider_options = {
			"dsn": "https://public@example.ingest.sentry.io/1",
			"debug": false,
		},
	)
FoundryObservability.configure(provider, config)
```

State that the first native build supports iOS device/simulator and that
Android, macOS, performance, breadcrumbs, user identity, and attachments are
not included. Remove stale wording that says no Sentry implementation exists.

- [ ] **Step 4: Run package checks and commit**

```sh
scripts/test-package
git add scripts Taskfile.yml README.md BUILD.md docs/API.md CHANGELOG.md
git commit -m "docs: publish Sentry provider installation and packaging"
```

Expected: package checks pass and no credentials or generated state are staged.

---

### Task 7: Verify the complete implementation

**Files:**
- Modify only files required to correct verified failures.

- [ ] **Step 1: Run focused checks**

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
scripts/test-sentry-ios-build-contract
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry && swift test
cd ../../../..
```

Expected: all FoundryScript, core, provider, contract, and Swift tests pass.

- [ ] **Step 2: Build the native artifact when the local iOS SDK is available**

```sh
task ios:sentry
```

Expected: the generated xcframework contains device and simulator slices. If
the local machine lacks required SDK/package artifacts, retain the passing
static contracts and report that environmental limitation precisely.

- [ ] **Step 3: Run the full repository gate**

```sh
task test
```

Expected: lint, FoundryScript, project, CI, and package gates all pass.

- [ ] **Step 4: Inspect final state**

```sh
git status --short
git log --oneline -10
```

Confirm only intentional files are present, no DSNs/credentials are committed,
and summarize exact test/build results in the handoff.
