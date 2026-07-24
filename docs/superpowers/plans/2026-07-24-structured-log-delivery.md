# Structured Log Delivery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add provider-neutral structured log capture with FoundryLib forwarding and native Apple/Android Sentry log delivery.

**Architecture:** Keep `ObservabilityEvent` as the provider-neutral envelope and add a dedicated `capture_log` service method. The core applies log enablement, severity filtering, and an optional deterministic per-second rate limit; Sentry routes log events to native structured-log APIs while preserving existing event capture for messages and exceptions.

**Tech Stack:** FoundryScript, Foundry testlib, FoundryLib logging, Swift 6/XCTest, Sentry Cocoa 9.23.0, Java 17, Sentry Android 8.50.1, Gradle, Bash, Task.

---

## File map

- Modify `addons/FoundryObservability/ObservabilityConfig.fs` with log controls and defensive values.
- Modify `addons/FoundryObservability/FoundryObservabilityApi.fs` and `FoundryObservability.fs` with `capture_log` and filtering/rate limiting.
- Modify `addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs` to use the first-class log method.
- Modify `test_project/tests/observability-core.test.fs` and `observability-foundrylib.test.fs` for core and adapter behavior.
- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs` and `test_project/tests/observability-sentry.test.fs` for bridge routing.
- Modify `test_project/tests/support/fake_sentry_bridge.notest.fs` and add `event_only_sentry_bridge.notest.fs` for deterministic bridge seams.
- Modify Apple `FoundryObservabilitySentry.swift` and `SentryEventMapper.swift`; extend `SentryEventMapperTests.swift`.
- Modify Android `SentryObservabilityBridge.java` and add structured-log mapping helpers/tests.
- Modify `scripts/test-sentry-ios-build-contract` and `scripts/test-sentry-android-build-contract` for native API contracts.
- Modify `README.md`, `docs/API.md`, and `CHANGELOG.md` for the public logging contract and platform notes.

### Task 1: Add the provider-neutral configuration and log API

**Files:**

- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write the failing core tests**

Add these tests after the existing memory-provider capture test:

```foundryscript
func test_structured_logs_are_enabled_by_default_and_preserve_shape() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {"build": 42},
		))).to_equal(Error.OK)
	Expect.that(service.capture_log(
			"player {id} missed",
			ObservabilityLevel.WARN,
			&"combat",
			1234,
			{"id": 7},
		)).to_equal("memory:1")

	var event: ObservabilityEvent = provider.events()[0]
	Expect.that(event.kind()).to_equal(&"log")
	Expect.that(event.level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(event.message()).to_equal("player {id} missed")
	Expect.that(event.source()).to_equal(&"combat")
	Expect.that(event.timestamp_msec()).to_equal(1234)
	Expect.that(event.attributes()).to_equal({"id": 7})
	service.shutdown()


func test_structured_logs_honor_disabled_and_minimum_level_configuration() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_logs_enabled = false,
			p_log_minimum_level = ObservabilityLevel.ERROR,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_log("disabled")).to_equal("")
	Expect.that(provider.events()).to_have_size(0)

	config.logs_enabled = true
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_log("filtered", ObservabilityLevel.WARN)).to_equal("")
	Expect.that(service.capture_log("kept", ObservabilityLevel.ERROR)).to_equal("memory:1")
	Expect.that(provider.events()).to_have_size(1)
	service.shutdown()


func test_structured_logs_apply_deterministic_per_second_rate_limit() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_log_rate_limit_per_second = 1,
		))).to_equal(Error.OK)
	Expect.that(service.capture_log("first", ObservabilityLevel.INFO, &"game", 1000)).to_equal("memory:1")
	Expect.that(service.capture_log("dropped", ObservabilityLevel.INFO, &"game", 1500)).to_equal("")
	Expect.that(service.capture_log("next window", ObservabilityLevel.INFO, &"game", 2000)).to_equal("memory:2")
	Expect.that(provider.events()).to_have_size(2)
	service.shutdown()
```

- [ ] **Step 2: Run the focused tests and verify the intended failure**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: the test project loads, then reports missing `capture_log` and
unknown `p_logs_enabled`, `p_log_minimum_level`, and
`p_log_rate_limit_per_second` members.

- [ ] **Step 3: Add configuration fields and accessors**

Append these constructor parameters after the existing provider options so
existing positional callers remain source-compatible:

```foundryscript
## Enables structured log capture independently from messages and exceptions.
var logs_enabled: bool = true
## Filters structured logs below this normalized severity.
var log_minimum_level: int = ObservabilityLevel.TRACE
## Limits accepted logs per one-second timestamp window; zero delegates to the provider.
var log_rate_limit_per_second: int = 0

func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0
) -> void:
	enabled = p_enabled
	environment = p_environment
	release = p_release
	dist = p_dist
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)
	logs_enabled = p_logs_enabled
	log_minimum_level = p_log_minimum_level
	log_rate_limit_per_second = maxi(0, p_log_rate_limit_per_second)
```

- [ ] **Step 4: Add the public API declaration**

Append this abstract method to `FoundryObservabilityApi.fs`:

```foundryscript
## Creates and captures a structured log record.
abstract func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
) -> String
```

- [ ] **Step 5: Implement filtering and rate limiting**

Add service state:

```foundryscript
var _log_window_second: int = -1
var _log_window_count: int = 0
```

Reset both values in `_init`, successful `configure`, and `shutdown`. Add the
public method and private helpers:

```foundryscript
func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
) -> String:
	if not _config.logs_enabled or level < _config.log_minimum_level:
		return ""
	var event_timestamp: int = timestamp_msec
	if event_timestamp < 0:
		event_timestamp = Time.get_ticks_msec()
	if not _accept_log(event_timestamp):
		return ""
	return _capture_event(ObservabilityEvent.new(
			p_kind = &"log",
			p_level = level,
			p_message = message,
			p_source = source,
			p_timestamp_msec = event_timestamp,
			p_attributes = attributes,
		))


func _capture_event(event: ObservabilityEvent) -> String:
	if event == null or not is_enabled() or _provider == null:
		return ""
	var event_id: String = _provider.capture(event)
	if event_id.is_empty():
		_last_error = Error.FAILED
	return event_id


func _accept_log(timestamp_msec: int) -> bool:
	var window_second: int = floori(float(timestamp_msec) / 1000.0)
	if window_second != _log_window_second:
		_log_window_second = window_second
		_log_window_count = 0
	if _config.log_rate_limit_per_second > 0:
		if _log_window_count >= _config.log_rate_limit_per_second:
			return false
		_log_window_count += 1
	return true
```

Change existing `capture_event` to call `_capture_event` for non-log events.
For events passed directly with `kind == &"log"`, apply the same
`logs_enabled`, minimum-level, and rate-limit checks before `_capture_event` so
custom callers cannot bypass log policy.

- [ ] **Step 6: Run the focused tests and verify they pass**

Run `FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project` again.
Expected: the new core tests and all existing core tests pass.

- [ ] **Step 7: Commit the core API**

```sh
git add addons/FoundryObservability/ObservabilityConfig.fs \
  addons/FoundryObservability/FoundryObservabilityApi.fs \
  addons/FoundryObservability/FoundryObservability.fs \
  test_project/tests/observability-core.test.fs
git commit -m "feat: add provider-neutral structured log API"
```

### Task 2: Route FoundryLib records through `capture_log`

**Files:**

- Modify: `addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs`
- Test: `test_project/tests/observability-foundrylib.test.fs`

- [ ] **Step 1: Strengthen the adapter test before implementation**

In `test_maps_structured_logs_to_observability_events`, add these assertions
after the existing attribute assertions:

```foundryscript
Expect.that(event.source()).to_equal(&"foundry.logging")
Expect.that(event.attributes()["logger_name"]).to_equal("combat")
Expect.that(event.attributes()["id"]).to_equal(7)
```

Add a test that service log filtering applies to the sink:

```foundryscript
func test_sink_uses_service_log_filtering() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_log_minimum_level = ObservabilityLevel.ERROR,
		))).to_equal(Error.OK)
	var sink := FoundryLibObservabilitySink.new(
			p_service = service,
			p_minimum_level = ObservabilityLevel.TRACE,
		)
	sink.emit(LogRecord.new(LogLevel.WARN, "combat", "filtered", {}, 10))
	sink.emit(LogRecord.new(LogLevel.ERROR, "combat", "kept", {}, 20))

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("kept")
	service.shutdown()
```

- [ ] **Step 2: Run the FoundryLib tests and verify the new test fails**

Run `FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project`.
Expected: the new filtering test fails because the current sink creates a
generic event and does not use the service log policy.

- [ ] **Step 3: Forward through the first-class method**

Replace the event construction and `_service.capture_event(event)` call in
`emit` with:

```foundryscript
var attributes: Dictionary = record.fields.duplicate(true)
attributes["logger_name"] = record.logger_name
_service.capture_log(
		LogFormatter.render_message(record),
		_map_level(record.level),
		&"foundry.logging",
		record.timestamp_msec,
		attributes,
)
```

Keep the null-service and local minimum-level guards unchanged. The sink’s
default local threshold remains `ObservabilityLevel.ERROR` for backward
compatibility; callers can lower it when the service configuration permits
more verbose logs.

- [ ] **Step 4: Run all core and FoundryLib tests**

Run `FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project`.
Expected: all core and FoundryLib tests pass.

- [ ] **Step 5: Commit the FoundryLib integration**

```sh
git add addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs \
  test_project/tests/observability-foundrylib.test.fs
git commit -m "feat: route FoundryLib records through structured logs"
```

### Task 3: Route log events through the Sentry FoundryScript provider

**Files:**

- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/event_only_sentry_bridge.notest.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing provider tests**

Extend the fake bridge with `captured_log_payloads`, `next_log_id`, and:

```foundryscript
func captureLog(payload: Dictionary) -> String:
	captured_log_payloads.append(payload.duplicate(true))
	var event_id := "sentry-log:%s" % next_log_id
	next_log_id += 1
	return event_id
```

Add this test:

```foundryscript
func test_routes_log_events_to_native_structured_log_method() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_logs_enabled = true,
			p_log_minimum_level = ObservabilityLevel.TRACE,
			p_global_attributes = {"build": 42},
			p_provider_options = {"dsn": "https://public@example/1"},
		)
	var event := ObservabilityEvent.new(
			p_kind = &"log",
			p_level = ObservabilityLevel.WARN,
			p_message = "missed",
			p_source = &"foundry.logging",
			p_timestamp_msec = 1234,
			p_attributes = {"logger_name": "combat", "id": 7},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture(event)).to_equal("sentry-log:1")
	Expect.that(bridge.captured_log_payloads[0]["kind"]).to_equal("log")
	Expect.that(bridge.captured_log_payloads[0]["timestamp_msec"]).to_equal(1234)
	Expect.that(bridge.configured_payload["logs_enabled"]).to_be_true()
	provider.shutdown()
```

Add `EventOnlySentryBridge` with the existing configure/isAvailable/capture/
flush/shutdown methods but no `captureLog`, then test that an enabled log
returns an empty ID without throwing:

```foundryscript
func test_structured_log_is_safe_when_bridge_does_not_support_it() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = EventOnlySentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"log",
			p_message = "unsupported",
		))).to_equal("")
	provider.shutdown()
```

- [ ] **Step 2: Run Sentry FoundryScript tests and verify they fail**

Run `FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project`.
Expected: the first test returns the ordinary event path or an empty ID, and
the configuration payload lacks `logs_enabled`.

- [ ] **Step 3: Add the log configuration payload and bridge dispatch**

Add these fields to the provider configuration payload:

```foundryscript
"logs_enabled": config.logs_enabled,
"log_minimum_level": config.log_minimum_level,
"log_rate_limit_per_second": config.log_rate_limit_per_second,
```

In `capture`, build the existing payload once, then dispatch log events only
when the bridge advertises `captureLog`:

```foundryscript
var method: String = "capture"
if event.kind() == &"log":
	method = "captureLog"
	if not bridge.has_method(method):
		return ""
return str(bridge.call(method, payload))
```

Keep the existing `capture` method for messages and exceptions. Reset log
configuration through the normal provider `configure` lifecycle.

- [ ] **Step 4: Run the provider tests green**

Run `FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project`.
Expected: all core, FoundryLib, and Sentry FoundryScript tests pass.

- [ ] **Step 5: Commit the provider routing**

```sh
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests/support/fake_sentry_bridge.notest.fs \
  test_project/tests/support/event_only_sentry_bridge.notest.fs \
  test_project/tests/observability-sentry.test.fs
git commit -m "feat: route Sentry structured logs through native bridge"
```

### Task 4: Add Apple native structured-log delivery

**Files:**

- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`
- Modify: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Write failing Swift mapper tests**

Add tests for all six level mappings and reserved attribute precedence:

```swift
func testMapsStructuredLogLevels() {
    XCTAssertEqual(sentryLogLevel(for: 10), .trace)
    XCTAssertEqual(sentryLogLevel(for: 20), .debug)
    XCTAssertEqual(sentryLogLevel(for: 30), .info)
    XCTAssertEqual(sentryLogLevel(for: 40), .warn)
    XCTAssertEqual(sentryLogLevel(for: 50), .error)
    XCTAssertEqual(sentryLogLevel(for: 60), .fatal)
    XCTAssertEqual(sentryLogLevel(for: 999), .error)
}

func testStructuredLogAttributesPreserveFieldsAndReservedMetadata() {
    let attributes = mergedLogAttributes(
        global: ["shared": "global", "build": 42],
        event: ["shared": "event", "foundry.kind": "caller"],
        kind: "log",
        source: "foundry.logging",
        timestampMsec: 1234
    )

    XCTAssertEqual(attributes["shared"] as? String, "event")
    XCTAssertEqual(attributes["build"] as? Int, 42)
    XCTAssertEqual(attributes["foundry.kind"] as? String, "log")
    XCTAssertEqual(attributes["foundry.source"] as? String, "foundry.logging")
    XCTAssertEqual(attributes["foundry.timestamp_msec"] as? Int64, 1234)
}
```

- [ ] **Step 2: Run Swift mapper tests and verify failure**

Run `task test:sentry-swift`.
Expected: compilation fails because `sentryLogLevel` and
`mergedLogAttributes` do not yet exist.

- [ ] **Step 3: Implement Apple mapping helpers**

Add to `SentryEventMapper.swift`:

```swift
func sentryLogLevel(for level: Int) -> SentryLog.Level {
    switch level {
    case 10: return .trace
    case 20: return .debug
    case 30: return .info
    case 40: return .warn
    case 50: return .error
    case 60: return .fatal
    default: return .error
    }
}

func mergedLogAttributes(
    global: [String: Any],
    event: [String: Any],
    kind: String,
    source: String,
    timestampMsec: Int64
) -> [String: Any] {
    var attributes = global
    for (key, value) in event {
        attributes[key] = value
    }
    attributes["foundry.kind"] = kind
    attributes["foundry.source"] = source
    attributes["foundry.timestamp_msec"] = timestampMsec
    return attributes
}
```

- [ ] **Step 4: Add the Apple bridge method and log enablement**

Track `logsEnabled` alongside `configured`. Set `options.enableLogs` from the
`logs_enabled` configuration payload and reset it when closing the client.
Add:

```swift
@Callable
func captureLog(payload: VariantDictionary) -> String {
    guard isAvailable(), logsEnabled else { return "" }
    let values = foundationDictionary(from: payload)
    let attributes = mergedLogAttributes(
        global: globalAttributes,
        event: dictionaryValue(values["attributes"]),
        kind: stringValue(values["kind"]),
        source: stringValue(values["source"]),
        timestampMsec: Int64(intValue(values["timestamp_msec"]))
    )
    let message = stringValue(values["message"])
    switch sentryLogLevel(for: intValue(values["level"])) {
    case .trace: SentrySDK.logger.trace(message, attributes: attributes)
    case .debug: SentrySDK.logger.debug(message, attributes: attributes)
    case .info: SentrySDK.logger.info(message, attributes: attributes)
    case .warn: SentrySDK.logger.warn(message, attributes: attributes)
    case .error: SentrySDK.logger.error(message, attributes: attributes)
    case .fatal: SentrySDK.logger.fatal(message, attributes: attributes)
    @unknown default: SentrySDK.logger.error(message, attributes: attributes)
    }
    return "sentry-log:\(UUID().uuidString)"
}
```

Use the existing scalar conversion path before passing values to the Sentry
logger so unsupported values are omitted rather than flattened into the
message. Keep ordinary `capture(payload:)` unchanged.

- [ ] **Step 5: Extend the Apple contract test**

Require `captureLog`, `enableLogs`, `SentrySDK.logger`, and all six logger
methods in `scripts/test-sentry-ios-build-contract`. Run:

```sh
scripts/test-sentry-ios-build-contract
task test:sentry-swift
```

Expected: both commands pass.

- [ ] **Step 6: Commit the Apple implementation**

```sh
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift \
  scripts/test-sentry-ios-build-contract
git commit -m "feat: deliver structured logs through Sentry Cocoa"
```

### Task 5: Add Android native structured-log delivery

**Files:**

- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLogMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLogMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`
- Modify: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Write failing Android mapper tests**

Create `SentryLogMapperTest.java` with:

```java
@Test
public void mapsAllStructuredLogLevels() {
  assertEquals(SentryLogLevel.TRACE, SentryLogMapper.sentryLevel(10));
  assertEquals(SentryLogLevel.DEBUG, SentryLogMapper.sentryLevel(20));
  assertEquals(SentryLogLevel.INFO, SentryLogMapper.sentryLevel(30));
  assertEquals(SentryLogLevel.WARN, SentryLogMapper.sentryLevel(40));
  assertEquals(SentryLogLevel.ERROR, SentryLogMapper.sentryLevel(50));
  assertEquals(SentryLogLevel.FATAL, SentryLogMapper.sentryLevel(60));
  assertEquals(SentryLogLevel.ERROR, SentryLogMapper.sentryLevel(999));
}

@Test
public void mergesScalarAttributesAndReservedMetadataLast() {
  Map<String, Object> global = Map.of("shared", "global", "build", 42L);
  Map<String, Object> event = Map.of("shared", "event", "foundry.kind", "caller");
  Map<String, Object> result = SentryLogMapper.mergedAttributes(
      global, event, "log", "foundry.logging", 1234L);

  assertEquals("event", result.get("shared"));
  assertEquals(42L, result.get("build"));
  assertEquals("log", result.get("foundry.kind"));
  assertEquals("foundry.logging", result.get("foundry.source"));
  assertEquals(1234L, result.get("foundry.timestamp_msec"));
}
```

- [ ] **Step 2: Run Android tests and verify failure**

Run `cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry && ./gradlew test`.
Expected: test compilation fails because `SentryLogMapper` does not exist.

- [ ] **Step 3: Implement the Android mapper**

Implement package-private methods:

```java
static SentryLogLevel sentryLevel(int level)
static Map<String, Object> mergedAttributes(
    Map<String, Object> global,
    Map<String, Object> event,
    String kind,
    String source,
    long timestampMsec)
static Map<String, Object> scalarAttributes(Map<?, ?> source)
```

Copy booleans, strings, integral numbers as `Long`, and floating numbers as
`Double`; omit null, nested-map, and unsupported values. Copy global values,
overlay event values, and write reserved metadata last.

- [ ] **Step 4: Add Android bridge log configuration and capture**

Add `logsEnabled` to the bridge state. In the `SentryAndroid.init` callback,
set `options.getLogs().setEnabled(booleanValue(payload.get("logs_enabled")))`.
Reset it in `closeActiveClient`. Add:

```java
@UsedByFoundry
public String captureLog(Dictionary payload) {
  if (!isAvailable() || !logsEnabled || payload == null) {
    return "";
  }
  Map<String, Object> attributes = SentryLogMapper.mergedAttributes(
      globalAttributes,
      payload.get("attributes") instanceof Map
          ? (Map<?, ?>) payload.get("attributes")
          : null,
      stringValue(payload.get("kind")),
      stringValue(payload.get("source")),
      longValue(payload.get("timestamp_msec"), 0L));
  SentryLogParameters parameters = SentryLogParameters.create(
      SentryAttributes.fromMap(attributes));
  Sentry.logger().log(
      SentryLogMapper.sentryLevel(intValue(payload.get("level"), 50)),
      parameters,
      stringValue(payload.get("message")));
  return "sentry-log:" + UUID.randomUUID();
}
```

Import `SentryAttributes`, `SentryLogLevel` support classes,
`SentryLogParameters`, and `UUID`. Keep `capture(Dictionary)` on the ordinary
event mapper path.

- [ ] **Step 5: Add a bridge integration test and contract assertions**

Configure the Robolectric bridge with `logs_enabled = true`, call
`captureLog`, and assert the returned ID is non-empty. Extend the Android
contract script to require `captureLog`, `Sentry.logger`,
`SentryLogParameters`, `SentryAttributes.fromMap`, and `getLogs().setEnabled`.

Run:

```sh
scripts/test-sentry-android-build-contract
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew test lintRelease assembleDebug assembleRelease
```

Expected: contract checks pass, all Java tests pass, lint exits zero, and both
AAR variants assemble.

- [ ] **Step 6: Commit the Android implementation**

```sh
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry \
  scripts/test-sentry-android-build-contract
git commit -m "feat: deliver structured logs through Sentry Android"
```

### Task 6: Update public documentation and changelog

**Files:**

- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `CHANGELOG.md`
- Modify: `scripts/test-package`

- [ ] **Step 1: Add documentation assertions**

Before editing docs, add these checks to `scripts/test-package` or a focused
shell contract so the public docs contain the API names and platform notes:

```sh
rg -q 'capture_log' "$repo_root/docs/API.md" || fail "API docs omit capture_log"
rg -q 'logs_enabled' "$repo_root/docs/API.md" || fail "API docs omit logs_enabled"
rg -q 'Apple.*Android|Android.*Apple' "$repo_root/docs/API.md" \
  || fail "API docs omit platform behavior"
```

- [ ] **Step 2: Document the API**

Update the API index, `ObservabilityConfig` constructor and field table,
`FoundryObservabilityApi`, and the FoundryLib section. Document that logs are
enabled by default when the provider is enabled, filtered by minimum level,
optionally rate-limited per second, and delivered through native Sentry log
records on Apple and Android. State that `foundry.timestamp_msec` is retained
as an attribute because it is an engine timestamp.

Remove the stale statement that Sentry is not part of the release; describe it
as the optional provider addon while keeping the core boundary provider-neutral.

- [ ] **Step 3: Update README and changelog**

Add structured log delivery to the README status list and quick-start example.
Add an Unreleased changelog entry naming the provider-neutral API, FoundryLib
forwarding, and Apple/Android native delivery.

- [ ] **Step 4: Run documentation contracts and package checks**

Run:

```sh
scripts/test-package
git diff --check
```

Expected: package contracts pass and the diff has no whitespace errors.

- [ ] **Step 5: Commit documentation**

```sh
git add README.md docs/API.md CHANGELOG.md scripts/test-package
git commit -m "docs: document structured log delivery"
```

### Task 7: Run the complete verification gate

**Files:** None; verification only.

- [ ] **Step 1: Run focused tests fresh**

```sh
task test:project
task test:sentry-swift
scripts/test-sentry-ios-build-contract
scripts/test-sentry-android-build-contract
```

Expected: every command exits zero.

- [ ] **Step 2: Run the complete repository gate**

```sh
task test
```

Expected: lint, CI workflow contracts, package checks, Swift tests, Apple and
Android build contracts, FoundryScript tests, and project tests all pass.

- [ ] **Step 3: Verify the branch diff and working tree**

```sh
git diff main...HEAD --stat
git status --short --branch
```

Expected: the diff contains only structured-log implementation, tests,
documentation, and the committed design/plan; the working tree is clean and
the current branch is `feature/structured-log-delivery`.
