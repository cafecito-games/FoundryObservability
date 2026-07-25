# Structured Stack Traces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add provider-neutral structured exception frames with source context and privacy-gated variables, then map them to native Sentry stack traces on Apple and Android without breaking string-only providers.

**Architecture:** A new immutable `ObservabilityStackFrame` value type is stored by `ObservabilityException`. `FoundryObservability` normalizes frames and applies source-context/variable policy before dispatch, while the Sentry bridge serializes normalized frames and each native mapper converts them to its SDK's native stack-trace objects. The existing formatted stack string remains an independent fallback.

**Tech Stack:** FoundryScript, Foundry testlib, Swift 6, Sentry Cocoa 9.23.0, Java 17, Sentry Android 8.50.1, JUnit 4, Task.

---

## File Map

- Create `addons/FoundryObservability/ObservabilityStackFrame.fs`: typed,
  immutable provider-neutral frame value.
- Create `addons/FoundryObservability/ObservabilityStackFrame.fs.uid`: tracked
  Foundry resource identifier.
- Modify `addons/FoundryObservability/ObservabilityException.fs`: retain frame
  arrays alongside the formatted stack fallback.
- Modify `addons/FoundryObservability/ObservabilityConfig.fs`: expose default-on
  source context and default-off variable policy.
- Modify `addons/FoundryObservability/FoundryObservability.fs`: validate,
  sanitize, and policy-normalize frames before every provider dispatch.
- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`:
  serialize normalized frames into the provider-neutral native bridge payload.
- Modify `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`:
  safely parse frame dictionaries and create Sentry Cocoa frames/stack traces.
- Modify `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`:
  use the shared safe exception parser.
- Modify `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java`:
  safely create Sentry Android frames/stack traces.
- Modify `test_project/tests/observability-core.test.fs`: cover value semantics,
  normalization, privacy, partial frames, and fallback.
- Modify `test_project/tests/observability-sentry.test.fs`: cover bridge payloads.
- Modify `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`:
  cover native Apple frame mapping.
- Modify `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java`:
  cover native Android mapping and malformed input.
- Modify `scripts/test-foundry-script`: assert the new public type exists.
- Modify `docs/API.md`, `README.md`, and `CHANGELOG.md`: publish the API,
  defaults, fallback, and platform behavior.

### Task 1: Add the typed frame and exception frame storage

**Files:**
- Create: `addons/FoundryObservability/ObservabilityStackFrame.fs`
- Create: `addons/FoundryObservability/ObservabilityStackFrame.fs.uid`
- Modify: `addons/FoundryObservability/ObservabilityException.fs`
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Write the failing value-object test**

Insert after `test_exception_and_event_copy_attributes()` in
`test_project/tests/observability-core.test.fs`:

```foundryscript
func test_stack_frame_and_exception_defensively_copy_structured_data() -> void:
	var pre := PackedStringArray(["before"])
	var post := PackedStringArray(["after"])
	var variables := {"player": {"health": 10}}
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_function = "attack",
			p_line = 42,
			p_language = "foundryscript",
			p_in_app = true,
			p_context_line = "deal_damage()",
			p_pre_context = pre,
			p_post_context = post,
			p_variables = variables,
		)
	var source_frames: Array[ObservabilityStackFrame] = [frame]
	var exception := ObservabilityException.new(
			p_type_name = "InvalidState",
			p_message = "bad state",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = source_frames,
		)

	pre.append("mutated")
	post.append("mutated")
	variables["player"]["health"] = 0
	source_frames.clear()
	var exposed_frames: Array[ObservabilityStackFrame] = exception.frames()
	exposed_frames.clear()
	var exposed_variables: Dictionary = frame.variables()
	exposed_variables["player"]["health"] = -1

	Expect.that(frame.file()).to_equal("res://player.fs")
	Expect.that(frame.function()).to_equal("attack")
	Expect.that(frame.line()).to_equal(42)
	Expect.that(frame.language()).to_equal("foundryscript")
	Expect.that(frame.in_app()).to_be_true()
	Expect.that(frame.context_line()).to_equal("deal_damage()")
	Expect.that(frame.pre_context()).to_equal(PackedStringArray(["before"]))
	Expect.that(frame.post_context()).to_equal(PackedStringArray(["after"]))
	Expect.that(frame.variables()).to_equal({"player": {"health": 10}})
	Expect.that(exception.stack_trace()).to_equal("formatted fallback")
	Expect.that(exception.frames()).to_have_size(1)
	Expect.that(exception.frames()[0]).to_equal(frame)
```

- [ ] **Step 2: Run the test suite and verify RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL during import/parse because `ObservabilityStackFrame` and
`ObservabilityException.frames()` do not exist.

- [ ] **Step 3: Add the frame value and exception storage**

Create `addons/FoundryObservability/ObservabilityStackFrame.fs`:

```foundryscript
namespace foundry.observability

## Provider-neutral immutable source frame in an exception stack trace.
class_name ObservabilityStackFrame
extends RefCounted

final var _file: String
final var _function: String
final var _line: int
final var _language: String
final var _in_app: bool
final var _context_line: String
final var _pre_context: PackedStringArray
final var _post_context: PackedStringArray
final var _variables: Dictionary


## Creates a frame and defensively copies source context and variables.
func _init(
		p_file: String = "",
		p_function: String = "",
		p_line: int = -1,
		p_language: String = "",
		p_in_app: bool = true,
		p_context_line: String = "",
		p_pre_context: PackedStringArray = PackedStringArray(),
		p_post_context: PackedStringArray = PackedStringArray(),
		p_variables: Dictionary = {},
) -> void:
	_file = p_file
	_function = p_function
	_line = p_line
	_language = p_language
	_in_app = p_in_app
	_context_line = p_context_line
	_pre_context = p_pre_context.duplicate()
	_post_context = p_post_context.duplicate()
	_variables = p_variables.duplicate(true)


func file() -> String:
	return _file


func function() -> String:
	return _function


func line() -> int:
	return _line


func language() -> String:
	return _language


func in_app() -> bool:
	return _in_app


func context_line() -> String:
	return _context_line


func pre_context() -> PackedStringArray:
	return _pre_context.duplicate()


func post_context() -> PackedStringArray:
	return _post_context.duplicate()


func variables() -> Dictionary:
	return _variables.duplicate(true)
```

Create `addons/FoundryObservability/ObservabilityStackFrame.fs.uid` with:

```text
uid://87ogw06rucvf
```

In `addons/FoundryObservability/ObservabilityException.fs`, add:

```foundryscript
var _frames: Array[ObservabilityStackFrame] = []
```

Replace the constructor with:

```foundryscript
## Creates an exception payload with copied attributes and structured frames.
func _init(
		p_type_name: String = "Error",
		p_message: String = "",
		p_stack_trace: String = "",
		p_attributes: Dictionary = {},
		p_frames: Array[ObservabilityStackFrame] = [],
) -> void:
	_type_name = p_type_name
	_message = p_message
	_stack_trace = p_stack_trace
	_attributes = p_attributes.duplicate(true)
	_frames = p_frames.duplicate()
```

Add the accessor:

```foundryscript
## Returns a copy of structured frames ordered oldest-to-newest.
func frames() -> Array[ObservabilityStackFrame]:
	return _frames.duplicate()
```

In `scripts/test-foundry-script`, add the file assertion beside the other core
value types:

```bash
[[ -f "$addon/ObservabilityStackFrame.fs" ]] || fail "stack frame value type is missing"
```

Add the class assertion beside `ObservabilityMetric`:

```bash
rg -q '^class_name ObservabilityStackFrame$' "$addon/ObservabilityStackFrame.fs" \
	|| fail "stack frame value type declaration is missing"
```

- [ ] **Step 4: Verify GREEN**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: all Foundry tests and contracts pass, including the new value-object
test and the tracked UID check.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservability/ObservabilityStackFrame.fs \
  addons/FoundryObservability/ObservabilityStackFrame.fs.uid \
  addons/FoundryObservability/ObservabilityException.fs \
  test_project/tests/observability-core.test.fs scripts/test-foundry-script
git commit -m "feat: add structured exception frames"
```

### Task 2: Apply capture policy and malformed-frame normalization

**Files:**
- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing policy and malformed-frame tests**

Add these tests before the configuration tests in
`test_project/tests/observability-core.test.fs`:

```foundryscript
func test_stack_frame_capture_defaults_keep_context_and_remove_variables() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_line = 10,
			p_context_line = "current",
			p_pre_context = PackedStringArray(["1", "2", "3", "4", "5", "6"]),
			p_post_context = PackedStringArray(["11", "12", "13", "14", "15", "16"]),
			p_variables = {"secret": "hidden"},
		)
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_stack_trace = "fallback",
			p_frames = [frame],
		))).to_equal("memory:1")

	var captured: ObservabilityStackFrame = provider.events()[0].exception().frames()[0]
	Expect.that(captured.context_line()).to_equal("current")
	Expect.that(captured.pre_context()).to_equal(PackedStringArray(["2", "3", "4", "5", "6"]))
	Expect.that(captured.post_context()).to_equal(PackedStringArray(["11", "12", "13", "14", "15"]))
	Expect.that(captured.variables()).to_equal({})
	service.shutdown()


func test_stack_frame_capture_can_disable_context_and_enable_variables() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_stack_trace_source_context_enabled = false,
			p_stack_trace_variables_enabled = true,
		)
	var frame := ObservabilityStackFrame.new(
			p_function = "attack",
			p_line = 0,
			p_context_line = "current",
			p_pre_context = PackedStringArray(["before"]),
			p_post_context = PackedStringArray(["after"]),
			p_variables = {
				"ok": [true, 2, 3.5, "value"],
				"nested": {"name": &"player"},
				"non_finite": NAN,
				"unsupported": Object.new(),
				7: "non-string key",
			},
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_frames = [frame],
		))).to_equal("memory:1")

	var captured: ObservabilityStackFrame = provider.events()[0].exception().frames()[0]
	Expect.that(captured.line()).to_equal(-1)
	Expect.that(captured.context_line()).to_equal("")
	Expect.that(captured.pre_context()).to_equal(PackedStringArray())
	Expect.that(captured.post_context()).to_equal(PackedStringArray())
	Expect.that(captured.variables()).to_equal({
			"ok": [true, 2, 3.5, "value"],
			"nested": {"name": "player"},
		})
	service.shutdown()


func test_stack_frame_capture_drops_empty_frames_and_preserves_partial_fallback() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var frames: Array[ObservabilityStackFrame] = [
		null,
		ObservabilityStackFrame.new(),
		ObservabilityStackFrame.new(p_language = "foundryscript"),
	]
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_stack_trace = "formatted only",
			p_frames = frames,
		))).to_equal("memory:1")

	var captured: ObservabilityException = provider.events()[0].exception()
	Expect.that(captured.stack_trace()).to_equal("formatted only")
	Expect.that(captured.frames()).to_have_size(1)
	Expect.that(captured.frames()[0].language()).to_equal("foundryscript")
	service.shutdown()
```

- [ ] **Step 2: Verify RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because the configuration arguments and normalization do not
exist; variables/context remain unchanged.

- [ ] **Step 3: Add configuration fields**

In `addons/FoundryObservability/ObservabilityConfig.fs`, add public fields:

```foundryscript
## Includes current and nearby source lines in structured stack frames.
var stack_trace_source_context_enabled: bool = true
## Allows explicitly collected frame variables to reach providers.
var stack_trace_variables_enabled: bool = false
```

Append constructor arguments after `p_metric_filter`:

```foundryscript
		p_stack_trace_source_context_enabled: bool = true,
		p_stack_trace_variables_enabled: bool = false,
) -> void:
```

Append assignments:

```foundryscript
	stack_trace_source_context_enabled = p_stack_trace_source_context_enabled
	stack_trace_variables_enabled = p_stack_trace_variables_enabled
```

- [ ] **Step 4: Normalize exception frames before dispatch**

In `capture_event()` in
`addons/FoundryObservability/FoundryObservability.fs`, wrap timestamp
normalization:

```foundryscript
	var normalized: ObservabilityEvent = _normalized_event_exception(
			_resolved_event_timestamp(
				event,
				capture_unix_msec,
				capture_engine_ticks_msec,
			),
		)
```

Add these helpers before `_normalized_metric()`:

```foundryscript
func _normalized_event_exception(event: ObservabilityEvent) -> ObservabilityEvent:
	var exception: ObservabilityException? = _normalized_exception(event.exception())
	if exception == event.exception():
		return event
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = event.timestamp_msec(),
			p_attributes = event.attributes(),
			p_exception = exception,
			p_engine_ticks_msec = event.engine_ticks_msec(),
		)


func _normalized_exception(exception: ObservabilityException?) -> ObservabilityException?:
	if exception == null:
		return null
	var normalized_frames: Array[ObservabilityStackFrame] = []
	for frame: ObservabilityStackFrame in exception.frames():
		if frame == null or _is_empty_stack_frame(frame):
			continue
		var context_line: String = frame.context_line()
		var pre_context: PackedStringArray = PackedStringArray()
		var post_context: PackedStringArray = PackedStringArray()
		if _config.stack_trace_source_context_enabled and not context_line.is_empty():
			var source_pre: PackedStringArray = frame.pre_context()
			var source_post: PackedStringArray = frame.post_context()
			pre_context = source_pre.slice(maxi(0, source_pre.size() - 5), source_pre.size())
			post_context = source_post.slice(0, mini(5, source_post.size()))
		else:
			context_line = ""
		var variables: Dictionary = {}
		if _config.stack_trace_variables_enabled:
			variables = _normalized_stack_variables(frame.variables())
		normalized_frames.append(ObservabilityStackFrame.new(
				p_file = frame.file(),
				p_function = frame.function(),
				p_line = frame.line() if frame.line() > 0 else -1,
				p_language = frame.language(),
				p_in_app = frame.in_app(),
				p_context_line = context_line,
				p_pre_context = pre_context,
				p_post_context = post_context,
				p_variables = variables,
			))
	return ObservabilityException.new(
			p_type_name = exception.type_name(),
			p_message = exception.message(),
			p_stack_trace = exception.stack_trace(),
			p_attributes = exception.attributes(),
			p_frames = normalized_frames,
		)


func _is_empty_stack_frame(frame: ObservabilityStackFrame) -> bool:
	return frame.file().is_empty() \
			and frame.function().is_empty() \
			and frame.language().is_empty() \
			and frame.line() < 1


func _normalized_stack_variables(source: Dictionary) -> Dictionary:
	var result: Dictionary = {}
	for key: Variant in source.keys():
		if not (key is String):
			continue
		var value: Variant = _normalized_stack_value(source[key])
		if value != null:
			result[key] = value
	return result


func _normalized_stack_value(value: Variant) -> Variant:
	if value is bool or value is int or value is String:
		return value
	if value is StringName:
		return String(value)
	if value is float:
		return value if is_finite(value) else null
	if value is Array:
		var normalized_array: Array = []
		for item: Variant in value:
			var normalized_item: Variant = _normalized_stack_value(item)
			if normalized_item != null:
				normalized_array.append(normalized_item)
		return normalized_array
	if value is Dictionary:
		return _normalized_stack_variables(value)
	return null
```

- [ ] **Step 5: Verify GREEN**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all tests pass. The memory provider receives normalized frames,
default capture omits variables, explicit enablement retains only supported
values, context is capped/disabled deterministically, and formatted fallback
survives.

- [ ] **Step 6: Commit**

```bash
git add addons/FoundryObservability/ObservabilityConfig.fs \
  addons/FoundryObservability/FoundryObservability.fs \
  test_project/tests/observability-core.test.fs
git commit -m "feat: normalize structured stack capture"
```

### Task 3: Forward structured frames through the Sentry bridge

**Files:**
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Write the failing bridge-payload test**

Add after `test_forwards_config_event_and_flush_to_native_bridge()`:

```foundryscript
func test_forwards_structured_exception_frames_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	var service: FoundryObservability = tree.root.get_node(
			"FoundryObservability") as FoundryObservability
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_function = "attack",
			p_line = 42,
			p_language = "foundryscript",
			p_in_app = true,
			p_context_line = "deal_damage()",
			p_pre_context = PackedStringArray(["func attack():"]),
			p_post_context = PackedStringArray(["return"]),
			p_variables = {"damage": 10},
		)
	var config := ObservabilityConfig.new(
			p_provider_options = {"dsn": "https://public@example/1"},
			p_stack_trace_variables_enabled = true,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "InvalidState",
			p_message = "boom",
			p_stack_trace = "fallback",
			p_frames = [frame],
		))).to_equal("sentry:1")

	var payload: Dictionary = bridge.captured_payloads[0]["exception"]
	Expect.that(payload["stack_trace"]).to_equal("fallback")
	Expect.that(payload["frames"]).to_have_size(1)
	Expect.that(payload["frames"][0]).to_equal({
			"file": "res://player.fs",
			"function": "attack",
			"line": 42,
			"language": "foundryscript",
			"in_app": true,
			"context_line": "deal_damage()",
			"pre_context": ["func attack():"],
			"post_context": ["return"],
			"variables": {"damage": 10},
		})
	service.shutdown()
```

Also add an assertion to the existing string-only forwarding test:

```foundryscript
	Expect.that(bridge.captured_payloads[0]["exception"].has("frames")).to_be_false()
```

- [ ] **Step 2: Verify RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because the captured exception payload has no `frames` key.

- [ ] **Step 3: Serialize frames**

Replace the inline exception assignment in
`addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs` with:

```foundryscript
	if exception != null:
		var exception_payload: Dictionary = {
			"type_name": exception.type_name(),
			"message": exception.message(),
			"stack_trace": exception.stack_trace(),
			"attributes": exception.attributes(),
		}
		var frames: Array[Dictionary] = []
		for frame: ObservabilityStackFrame in exception.frames():
			var frame_payload: Dictionary = {
				"file": frame.file(),
				"function": frame.function(),
				"line": frame.line(),
				"language": frame.language(),
				"in_app": frame.in_app(),
			}
			if not frame.context_line().is_empty():
				frame_payload["context_line"] = frame.context_line()
				frame_payload["pre_context"] = Array(frame.pre_context())
				frame_payload["post_context"] = Array(frame.post_context())
			var variables: Dictionary = frame.variables()
			if not variables.is_empty():
				frame_payload["variables"] = variables
			frames.append(frame_payload)
		if not frames.is_empty():
			exception_payload["frames"] = frames
		payload["exception"] = exception_payload
```

- [ ] **Step 4: Verify GREEN**

Run:

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all tests pass; structured frames use provider-neutral bridge keys,
and string-only exceptions still omit `frames`.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests/observability-sentry.test.fs
git commit -m "feat: forward structured frames to native bridges"
```

### Task 4: Map Apple frames to native Sentry stack traces

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`

- [ ] **Step 1: Write failing Apple mapping tests**

Add to `SentryEventMapperTests.swift`:

```swift
func testBuildsNativeStructuredStackTrace() {
    let payload: [String: Any] = [
        "type_name": "InvalidState",
        "message": "bad state",
        "stack_trace": "formatted fallback",
        "attributes": [:],
        "frames": [[
            "file": "res://player.fs",
            "function": "attack",
            "line": 42,
            "language": "foundryscript",
            "in_app": true,
            "context_line": "deal_damage()",
            "pre_context": ["func attack():"],
            "post_context": ["return"],
            "variables": ["damage": 10],
        ]],
    ]

    let event = makeSentryEvent(
        message: "boom",
        level: 50,
        source: "game",
        kind: "exception",
        timestampMsec: 1_612_325_106_123,
        engineTicksMsec: 4567,
        exception: foundryExceptionPayload(payload)
    )

    let frame = event.exceptions?.first?.stacktrace?.frames.first
    XCTAssertEqual(frame?.fileName, "res://player.fs")
    XCTAssertEqual(frame?.function, "attack")
    XCTAssertEqual(frame?.lineNumber?.intValue, 42)
    XCTAssertEqual(frame?.platform, "foundryscript")
    XCTAssertEqual(frame?.inApp?.boolValue, true)
    XCTAssertEqual(frame?.contextLine, "deal_damage()")
    XCTAssertEqual(frame?.preContext, ["func attack():"])
    XCTAssertEqual(frame?.postContext, ["return"])
    XCTAssertEqual(frame?.vars?["damage"] as? Int, 10)
    XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "formatted fallback")
}

func testMalformedAppleFramesAreOmittedSafely() {
    let payload: [String: Any] = [
        "type_name": "InvalidState",
        "message": "bad state",
        "stack_trace": "fallback",
        "attributes": [:],
        "frames": [
            "not-a-dictionary",
            ["file": "", "function": "", "line": -1, "language": ""],
            ["file": "res://partial.fs", "line": "not-an-int"],
        ],
    ]

    let parsed = foundryExceptionPayload(payload)
    XCTAssertEqual(parsed?.frames.count, 1)
    XCTAssertEqual(parsed?.frames.first?.file, "res://partial.fs")
    XCTAssertNil(parsed?.frames.first?.line)
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test --filter SentryEventMapperTests
```

Expected: compile failure because `foundryExceptionPayload`, frame payloads,
and native stack-trace mapping do not exist.

- [ ] **Step 3: Add safe payload parsing and native mapping**

In `SentryEventMapper.swift`, replace `FoundryExceptionPayload` with:

```swift
struct FoundryStackFramePayload {
    let file: String
    let function: String
    let line: Int?
    let language: String
    let inApp: Bool
    let contextLine: String?
    let preContext: [String]
    let postContext: [String]
    let variables: [String: Any]
}

struct FoundryExceptionPayload {
    let typeName: String
    let message: String
    let stackTrace: String
    let attributes: [String: Any]
    let frames: [FoundryStackFramePayload]

    init(
        typeName: String,
        message: String,
        stackTrace: String,
        attributes: [String: Any],
        frames: [FoundryStackFramePayload] = []
    ) {
        self.typeName = typeName
        self.message = message
        self.stackTrace = stackTrace
        self.attributes = attributes
        self.frames = frames
    }
}

private func stringDictionary(_ value: Any?) -> [String: Any] {
    value as? [String: Any] ?? [:]
}

private func stringArray(_ value: Any?) -> [String] {
    (value as? [Any])?.compactMap { $0 as? String } ?? []
}

private func foundryStackFramePayload(_ value: Any) -> FoundryStackFramePayload? {
    guard let values = value as? [String: Any] else {
        return nil
    }
    let file = values["file"] as? String ?? ""
    let function = values["function"] as? String ?? ""
    let language = values["language"] as? String ?? ""
    let rawLine = values["line"] as? NSNumber
    let line = rawLine.map(\.intValue).flatMap { $0 > 0 ? $0 : nil }
    guard !file.isEmpty || !function.isEmpty || !language.isEmpty || line != nil else {
        return nil
    }
    let contextLine = (values["context_line"] as? String).flatMap {
        $0.isEmpty ? nil : $0
    }
    return FoundryStackFramePayload(
        file: file,
        function: function,
        line: line,
        language: language,
        inApp: values["in_app"] as? Bool ?? true,
        contextLine: contextLine,
        preContext: contextLine == nil ? [] : stringArray(values["pre_context"]),
        postContext: contextLine == nil ? [] : stringArray(values["post_context"]),
        variables: stringDictionary(values["variables"])
    )
}

func foundryExceptionPayload(_ value: Any?) -> FoundryExceptionPayload? {
    let values = stringDictionary(value)
    guard !values.isEmpty else {
        return nil
    }
    let frames = (values["frames"] as? [Any] ?? []).compactMap(foundryStackFramePayload)
    return FoundryExceptionPayload(
        typeName: values["type_name"] as? String ?? "",
        message: values["message"] as? String ?? "",
        stackTrace: values["stack_trace"] as? String ?? "",
        attributes: stringDictionary(values["attributes"]),
        frames: frames
    )
}

private func sentryStacktrace(_ frames: [FoundryStackFramePayload]) -> Stacktrace? {
    let sentryFrames = frames.map { value in
        let frame = Frame()
        frame.fileName = value.file.isEmpty ? nil : value.file
        frame.function = value.function.isEmpty ? nil : value.function
        frame.lineNumber = value.line.map { NSNumber(value: $0) }
        frame.platform = value.language.isEmpty ? nil : value.language
        frame.inApp = NSNumber(value: value.inApp)
        frame.contextLine = value.contextLine
        frame.preContext = value.preContext.isEmpty ? nil : value.preContext
        frame.postContext = value.postContext.isEmpty ? nil : value.postContext
        frame.vars = value.variables.isEmpty ? nil : value.variables
        return frame
    }
    return sentryFrames.isEmpty ? nil : Stacktrace(frames: sentryFrames, registers: [:])
}
```

Replace the exception construction at the end of `makeSentryEvent`:

```swift
    if let exception {
        let sentryException = Exception(value: exception.message, type: exception.typeName)
        sentryException.stacktrace = sentryStacktrace(exception.frames)
        event.exceptions = [sentryException]
    }
```

In `FoundryObservabilitySentry.swift`, delete the private `exceptionPayload`
function and replace:

```swift
let exception = exceptionPayload(values["exception"])
```

with:

```swift
let exception = foundryExceptionPayload(values["exception"])
```

- [ ] **Step 4: Verify GREEN**

Run:

```bash
cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
swift test --filter SentryEventMapperTests
```

Expected: all mapper tests pass, including native stack fields, fallback extra,
and safe malformed-frame omission.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift
git commit -m "feat: map structured stacks on Apple"
```

### Task 5: Map Android frames to native Sentry stack traces

**Files:**
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java`

- [ ] **Step 1: Write failing Android tests**

Add imports:

```java
import io.sentry.protocol.SentryStackFrame;
import java.util.List;
```

Add tests:

```java
@Test
public void mapsNativeStructuredStackTrace() {
  Map<String, Object> frame = new HashMap<>();
  frame.put("file", "res://player.fs");
  frame.put("function", "attack");
  frame.put("line", 42);
  frame.put("language", "foundryscript");
  frame.put("in_app", true);
  frame.put("context_line", "deal_damage()");
  frame.put("pre_context", List.of("func attack():"));
  frame.put("post_context", List.of("return"));
  frame.put("variables", Map.of("damage", 10));

  Map<String, Object> payload = exceptionEventPayload();
  payload.put("exception", Map.of(
      "type_name", "InvalidState",
      "message", "boom",
      "stack_trace", "fallback",
      "attributes", Map.of(),
      "frames", List.of(frame)));

  SentryEvent event = SentryEventMapper.makeEvent(payload, Map.of());
  SentryStackFrame result = event.getExceptions().get(0).getStacktrace().getFrames().get(0);

  assertEquals("res://player.fs", result.getFilename());
  assertEquals("attack", result.getFunction());
  assertEquals(Integer.valueOf(42), result.getLineno());
  assertEquals("foundryscript", result.getPlatform());
  assertEquals(Boolean.TRUE, result.isInApp());
  assertEquals("deal_damage()", result.getContextLine());
  assertEquals(List.of("func attack():"), result.getPreContext());
  assertEquals(List.of("return"), result.getPostContext());
  assertEquals(10L, result.getVars().get("damage"));
  assertEquals("fallback", event.getExtras().get("foundry.stack_trace"));
}

@Test
public void ignoresMalformedAndroidFramesWithoutThrowing() {
  Map<String, Object> payload = exceptionEventPayload();
  payload.put("exception", Map.of(
      "type_name", "InvalidState",
      "message", "boom",
      "stack_trace", "fallback",
      "attributes", Map.of(),
      "frames", List.of(
          "not-a-map",
          Map.of("file", "", "function", "", "line", -1, "language", ""),
          Map.of("file", "res://partial.fs", "line", "invalid"))));

  SentryEvent event = SentryEventMapper.makeEvent(payload, Map.of());
  List<SentryStackFrame> frames =
      event.getExceptions().get(0).getStacktrace().getFrames();

  assertEquals(1, frames.size());
  assertEquals("res://partial.fs", frames.get(0).getFilename());
  assertNull(frames.get(0).getLineno());
}

private static Map<String, Object> exceptionEventPayload() {
  Map<String, Object> payload = new HashMap<>();
  payload.put("kind", "exception");
  payload.put("level", 50);
  payload.put("message", "boom");
  payload.put("source", "game");
  payload.put("timestamp_msec", 1612325106123L);
  payload.put("engine_ticks_msec", 4567L);
  payload.put("attributes", Map.of());
  return payload;
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew testDebugUnitTest \
  --tests games.cafecito.android.foundryobservabilitysentry.SentryEventMapperTest
```

Expected: FAIL because `SentryException.getStacktrace()` is null.

- [ ] **Step 3: Implement safe Android mapping**

Add imports to `SentryEventMapper.java`:

```java
import io.sentry.protocol.SentryStackFrame;
import io.sentry.protocol.SentryStackTrace;
```

After setting exception type/value in `makeEvent`, add:

```java
        SentryStackTrace stacktrace = stacktrace(exception.get("frames"));
        if (stacktrace != null) {
          sentryException.setStacktrace(stacktrace);
        }
```

Add these helpers before `metricPayload()`:

```java
  private static SentryStackTrace stacktrace(Object value) {
    List<Object> elements = new ArrayList<>();
    if (value instanceof Iterable) {
      for (Object element : (Iterable<?>) value) {
        elements.add(element);
      }
    } else if (value != null && value.getClass().isArray()) {
      for (int index = 0; index < Array.getLength(value); index++) {
        elements.add(Array.get(value, index));
      }
    } else {
      return null;
    }
    List<SentryStackFrame> frames = new ArrayList<>();
    for (Object element : elements) {
      SentryStackFrame frame = stackFrame(element);
      if (frame != null) {
        frames.add(frame);
      }
    }
    return frames.isEmpty() ? null : new SentryStackTrace(frames);
  }

  private static SentryStackFrame stackFrame(Object value) {
    if (!(value instanceof Map)) {
      return null;
    }
    Map<?, ?> values = (Map<?, ?>) value;
    String file = values.get("file") instanceof String
        ? (String) values.get("file")
        : "";
    String function = values.get("function") instanceof String
        ? (String) values.get("function")
        : "";
    String language = values.get("language") instanceof String
        ? (String) values.get("language")
        : "";
    int line = intValue(values.get("line"), -1);
    if (file.isEmpty() && function.isEmpty() && language.isEmpty() && line < 1) {
      return null;
    }

    SentryStackFrame frame = new SentryStackFrame();
    frame.setFilename(file.isEmpty() ? null : file);
    frame.setFunction(function.isEmpty() ? null : function);
    frame.setLineno(line > 0 ? line : null);
    frame.setPlatform(language.isEmpty() ? null : language);
    frame.setInApp(values.get("in_app") instanceof Boolean
        ? (Boolean) values.get("in_app")
        : true);

    String contextLine = values.get("context_line") instanceof String
        ? (String) values.get("context_line")
        : "";
    if (!contextLine.isEmpty()) {
      frame.setContextLine(contextLine);
      frame.setPreContext(stringList(values.get("pre_context")));
      frame.setPostContext(stringList(values.get("post_context")));
    }
    Map<String, Object> variables = asMap(values.get("variables"));
    if (!variables.isEmpty()) {
      frame.setVars(variables);
    }
    return frame;
  }

  private static List<String> stringList(Object value) {
    List<String> result = new ArrayList<>();
    if (value instanceof Iterable) {
      for (Object element : (Iterable<?>) value) {
        if (element instanceof String) {
          result.add((String) element);
        }
      }
    } else if (value != null && value.getClass().isArray()) {
      for (int index = 0; index < Array.getLength(value); index++) {
        Object element = Array.get(value, index);
        if (element instanceof String) {
          result.add((String) element);
        }
      }
    }
    return result;
  }
```

- [ ] **Step 4: Verify GREEN**

Run:

```bash
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew testDebugUnitTest \
  --tests games.cafecito.android.foundryobservabilitysentry.SentryEventMapperTest
```

Expected: all mapper tests pass, including full frame mapping and malformed
frame omission.

- [ ] **Step 5: Commit**

```bash
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java \
  addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java
git commit -m "feat: map structured stacks on Android"
```

### Task 6: Publish the API and run platform verification

**Files:**
- Modify: `docs/API.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add public documentation**

Add `ObservabilityStackFrame` to the API value-type index. Before the
`ObservabilityException` section in `docs/API.md`, add:

```markdown
## ObservabilityStackFrame

ObservabilityStackFrame describes one provider-neutral exception frame.
Frames are ordered oldest-to-newest. Positive line numbers are one-based and
`-1` means unknown.

~~~
ObservabilityStackFrame.new(
		p_file: String = "",
		p_function: String = "",
		p_line: int = -1,
		p_language: String = "",
		p_in_app: bool = true,
		p_context_line: String = "",
		p_pre_context: PackedStringArray = PackedStringArray(),
		p_post_context: PackedStringArray = PackedStringArray(),
		p_variables: Dictionary = {},
)
~~~

Accessors are `file()`, `function()`, `line()`, `language()`, `in_app()`,
`context_line()`, `pre_context()`, `post_context()`, and `variables()`.
Context arrays and variables are defensively copied.

Source context includes at most the nearest five lines before and after the
current line. Completely empty frames are omitted; useful partial frames are
preserved. Variables are privacy-sensitive and must not be acquired unless
`ObservabilityConfig.stack_trace_variables_enabled` is true.
```

Update the exception constructor/accessor block to include:

```foundryscript
		p_frames: Array[ObservabilityStackFrame] = [],
```

```foundryscript
func frames() -> Array[ObservabilityStackFrame]
```

Add this example:

```foundryscript
var frame := ObservabilityStackFrame.new(
		p_file = "res://player.fs",
		p_function = "attack",
		p_line = 42,
		p_language = "foundryscript",
		p_context_line = "deal_damage()",
)
FoundryObservability.capture_exception(ObservabilityException.new(
		p_type_name = "InvalidState",
		p_message = "Player cannot attack",
		p_stack_trace = "formatted fallback for string-only providers",
		p_frames = [frame],
))
```

Add both configuration fields/arguments to the config tables and document:

```markdown
Source context is enabled by default and variable capture is disabled by
default. The service applies both policies before every provider dispatch.
Producers must check `stack_trace_variables_enabled` before collecting locals;
disabling it after collection avoids transmission but cannot recover the
collection cost.
```

In `README.md`, add this status bullet:

```markdown
- Structured exception frames with source context, privacy-gated variables,
  formatted-string fallback, and native Apple/Android Sentry mapping.
```

In `CHANGELOG.md`, add:

```markdown
- Added provider-neutral structured exception frames with bounded source
  context, opt-in variables, formatted-stack fallback, and native Apple and
  Android Sentry stack-trace mapping.
- Removed the incompatible gdtoolkit/Python development dependency; repository
  script validation uses Foundry's native formatter and linter.
```

- [ ] **Step 2: Run formatting and focused contracts**

Run:

```bash
foundry_bin_path=${FOUNDRY_BIN:-/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64}
"$foundry_bin_path" --headless script format addons/FoundryObservability \
  addons/FoundryObservabilitySentry
task lint
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-project
```

Expected: formatting produces no unintended changes; lint/contracts pass; all
Foundry tests pass.

- [ ] **Step 3: Run native mapper suites**

Run:

```bash
(
  cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry
  swift test
)
(
  cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
  ./gradlew testDebugUnitTest
)
```

Expected: all Swift and Android unit tests pass with zero failures.

- [ ] **Step 4: Build both native deliverables**

Run:

```bash
task ios:sentry
task android:sentry
```

Expected: Apple device/simulator/macOS frameworks and Android debug/release
AARs build successfully. Generated binary outputs remain ignored.

- [ ] **Step 5: Run the complete repository gate**

Run:

```bash
task test
git diff --check
git status --short
```

Expected: every repository validation gate passes, the diff has no whitespace
errors, and status contains only intentional source, test, and documentation
changes.

- [ ] **Step 6: Commit documentation**

```bash
git add docs/API.md README.md CHANGELOG.md
git commit -m "docs: publish structured stack trace API"
```

- [ ] **Step 7: Review requirement coverage**

Check the final diff against
`docs/superpowers/specs/2026-07-24-structured-stack-traces-design.md`:

```bash
git diff origin/main...HEAD --stat
git diff origin/main...HEAD -- \
  addons/FoundryObservability \
  addons/FoundryObservabilitySentry \
  test_project/tests docs README.md CHANGELOG.md BUILD.md
```

Expected coverage:

- file, function, line, language, in-app, context, and variables exist;
- source context defaults on and variables default off;
- malformed/partial frames follow documented normalization;
- formatted stack fallback remains;
- Apple and Android use native stack-trace models;
- deterministic core, bridge, Swift, and Android tests exist;
- public docs and changelog describe the behavior;
- no Python/gdtoolkit runtime or development dependency remains.
