# First-Class Custom Metrics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add validated, filtered, sampled provider-neutral counters, gauges, and distributions with native Sentry delivery on macOS, iOS, and Android.

**Architecture:** The core owns immutable metric values, validation, global/per-record attribute normalization, filtering, and deterministic accumulator sampling. Metrics are an optional provider capability so existing event-only providers remain valid. Sentry Cocoa and Sentry Android own aggregation, batching, transport, and flushing through their existing SDK queues.

**Tech Stack:** FoundryScript, Foundry testlib, Swift 6/Sentry Cocoa 9.23.0, Java 17/Sentry Android 8.50.1, Bash contract tests, Task.

---

## File map

- Create `addons/FoundryObservability/ObservabilityMetricType.fs`: shared type constants.
- Create `addons/FoundryObservability/ObservabilityMetric.fs`: defensive normalized metric value.
- Create `addons/FoundryObservability/ObservabilityMetricsProvider.fs`: optional provider capability.
- Create UID companions for all three FoundryScript sources.
- Modify `addons/FoundryObservability/ObservabilityConfig.fs`: metrics enablement, sample rate, and filter.
- Modify `addons/FoundryObservability/FoundryObservabilityApi.fs`: public metric capture API.
- Modify `addons/FoundryObservability/FoundryObservability.fs`: validation, normalization, filtering, sampling, and dispatch.
- Modify `addons/FoundryObservability/MemoryObservabilityProvider.fs`: deterministic metric storage.
- Modify `addons/FoundryObservability/NullObservabilityProvider.fs`: allocation-free metric no-op.
- Create `test_project/tests/support/metricless_observability_provider.notest.fs`: unsupported-capability fixture.
- Modify `test_project/tests/observability-core.test.fs`: core metric regression coverage.
- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`: normalized bridge forwarding.
- Modify Sentry FoundryScript fake bridge and tests for optional metric support.
- Modify the existing Swift mapper, bridge, and tests for Sentry Cocoa metrics.
- Create Android `SentryMetricMapper.java` and `SentryMetricMapperTest.java`.
- Modify the Android bridge and bridge tests for Sentry Android metrics.
- Modify repository contract scripts and public documentation.

### Task 1: Metric value and optional capability

**Files:**

- Create: `addons/FoundryObservability/ObservabilityMetricType.fs`
- Create: `addons/FoundryObservability/ObservabilityMetricType.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityMetric.fs`
- Create: `addons/FoundryObservability/ObservabilityMetric.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityMetricsProvider.fs`
- Create: `addons/FoundryObservability/ObservabilityMetricsProvider.fs.uid`
- Modify: `scripts/test-foundry-script`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write the failing value-object tests**

Add tests that require all three constants and defensive attribute copying:

```foundryscript
func test_metric_types_and_value_copy_attributes() -> void:
	var source := {"region": "iad", "nested": {"attempt": 1}}
	var metric := ObservabilityMetric.new(
			p_type = ObservabilityMetricType.DISTRIBUTION,
			p_name = "match.duration",
			p_value = 125.5,
			p_unit = "millisecond",
			p_attributes = source,
		)
	source["region"] = "changed"
	var exposed: Dictionary = metric.attributes()
	exposed["region"] = "also changed"

	Expect.that(ObservabilityMetricType.COUNTER).to_equal(0)
	Expect.that(ObservabilityMetricType.GAUGE).to_equal(1)
	Expect.that(ObservabilityMetricType.DISTRIBUTION).to_equal(2)
	Expect.that(metric.type()).to_equal(ObservabilityMetricType.DISTRIBUTION)
	Expect.that(metric.name()).to_equal("match.duration")
	Expect.that(metric.value()).to_be_close_to(125.5)
	Expect.that(metric.unit()).to_equal("millisecond")
	Expect.that(metric.attributes()).to_equal({
			"region": "iad", "nested": {"attempt": 1},
		})
```

- [ ] **Step 2: Run the focused suite and verify RED**

Run:

```sh
scripts/test-project
```

Expected: FAIL because `ObservabilityMetric` and `ObservabilityMetricType` do
not exist.

- [ ] **Step 3: Add the minimal metric model and capability**

Create the type constants:

```foundryscript
namespace foundry.observability

## Provider-neutral custom metric kinds.
class_name ObservabilityMetricType
extends RefCounted

const COUNTER: int = 0
const GAUGE: int = 1
const DISTRIBUTION: int = 2
```

Create `ObservabilityMetric` with private scalar fields, a deep-copied
dictionary, and these exact accessors:

```foundryscript
func type() -> int
func name() -> String
func value() -> float
func unit() -> String
func attributes() -> Dictionary
```

Create the optional provider trait:

```foundryscript
namespace foundry.observability

## Optional provider capability for accepting normalized custom metrics.
trait_name ObservabilityMetricsProvider

## Accepts a normalized metric into the provider's local SDK or store.
abstract func capture_metric(metric: ObservabilityMetric) -> bool
```

Use these UID values:

```text
ObservabilityMetricType.fs.uid: uid://c3x9m7q2v5k8n
ObservabilityMetric.fs.uid: uid://d8r4p6w1y9t2h
ObservabilityMetricsProvider.fs.uid: uid://b5n7k3s8q1m4v
```

Extend `scripts/test-foundry-script` to assert all three source files,
`class_name ObservabilityMetric`, `class_name ObservabilityMetricType`, and
`trait_name ObservabilityMetricsProvider`.

- [ ] **Step 4: Run focused verification and verify GREEN**

Run:

```sh
scripts/test-project
scripts/test-foundry-script
```

Expected: all existing tests plus the new metric value test pass; both addon
lint passes report no diagnostics.

- [ ] **Step 5: Commit the model**

```sh
git add addons/FoundryObservability scripts/test-foundry-script test_project/tests/observability-core.test.fs
git commit -m "feat: add provider-neutral metric values"
```

### Task 2: Core validation, filtering, sampling, and memory storage

**Files:**

- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs`
- Modify: `addons/FoundryObservability/NullObservabilityProvider.fs`
- Create: `test_project/tests/support/metricless_observability_provider.notest.fs`
- Create: `test_project/tests/support/metricless_observability_provider.notest.fs.uid`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing API and normalized payload tests**

Add separate tests for:

```foundryscript
func test_metric_convenience_methods_store_normalized_payloads() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {"build": 42, "shared": "global"},
		))).to_equal(Error.OK)

	Expect.that(service.capture_counter(
			"match.started", 2, {"shared": "metric"},
		)).to_be_true()
	Expect.that(service.capture_gauge(
			"players.active", 7.0, "player",
		)).to_be_true()
	Expect.that(service.capture_distribution(
			"match.duration", 125.5, "millisecond", {"region": "iad"},
		)).to_be_true()

	Expect.that(provider.metrics()).to_have_size(3)
	Expect.that(provider.metrics()[0].type()).to_equal(ObservabilityMetricType.COUNTER)
	Expect.that(provider.metrics()[0].attributes()).to_equal({
			"build": 42, "shared": "metric",
		})
	Expect.that(provider.metrics()[2].unit()).to_equal("millisecond")
	service.shutdown()
```

Also add tests named:

```text
test_metrics_reject_invalid_names_values_units_and_attributes
test_metrics_honor_disabled_configuration_and_filter
test_metrics_apply_deterministic_sampling_after_filtering
test_metricless_provider_keeps_event_capture_operational
test_metrics_do_not_affect_events_feedback_logs_or_flush
test_invalid_metric_configuration_keeps_active_provider
```

The `0.25` sampling test must submit eight eligible metrics and expect only the
fourth and eighth in memory. The filter test must use
`Callable(self, "_keep_combat_metric")`, where:

```foundryscript
func _keep_combat_metric(metric: ObservabilityMetric) -> bool:
	return metric.name().begins_with("combat.")
```

Create a metricless fixture implementing only `ObservabilityProvider`; its
event capture returns sequential `metricless:<n>` IDs.

- [ ] **Step 2: Run core tests and verify RED**

Run:

```sh
scripts/test-project
```

Expected: FAIL on missing configuration fields and capture methods.

- [ ] **Step 3: Extend configuration and public API**

Add to `ObservabilityConfig`:

```foundryscript
var metrics_enabled: bool = true
var metric_sample_rate: float = 1.0
var metric_filter: Callable = Callable()
```

Add matching constructor parameters after existing log parameters and assign
them without clamping so `configure()` can reject invalid rates.

Add to `FoundryObservabilityApi`:

```foundryscript
abstract func capture_metric(metric: ObservabilityMetric) -> bool
abstract func capture_counter(name: String, value: int = 1, attributes: Dictionary = {}) -> bool
abstract func capture_gauge(name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
abstract func capture_distribution(name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
```

- [ ] **Step 4: Implement core validation and deterministic sampling**

Add constants for maximum name, unit, and attribute-key lengths, plus
`_metric_sample_accumulator: float`. Before configuring a candidate provider,
reject non-finite or out-of-range sample rates with
`Error.ERR_INVALID_PARAMETER`.

Implement convenience methods by constructing `ObservabilityMetric` and
calling `capture_metric`. `capture_metric` must:

1. Validate type, name, value, unit, and merged attribute keys/values.
2. Return `false` without an error when core or metrics are disabled.
3. Apply global attributes first and metric attributes second.
4. Invoke a valid filter and require a boolean result.
5. Apply accumulator sampling only after validation/filtering.
6. Use `_provider.has_method("capture_metric")` for optional capability.
7. Call dynamically and require a boolean result.
8. Set `Error.OK`, `Error.ERR_INVALID_PARAMETER`,
   `Error.ERR_UNAVAILABLE`, or `Error.FAILED` exactly as specified.

Supported attribute values are `bool`, `int`, finite `float`, `String`, and
`StringName`. Reject all other values. Add helpers with single responsibilities:

```foundryscript
func _normalized_metric(metric: ObservabilityMetric) -> ObservabilityMetric?
func _is_valid_metric_name(value: String) -> bool
func _is_valid_metric_unit(value: String) -> bool
func _is_valid_metric_attributes(attributes: Dictionary) -> bool
func _is_valid_metric_attribute_value(value: Variant) -> bool
func _has_whitespace(value: String) -> bool
func _accept_metric_sample() -> bool
func _reset_metric_sampling() -> void
```

Reset sampling after successful configuration and shutdown. Filtered/disabled
metrics must not advance it.

- [ ] **Step 5: Implement built-in providers**

Make memory and null providers use both traits:

```foundryscript
uses ObservabilityProvider, ObservabilityMetricsProvider
```

Add `metric_capture_result: bool = true`, `_metrics`, `capture_metric`,
`metrics`, and `clear_metrics` to the memory provider. Store the normalized
object only when enabled, not shut down, and `metric_capture_result` is true.

Add a `capture_metric` method returning `false` to the null provider.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run:

```sh
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: all core/provider tests pass, FoundryScript lint is clean, and every
source has a tracked valid UID.

- [ ] **Step 7: Commit the core behavior**

```sh
git add addons/FoundryObservability test_project/tests
git commit -m "feat: validate filter and sample custom metrics"
```

### Task 3: FoundryScript Sentry bridge contract

**Files:**

- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/metricless_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/metricless_sentry_bridge.notest.fs.uid`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Write failing Sentry provider tests**

Add a test that configures metrics, captures a normalized gauge, and expects:

```foundryscript
Expect.that(provider.capture_metric(ObservabilityMetric.new(
		p_type = ObservabilityMetricType.GAUGE,
		p_name = "players.active",
		p_value = 7.0,
		p_unit = "player",
		p_attributes = {"region": "iad"},
	))).to_be_true()
Expect.that(bridge.configured_payload["metrics_enabled"]).to_be_true()
Expect.that(bridge.captured_metric_payloads[0]).to_equal({
		"type": ObservabilityMetricType.GAUGE,
		"name": "players.active",
		"value": 7.0,
		"unit": "player",
		"attributes": {"region": "iad"},
	})
```

Add a second test using `MetriclessSentryBridge` that verifies configuration
and ordinary event capture succeed while `capture_metric` returns `false`.

- [ ] **Step 2: Run Sentry FoundryScript tests and verify RED**

Run:

```sh
scripts/test-project
```

Expected: FAIL because the provider and fake bridge lack metric capture.

- [ ] **Step 3: Implement the optional Sentry capability**

Make the provider use both traits. Forward `metrics_enabled` in configuration.
Add:

```foundryscript
func capture_metric(metric: ObservabilityMetric) -> bool:
	if metric == null or not _enabled or _shutdown:
		return false
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureMetric"):
		return false
	var result: Variant = bridge.call("captureMetric", {
			"type": metric.type(),
			"name": metric.name(),
			"value": metric.value(),
			"unit": metric.unit(),
			"attributes": metric.attributes(),
		})
	return result is bool and result
```

Extend `FakeSentryBridge` with a metric payload array and boolean
`captureMetric`. Create `MetriclessSentryBridge` with the ordinary
configure/isAvailable/capture/flush/shutdown surface but no metric method.

- [ ] **Step 4: Verify GREEN and commit**

Run:

```sh
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: all Sentry FoundryScript tests pass.

```sh
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs test_project/tests
git commit -m "feat: forward normalized metrics to Sentry bridges"
```

### Task 4: Sentry Cocoa metric mapping

**Files:**

- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`
- Modify: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Write failing mapper tests**

Add tests for a helper:

```swift
func sentryMetricAttributes(_ values: [String: Any]) -> [String: SentryAttributeValue]
```

Verify string, boolean, integer, `Int64`, float, and double inputs survive;
nested dictionaries are omitted. Add unit tests requiring:

```swift
XCTAssertNil(sentryMetricUnit(for: ""))
XCTAssertEqual(sentryMetricUnit(for: "millisecond"), .millisecond)
XCTAssertEqual(sentryMetricUnit(for: "player"), .generic("player"))
```

- [ ] **Step 2: Run Swift tests and verify RED**

Run:

```sh
task test:sentry-swift
```

Expected: compile failure because the metric mapper helpers do not exist.

- [ ] **Step 3: Implement mapper helpers and bridge routing**

Add `sentryMetricAttributes` and `sentryMetricUnit` to the mapper. In the bridge:

- Add `metricsEnabled`.
- Read `metrics_enabled` during configure.
- Set `options.enableMetrics = metricsEnabled`.
- Add `@Callable func captureMetric(payload: VariantDictionary) -> Bool`.
- Validate availability, enabled state, type `0...2`, non-empty name, and
  supported scalar attributes.
- Route type 0 to `SentrySDK.metrics.count`, type 1 to `.gauge`, and type 2 to
  `.distribution`.
- Return `true` after SDK handoff and `false` for malformed payloads.
- Reset `metricsEnabled` in `closeActiveClient`.

- [ ] **Step 4: Extend Apple contract tests**

Require `captureMetric`, `enableMetrics`, `SentrySDK.metrics.count`,
`SentrySDK.metrics.gauge`, and `SentrySDK.metrics.distribution` in the bridge
source.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```sh
task test:sentry-swift
scripts/test-sentry-ios-build-contract
```

Expected: Swift tests and Apple source/build contracts pass.

```sh
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry scripts/test-sentry-ios-build-contract
git commit -m "feat: map custom metrics to Sentry Cocoa"
```

### Task 5: Sentry Android metric mapping

**Files:**

- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryMetricMapper.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryMetricMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`
- Modify: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Write failing mapper tests**

Require `SentryMetricMapper.payload(Dictionary)` to return a parsed value with
type, name, double value, unit, and `SentryMetricsParameters`. Test all three
types, scalar attributes, and rejection of null payloads, unknown types, empty
names, non-numeric values, and unsupported attributes.

- [ ] **Step 2: Run Android unit tests and verify RED**

Run:

```sh
(
	cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
	./gradlew testDebugUnitTest
)
```

Expected: compile failure because `SentryMetricMapper` does not exist.

- [ ] **Step 3: Implement mapper and bridge**

`SentryMetricMapper` must parse without accessing global SDK state and create:

```java
SentryMetricsParameters.create(SentryAttributes.fromMap(attributes))
```

The bridge must:

- Add `metricsEnabled`.
- Configure `options.getMetrics().setEnabled(metricsEnabled)`.
- Add `@UsedByFoundry public boolean captureMetric(Dictionary payload)`.
- Parse with the mapper and call `Sentry.metrics().count`, `.gauge`, or
  `.distribution`.
- Return false for unavailable, disabled, malformed, or runtime-failing calls.
- Reset metrics state in `closeActiveClient`.

Add bridge tests for disabled metrics, all three enabled routes, and malformed
payload rejection.

- [ ] **Step 4: Extend Android contract tests**

Require `captureMetric`, `getMetrics().setEnabled`, `Sentry.metrics()`,
`SentryMetricsParameters`, and all three metric method names.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```sh
(
	cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
	./gradlew test lintRelease
)
scripts/test-sentry-android-build-contract
```

Expected: Android tests, lint, and source contracts pass.

```sh
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry scripts/test-sentry-android-build-contract
git commit -m "feat: map custom metrics to Sentry Android"
```

### Task 6: Documentation and package contracts

**Files:**

- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `CHANGELOG.md`
- Modify: `scripts/test-foundry-script`
- Modify: `scripts/test-package`

- [ ] **Step 1: Add failing documentation/package assertions**

Require the core package to contain:

```text
addons/FoundryObservability/ObservabilityMetric.fs
addons/FoundryObservability/ObservabilityMetric.fs.uid
addons/FoundryObservability/ObservabilityMetricType.fs
addons/FoundryObservability/ObservabilityMetricType.fs.uid
addons/FoundryObservability/ObservabilityMetricsProvider.fs
addons/FoundryObservability/ObservabilityMetricsProvider.fs.uid
```

Require README/API references to `capture_counter`,
`capture_gauge`, and `capture_distribution`.

- [ ] **Step 2: Run package contracts and verify RED**

Run:

```sh
scripts/test-package
scripts/test-foundry-script
```

Expected: documentation assertion failure until the docs are updated.

- [ ] **Step 3: Update public documentation**

Document:

- All public metric types and methods.
- Validation rules and supported scalar attributes.
- Global/per-metric precedence.
- `metrics_enabled`, `metric_sample_rate`, and `metric_filter`.
- Deterministic sampling order.
- Optional provider capability and return/error semantics.
- Native SDK batching ownership and existing flush behavior.

Add a quick-start example:

```foundryscript
FoundryObservability.capture_counter("match.started")
FoundryObservability.capture_gauge("players.active", 7.0, "player")
FoundryObservability.capture_distribution(
		"match.duration",
		125.5,
		"millisecond",
		{"region": "iad"},
)
```

- [ ] **Step 4: Verify GREEN and commit**

Run:

```sh
scripts/test-package
scripts/test-foundry-script
```

Expected: package and documentation contracts pass.

```sh
git add README.md docs/API.md CHANGELOG.md scripts/test-foundry-script scripts/test-package
git commit -m "docs: document first-class custom metrics"
```

### Task 7: Full verification and review

**Files:**

- Review all changes relative to `main`.

- [ ] **Step 1: Run formatting and diff checks**

```sh
git diff --check main...HEAD
prek run --all-files
```

Expected: no whitespace errors and all hooks pass.

- [ ] **Step 2: Run focused platform verification**

```sh
scripts/test-project
task test:sentry-swift
(
	cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
	./gradlew test lintRelease
)
scripts/test-sentry-ios-build-contract
scripts/test-sentry-android-build-contract
scripts/test-package
scripts/test-foundry-uids
```

Expected: all focused tests and contracts pass.

- [ ] **Step 3: Run the complete validation gate**

```sh
task test
```

Expected: exit 0, all FoundryScript and Swift tests pass, and every repository
contract succeeds. Source-only missing-native-artifact diagnostics may appear
but must not produce test failures.

- [ ] **Step 4: Review requirements against issue #14**

Confirm each requirement has direct evidence:

- Counters, gauges, and distributions.
- Names, numeric values, units, and scalar attributes.
- Global/per-metric precedence.
- Validation and unsupported-value safety.
- Enable/disable, filter, and deterministic sampling.
- Provider-owned batching plus flush.
- Optional unsupported capability.
- Native Apple/Android mapping.
- Documentation and deterministic tests.

- [ ] **Step 5: Inspect the final diff**

```sh
git status --short --branch
git log --oneline main..HEAD
git diff --stat main...HEAD
git diff main...HEAD
```

Expected: only issue #14 implementation, tests, contracts, spec, plan, and
documentation are present.
