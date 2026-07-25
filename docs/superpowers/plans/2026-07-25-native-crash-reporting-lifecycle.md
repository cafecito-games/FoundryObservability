# Native Crash Reporting Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make macOS, iOS, and Android native crash reporting an explicit, owner-safe Sentry lifecycle with durable metadata and repository-only crash validation.

**Architecture:** The FoundryScript provider assigns each instance an opaque lifecycle owner and requires a versioned native bridge contract. Process-global Swift and Java lifecycle coordinators validate configurations, avoid equivalent restarts, restore the previous client after failed replacement, and ignore stale shutdown. Production bridges configure native crash handlers and initial crash metadata, while guarded repository tooling exercises real fatal signals without entering addon packages.

**Tech Stack:** FoundryScript, Swift 6/Sentry Cocoa 9.23.0/XCTest, Java 17/Sentry Android 8.50.1/JUnit/Robolectric, Bash, Task, Markdown.

---

### Task 1: Version and own the FoundryScript bridge lifecycle

**Files:**
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`

- [ ] **Step 1: Extend the fake bridge and write failing lifecycle tests**

Update `FakeSentryBridge` so its lifecycle methods record and enforce an owner:

```foundryscript
var active_owner: String = ""
var configured_payloads: Array[Dictionary] = []
var flush_owners: Array[String] = []
var shutdown_owners: Array[String] = []


func lifecycleVersion() -> int:
	return 1


func configure(payload: Dictionary) -> int:
	configured_payload = payload.duplicate(true)
	configured_payloads.append(configured_payload)
	if configure_result == Error.OK:
		if payload.get("enabled", false):
			active_owner = str(payload.get("lifecycle_owner", ""))
		elif active_owner == str(payload.get("lifecycle_owner", "")):
			active_owner = ""
	return configure_result


func isAvailable(owner: String) -> bool:
	return available and not owner.is_empty() and owner == active_owner


func flush(owner: String, timeout_msec: int) -> int:
	flush_owners.append(owner)
	flush_timeouts.append(timeout_msec)
	return flush_result


func shutdown(owner: String) -> void:
	shutdown_owners.append(owner)
	if owner == active_owner:
		active_owner = ""
		shutdown_count += 1
```

Add focused tests to `observability-sentry.test.fs`:

```foundryscript
func test_enabled_configuration_reports_missing_or_outdated_bridge_as_unavailable() -> void:
	var missing := SentryObservabilityProvider.new()
	Expect.that(missing.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.ERR_UNAVAILABLE)

	var outdated := SentryObservabilityProvider.new(p_bridge = IncompatibleSentryBridge.new())
	Expect.that(outdated.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.ERR_UNAVAILABLE)


func test_forwards_stable_lifecycle_owner_to_bridge_calls() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	var owner: String = bridge.configured_payload["lifecycle_owner"]
	Expect.that(owner.is_empty()).to_be_false()
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.flush(321)).to_equal(Error.OK)
	provider.shutdown()
	Expect.that(bridge.flush_owners).to_equal([owner])
	Expect.that(bridge.shutdown_owners).to_equal([owner])


func test_replaced_sentry_provider_ignores_stale_shutdown() -> void:
	var bridge := FakeSentryBridge.new()
	var first := SentryObservabilityProvider.new(p_bridge = bridge)
	var second := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)
	Expect.that(first.configure(config)).to_equal(Error.OK)
	var first_owner: String = bridge.active_owner
	Expect.that(second.configure(config)).to_equal(Error.OK)
	var second_owner: String = bridge.active_owner
	Expect.that(second_owner).to_not_equal(first_owner)
	first.shutdown()
	Expect.that(bridge.active_owner).to_equal(second_owner)
	Expect.that(second.is_available()).to_be_true()
	second.shutdown()
```

- [ ] **Step 2: Run the FoundryScript suite and verify the new tests fail**

Run:

```bash
scripts/test-project
```

Expected: FAIL because `SentryObservabilityProvider` does not supply an owner, calls the old bridge signatures, and returns `Error.FAILED` for unavailable bridges.

- [ ] **Step 3: Implement the owner-aware provider contract**

In `SentryObservabilityProvider.fs`:

```foundryscript
const _LIFECYCLE_VERSION: int = 1

var _owner: String = ""


func _init(p_bridge: Object? = null) -> void:
	_bridge = p_bridge
	_owner = str(get_instance_id())


func _has_lifecycle_contract(bridge: Object) -> bool:
	for method: String in ["lifecycleVersion", "configure", "isAvailable", "flush", "shutdown"]:
		if not bridge.has_method(method):
			return false
	var version_result: Variant = bridge.call("lifecycleVersion")
	return version_result is int and version_result >= _LIFECYCLE_VERSION
```

Require this contract before enabled configuration, add
`"lifecycle_owner": _owner` to the payload, return
`Error.ERR_UNAVAILABLE` for a missing or outdated bridge, and use these exact
call shapes:

```foundryscript
bridge.call("isAvailable", _owner)
bridge.call("flush", _owner, timeout_msec)
bridge.call("shutdown", _owner)
```

Keep disabled configuration safe without a bridge. Make flush return
`Error.OK` without a native call when this owner is inactive.

- [ ] **Step 4: Run focused and complete FoundryScript verification**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all FoundryScript tests pass with no new diagnostics.

- [ ] **Step 5: Commit the provider lifecycle contract**

```bash
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests/observability-sentry.test.fs \
  test_project/tests/support/fake_sentry_bridge.notest.fs
git commit -m "feat: own Sentry native lifecycle sessions"
```

### Task 2: Build and test the Apple lifecycle coordinator

**Files:**
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryLifecycleCoordinator.swift`
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryLifecycleCoordinatorTests.swift`

- [ ] **Step 1: Write failing coordinator tests**

Create a fake driver recording `start`, `flush`, and `close`, with a
`failNextStart` switch. Cover:

```swift
func testPublishesOwnerOnlyAfterSuccessfulStart()
func testEquivalentConfigurationDoesNotRestart()
func testChangedConfigurationClosesThenStarts()
func testStaleOwnerCannotFlushOrShutdown()
func testFailedReplacementRestoresPreviousOwnerAndConfiguration()
func testShutdownIsIdempotent()
func testAppleOptionsEnableCrashHandlerAndStableMetadata()
```

Use configurations that differ by release and assert the exact driver
operation sequences:

```swift
XCTAssertEqual(driver.operations, ["start:1.0.0"])
XCTAssertEqual(driver.operations, ["start:1.0.0", "close", "start:2.0.0"])
XCTAssertEqual(
    driver.operations,
    ["start:1.0.0", "close", "start:2.0.0", "start:1.0.0"]
)
```

Verify the option mapper:

```swift
let options = makeAppleSentryOptions(configuration)
XCTAssertTrue(options.enableCrashHandler)
XCTAssertEqual(options.shutdownTimeInterval, 2.0)
XCTAssertEqual(options.releaseName, "game@1.2.3")
XCTAssertEqual(options.environment, "qa")
XCTAssertEqual(options.dist, "macos")
XCTAssertEqual(
    foundryCrashContext(configuration),
    ["global_attributes": ["build": 42]]
)
```

- [ ] **Step 2: Run the focused Swift tests and verify RED**

Run:

```bash
swift test --package-path \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry \
  --filter SentryLifecycleCoordinatorTests
```

Expected: FAIL because the coordinator, configuration, driver protocol, and option mapper do not exist.

- [ ] **Step 3: Implement the coordinator and typed configuration**

Create `SentryLifecycleCoordinator.swift` with:

```swift
import Foundation
@preconcurrency import Sentry

let sentryLifecycleVersion = 1
let sentryShutdownTimeoutSeconds = 2.0

struct SentryLifecycleConfiguration: Equatable {
    let dsn: String
    let environment: String
    let release: String
    let dist: String
    let globalAttributes: [String: Any]
    let providerOptions: [String: Any]
    let logsEnabled: Bool
    let metricsEnabled: Bool
    let applicationHangDetectionEnabled: Bool
    let applicationHangTimeoutMsec: Int

    static func == (lhs: Self, rhs: Self) -> Bool {
        lhs.dsn == rhs.dsn
            && lhs.environment == rhs.environment
            && lhs.release == rhs.release
            && lhs.dist == rhs.dist
            && NSDictionary(dictionary: lhs.globalAttributes)
                .isEqual(to: rhs.globalAttributes)
            && NSDictionary(dictionary: lhs.providerOptions)
                .isEqual(to: rhs.providerOptions)
            && lhs.logsEnabled == rhs.logsEnabled
            && lhs.metricsEnabled == rhs.metricsEnabled
            && lhs.applicationHangDetectionEnabled
                == rhs.applicationHangDetectionEnabled
            && lhs.applicationHangTimeoutMsec
                == rhs.applicationHangTimeoutMsec
    }
}

protocol SentryLifecycleDriving: AnyObject {
    var isEnabled: Bool { get }
    func start(_ configuration: SentryLifecycleConfiguration) -> Bool
    func flush(timeout: TimeInterval)
    func close()
}

final class SentryLifecycleCoordinator {
    private(set) var activeOwner: String?
    private var activeConfiguration: SentryLifecycleConfiguration?

    func configure(
        owner: String,
        configuration: SentryLifecycleConfiguration,
        driver: SentryLifecycleDriving
    ) -> Bool {
        guard !owner.isEmpty else { return false }
        if activeConfiguration == configuration,
           driver.isEnabled {
            activeOwner = owner
            return true
        }
        let previousOwner = activeOwner
        let previousConfiguration = activeConfiguration
        if driver.isEnabled {
            driver.close()
        }
        if driver.start(configuration) {
            activeOwner = owner
            activeConfiguration = configuration
            return true
        }
        activeOwner = nil
        activeConfiguration = nil
        if let previousOwner, let previousConfiguration,
           driver.start(previousConfiguration) {
            activeOwner = previousOwner
            activeConfiguration = previousConfiguration
        }
        return false
    }

    func disable(owner: String, driver: SentryLifecycleDriving) {
        shutdown(owner: owner, driver: driver)
    }

    func isAvailable(owner: String, driver: SentryLifecycleDriving) -> Bool {
        activeOwner == owner && driver.isEnabled
    }

    func flush(
        owner: String,
        timeout: TimeInterval,
        driver: SentryLifecycleDriving
    ) -> Bool {
        guard isAvailable(owner: owner, driver: driver) else { return false }
        driver.flush(timeout: timeout)
        return true
    }

    func shutdown(owner: String, driver: SentryLifecycleDriving) {
        guard activeOwner == owner else { return }
        if driver.isEnabled {
            driver.close()
        }
        activeOwner = nil
        activeConfiguration = nil
    }
}
```

Add `makeAppleSentryOptions`, `foundryCrashContext`, and
`AppleSentrySDKDriver`. The option mapper must assign:

```swift
options.enableCrashHandler = true
options.shutdownTimeInterval = sentryShutdownTimeoutSeconds
options.dsn = configuration.dsn
options.releaseName = configuration.release.isEmpty ? nil : configuration.release
options.environment = configuration.environment.isEmpty ? nil : configuration.environment
options.dist = configuration.dist.isEmpty ? nil : configuration.dist
options.initialScope = { scope in
    scope.setContext(
        value: foundryCrashContext(configuration),
        key: "foundry"
    )
    return scope
}
```

It must also preserve the existing debug, PII, logs, metrics, and Apple hang
option assignments. `AppleSentrySDKDriver.close()` calls `SentrySDK.close()`;
that API performs the bounded flush using `shutdownTimeInterval`.

- [ ] **Step 4: Run focused then complete Swift tests**

Run:

```bash
swift test --package-path \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry \
  --filter SentryLifecycleCoordinatorTests
task test:sentry-swift
```

Expected: all lifecycle and existing mapper/configurator tests pass.

- [ ] **Step 5: Commit the Apple coordinator**

```bash
git add \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryLifecycleCoordinator.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryLifecycleCoordinatorTests.swift
git commit -m "feat: coordinate Apple crash reporting lifecycle"
```

### Task 3: Connect the Swift bridge to the lifecycle coordinator

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj/project.pbxproj`
- Modify: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Add failing Apple bridge contract assertions**

Require these production markers in `scripts/test-sentry-ios-build-contract`:

```bash
rg -q 'func lifecycleVersion' "$bridge_source" \
  || fail "Apple bridge must expose its lifecycle contract version"
rg -q 'SentryLifecycleCoordinator' "$bridge_source" \
  || fail "Apple bridge must use the crash lifecycle coordinator"
rg -q 'lifecycle_owner' "$bridge_source" \
  || fail "Apple bridge must require a lifecycle owner"
rg -q 'enableCrashHandler[[:space:]]*=[[:space:]]*true' "$lifecycle_source" \
  || fail "Apple crash handling must be explicitly enabled"
rg -q 'shutdownTimeInterval[[:space:]]*=' "$lifecycle_source" \
  || fail "Apple shutdown timeout must be explicit"
```

- [ ] **Step 2: Run the Apple contract and verify RED**

Run:

```bash
scripts/test-sentry-ios-build-contract
```

Expected: FAIL on the missing versioned lifecycle integration.

- [ ] **Step 3: Refactor the Swift bridge**

Add shared process-global state:

```swift
private static let lifecycle = SentryLifecycleCoordinator()
private static let lifecycleDriver = AppleSentrySDKDriver()
private var lifecycleOwner = ""
```

Expose:

```swift
@Callable
func lifecycleVersion() -> Int {
    sentryLifecycleVersion
}
```

During `configure`, validate the owner and construct
`SentryLifecycleConfiguration` before touching the active client. Disabled
configuration calls `lifecycle.disable(owner:driver:)` only for the matching
owner. Enabled configuration calls:

```swift
guard Self.lifecycle.configure(
    owner: owner,
    configuration: configuration,
    driver: Self.lifecycleDriver
) else {
    return bridgeErrorFailed
}
lifecycleOwner = owner
globalAttributes = configuration.globalAttributes
logsEnabled = configuration.logsEnabled
metricsEnabled = configuration.metricsEnabled
return bridgeErrorOK
```

Replace native lifecycle callables with:

```swift
@Callable
func isAvailable(_ owner: String) -> Bool

@Callable
func flush(_ owner: String, _ timeoutMsec: Int) -> Int

@Callable
func shutdown(_ owner: String)
```

All capture methods must check the stored owner through the coordinator before
using the global SDK. Remove the per-instance `configured`, `didShutdown`, and
`closeActiveClient()` lifecycle state.

- [ ] **Step 4: Verify the source contract and compile the Apple bridge**

Run:

```bash
scripts/test-sentry-ios-build-contract
task ios:sentry
```

Expected: contract checks pass and Xcode builds the iOS device, iOS simulator,
and macOS release frameworks.

- [ ] **Step 5: Commit the Swift bridge integration**

```bash
git add \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj/project.pbxproj \
  scripts/test-sentry-ios-build-contract
git commit -m "feat: activate owner-safe Apple crash handling"
```

### Task 4: Build and integrate the Android lifecycle coordinator

**Files:**
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinator.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinatorTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`
- Modify: `Taskfile.yml`

- [ ] **Step 1: Write failing Java coordinator and option tests**

Create tests matching the Swift transition matrix:

```java
@Test public void publishesOwnerOnlyAfterSuccessfulStart()
@Test public void equivalentConfigurationDoesNotRestart()
@Test public void changedConfigurationClosesThenStarts()
@Test public void staleOwnerCannotFlushOrShutdown()
@Test public void failedReplacementRestoresPreviousOwnerAndConfiguration()
@Test public void shutdownIsIdempotent()
@Test public void appliesNativeCrashAndMetadataOptions()
```

The option test must assert:

```java
assertTrue(options.isEnableUncaughtExceptionHandler());
assertTrue(options.isEnableNdk());
assertTrue(options.isEnableScopeSync());
assertEquals(2000L, options.getShutdownTimeoutMillis());
assertEquals("game@1.2.3", options.getRelease());
assertEquals("qa", options.getEnvironment());
assertEquals("android", options.getDist());
```

Update bridge tests to supply `"lifecycle_owner"` and call owner-aware
availability, flush, and shutdown methods.

- [ ] **Step 2: Run the focused Java tests and verify RED**

Run:

```bash
addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/gradlew \
  -p addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry \
  testDebugUnitTest \
  --tests '*SentryLifecycleCoordinatorTest'
```

Expected: FAIL because the coordinator and option mapper do not exist.

- [ ] **Step 3: Implement the Android coordinator**

Create a package-private `SentryLifecycleCoordinator` with:

```java
static final int LIFECYCLE_VERSION = 1;
static final long SHUTDOWN_TIMEOUT_MILLIS = 2000L;

interface Driver {
  boolean isEnabled();
  boolean start(Configuration configuration);
  void flush(long timeoutMillis);
  void close();
}
```

Its `configure`, `disable`, `isAvailable`, `flush`, and `shutdown` behavior
must match the Swift coordinator exactly, including previous-client
restoration and stale-owner no-ops. `Configuration.equals()` must compare every
SDK-start field and a defensive copy of global attributes and provider
options.

Add:

```java
static void applyNativeCrashOptions(
    SentryAndroidOptions options,
    Configuration configuration) {
  options.setEnableUncaughtExceptionHandler(true);
  options.setEnableNdk(true);
  options.setEnableScopeSync(true);
  options.setShutdownTimeoutMillis(SHUTDOWN_TIMEOUT_MILLIS);
  options.setRelease(emptyToNull(configuration.release));
  options.setEnvironment(emptyToNull(configuration.environment));
  options.setDist(emptyToNull(configuration.dist));
}
```

After `SentryAndroid.init`, install global attributes as a native scope
context:

```java
Sentry.configureScope(scope -> scope.setContexts(
    "foundry",
    Map.of("global_attributes", configuration.globalAttributes)));
```

- [ ] **Step 4: Integrate the Android bridge and local test gate**

Use one static coordinator, read the owner from the payload, expose
`lifecycleVersion()`, and change bridge signatures to:

```java
public boolean isAvailable(String owner)
public int flush(String owner, int timeoutMsec)
public void shutdown(String owner)
```

Remove per-instance `configured`, `didShutdown`, and `closeActiveClient()`
state. Preserve existing event/log/breadcrumb/feedback/metric mappings and ANR
configuration.

Add to `Taskfile.yml`:

```yaml
  test:sentry-java:
    desc: Run deterministic Sentry Android JUnit cases
    dir: addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
    cmds:
      - ./gradlew test

  test:sentry-android:
    desc: Run Android Sentry unit and build contract tests
    deps:
      - test:sentry-java
      - test:sentry-android-contract
```

- [ ] **Step 5: Run Java and Android build verification**

Run:

```bash
task test:sentry-java
task android:sentry
```

Expected: all JUnit/Robolectric tests pass, Android lint passes, and debug and
release AARs build.

- [ ] **Step 6: Commit the Android lifecycle**

```bash
git add Taskfile.yml \
  addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinator.java \
  addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java \
  addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinatorTest.java \
  addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java
git commit -m "feat: activate owner-safe Android crash handling"
```

### Task 5: Enforce NDK and no-shipped-trigger contracts

**Files:**
- Modify: `scripts/test-sentry-android-build-contract`
- Modify: `scripts/test-sentry-ios-build-contract`
- Modify: `scripts/test-package`
- Create: `scripts/trigger-test-native-crash`

- [ ] **Step 1: Write failing contract checks**

Add Android source checks for:

```bash
rg -q 'setEnableUncaughtExceptionHandler(true)' "$lifecycle_source"
rg -q 'setEnableNdk(true)' "$lifecycle_source"
rg -q 'setEnableScopeSync(true)' "$lifecycle_source"
rg -q 'setShutdownTimeoutMillis' "$lifecycle_source"
```

Resolve the release runtime dependencies with Gradle and require
`io.sentry:sentry-android-ndk:8.50.1`:

```bash
"$android_source/gradlew" -q -p "$android_source" \
  dependencies --configuration releaseRuntimeClasspath \
  | rg -q 'io\.sentry:sentry-android-ndk:8\.50\.1'
```

In the Apple and Android contract scripts, reject production method names:

```bash
if rg -n 'crashWith|crash_with|triggerNativeCrash|trigger_native_crash|badCode|bad_code' \
    "$bridge_source" "$lifecycle_source"; then
  fail "production bridge contains a deliberate crash trigger"
fi
```

In `scripts/test-package`, reject any archive entry containing:

```bash
if rg -n -i 'trigger.*crash|crash.*trigger|bad[_-]?code|native-crash-validation' \
    <<<"$listing"$'\n'"$sentry_listing"; then
  fail "package contains deliberate crash validation tooling"
fi
```

- [ ] **Step 2: Run contract tests and verify RED**

Run:

```bash
scripts/test-sentry-android-build-contract
scripts/test-sentry-ios-build-contract
scripts/test-package
```

Expected: at least the lifecycle source and repository helper checks fail
before the helper and final contracts are present.

- [ ] **Step 3: Add the guarded repository-only helper**

Create executable `scripts/trigger-test-native-crash` with these interfaces:

```text
scripts/trigger-test-native-crash macos <pid> --i-understand-this-will-crash
scripts/trigger-test-native-crash android <debuggable-package> --i-understand-this-will-crash
```

The script must:

- reject missing confirmation;
- reject PID 0 or 1 and nonnumeric macOS PIDs;
- validate Android package identifiers;
- print the resolved process before signalling it;
- use `kill -ABRT -- "$pid"` on macOS;
- resolve Android with `adb shell pidof -s "$package"` and use
  `adb shell run-as "$package" kill -6 "$pid"`;
- reject every other platform without signalling anything.

- [ ] **Step 4: Run contract and package verification**

Run:

```bash
scripts/test-sentry-android-build-contract
scripts/test-sentry-ios-build-contract
scripts/test-package
```

Expected: all checks pass, and the generated addon archives contain no
repository crash helper.

- [ ] **Step 5: Commit the safety contracts and helper**

```bash
git add scripts/test-sentry-android-build-contract \
  scripts/test-sentry-ios-build-contract \
  scripts/test-package \
  scripts/trigger-test-native-crash
git commit -m "test: guard native crash validation tooling"
```

### Task 6: Document startup, recovery, and safe validation

**Files:**
- Create: `docs/NATIVE_CRASH_VALIDATION.md`
- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `BUILD.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add documentation contract checks**

Extend the relevant build-contract scripts to require the public guide and the
terms:

```text
previous launch
release
environment
macOS
iOS
Android
--i-understand-this-will-crash
```

Require README and API documentation to mention
`Error.ERR_UNAVAILABLE`, earliest startup configuration, owner-safe
reconfiguration, and bounded shutdown.

- [ ] **Step 2: Run the documentation contracts and verify RED**

Run:

```bash
scripts/test-sentry-ios-build-contract
scripts/test-sentry-android-build-contract
```

Expected: FAIL because the native crash validation guide and lifecycle
documentation are absent.

- [ ] **Step 3: Write the validation guide**

`docs/NATIVE_CRASH_VALIDATION.md` must include:

- a non-production Sentry project warning;
- configuration from the earliest startup hook;
- a unique release/environment/dist per validation run;
- an `is_available()` precondition;
- the two-run crash/relaunch/delivery protocol;
- guarded helper commands for macOS and Android;
- LLDB/Xcode steps for iOS simulator and physical devices;
- required Sentry event inspection for release, environment, distribution,
  global attributes, native mechanism/stack, device, OS, and app contexts;
- a normal-shutdown/relaunch duplicate check;
- cleanup and a warning never to use production player data.

- [ ] **Step 4: Update public lifecycle documentation**

Document:

- native crash capture and next-launch delivery in `README.md`;
- configure result and availability semantics in `docs/API.md`;
- earliest supported configuration boundary and the pre-configuration gap;
- metadata attachment at SDK startup;
- owner-safe Sentry replacement and bounded 2-second shutdown;
- LLDB/Xcode/ADB prerequisites in `BUILD.md`;
- issue #7 behavior in `CHANGELOG.md`.

- [ ] **Step 5: Run documentation and formatting checks**

Run:

```bash
scripts/test-sentry-ios-build-contract
scripts/test-sentry-android-build-contract
prek run --files README.md docs/API.md docs/NATIVE_CRASH_VALIDATION.md \
  BUILD.md CHANGELOG.md
```

Expected: all documentation contracts and formatting hooks pass.

- [ ] **Step 6: Commit documentation**

```bash
git add README.md docs/API.md docs/NATIVE_CRASH_VALIDATION.md \
  BUILD.md CHANGELOG.md \
  scripts/test-sentry-ios-build-contract \
  scripts/test-sentry-android-build-contract
git commit -m "docs: explain native crash recovery validation"
```

### Task 7: Verify the complete lifecycle and package

**Files:**
- Modify only if verification exposes a defect in files from Tasks 1-6.

- [ ] **Step 1: Run focused lifecycle suites**

Run:

```bash
task test:sentry-swift
task test:sentry-java
scripts/test-project
```

Expected: all Swift, Java, and FoundryScript lifecycle tests pass.

- [ ] **Step 2: Run native build and packaging gates**

Run:

```bash
task ios:sentry
task android:sentry
REQUIRE_NATIVE_ARTIFACTS=1 task package VERSION=0.1.0
task verify:sentry-package VERSION=0.1.0
```

Expected: Apple and Android artifacts build, strict packaging succeeds, and
the native package verifier accepts both platform artifacts.

- [ ] **Step 3: Run the complete repository gate**

Run:

```bash
task test
```

Expected: lint, workflow, package, FoundryScript, Swift, Java, Apple contract,
and Android contract gates all pass.

- [ ] **Step 4: Inspect final state**

Run:

```bash
git diff --check
git status --short
git log --oneline --decorate -8
```

Expected: no whitespace errors; only intentional uncommitted verification
artifacts, if any, are ignored; all implementation commits are visible.

- [ ] **Step 5: Commit any verification-only corrections**

If verification required corrections, stage only those exact files and commit:

```bash
git commit -m "fix: satisfy native crash lifecycle verification"
```

If no corrections remain, do not create an empty commit.
