# Automatic Runtime Context Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich macOS, iOS, and Android Sentry events with safe stable and capture-time Godot runtime context without changing the provider-neutral API.

**Architecture:** A FoundryScript probe reads Godot singletons and a collector normalizes stable and volatile custom contexts. `SentryObservabilityProvider` forwards stable context during configuration and a merged snapshot during capture; Swift and Java install stable context globally for crashes and use capture-local scope callbacks for refreshed ordinary-event context.

**Tech Stack:** FoundryScript, Foundry testlib, Swift 6/XCTest, Sentry Cocoa 9.23.0, Java 17/JUnit, Sentry Android 8.50.1, Bash contract tests, Task.

---

## File structure

- Create `addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs`:
  production-only adapter around Godot singletons.
- Create `addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs`:
  provider-private normalization, privacy filtering, runtime classification,
  and stable/volatile merge behavior.
- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`:
  collect, cache, and forward context without changing public core contracts.
- Modify `test_project/tests/observability-sentry.test.fs`:
  deterministic collector and provider behavior tests with a local fake probe.
- Modify Swift mapper, lifecycle, bridge, and tests under
  `addons/FoundryObservabilitySentry/FoundryObservabilitySentry`.
- Modify Java mapper, lifecycle configuration, driver, bridge, and tests under
  `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry`.
- Modify `scripts/test-foundry-script`, `scripts/test-package`, and
  `docs/API.md` for resource/package/documentation contracts.

### Task 1: FoundryScript context collection

**Files:**
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs.uid`
- Modify: `test_project/tests/observability-sentry.test.fs`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Add failing resource and collector tests**

Add resource assertions to `scripts/test-foundry-script`:

```bash
[[ -f "$sentry_addon/SentryRuntimeContextProbe.fs" ]] \
	|| fail "Sentry runtime context probe is missing"
[[ -f "$sentry_addon/SentryRuntimeContextCollector.fs" ]] \
	|| fail "Sentry runtime context collector is missing"
```

Add a local fake probe to `observability-sentry.test.fs`. It returns complete
realistic snapshots and increments `memory_call_count` from `memory_values()`:

```gdscript
class FakeRuntimeContextProbe extends RefCounted:
	var platform: String = "macOS"
	var memory_call_count: int = 0
	var volatile_free_memory: int = 2048
	var volatile_usable_memory: int = 4096
	var volatile_free_storage: int = 8192
	var volatile_orientation: String = "landscape"

	func platform_name() -> String:
		return platform

	func application_values() -> Dictionary:
		return {
			"name": "Oakhaven",
			"version": "1.2.3",
			"start_time": "2026-07-25T12:00:00Z",
			"architecture": "arm64",
		}

	func engine_values() -> Dictionary:
		return {
			"version": "4.5.stable",
			"version_commit": "abc123",
			"architecture": "arm64",
			"editor": false,
			"debug_build": true,
			"headless": false,
			"dedicated_server": false,
		}

	func device_values() -> Dictionary:
		return {
			"model": "Mac16,1",
			"processor_name": "Apple M4",
			"processor_count": 10,
		}

	func memory_values() -> Dictionary:
		memory_call_count += 1
		return {
			"physical": 17179869184,
			"free": volatile_free_memory,
			"available": volatile_usable_memory,
		}

	func free_storage() -> int:
		return volatile_free_storage

	func display_values() -> Dictionary:
		return {
			"server": "macOS",
			"screen_count": 1,
			"touchscreen_available": false,
			"primary_width_pixels": 3024,
			"primary_height_pixels": 1964,
			"primary_dpi": 254,
			"primary_refresh_rate": 120.0,
			"primary_orientation": volatile_orientation,
		}

	func primary_orientation() -> String:
		return volatile_orientation

	func gpu_values() -> Dictionary:
		return {
			"name": "Apple M4",
			"vendor_name": "Apple",
			"api_version": "Metal 3",
			"device_type": "integrated_gpu",
			"driver_name": "Metal",
			"driver_version": "1",
			"rendering_method": "gl_compatibility",
		}

	func runtime_values() -> Dictionary:
		return {"sandboxed": true, "userfs_persistent": true}

	func privacy_values() -> Dictionary:
		return {
			"unique_identifier": "private-device-id",
			"locale": "en_US",
			"timezone": "America/New_York",
		}
```

Add tests which construct `SentryRuntimeContextCollector.new(probe)` and
assert:

```gdscript
var stable := collector.stable_contexts("production", false)
Expect.that(stable["foundry_app"]["name"]).to_equal("Oakhaven")
Expect.that(stable["godot_engine"]["runtime_mode"]).to_equal("debug_export")
Expect.that(stable["foundry_device"]["memory_size"]).to_equal(17179869184)
Expect.that(stable["display"]["primary_width_pixels"]).to_equal(3024)
Expect.that(stable["gpu"]["name"]).to_equal("Apple M4")
Expect.that(stable["foundry_runtime"]["environment"]).to_equal("production")
Expect.that(stable["foundry_device"].has("unique_identifier")).to_be_false()
Expect.that(stable["foundry_device"].has("locale")).to_be_false()
Expect.that(stable["foundry_device"].has("timezone")).to_be_false()
```

Add separate tests for:

```gdscript
var pii := collector.stable_contexts("production", true)
Expect.that(pii["foundry_device"]["unique_identifier"]).to_equal("private-device-id")

probe.platform = "iOS"
var ios := collector.stable_contexts("production", true)
Expect.that(probe.memory_call_count).to_equal(0)
Expect.that(ios["foundry_device"].has("memory_size")).to_be_false()

probe.volatile_free_memory = 1024
probe.volatile_free_storage = 4096
probe.volatile_orientation = "portrait"
var merged := collector.contexts_for_capture(stable)
Expect.that(merged["foundry_device"]["free_memory"]).to_equal(1024)
Expect.that(merged["foundry_device"]["free_storage"]).to_equal(4096)
Expect.that(merged["display"]["primary_orientation"]).to_equal("portrait")
Expect.that(stable["foundry_device"]["free_memory"]).to_equal(2048)
```

Runtime-mode cases must assert the exact precedence `headless`, `editor`,
`debug_export`, and `release_export`. Invalid-value cases must assert omission
of `GenericDevice`, empty GPU names, nonpositive dimensions/counts, negative
capacities, and empty privacy values.

- [ ] **Step 2: Run the red checks**

Run:

```bash
scripts/test-foundry-script
```

Expected: failure because both new Sentry resources are missing. After adding
empty class shells so the test project parses, run:

```bash
scripts/test-project
```

Expected: assertion failures because the shell collectors return empty
dictionaries.

- [ ] **Step 3: Implement the probe**

Implement `SentryRuntimeContextProbe` with these production methods:

```gdscript
namespace foundry.observability.sentry

class_name SentryRuntimeContextProbe
extends RefCounted

func platform_name() -> String
func application_values() -> Dictionary
func engine_values() -> Dictionary
func device_values() -> Dictionary
func memory_values() -> Dictionary
func free_storage() -> int
func display_values() -> Dictionary
func primary_orientation() -> String
func gpu_values() -> Dictionary
func runtime_values() -> Dictionary
func privacy_values() -> Dictionary
```

Use `ProjectSettings`, `Engine`, `OS`, `Time`, `DisplayServer`,
`RenderingServer`, and `DirAccess.open("user://")`. Normalize screen
orientation and GPU device type inside the probe. Return empty strings,
negative capacities, or empty dictionaries when a singleton value is
unavailable; do not emit diagnostics.

- [ ] **Step 4: Implement the collector**

Implement:

```gdscript
namespace foundry.observability.sentry

class_name SentryRuntimeContextCollector
extends RefCounted

var _probe: Object

func _init(p_probe: Object) -> void
func stable_contexts(environment: String, send_default_pii: bool) -> Dictionary
func volatile_contexts() -> Dictionary
func contexts_for_capture(stable_contexts: Dictionary) -> Dictionary
```

Build `foundry_app`, `godot_engine`, `foundry_device`, `display`, `gpu`, and
`foundry_runtime` exactly as specified in the design. Skip
`_probe.memory_values()` when `platform_name() == "iOS"`. Copy stable input
before deep-merging only present volatile fields. Filter values through small
private helpers for nonempty strings, positive numbers, nonnegative
capacities, valid booleans, and nonempty context dictionaries.

- [ ] **Step 5: Generate UIDs and verify green**

Run:

```bash
/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64 \
  --headless project import --project test_project
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: collector tests pass; strict FoundryScript lint and UID contracts
pass.

- [ ] **Step 6: Commit**

```bash
git add addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs \
  addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs.uid \
  addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs \
  addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs.uid \
  test_project/tests/observability-sentry.test.fs scripts/test-foundry-script
git commit -m "feat: collect automatic runtime context"
```

### Task 2: Provider lifecycle and payload forwarding

**Files:**
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`
- Test: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing provider tests**

Add tests that inject `p_runtime_context_probe` and verify:

```gdscript
var provider := SentryObservabilityProvider.new(
		p_bridge = bridge,
		p_runtime_context_probe = probe,
	)
Expect.that(provider.configure(config)).to_equal(Error.OK)
Expect.that(
		bridge.configured_payload["stable_contexts"]["foundry_app"]["name"]
	).to_equal("Oakhaven")

probe.volatile_free_memory = 777
Expect.that(provider.capture(ObservabilityEvent.new(
		p_message = "context capture",
		p_attributes = {"explicit": "preserved"},
	))).to_equal("sentry:1")
Expect.that(
		bridge.captured_payloads[0]["contexts"]["foundry_device"]["free_memory"]
	).to_equal(777)
Expect.that(bridge.captured_payloads[0]["attributes"]).to_equal(
		{"explicit": "preserved"},
	)
```

Add a failed-reconfiguration test that changes the fake stable application
name, forces `bridge.configure_result = Error.FAILED`, and verifies the next
capture still uses the original cached stable context. Add disabled and
shutdown tests verifying no stale context is captured.

- [ ] **Step 2: Verify red**

Run:

```bash
scripts/test-project
```

Expected: failures because the provider constructor rejects
`p_runtime_context_probe` or payloads omit `stable_contexts` and `contexts`.

- [ ] **Step 3: Implement provider forwarding**

Extend the provider with:

```gdscript
var _context_collector: SentryRuntimeContextCollector
var _stable_contexts: Dictionary = {}

func _init(
		p_bridge: Object? = null,
		p_runtime_context_probe: Object? = null,
) -> void
```

Create the real probe when none is injected. During `configure`, compute a
candidate stable snapshot only for enabled configuration, pass it as
`stable_contexts`, and cache it only when the bridge returns `Error.OK`.
Preserve the prior cache after failure. Clear it after successful disable and
shutdown. During ordinary capture, add a nonempty
`_context_collector.contexts_for_capture(_stable_contexts)` as `contexts`
without modifying attributes.

- [ ] **Step 4: Verify green and commit**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: 0 test failures and no FoundryScript warnings.

```bash
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests/observability-sentry.test.fs
git commit -m "feat: forward runtime context through Sentry provider"
```

### Task 3: Apple context conversion and capture-local scope

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryLifecycleCoordinator.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryLifecycleCoordinatorTests.swift`

- [ ] **Step 1: Add failing Swift tests**

Add mapper tests for:

```swift
let contexts = foundrySentryContexts([
    "godot_engine": [
        "version": "4.5",
        "debug_build": true,
        "nested": ["kept": 1, "infinite": Double.infinity],
        "unsupported": NSObject(),
    ],
    "empty": [:],
])
XCTAssertEqual(contexts["godot_engine"]?["version"] as? String, "4.5")
XCTAssertNil((contexts["godot_engine"]?["nested"] as? [String: Any])?["infinite"])
XCTAssertNil(contexts["godot_engine"]?["unsupported"])
XCTAssertNil(contexts["empty"])
```

Add a scope helper test:

```swift
let scope = Scope()
applySentryContexts(contexts, to: scope)
let serialized = scope.serialize()["context"] as? [String: Any]
XCTAssertEqual(
    (serialized?["godot_engine"] as? [String: Any])?["version"] as? String,
    "4.5"
)
```

Extend lifecycle tests so two configurations differing only in
`stableContexts` restart, and initial scope serialization contains both the
existing `foundry` crash context and the automatic contexts.

- [ ] **Step 2: Verify red**

Run:

```bash
swift test --package-path \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry
```

Expected: compile failures for missing `foundrySentryContexts`,
`applySentryContexts`, and `stableContexts`.

- [ ] **Step 3: Implement Swift conversion and lifecycle**

Add:

```swift
func foundrySentryContexts(_ value: Any?) -> [String: [String: Any]]
func applySentryContexts(_ contexts: [String: [String: Any]], to scope: Scope)
```

Reuse the existing bounded, cycle-safe recursive sanitizer for each outer
context dictionary; require nonempty string keys and omit empty contexts.
Add `stableContexts: [String: Any]` to `SentryLifecycleConfiguration`, include
it in equality, parse `stable_contexts` in the bridge, and apply it from
`options.initialScope`.

In `capture`, parse `contexts` and call:

```swift
let eventID = SentrySDK.capture(event: event) { scope in
    applySentryContexts(contexts, to: scope)
}
```

This capture-local scope must be used instead of assigning `Event.context`,
because Cocoa scope enrichment overwrites raw event context.

- [ ] **Step 4: Verify green and commit**

Run:

```bash
swift test --package-path \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry
```

Expected: all XCTest cases pass.

```bash
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry
git commit -m "feat: attach runtime context on Apple"
```

### Task 4: Android context conversion and capture-local scope

**Files:**
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleConfiguration.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/AndroidSentrySdkDriver.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinatorTest.java`

- [ ] **Step 1: Add failing Java tests**

Add mapper tests:

```java
Map<String, Map<String, Object>> contexts = SentryEventMapper.contexts(Map.of(
    "godot_engine", Map.of("version", "4.5", "debug_build", true),
    "empty", Map.of()));
assertEquals("4.5", contexts.get("godot_engine").get("version"));
assertFalse(contexts.containsKey("empty"));
```

Use mutable dictionaries to verify unsupported objects, non-finite numbers,
cycles, and invalid outer keys are omitted. Instantiate
`new Scope(new SentryOptions())`, call
`AndroidSentrySdkDriver.applyContexts(scope, contexts)`, and assert the
resulting `scope.getContexts()` contains the sanitized context.

Extend lifecycle tests so `stableContexts` participates in equality and the
existing `foundryCrashContext` is installed beside the automatic contexts on
scope.

- [ ] **Step 2: Verify red**

Run:

```bash
ANDROID_HOME=/Users/christian/Library/Android/sdk \
  ./addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/gradlew \
  -p addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry test
```

Expected: compile failures for missing `contexts`, `applyContexts`, and the
`stableContexts` configuration member.

- [ ] **Step 3: Implement Java conversion and lifecycle**

Add a bounded `contexts(Object)` outer-map sanitizer that reuses the existing
cycle-safe variable sanitizer. Add immutable `stableContexts` to lifecycle
configuration, equality, and hashing. Parse `stable_contexts` in the bridge.
Install stable context after Android SDK initialization:

```java
Sentry.configureScope(scope -> {
  scope.setContexts("foundry", foundryCrashContext(configuration));
  applyContexts(scope, configuration.stableContexts);
});
```

Capture ordinary events with:

```java
Map<String, Map<String, Object>> contexts =
    SentryEventMapper.contexts(payload.get("contexts"));
return eventIdString(Sentry.captureEvent(
    event,
    scope -> AndroidSentrySdkDriver.applyContexts(scope, contexts)));
```

- [ ] **Step 4: Verify green and commit**

Run:

```bash
ANDROID_HOME=/Users/christian/Library/Android/sdk \
  ./addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/gradlew \
  -p addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry test
```

Expected: `BUILD SUCCESSFUL`, with debug and release unit tests passing.

```bash
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
git commit -m "feat: attach runtime context on Android"
```

### Task 5: Package contract, public documentation, and complete verification

**Files:**
- Modify: `scripts/test-package`
- Modify: `docs/API.md`
- Modify: `docs/superpowers/specs/2026-07-25-automatic-runtime-context-enrichment-design.md`
- Modify: `docs/superpowers/plans/2026-07-25-automatic-runtime-context-enrichment.md`

- [ ] **Step 1: Add failing package/documentation assertions**

Before changing documentation, add:

```bash
for context_resource in SentryRuntimeContextProbe SentryRuntimeContextCollector; do
	grep -qx "addons/FoundryObservabilitySentry/${context_resource}.fs" \
		<<<"$sentry_listing" \
		|| fail "Sentry package is missing ${context_resource}.fs"
done
rg -q 'Automatic runtime context' "$repo_root/docs/API.md" \
	|| fail "API docs are missing automatic runtime context"
rg -q 'send_default_pii' "$repo_root/docs/API.md" \
	|| fail "API docs are missing runtime context privacy behavior"
```

Run:

```bash
scripts/test-package
```

Expected: failure because the API documentation section is missing.

- [ ] **Step 2: Document the behavior**

Add an `Automatic runtime context` subsection to `docs/API.md` containing:

- the six custom context names and field families;
- configuration-time versus capture-time collection;
- runtime-mode precedence;
- iOS memory omission;
- unsupported-field omission;
- `send_default_pii` behavior;
- native app/device/OS non-replacement;
- stable crash context and deliberate absence of volatile next-launch crash
  context.

Keep the corrected design language explaining capture-local scope callbacks.

- [ ] **Step 3: Verify package and focused suites**

Run:

```bash
scripts/test-package
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
swift test --package-path \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry
ANDROID_HOME=/Users/christian/Library/Android/sdk \
  ./addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/gradlew \
  -p addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry test
```

Expected: every command exits 0.

- [ ] **Step 4: Run the complete validation gate**

Run:

```bash
ANDROID_HOME=/Users/christian/Library/Android/sdk task test
```

Expected: lint, package, CI contracts, Foundry tests, Swift tests, Android
tests, and platform build contracts all exit 0.

- [ ] **Step 5: Review requirements and commit**

Compare the implementation with every acceptance criterion in the design,
inspect `git diff --check`, and confirm no provider-neutral API file changed.

```bash
git add docs/API.md scripts/test-package \
  docs/superpowers/specs/2026-07-25-automatic-runtime-context-enrichment-design.md \
  docs/superpowers/plans/2026-07-25-automatic-runtime-context-enrichment.md
git commit -m "docs: describe automatic runtime context"
```
