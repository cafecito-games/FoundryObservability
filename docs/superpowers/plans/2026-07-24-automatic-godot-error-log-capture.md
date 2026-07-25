# Automatic Godot Error and Log Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically turn Foundry engine errors and configured output messages into provider-neutral events, breadcrumbs, and structured logs with deterministic filtering and throttling.

**Architecture:** A core `AutomaticObservabilityLogger` extends Foundry's script-visible `Logger` and is registered by the `FoundryObservability` autoload only while enabled configuration is active. It normalizes engine callbacks once, applies independent destination masks and deterministic limits, and dispatches through existing event/log APIs plus a new optional breadcrumb provider capability; Sentry's Swift and Android bridges only implement normalized breadcrumb delivery.

**Tech Stack:** FoundryScript, Foundry testlib, Foundry `Logger`/`OS.add_logger`, Swift 6/XCTest, Sentry Cocoa 9.23.0, Java 17/JUnit, Sentry Android 8.50.1, Bash, Task.

---

## File map

- Create `addons/FoundryObservability/ObservabilityCaptureMask.fs` and `.uid` for public automatic-capture category flags.
- Create `addons/FoundryObservability/ObservabilityBreadcrumb.fs` and `.uid` for the provider-neutral breadcrumb value.
- Create `addons/FoundryObservability/ObservabilityBreadcrumbsProvider.fs` and `.uid` for optional provider support.
- Create `addons/FoundryObservability/AutomaticObservabilityLogger.fs` and `.uid` for normalization, masks, recursion checks, and throttling.
- Modify `addons/FoundryObservability/ObservabilityConfig.fs` with automatic-capture settings.
- Modify `addons/FoundryObservability/FoundryObservabilityApi.fs` and `FoundryObservability.fs` with breadcrumb delivery, provider-call guarding, and logger lifecycle.
- Modify `addons/FoundryObservability/MemoryObservabilityProvider.fs` with deterministic breadcrumb storage.
- Modify `test_project/tests/observability-core.test.fs` with core/logger/lifecycle/integration coverage.
- Create `test_project/tests/support/breadcrumbless_observability_provider.notest.fs` and `.uid` for capability fallback.
- Create `test_project/tests/support/reentrant_observability_provider.notest.fs` and `.uid` for recursion coverage.
- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`, `test_project/tests/observability-sentry.test.fs`, and fake bridge fixtures for breadcrumb routing.
- Modify the Swift bridge/mapper tests and iOS contract script for Sentry Cocoa breadcrumb delivery.
- Modify the Android bridge/tests and Android contract script for Sentry Android breadcrumb delivery.
- Modify `scripts/test-foundry-script`, `README.md`, `docs/API.md`, and `CHANGELOG.md`.

### Task 1: Add capture masks, breadcrumb values, and configuration

**Files:**

- Create: `addons/FoundryObservability/ObservabilityCaptureMask.fs`
- Create: `addons/FoundryObservability/ObservabilityCaptureMask.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityBreadcrumb.fs`
- Create: `addons/FoundryObservability/ObservabilityBreadcrumb.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityBreadcrumbsProvider.fs`
- Create: `addons/FoundryObservability/ObservabilityBreadcrumbsProvider.fs.uid`
- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing public-value and configuration tests**

Add tests that require the new flags, copied breadcrumb attributes, copied
prefixes, safe defaults, and non-negative limits:

```foundryscript
func test_automatic_capture_masks_and_config_defaults() -> void:
	var config := ObservabilityConfig.new()
	Expect.that(config.automatic_capture_enabled).to_be_true()
	Expect.that(config.automatic_event_mask).to_equal(
			ObservabilityCaptureMask.ERROR
			| ObservabilityCaptureMask.SCRIPT
			| ObservabilityCaptureMask.SHADER)
	Expect.that(config.automatic_breadcrumb_mask).to_equal(
			ObservabilityCaptureMask.ALL)
	Expect.that(config.automatic_log_mask).to_equal(ObservabilityCaptureMask.NONE)
	Expect.that(config.automatic_events_per_frame).to_equal(5)
	Expect.that(config.automatic_repeated_error_window_msec).to_equal(1000)
	Expect.that(config.automatic_event_throttle_count).to_equal(20)
	Expect.that(config.automatic_event_throttle_window_msec).to_equal(10000)


func test_automatic_capture_config_and_breadcrumb_copy_inputs() -> void:
	var prefixes := PackedStringArray(["Internal: "])
	var attributes := {"file": "res://player.fs"}
	var config := ObservabilityConfig.new(
			p_automatic_events_per_frame = -1,
			p_automatic_repeated_error_window_msec = -1,
			p_automatic_event_throttle_count = -1,
			p_automatic_event_throttle_window_msec = -1,
			p_automatic_message_filter_prefixes = prefixes,
		)
	var breadcrumb := ObservabilityBreadcrumb.new(
			p_message = "warning",
			p_level = ObservabilityLevel.WARN,
			p_category = &"error",
			p_timestamp_msec = 1234,
			p_attributes = attributes,
		)
	prefixes[0] = "changed"
	attributes["file"] = "changed"

	Expect.that(config.automatic_events_per_frame).to_equal(0)
	Expect.that(config.automatic_repeated_error_window_msec).to_equal(0)
	Expect.that(config.automatic_event_throttle_count).to_equal(0)
	Expect.that(config.automatic_event_throttle_window_msec).to_equal(0)
	Expect.that(config.automatic_message_filter_prefixes()).to_equal(
			PackedStringArray(["Internal: "]))
	Expect.that(breadcrumb.message()).to_equal("warning")
	Expect.that(breadcrumb.level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(breadcrumb.category()).to_equal(&"error")
	Expect.that(breadcrumb.timestamp_msec()).to_equal(1234)
	Expect.that(breadcrumb.attributes()).to_equal({"file": "res://player.fs"})
```

- [ ] **Step 2: Run the focused suite and verify RED**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: import/test failure for missing `ObservabilityCaptureMask`,
`ObservabilityBreadcrumb`, and automatic configuration parameters.

- [ ] **Step 3: Add the public values and optional capability**

Create:

```foundryscript
namespace foundry.observability

## Bit flags selecting automatic engine diagnostics and messages.
class_name ObservabilityCaptureMask
extends RefCounted

const NONE: int = 0
const ERROR: int = 1 << 0
const WARNING: int = 1 << 1
const SCRIPT: int = 1 << 2
const SHADER: int = 1 << 3
const MESSAGE: int = 1 << 7
const ALL_ERRORS: int = ERROR | WARNING | SCRIPT | SHADER
const ALL: int = ALL_ERRORS | MESSAGE
const DEFAULT_EVENTS: int = ERROR | SCRIPT | SHADER
const DEFAULT_BREADCRUMBS: int = ALL
```

Create the breadcrumb with private fields, a copying constructor, and getters:

```foundryscript
namespace foundry.observability

class_name ObservabilityBreadcrumb
extends RefCounted

var _message: String
var _level: int
var _category: StringName
var _timestamp_msec: int
var _attributes: Dictionary

func _init(
		p_message: String = "",
		p_level: int = ObservabilityLevel.INFO,
		p_category: StringName = &"",
		p_timestamp_msec: int = 0,
		p_attributes: Dictionary = {},
) -> void:
	_message = p_message
	_level = p_level
	_category = p_category
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)
```

Create:

```foundryscript
namespace foundry.observability

trait_name ObservabilityBreadcrumbsProvider

abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
```

Add stable UID companions and extend `scripts/test-foundry-script` with file,
class, trait, and `capture_breadcrumb` contract checks.

- [ ] **Step 4: Extend `ObservabilityConfig`**

Append automatic fields and constructor parameters after the existing metric
parameters so existing positional call sites remain compatible. Clamp limit
values with `maxi(0, value)`, copy the prefix array into a private field, and
return a copy from:

```foundryscript
func automatic_message_filter_prefixes() -> PackedStringArray:
	return _automatic_message_filter_prefixes.duplicate()
```

- [ ] **Step 5: Run the focused suite and verify GREEN**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
```

Expected: all existing tests plus the new value/configuration tests pass.

- [ ] **Step 6: Commit the public model**

```sh
git add addons/FoundryObservability test_project/tests/observability-core.test.fs \
  scripts/test-foundry-script
git commit -m "feat: define automatic capture configuration"
```

### Task 2: Add provider-neutral breadcrumb delivery and recursion guarding

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs`
- Create: `test_project/tests/support/breadcrumbless_observability_provider.notest.fs`
- Create: `test_project/tests/support/breadcrumbless_observability_provider.notest.fs.uid`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing delivery and fallback tests**

```foundryscript
func test_memory_provider_captures_and_clears_breadcrumbs() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var breadcrumb := ObservabilityBreadcrumb.new(p_message = "trail")
	Expect.that(service.capture_breadcrumb(breadcrumb)).to_be_true()
	Expect.that(provider.breadcrumbs()).to_equal([breadcrumb])
	provider.clear_breadcrumbs()
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	service.shutdown()


func test_missing_breadcrumb_capability_is_observable_and_isolated() -> void:
	var service: FoundryObservability = _service()
	var provider := BreadcrumblessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "unsupported"))).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("still works")).to_equal("event:1")
	service.shutdown()
```

- [ ] **Step 2: Run the focused suite and verify RED**

Run the test-project command and expect missing API/provider methods.

- [ ] **Step 3: Implement breadcrumb delivery**

Add to `FoundryObservabilityApi`:

```foundryscript
abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
```

Have `MemoryObservabilityProvider` use
`ObservabilityBreadcrumbsProvider`, store a separate typed array, and expose
copy-returning accessors.

In the service, validate non-null, honor `enabled`, use `has_method` before
calling the optional capability, require a boolean true result, and set
`ERR_UNAVAILABLE` or `FAILED` without emitting diagnostics.

- [ ] **Step 4: Guard every provider operation**

Add:

```foundryscript
var _pipeline_mutex: Mutex = Mutex.new()
var _provider_call_count: int = 0

func _begin_provider_call() -> void:
	_pipeline_mutex.lock()
	_provider_call_count += 1
	_pipeline_mutex.unlock()

func _end_provider_call() -> void:
	_pipeline_mutex.lock()
	_provider_call_count = maxi(0, _provider_call_count - 1)
	_pipeline_mutex.unlock()

func try_begin_automatic_capture() -> bool:
	if not _pipeline_mutex.try_lock():
		return false
	if _provider_call_count > 0:
		_pipeline_mutex.unlock()
		return false
	_provider_call_count += 1
	_pipeline_mutex.unlock()
	return true

func end_automatic_capture() -> void:
	_end_provider_call()
```

Wrap provider `configure`, event, breadcrumb, feedback, metric, flush, and
shutdown calls with balanced begin/end calls on every return path. Do not hold
the mutex while invoking provider code.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the test project and FoundryScript contract checks. Expected: all tests
pass with breadcrumb fallback isolated from event capture.

- [ ] **Step 6: Commit breadcrumb delivery**

```sh
git add addons/FoundryObservability test_project/tests
git commit -m "feat: add provider-neutral breadcrumbs"
```

### Task 3: Implement automatic logger normalization and filtering

**Files:**

- Create: `addons/FoundryObservability/AutomaticObservabilityLogger.fs`
- Create: `addons/FoundryObservability/AutomaticObservabilityLogger.fs.uid`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing error and message normalization tests**

Construct the logger directly with mutable fake clock/frame values and invoke
its virtual callbacks:

```foundryscript
func test_automatic_logger_routes_error_metadata_by_independent_masks() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1234, func() -> int: return 7)
	logger._log_error(
			"attack", "res://player.fs", 42, "ERR_INVALID_DATA", "bad hit",
			false, Logger.ERROR_TYPE_ERROR, [])

	Expect.that(provider.events()).to_have_size(2)
	var exception_event: ObservabilityEvent = provider.events()[0]
	Expect.that(exception_event.kind()).to_equal(&"exception")
	Expect.that(exception_event.exception().type_name()).to_equal("ERROR")
	Expect.that(exception_event.attributes()["error.function"]).to_equal("attack")
	Expect.that(exception_event.attributes()["error.file"]).to_equal("res://player.fs")
	Expect.that(exception_event.attributes()["error.line"]).to_equal(42)
	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.events()[1].kind()).to_equal(&"log")
	service.shutdown()
```

Add:

```foundryscript
func test_automatic_logger_maps_error_categories_and_levels() -> void:
	var cases: Array = [
		[Logger.ERROR_TYPE_WARNING, ObservabilityLevel.WARN, "WARNING"],
		[Logger.ERROR_TYPE_SCRIPT, ObservabilityLevel.ERROR, "SCRIPT ERROR"],
		[Logger.ERROR_TYPE_SHADER, ObservabilityLevel.ERROR, "SHADER ERROR"],
		[Logger.ERROR_TYPE_FATAL, ObservabilityLevel.FATAL, "FATAL"],
	]
	for case: Array in cases:
		var service: FoundryObservability = _service()
		var provider := MemoryObservabilityProvider.new()
		var config := ObservabilityConfig.new(
				p_automatic_event_mask = ObservabilityCaptureMask.ALL_ERRORS,
				p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
				p_automatic_repeated_error_window_msec = 0,
			)
		Expect.that(service.configure(provider, config)).to_equal(Error.OK)
		var logger := AutomaticObservabilityLogger.new(
				service, config, func() -> int: return 1, func() -> int: return 1)
		logger._log_error("run", "res://case.fs", 3, "fallback", "", false,
				int(case[0]), [])
		Expect.that(provider.events()[0].level()).to_equal(int(case[1]))
		Expect.that(provider.events()[0].message()).to_equal("fallback")
		Expect.that(provider.events()[0].exception().type_name()).to_equal(str(case[2]))
		service.shutdown()


func test_automatic_logger_filters_and_routes_messages_without_events() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_automatic_event_mask = ObservabilityCaptureMask.ALL_ERRORS,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.MESSAGE,
			p_automatic_log_mask = ObservabilityCaptureMask.MESSAGE,
			p_automatic_message_filter_prefixes = PackedStringArray(["Internal: "]),
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1234, func() -> int: return 1)
	logger._log_message("\u001b[31mhello\u001b[0m\n", false)
	logger._log_message("Internal: ignored", true)

	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("hello")
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].kind()).to_equal(&"log")
	Expect.that(provider.events()[0].level()).to_equal(ObservabilityLevel.INFO)
	service.shutdown()
```

- [ ] **Step 2: Run the focused suite and verify RED**

Expected: missing `AutomaticObservabilityLogger`.

- [ ] **Step 3: Add the logger shell and deterministic dependencies**

Create:

```foundryscript
namespace foundry.observability

class_name AutomaticObservabilityLogger
extends Logger

var _service: FoundryObservability
var _config: ObservabilityConfig
var _clock: Callable
var _frame: Callable
var _state_mutex: Mutex = Mutex.new()

func _init(
		service: FoundryObservability,
		config: ObservabilityConfig,
		clock: Callable = Callable(),
		frame: Callable = Callable(),
) -> void:
	_service = service
	_config = config
	_clock = clock if clock.is_valid() else func() -> int: return Time.get_ticks_msec()
	_frame = frame if frame.is_valid() else func() -> int: return Engine.get_process_frames()
```

- [ ] **Step 4: Normalize error callbacks**

Implement `_log_error` with exact Foundry virtual parameter types. Map logger
error types to mask, level, and stable names; build source attributes and
serialized backtrace dictionaries; render a deterministic stack string; then
independently call `capture_exception`, `capture_breadcrumb`, and
`capture_log` based on masks.

Before work, continue only when `_service.try_begin_automatic_capture()` returns
true, and always release that reservation with
`_service.end_automatic_capture()` after the callback.
Never call engine logging from this class.

- [ ] **Step 5: Normalize message callbacks**

Implement `_log_message(message, error)` by removing ANSI escape sequences and
control characters, rejecting empty/prefix-filtered output, and independently
routing `MESSAGE` to a `log` breadcrumb and/or structured log. Never create an
event for a message.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
```

- [ ] **Step 7: Commit logger normalization**

```sh
git add addons/FoundryObservability/AutomaticObservabilityLogger.fs* \
  scripts/test-foundry-script test_project/tests/observability-core.test.fs
git commit -m "feat: normalize automatic engine diagnostics"
```

### Task 4: Add deterministic throttling and logger lifecycle

**Files:**

- Modify: `addons/FoundryObservability/AutomaticObservabilityLogger.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Create: `test_project/tests/support/reentrant_observability_provider.notest.fs`
- Create: `test_project/tests/support/reentrant_observability_provider.notest.fs.uid`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing throttling tests**

Use injected mutable clock and frame suppliers to cover:

```foundryscript
func test_automatic_logger_suppresses_duplicate_errors_deterministically() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 1000,
			p_automatic_events_per_frame = 0,
			p_automatic_event_throttle_count = 0,
		)
	var now := [1000]
	var frame := [1]
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service,
			config,
			func() -> int: return now[0],
			func() -> int: return frame[0],
		)

	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	now[0] = 1500
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.breadcrumbs()).to_have_size(1)

	now[0] = 2000
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(4)
	Expect.that(provider.breadcrumbs()).to_have_size(2)
	service.shutdown()

func test_automatic_event_limits_do_not_suppress_breadcrumbs_or_logs() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
			p_automatic_events_per_frame = 1,
			p_automatic_event_throttle_count = 2,
			p_automatic_event_throttle_window_msec = 1000,
		)
	var now := [1000]
	var frame := [1]
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service,
			config,
			func() -> int: return now[0],
			func() -> int: return frame[0],
		)

	for message: String in ["a", "b"]:
		logger._log_error("tick", "res://loop.fs", 9, message, "", false,
				Logger.ERROR_TYPE_ERROR, [])
	frame[0] = 2
	now[0] = 1002
	for message: String in ["c", "d"]:
		logger._log_error("tick", "res://loop.fs", 9, message, "", false,
				Logger.ERROR_TYPE_ERROR, [])
	frame[0] = 3
	now[0] = 1003
	logger._log_error("tick", "res://loop.fs", 9, "e", "", false,
			Logger.ERROR_TYPE_ERROR, [])

	var exception_count: int = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() == &"exception":
			exception_count += 1
	Expect.that(exception_count).to_equal(2)
	Expect.that(provider.breadcrumbs()).to_have_size(5)
	Expect.that(provider.events()).to_have_size(7)

	frame[0] = 4
	now[0] = 2001
	logger._log_error("tick", "res://loop.fs", 9, "f", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.breadcrumbs()).to_have_size(6)
	Expect.that(provider.events()).to_have_size(9)
	service.shutdown()
```

Add:

```foundryscript
func test_automatic_logger_bounds_identity_state_and_resets_limits() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 100000,
			p_automatic_events_per_frame = 0,
			p_automatic_event_throttle_count = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)
	for index: int in range(101):
		logger._log_error("tick", "res://loop.fs", index, str(index), "", false,
				Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(101)

	logger._log_error("tick", "res://loop.fs", 0, "0", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(102)
	logger.reset()
	logger._log_error("tick", "res://loop.fs", 0, "0", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(103)
	service.shutdown()
```

- [ ] **Step 2: Verify RED**

Run the focused suite. Expected: all repeated and high-volume records are
currently captured.

- [ ] **Step 3: Implement mutex-protected throttling**

Under `_state_mutex`, track:

```foundryscript
var _error_timepoints: Dictionary = {}
var _event_timepoints: Array[int] = []
var _current_frame: int = -1
var _frame_event_count: int = 0
```

Duplicate identity is `JSON.stringify([message, file, line, error_type])`.
Prune sliding-window timestamps on every error, reset the frame counter when
the frame changes, clear the identity table above 100 entries, and record the
duplicate timestamp only if any destination is selected. Release the mutex
before provider delivery.

Add `reset()` and `reconfigure(config)` methods.

- [ ] **Step 4: Write failing lifecycle, integration, and recursion tests**

Test private logger presence through observable engine output:

```foundryscript
func test_successful_configuration_registers_real_engine_logger() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
		))).to_equal(Error.OK)

	Expect.that(func() -> void: push_error("automatic integration")).to_push_error(
			"automatic integration")
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("automatic integration")
	service.shutdown()
```

Also verify disabled configuration installs nothing, failed replacement keeps
the previous logger, same-provider reconfiguration resets limits, replacement
does not duplicate logger registration, shutdown removes it, and a reentrant
provider-triggered `push_error` produces no recursive second event.

- [ ] **Step 5: Verify RED**

Run the focused suite. Expected: direct logger tests pass but real engine
output is not captured and recursion/lifecycle assertions fail.

- [ ] **Step 6: Implement logger lifecycle**

Add a nullable `_automatic_logger`. After successful configuration:

- reuse/reconfigure it for the same active provider;
- remove the old logger before provider replacement shutdown;
- install a new logger only when `enabled && automatic_capture_enabled`;
- remove before flush/shutdown;
- leave the existing logger untouched on failed candidate configuration.

Use private `_install_automatic_logger`, `_remove_automatic_logger`, and
`_refresh_automatic_logger` helpers. Ensure `OS.add_logger`/`remove_logger`
are each called exactly once per state transition.

- [ ] **Step 7: Run focused tests and verify GREEN**

Run the test project twice to detect leaked/duplicate logger state. Expected:
all tests pass both times.

- [ ] **Step 8: Commit throttling and lifecycle**

```sh
git add addons/FoundryObservability test_project/tests
git commit -m "feat: install and throttle automatic capture"
```

### Task 5: Route breadcrumbs through the Sentry provider

**Files:**

- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/breadcrumbless_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/breadcrumbless_sentry_bridge.notest.fs.uid`
- Test: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Write failing Sentry provider tests**

```foundryscript
func test_routes_breadcrumbs_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var breadcrumb := ObservabilityBreadcrumb.new(
			p_message = "warning",
			p_level = ObservabilityLevel.WARN,
			p_category = &"error",
			p_timestamp_msec = 1234,
			p_attributes = {"error.file": "res://player.fs"},
		)
	Expect.that(provider.capture_breadcrumb(breadcrumb)).to_be_true()
	Expect.that(bridge.captured_breadcrumb_payloads[0]["category"]).to_equal("error")
	Expect.that(bridge.captured_breadcrumb_payloads[0]["timestamp_msec"]).to_equal(1234)
	provider.shutdown()
```

Add:

```foundryscript
func test_missing_native_breadcrumb_method_does_not_break_events() -> void:
	var provider := SentryObservabilityProvider.new(
			p_bridge = BreadcrumblessSentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "unsupported"))).to_be_false()
	Expect.that(provider.capture(
			ObservabilityEvent.new(p_message = "still works"))).to_equal("sentry:1")
	provider.shutdown()
```

- [ ] **Step 2: Verify RED**

Run the test project and expect missing provider/bridge methods.

- [ ] **Step 3: Implement Sentry breadcrumb forwarding**

Add `ObservabilityBreadcrumbsProvider` to the provider's `uses` list. Implement
`capture_breadcrumb` with enabled/shutdown/availability guards, method
capability detection, normalized payload copying, strict boolean return
validation, and no event fallback.

Extend the fake bridge with `captured_breadcrumb_payloads` and a boolean
`captureBreadcrumb` method.

- [ ] **Step 4: Verify GREEN and commit**

Run focused tests, then:

```sh
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests
git commit -m "feat: forward breadcrumbs to Sentry bridges"
```

### Task 6: Deliver breadcrumbs through Sentry Cocoa

**Files:**

- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryEventMapper.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryEventMapperTests.swift`
- Modify: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Write failing Swift mapper tests**

Add tests for normalized level mapping and merged data precedence:

```swift
func testBreadcrumbDataPreservesFieldsAndReservedMetadata() {
    let data = sentryBreadcrumbData(
        global: ["build": 42, "error.file": "global"],
        breadcrumb: ["error.file": "res://player.fs"],
        timestampMsec: 1234
    )
    XCTAssertEqual(data["build"] as? Int, 42)
    XCTAssertEqual(data["error.file"] as? String, "res://player.fs")
    XCTAssertEqual(data["foundry.timestamp_msec"] as? Int64, 1234)
}
```

Add:

```swift
func testMapsBreadcrumbLevels() {
    XCTAssertEqual(sentryLevel(for: 10), .debug)
    XCTAssertEqual(sentryLevel(for: 20), .debug)
    XCTAssertEqual(sentryLevel(for: 30), .info)
    XCTAssertEqual(sentryLevel(for: 40), .warning)
    XCTAssertEqual(sentryLevel(for: 50), .error)
    XCTAssertEqual(sentryLevel(for: 60), .fatal)
    XCTAssertEqual(sentryLevel(for: 999), .error)
}
```

- [ ] **Step 2: Verify RED**

Run `swift test`; expect missing breadcrumb mapper helpers.

- [ ] **Step 3: Add mapper helpers and bridge method**

Create pure helpers for level and data conversion. Add:

```swift
@Callable
func captureBreadcrumb(payload: VariantDictionary) -> Bool {
    guard isAvailable() else { return false }
    let values = foundationDictionary(from: payload)
    let breadcrumb = Breadcrumb()
    breadcrumb.message = stringValue(values["message"])
    breadcrumb.category = stringValue(values["category"])
    breadcrumb.level = sentryLevel(for: intValue(values["level"]))
    breadcrumb.timestamp = Date()
    breadcrumb.data = sentryBreadcrumbData(
        global: globalAttributes,
        breadcrumb: dictionaryValue(values["attributes"]),
        timestampMsec: Int64(intValue(values["timestamp_msec"]))
    )
    SentrySDK.addBreadcrumb(crumb: breadcrumb)
    return true
}
```

Use the exact Sentry Cocoa 9.23.0 signatures confirmed by compilation.

- [ ] **Step 4: Extend the iOS contract**

Require `captureBreadcrumb`, `Breadcrumb`, `SentrySDK.addBreadcrumb`, level
mapping, and reserved timestamp metadata in
`scripts/test-sentry-ios-build-contract`.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```sh
task test:sentry-swift
task test:sentry-contract
```

Then commit the Swift bridge and tests.

### Task 7: Deliver breadcrumbs through Sentry Android

**Files:**

- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryBreadcrumbMapper.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryBreadcrumbMapperTest.java`
- Modify: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Write failing mapper tests**

Add:

```java
@Test
public void mapsAllBreadcrumbLevels() {
  assertEquals(SentryLevel.DEBUG, SentryBreadcrumbMapper.sentryLevel(10));
  assertEquals(SentryLevel.DEBUG, SentryBreadcrumbMapper.sentryLevel(20));
  assertEquals(SentryLevel.INFO, SentryBreadcrumbMapper.sentryLevel(30));
  assertEquals(SentryLevel.WARNING, SentryBreadcrumbMapper.sentryLevel(40));
  assertEquals(SentryLevel.ERROR, SentryBreadcrumbMapper.sentryLevel(50));
  assertEquals(SentryLevel.FATAL, SentryBreadcrumbMapper.sentryLevel(60));
  assertEquals(SentryLevel.ERROR, SentryBreadcrumbMapper.sentryLevel(999));
}

@Test
public void mergesBreadcrumbDataWithReservedTimestampLast() {
  Map<String, Object> result = SentryBreadcrumbMapper.mergedData(
      Map.of("shared", "global", "build", 42L),
      Map.of("shared", "breadcrumb", "foundry.timestamp_msec", -1L),
      1234L);
  assertEquals("breadcrumb", result.get("shared"));
  assertEquals(42L, result.get("build"));
  assertEquals(1234L, result.get("foundry.timestamp_msec"));
}
```

- [ ] **Step 2: Verify RED**

Run the Gradle unit tests and expect missing mapper/bridge methods.

- [ ] **Step 3: Implement Android mapping and bridge delivery**

Map to `io.sentry.Breadcrumb`, `SentryLevel`, message, category, timestamp, and
scalar data. Add a `captureBreadcrumb` callable that returns false while
unavailable and true after `Sentry.addBreadcrumb`.

Keep mapping in `SentryBreadcrumbMapper` so unit tests do not require a live
Sentry hub.

- [ ] **Step 4: Extend the Android contract**

Require mapper source/tests and `captureBreadcrumb`/`Sentry.addBreadcrumb`
symbols.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```sh
task test:sentry-android-contract
(cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry && ./gradlew test)
```

Commit the Android bridge and tests.

### Task 8: Document and verify the completed integration

**Files:**

- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update public documentation**

Document:

- automatic-on-after-configure lifecycle;
- event/breadcrumb/log masks and exact defaults;
- category mapping;
- preserved source/backtrace metadata;
- repeated/per-frame/sliding limits and zero-value behavior;
- recursion and prefix filtering;
- optional breadcrumb-provider behavior;
- macOS/iOS/Android support;
- an example that opts automatic structured logs in.

- [ ] **Step 2: Run formatting and focused gates**

```sh
git diff --check
task lint
task test:project
task test:foundry-script
task test:sentry-swift
task test:sentry-contract
task test:sentry-android-contract
```

Expected: every command exits zero with no new warnings.

- [ ] **Step 3: Run the full validation gate**

```sh
task test
```

Expected: all repository validation passes.

- [ ] **Step 4: Self-review issue coverage**

Verify the diff explicitly covers capture, independent filters, source
metadata, recursion, throttling, disabled state, and all three target
platforms. Confirm no generated build output or `dist/` files are tracked.

- [ ] **Step 5: Commit documentation/final adjustments**

```sh
git add README.md docs/API.md CHANGELOG.md
git commit -m "docs: explain automatic engine capture"
```

- [ ] **Step 6: Run independent review and address findings**

Use the repository's requested review workflow, rerun affected tests after
every fix, and finish with another full `task test`.
