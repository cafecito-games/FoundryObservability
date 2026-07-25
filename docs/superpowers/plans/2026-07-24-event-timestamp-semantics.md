# Event Timestamp Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct event timestamps to use Unix epoch milliseconds across the provider-neutral API and native Sentry mappings while preserving monotonic engine ticks separately.

**Architecture:** Normalize missing or tick-only timestamps once in `FoundryObservability`, using a pure integer clock-conversion helper. Carry corrected wall time and optional engine ticks through the existing event and provider payloads, then explicitly set native Sentry event timestamps on Apple and Android. Keep structured-log rate limiting monotonic and retain both clocks as reserved metadata where native log APIs cannot accept an occurrence timestamp.

**Tech Stack:** FoundryScript, Foundry testlib, Swift/Foundation/Sentry Cocoa, Java/JUnit/Sentry Android, Task, Gradle

---

### Task 1: Separate wall-clock and monotonic values in `ObservabilityEvent`

**Files:**
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `addons/FoundryObservability/ObservabilityEvent.fs`

- [ ] **Step 1: Write the failing event value test**

Add this test after `test_exception_and_event_copy_attributes`:

```foundryscript
func test_event_separates_wall_clock_timestamp_and_engine_ticks() -> void:
	var event := ObservabilityEvent.new(
			p_timestamp_msec = 1721865600123,
			p_engine_ticks_msec = 4567,
		)
	var epoch := ObservabilityEvent.new(p_timestamp_msec = 0)
	var missing := ObservabilityEvent.new()

	Expect.that(event.timestamp_msec()).to_equal(1721865600123)
	Expect.that(event.engine_ticks_msec()).to_equal(4567)
	Expect.that(epoch.timestamp_msec()).to_equal(0)
	Expect.that(missing.timestamp_msec()).to_equal(-1)
	Expect.that(missing.engine_ticks_msec()).to_equal(-1)
```

- [ ] **Step 2: Run the focused FoundryScript suite and verify RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because `p_engine_ticks_msec` and `engine_ticks_msec()` do not exist and the current default timestamp is `0`.

- [ ] **Step 3: Implement the separate event fields**

Change `ObservabilityEvent.fs` to:

```foundryscript
var _timestamp_msec: int = -1
var _engine_ticks_msec: int = -1
```

Keep the existing argument order, change the timestamp default, and append the new argument:

```foundryscript
func _init(
		p_kind: StringName = &"message",
		p_level: int = ObservabilityLevel.INFO,
		p_message: String = "",
		p_source: StringName = &"",
		p_timestamp_msec: int = -1,
		p_attributes: Dictionary = {},
		p_exception: ObservabilityException? = null,
		p_engine_ticks_msec: int = -1,
) -> void:
	_kind = p_kind
	_level = p_level
	_message = p_message
	_source = p_source
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)
	_exception = p_exception
	_engine_ticks_msec = p_engine_ticks_msec
```

Update the timestamp comment and add:

```foundryscript
## Returns the wall-clock event occurrence time in Unix epoch milliseconds, or -1 when unspecified.
func timestamp_msec() -> int:
	return _timestamp_msec


## Returns the original monotonic engine tick in milliseconds, or -1 when unavailable.
func engine_ticks_msec() -> int:
	return _engine_ticks_msec
```

- [ ] **Step 4: Run the focused suite and verify GREEN**

Run `scripts/test-project`.

Expected: all FoundryScript tests pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservability/ObservabilityEvent.fs test_project/tests/observability-core.test.fs
git commit -m "feat: separate event wall time and engine ticks"
```

### Task 2: Normalize timestamps once and rate-limit with monotonic time

**Files:**
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`

- [ ] **Step 1: Write failing conversion, fallback, and rate-limit tests**

Add:

```foundryscript
func test_converts_engine_ticks_to_unix_epoch_milliseconds() -> void:
	Expect.that(FoundryObservability._unix_msec_from_engine_ticks(
			4000, 5000, 1721865600000,
		)).to_equal(1721865599000)
	Expect.that(FoundryObservability._unix_msec_from_engine_ticks(
			6000, 5000, 1721865600000,
		)).to_equal(1721865601000)


func test_capture_preserves_custom_wall_time_and_resolves_missing_time() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)

	var explicit := ObservabilityEvent.new(p_message = "explicit", p_timestamp_msec = 0)
	Expect.that(service.capture_event(explicit)).to_equal("memory:1")
	var before_unix_msec: int = floori(Time.get_unix_time_from_system() * 1000.0)
	Expect.that(service.capture_event(ObservabilityEvent.new(p_message = "fallback"))).to_equal("memory:2")
	var after_unix_msec: int = floori(Time.get_unix_time_from_system() * 1000.0)

	Expect.that(provider.events()[0].timestamp_msec()).to_equal(0)
	var fallback: ObservabilityEvent = provider.events()[1]
	Expect.that(fallback.timestamp_msec()).not_().to_be_less_than(before_unix_msec)
	Expect.that(fallback.timestamp_msec()).not_().to_be_greater_than(after_unix_msec)
	Expect.that(fallback.engine_ticks_msec()).not_().to_be_less_than(0)
	service.shutdown()
```

Update `test_structured_logs_apply_deterministic_per_second_rate_limit` so its
deterministic window values are passed as `engine_ticks_msec`:

```foundryscript
service.capture_log("first", ObservabilityLevel.INFO, &"game", -1, {}, 1000)
service.capture_log("dropped", ObservabilityLevel.INFO, &"game", -1, {}, 1500)
service.capture_log("next window", ObservabilityLevel.INFO, &"game", -1, {}, 2000)
```

Change both calls in `test_disabled_structured_logs_do_not_consume_rate_limit`
to:

```foundryscript
service.capture_log("suppressed", ObservabilityLevel.INFO, &"game", -1, {}, 1000)
service.capture_log("accepted", ObservabilityLevel.INFO, &"game", -1, {}, 1000)
```

In `test_direct_structured_log_events_apply_enabled_gate_before_rate_limit`, use:

```foundryscript
p_timestamp_msec = 1721865600123,
p_engine_ticks_msec = 1000,
```

Update `test_memory_provider_captures_messages_and_exceptions` to assert each
convenience-created event has both clocks:

```foundryscript
Expect.that(provider.events()[0].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
Expect.that(provider.events()[0].engine_ticks_msec()).not_().to_be_less_than(0)
Expect.that(provider.events()[1].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
Expect.that(provider.events()[1].engine_ticks_msec()).not_().to_be_less_than(0)
```

- [ ] **Step 2: Run the suite and verify RED**

Run `scripts/test-project`.

Expected: FAIL because the conversion helper, fallback normalization, and new `capture_log` argument are absent.

- [ ] **Step 3: Implement centralized normalization**

In `FoundryObservability.fs`, add:

```foundryscript
static func _unix_msec_from_engine_ticks(
		event_engine_ticks_msec: int,
		capture_engine_ticks_msec: int,
		capture_unix_msec: int,
) -> int:
	return capture_unix_msec + event_engine_ticks_msec - capture_engine_ticks_msec


func _resolved_event_timestamp(
		event: ObservabilityEvent,
		capture_unix_msec: int,
		capture_engine_ticks_msec: int,
) -> ObservabilityEvent:
	if event.timestamp_msec() >= 0:
		return event
	var resolved_unix_msec: int = capture_unix_msec
	var resolved_engine_ticks_msec: int = event.engine_ticks_msec()
	if resolved_engine_ticks_msec >= 0:
		resolved_unix_msec = _unix_msec_from_engine_ticks(
				resolved_engine_ticks_msec,
				capture_engine_ticks_msec,
				capture_unix_msec,
			)
	else:
		resolved_engine_ticks_msec = capture_engine_ticks_msec
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = resolved_unix_msec,
			p_attributes = event.attributes(),
			p_exception = event.exception(),
			p_engine_ticks_msec = resolved_engine_ticks_msec,
		)
```

Refactor `capture_event` to read the pair once after the enabled gate:

```foundryscript
	var capture_engine_ticks_msec: int = Time.get_ticks_msec()
	var capture_unix_msec: int = floori(Time.get_unix_time_from_system() * 1000.0)
	var normalized: ObservabilityEvent = _resolved_event_timestamp(
			event, capture_unix_msec, capture_engine_ticks_msec,
		)
	if normalized.kind() == &"log":
		if not _config.logs_enabled or normalized.level() < _config.log_minimum_level:
			return ""
		var rate_limit_ticks_msec: int = normalized.engine_ticks_msec()
		if rate_limit_ticks_msec < 0:
			rate_limit_ticks_msec = capture_engine_ticks_msec
		if not _accept_log(rate_limit_ticks_msec):
			return ""
	return _capture_event(normalized)
```

Remove `Time.get_ticks_msec()` from message and exception constructors so the
central fallback supplies both values. Refactor `capture_log` to construct an
event and delegate to `capture_event`:

```foundryscript
func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
		engine_ticks_msec: int = -1,
) -> String:
	return capture_event(ObservabilityEvent.new(
			p_kind = &"log",
			p_level = level,
			p_message = message,
			p_source = source,
			p_timestamp_msec = timestamp_msec,
			p_attributes = attributes,
			p_engine_ticks_msec = engine_ticks_msec,
		))
```

Rename `_accept_log`'s parameter to `engine_ticks_msec` and retain its integer
one-second window calculation.

- [ ] **Step 4: Run tests and verify GREEN**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all tests and strict lint checks pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservability/FoundryObservability.fs test_project/tests/observability-core.test.fs
git commit -m "fix: normalize event occurrence timestamps"
```

### Task 3: Preserve FoundryLib engine ticks through the public log contract

**Files:**
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs`
- Modify: `test_project/tests/support/recording_observability_api.notest.fs`
- Modify: `test_project/tests/observability-foundrylib.test.fs`

- [ ] **Step 1: Write the failing sink expectations**

Change `test_sink_calls_first_class_log_method` to expect:

```foundryscript
Expect.that(recording.captured_logs[0]).to_equal({
		"message": "player 7 missed",
		"level": ObservabilityLevel.WARN,
		"source": &"foundry.logging",
		"timestamp_msec": -1,
		"attributes": {"logger_name": "combat", "id": 7},
		"engine_ticks_msec": 99,
	})
```

Update `test_maps_structured_logs_to_observability_events`:

```foundryscript
Expect.that(event.timestamp_msec()).to_be_greater_than(1_000_000_000_000)
Expect.that(event.engine_ticks_msec()).to_equal(99)
```

- [ ] **Step 2: Run and verify RED**

Run `scripts/test-project`.

Expected: FAIL because the sink still forwards `99` as wall time and the trait has no engine-tick argument.

- [ ] **Step 3: Extend the trait, double, and sink**

Append this argument to `capture_log` in `FoundryObservabilityApi.fs` and
`RecordingObservabilityApi` (the concrete implementation received it in Task
2):

```foundryscript
engine_ticks_msec: int = -1,
```

Record it in the double:

```foundryscript
"engine_ticks_msec": engine_ticks_msec,
```

Change the sink call to:

```foundryscript
_service.capture_log(
		LogFormatter.render_message(record),
		event_level,
		&"foundry.logging",
		-1,
		attributes,
		record.timestamp_msec,
	)
```

- [ ] **Step 4: Run and verify GREEN**

Run `scripts/test-project`.

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservability/FoundryObservabilityApi.fs addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs test_project/tests/support/recording_observability_api.notest.fs test_project/tests/observability-foundrylib.test.fs
git commit -m "fix: preserve FoundryLib log engine ticks"
```

### Task 4: Carry both clocks through the FoundryScript Sentry adapter

**Files:**
- Modify: `test_project/tests/observability-sentry.test.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`

- [ ] **Step 1: Write failing provider payload assertions**

Change the event and log fixtures to
`p_timestamp_msec = 1721865600123` and
`p_engine_ticks_msec = 4567`, then add:

```foundryscript
Expect.that(bridge.captured_payloads[0]["timestamp_msec"]).to_equal(1721865600123)
Expect.that(bridge.captured_payloads[0]["engine_ticks_msec"]).to_equal(4567)
```

and equivalent assertions for `captured_log_payloads`.

- [ ] **Step 2: Run and verify RED**

Run `scripts/test-project`.

Expected: FAIL because `engine_ticks_msec` is absent from native payloads.

- [ ] **Step 3: Add the payload field**

In `SentryObservabilityProvider.capture`, add:

```foundryscript
"engine_ticks_msec": event.engine_ticks_msec(),
```

Keep `timestamp_msec` as the corrected Unix epoch value.

- [ ] **Step 4: Run and verify GREEN**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all adapter tests and strict FoundryScript checks pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs test_project/tests/observability-sentry.test.fs
git commit -m "feat: forward event wall time and engine ticks"
```

### Task 5: Set Apple Sentry event timestamps explicitly

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`

- [ ] **Step 1: Write failing Swift timestamp tests**

Add:

```swift
func testConvertsUnixMillisecondsWithoutTimezoneDependence() {
    let timestampMsec: Int64 = 1_612_325_106_123
    let original = NSTimeZone.default
    NSTimeZone.default = TimeZone(secondsFromGMT: 9 * 3_600)!
    defer { NSTimeZone.default = original }

    let date = sentryDate(timestampMsec: timestampMsec)

    XCTAssertEqual(date.timeIntervalSince1970, 1_612_325_106.123, accuracy: 0.000_001)
}
```

Update the exception event test to pass `timestampMsec: 1_612_325_106_123` and
`engineTicksMsec: 4567`, then assert:

```swift
XCTAssertEqual(event.timestamp.timeIntervalSince1970, 1_612_325_106.123, accuracy: 0.000_001)
XCTAssertEqual(event.extra?["foundry.timestamp_msec"] as? Int64, 1_612_325_106_123)
XCTAssertEqual(event.extra?["foundry.engine_ticks_msec"] as? Int64, 4567)
```

Update merged event/log metadata tests to require the new reserved key.

- [ ] **Step 2: Run Swift tests and verify RED**

Run:

```bash
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test
```

Expected: compile/test failure because `sentryDate` and `engineTicksMsec` are absent and `Event.timestamp` is not set by the mapper.

- [ ] **Step 3: Implement Apple conversion and mapping**

Add:

```swift
func sentryDate(timestampMsec: Int64) -> Date {
    Date(timeIntervalSince1970: TimeInterval(timestampMsec) / 1_000.0)
}
```

Append `engineTicksMsec: Int64` to `mergedLogAttributes`, `mergedExtras`, and
`makeSentryEvent`. Write the reserved diagnostic key only when non-negative:

```swift
if engineTicksMsec >= 0 {
    attributes["foundry.engine_ticks_msec"] = engineTicksMsec
}
```

In `makeSentryEvent`:

```swift
event.timestamp = sentryDate(timestampMsec: timestampMsec)
```

Update bridge calls:

```swift
timestampMsec: Int64(intValue(values["timestamp_msec"])),
engineTicksMsec: Int64(intValue(values["engine_ticks_msec"])),
```

Because FoundryScript always supplies `-1` for unavailable engine ticks, the
existing integer conversion helper remains sufficient.

- [ ] **Step 4: Run Swift tests and verify GREEN**

Run `swift test` from the Swift package directory.

Expected: all Swift mapper tests pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests
git commit -m "fix: set Apple Sentry event timestamps"
```

### Task 6: Set Android Sentry event timestamps explicitly

**Files:**
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLogMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLogMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`

- [ ] **Step 1: Write failing Android timestamp tests**

Add imports for `Date`, `TimeZone`, and assert:

```java
@Test
public void convertsUnixMillisecondsWithoutTimezoneDependence() {
  TimeZone original = TimeZone.getDefault();
  try {
    TimeZone.setDefault(TimeZone.getTimeZone("GMT+09:00"));
    Date result = SentryEventMapper.sentryDate(1612325106123L);
    assertEquals(1612325106123L, result.getTime());
  } finally {
    TimeZone.setDefault(original);
  }
}
```

Add `engine_ticks_msec = 4567L` to the event payload and assert:

```java
assertEquals(1612325106123L, result.getTimestamp().getTime());
assertEquals(4567L, result.getExtras().get("foundry.engine_ticks_msec"));
```

Extend `SentryLogMapperTest` to pass `4567L` and expect
`foundry.engine_ticks_msec`.

- [ ] **Step 2: Run Android tests and verify RED**

Run:

```bash
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew test
```

Expected: compile/test failure because `sentryDate`, the engine-tick mapper
arguments, and explicit `SentryEvent` timestamp assignment are absent.

- [ ] **Step 3: Implement Android conversion and metadata**

Import `java.util.Date` and add:

```java
static Date sentryDate(long timestampMsec) {
  return new Date(timestampMsec);
}
```

Read both payload values:

```java
long timestampMsec = longValue(values.get("timestamp_msec"), 0L);
long engineTicksMsec = longValue(values.get("engine_ticks_msec"), -1L);
```

Set the event timestamp:

```java
event.setTimestamp(sentryDate(timestampMsec));
```

Append `engineTicksMsec` to `mergedExtras` and `SentryLogMapper.mergedAttributes`,
then write:

```java
if (engineTicksMsec >= 0L) {
  extras.put("foundry.engine_ticks_msec", engineTicksMsec);
}
```

Update `SentryObservabilityBridge.captureLog` to pass
`longValue(values.get("engine_ticks_msec"), -1L)`.

- [ ] **Step 4: Run Android tests and verify GREEN**

Run:

```bash
./gradlew test
```

Expected: all Android unit tests pass.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src
git commit -m "fix: set Android Sentry event timestamps"
```

### Task 7: Document the corrected contract and run the full gate

**Files:**
- Modify: `docs/API.md`
- Modify: `README.md` only if the API reference cannot carry the migration note clearly
- Modify: `CHANGELOG.md`
- Modify: any affected contract fixtures identified by strict validation

- [ ] **Step 1: Update public documentation**

Replace the timestamp convention with:

```markdown
### Timestamps

Event `timestamp_msec` values are integer Unix epoch milliseconds representing
wall-clock occurrence time. Negative values are unspecified and resolve once
to capture time before provider dispatch; zero is the Unix epoch and is
preserved. `engine_ticks_msec` is an optional monotonic engine timestamp used
for elapsed-time diagnostics and rate limiting, never as a backend occurrence
timestamp.
```

Update constructor/accessor/signature examples, FoundryLib mapping, and Sentry
mapping. State that native Sentry events receive explicit occurrence dates,
while pinned structured-log APIs retain occurrence time in
`foundry.timestamp_msec` metadata and original ticks in
`foundry.engine_ticks_msec`.

Add to `CHANGELOG.md`:

```markdown
- Corrected event timestamps to Unix epoch milliseconds across the core and
  Apple/Android Sentry bridges, preserved monotonic engine ticks separately,
  and made missing timestamps resolve once to capture time.
```

- [ ] **Step 2: Run documentation and package checks**

Run:

```bash
task lint
task test:package
scripts/test-sentry-ios-build-contract
scripts/test-sentry-android-build-contract
```

Expected: all checks pass.

- [ ] **Step 3: Run the full validation gate**

Run:

```bash
task test
```

Expected: exit 0; all FoundryScript, Swift, contract, package, and hygiene checks pass.

- [ ] **Step 4: Run the Android build/test gate**

Run:

```bash
task android:sentry
```

Expected: Gradle unit tests, lint, and debug/release AAR builds pass.

- [ ] **Step 5: Review the final diff**

Run:

```bash
git diff origin/main...HEAD --check
git status --short
git diff --stat origin/main...HEAD
```

Expected: no whitespace errors or unintended generated files.

- [ ] **Step 6: Commit documentation and final adjustments**

```bash
git add docs/API.md README.md CHANGELOG.md
git commit -m "docs: document corrected timestamp semantics"
```

If `README.md` was not changed, omit it from `git add`.

### Task 8: Review, publish, and merge

**Files:**
- Review all branch changes against `origin/main`

- [ ] **Step 1: Run the required supervised Codex review**

Run:

```bash
python3 ~/.claude/scripts/codex_review/await_review.py start-wait \
  --cwd /Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-15 \
  --scope branch --base origin/main --deadline 540
```

Expected: clean verdict. Triage every finding, fix in-scope issues test-first,
and repeat on the new HEAD until clean or only documented follow-ups remain.

- [ ] **Step 2: Push and open the PR**

Run:

```bash
git push -u origin issue-15
```

Open a PR targeting `main` with a focused summary, test evidence, review
convergence, and a final `Closes #15`.

- [ ] **Step 3: Enable squash auto-merge**

Run:

```bash
gh pr merge --squash --auto
```

- [ ] **Step 4: Clean up after merge**

After GitHub reports the PR merged:

```bash
git -C /Users/christian/CafecitoGames/FoundryObservability worktree remove \
  /Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-15
git -C /Users/christian/CafecitoGames/FoundryObservability branch -D issue-15
```

If auto-merge is pending, leave the worktree and branch in place.
