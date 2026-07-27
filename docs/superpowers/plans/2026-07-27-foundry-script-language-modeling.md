# Foundry Script Language Modeling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace undermodeled Foundry Script seams with typed callables, traits, immutable generic results, focused configurations, typed Sentry boundaries, and cohesive namespaces without preserving source compatibility.

**Architecture:** Keep `FoundryObservability` as the synchronous public facade, inject one `ObservabilityRuntime`, and delegate normalization, processing, and provider lifecycle to focused collaborators. Use exact callables for functional processors, traits for object-shaped dependencies, typed immutable values for outcomes, and dictionaries only for heterogeneous payload data or native wire values.

**Tech Stack:** Foundry Script, Foundry traits/generics/named enums/typed callables/namespaces, FoundryLib testlib, Bash source contracts, Swift and Java native bridge contract tests.

---

## File structure

### Core public root

- `addons/FoundryObservability/ObservabilityConfig.fs`: immutable root configuration aggregate.
- `addons/FoundryObservability/ObservabilityProcessingConfig.fs`: processor, filter, redaction, sampling, and limit configuration.
- `addons/FoundryObservability/ObservabilityAutomaticCaptureConfig.fs`: engine logger routing configuration.
- `addons/FoundryObservability/ObservabilityAttachmentConfig.fs`: attachment configuration.
- `addons/FoundryObservability/ObservabilityStackTraceConfig.fs`: stack-trace configuration.
- `addons/FoundryObservability/ObservabilityMobileDiagnosticsConfig.fs`: hang/ANR configuration.
- `addons/FoundryObservability/FoundryObservability.fs`: thin synchronous facade and startup composition.
- `addons/FoundryObservability/FoundryObservabilityApi.fs`: exact public contract.
- `addons/FoundryObservability/AutomaticObservabilityLogger.fs`: engine callback translation only.

### Runtime namespace

- `addons/FoundryObservability/runtime/ObservabilityRuntime.fs`: injected runtime trait.
- `addons/FoundryObservability/runtime/SystemObservabilityRuntime.fs`: engine-backed implementation.

### Processing namespace

- `addons/FoundryObservability/processing/ObservabilitySignal.fs`: closed signal enum.
- `addons/FoundryObservability/processing/ObservabilityProcessingOutcome.fs`: closed outcome enum.
- `addons/FoundryObservability/processing/ObservabilityProcessingReason.fs`: closed reason enum.
- `addons/FoundryObservability/processing/ObservabilityLimitKind.fs`: closed limit enum.
- `addons/FoundryObservability/processing/ObservabilityAdmissionDecision.fs`: typed limiter result.
- `addons/FoundryObservability/processing/ObservabilityRedactionResult.fs`: generic redaction result.
- `addons/FoundryObservability/processing/ObservabilityProcessingResult.fs`: generic pipeline result.
- `addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs`: generic normalization result.
- `addons/FoundryObservability/processing/ObservabilityProcessingLease.fs`: internal immutable operation snapshot.
- `addons/FoundryObservability/processing/ObservabilityValueVisitDecision.fs`: typed heterogeneous traversal decision.
- `addons/FoundryObservability/processing/ObservabilityValuePolicy.fs`: domain-specific traversal policy trait.
- `addons/FoundryObservability/processing/ObservabilityValueWalker.fs`: shared bounded/cycle-safe traversal mechanics.
- `addons/FoundryObservability/processing/ObservabilitySignalLimiter.fs`: typed admission state.
- `addons/FoundryObservability/processing/ObservabilityRedactor.fs`: typed redaction mapping.
- `addons/FoundryObservability/processing/ObservabilityProcessingPipeline.fs`: processing coordinator.
- `addons/FoundryObservability/processing/ObservabilityNormalizer.fs`: event, metric, feedback, and exception normalization.
- `addons/FoundryObservability/processing/ObservabilityProviderSnapshot.fs`: immutable provider generation snapshot.
- `addons/FoundryObservability/processing/ObservabilityProviderCall.fs`: typed in-flight provider call.
- `addons/FoundryObservability/processing/ObservabilityProviderSession.fs`: provider lifecycle and concurrency owner.

Existing public `ObservabilityProcessingDiagnostic`,
`ObservabilityRedactionPolicy`, `ObservabilityRedactionRule`, and
`ObservabilitySignalLimits` resources remain in the public root namespace.

### Sentry namespace

- `addons/FoundryObservabilitySentry/SentryRuntimeSnapshot.fs`: typed stable/volatile runtime facts.
- `addons/FoundryObservabilitySentry/SentryRuntimeContextSource.fs`: runtime-context source trait.
- `addons/FoundryObservabilitySentry/SystemSentryRuntimeContextSource.fs`: engine-backed source.
- `addons/FoundryObservabilitySentry/SentryAttachmentSource.fs`: attachment source trait.
- `addons/FoundryObservabilitySentry/SystemSentryAttachmentSource.fs`: engine-backed source.
- `addons/FoundryObservabilitySentry/SentryAttachmentCollection.fs`: typed collector result.
- `addons/FoundryObservabilitySentry/SentryNativeBridge.fs`: native bridge trait.
- `addons/FoundryObservabilitySentry/DynamicSentryNativeBridgeAdapter.fs`: sole dynamic native boundary.
- `addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs`: snapshot-to-wire mapper.
- `addons/FoundryObservabilitySentry/SentryBuiltInAttachmentCollector.fs`: typed attachment collector.
- `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`: provider depending only on typed traits.

The old `SentryRuntimeContextProbe.fs` and `SentryAttachmentRuntimeProbe.fs` are replaced, not retained as aliases.

### Tests and contracts

- `test_project/tests/support/fake_observability_runtime.notest.fs`: deterministic runtime trait fake.
- `test_project/tests/observability-core.test.fs`: core behavior and typed model tests.
- `test_project/tests/observability-sentry.test.fs`: typed Sentry source, adapter, and provider tests.
- `test_project/tests/support/fake_sentry_native_bridge.notest.fs`: full typed bridge fake.
- `scripts/test-foundry-script`: source-layout, typing, dynamic-boundary, documentation, and API contracts.
- `scripts/test-package`: recursive namespace directories and source/UID packaging contracts.
- `README.md`, `docs/API.md`, `BUILD.md`, `CONTRIBUTING.md`, `CHANGELOG.md`: hard-cut public documentation.

## Stable UID allocation

Create these companions with the exact contents shown. Existing moved resources keep their current UIDs.

```text
runtime/ObservabilityRuntime.fs.uid                         uid://d14runtime001
runtime/SystemObservabilityRuntime.fs.uid                   uid://d14runtime002
ObservabilityProcessingConfig.fs.uid                       uid://d14config001
ObservabilityAutomaticCaptureConfig.fs.uid                  uid://d14config002
ObservabilityAttachmentConfig.fs.uid                        uid://d14config003
ObservabilityStackTraceConfig.fs.uid                        uid://d14config004
ObservabilityMobileDiagnosticsConfig.fs.uid                 uid://d14config005
processing/ObservabilitySignal.fs.uid                       uid://d14process001
processing/ObservabilityProcessingOutcome.fs.uid            uid://d14process002
processing/ObservabilityProcessingReason.fs.uid             uid://d14process003
processing/ObservabilityLimitKind.fs.uid                    uid://d14process004
processing/ObservabilityAdmissionDecision.fs.uid            uid://d14process005
processing/ObservabilityRedactionResult.fs.uid              uid://d14process006
processing/ObservabilityProcessingResult.fs.uid             uid://d14process007
processing/ObservabilityProcessingLease.fs.uid              uid://d14process008
processing/ObservabilityValueVisitDecision.fs.uid           uid://d14process009
processing/ObservabilityValuePolicy.fs.uid                  uid://d14process010
processing/ObservabilityValueWalker.fs.uid                  uid://d14process011
processing/ObservabilityNormalizer.fs.uid                   uid://d14process012
processing/ObservabilityProviderSnapshot.fs.uid             uid://d14process013
processing/ObservabilityProviderCall.fs.uid                 uid://d14process014
processing/ObservabilityProviderSession.fs.uid              uid://d14process015
processing/ObservabilityNormalizationResult.fs.uid          uid://d14process016
SentryRuntimeSnapshot.fs.uid                                uid://d14sentry001
SentryRuntimeContextSource.fs.uid                           uid://d14sentry002
SystemSentryRuntimeContextSource.fs.uid                     uid://d14sentry003
SentryAttachmentSource.fs.uid                               uid://d14sentry004
SystemSentryAttachmentSource.fs.uid                         uid://d14sentry005
SentryAttachmentCollection.fs.uid                           uid://d14sentry006
SentryNativeBridge.fs.uid                                   uid://d14sentry007
DynamicSentryNativeBridgeAdapter.fs.uid                     uid://d14sentry008
tests/support/fake_observability_runtime.notest.fs.uid       uid://d14testcore01
tests/support/fake_sentry_native_bridge.notest.fs.uid        uid://d14testsentry1
```

### Task 1: Introduce the runtime trait and deterministic fake

**Files:**

- Create: `addons/FoundryObservability/runtime/ObservabilityRuntime.fs`
- Create: `addons/FoundryObservability/runtime/ObservabilityRuntime.fs.uid`
- Create: `addons/FoundryObservability/runtime/SystemObservabilityRuntime.fs`
- Create: `addons/FoundryObservability/runtime/SystemObservabilityRuntime.fs.uid`
- Create: `test_project/tests/support/fake_observability_runtime.notest.fs`
- Create: `test_project/tests/support/fake_observability_runtime.notest.fs.uid`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write the failing runtime tests**

Add these tests to `observability-core.test.fs`:

```foundryscript
func test_fake_observability_runtime_exposes_deterministic_sources() -> void:
	var runtime := FakeObservabilityRuntime.new(
			101,
			202,
			303,
			404,
			505,
		)

	Expect.that(runtime.monotonic_time_msec()).to_equal(101)
	Expect.that(runtime.unix_time_msec()).to_equal(202)
	Expect.that(runtime.process_frame()).to_equal(303)
	Expect.that(runtime.caller_id()).to_equal(404)
	Expect.that(runtime.main_thread_id()).to_equal(505)
	Expect.that(runtime is ObservabilityRuntime).to_be_true()


func test_system_observability_runtime_has_sane_units() -> void:
	var runtime: ObservabilityRuntime = SystemObservabilityRuntime.new()

	Expect.that(runtime.monotonic_time_msec()).to_be_greater_than_or_equal(0)
	Expect.that(runtime.unix_time_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(runtime.process_frame()).to_be_greater_than_or_equal(0)
	Expect.that(runtime.caller_id()).to_be_greater_than_or_equal(0)
	Expect.that(runtime.main_thread_id()).to_be_greater_than_or_equal(0)
```

- [ ] **Step 2: Run the test to verify RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing `FakeObservabilityRuntime`, `ObservabilityRuntime`, and `SystemObservabilityRuntime`.

- [ ] **Step 3: Add the runtime trait**

Create `runtime/ObservabilityRuntime.fs`:

```foundryscript
namespace foundry.observability.runtime

## Supplies all engine runtime facts used by provider-neutral observability.
trait_name ObservabilityRuntime

## Returns monotonic engine time in milliseconds.
abstract func monotonic_time_msec() -> int
## Returns Unix epoch time in milliseconds.
abstract func unix_time_msec() -> int
## Returns the current processed frame index.
abstract func process_frame() -> int
## Returns the current execution owner identifier.
abstract func caller_id() -> int
## Returns the engine main-thread identifier.
abstract func main_thread_id() -> int
```

Create `runtime/SystemObservabilityRuntime.fs`:

```foundryscript
namespace foundry.observability.runtime

## Engine-backed production observability runtime.
final class_name SystemObservabilityRuntime extends RefCounted
uses ObservabilityRuntime


func monotonic_time_msec() -> int:
	return Time.get_ticks_msec()


func unix_time_msec() -> int:
	return floori(Time.get_unix_time_from_system() * 1000.0)


func process_frame() -> int:
	return Engine.get_process_frames()


func caller_id() -> int:
	return OS.get_thread_caller_id()


func main_thread_id() -> int:
	return OS.get_main_thread_id()
```

Add the allocated UID companions.

- [ ] **Step 4: Add the deterministic fake**

Create `test_project/tests/support/fake_observability_runtime.notest.fs`:

```foundryscript
namespace foundry.observability.tests

import foundry.observability.runtime

## Mutable deterministic runtime for core tests.
class_name FakeObservabilityRuntime extends RefCounted
uses ObservabilityRuntime

var monotonic_msec: int
var unix_msec: int
var frame: int
var caller: int
var main_thread: int


func _init(
		p_monotonic_msec: int = 0,
		p_unix_msec: int = 0,
		p_frame: int = 0,
		p_caller: int = 0,
		p_main_thread: int = 0,
) -> void:
	monotonic_msec = p_monotonic_msec
	unix_msec = p_unix_msec
	frame = p_frame
	caller = p_caller
	main_thread = p_main_thread


func monotonic_time_msec() -> int:
	return monotonic_msec


func unix_time_msec() -> int:
	return unix_msec


func process_frame() -> int:
	return frame


func caller_id() -> int:
	return caller


func main_thread_id() -> int:
	return main_thread
```

Add its allocated UID companion and add `import foundry.observability.runtime` to the core test.

- [ ] **Step 5: Run the tests to verify GREEN**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: the Foundry Script contract and project tests pass; whitespace check prints nothing.

- [ ] **Step 6: Stage UID resources and verify UID coverage**

Run:

```bash
git add addons/FoundryObservability/runtime test_project/tests/support/fake_observability_runtime.notest.fs*
./scripts/test-foundry-uids
```

Expected: `Foundry UID contract checks passed`.

- [ ] **Step 7: Commit**

```bash
git add test_project/tests/observability-core.test.fs
git commit -m "feat: add typed observability runtime"
```

### Task 2: Replace core clock, frame, and owner callables

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/AutomaticObservabilityLogger.fs`
- Modify: `addons/FoundryObservability/ObservabilityProcessingPipeline.fs`
- Modify: `test_project/tests/observability-core.test.fs`
- Delete: `test_project/tests/support/automatic_capture_time.notest.fs`
- Delete: `test_project/tests/support/automatic_capture_time.notest.fs.uid`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Write failing shared-runtime tests**

Replace callable-clock test setup with:

```foundryscript
func test_processing_pipeline_uses_injected_runtime_for_admission() -> void:
	var runtime := FakeObservabilityRuntime.new(10, 20, 30, 40, 50)
	var pipeline := ObservabilityProcessingPipeline.new(runtime)
	var config := _ordinary_generation_config(true)
	Expect.that(pipeline.configure(config)).to_equal(Error.OK)

	var first := pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"message",
			p_message = "one",
		))
	runtime.monotonic_msec = 11
	runtime.frame = 31
	var second := pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"message",
			p_message = "two",
		))

	Expect.that(first["accepted"]).to_be_true()
	Expect.that(second["accepted"]).to_be_true()


func test_service_resolves_both_capture_times_from_one_runtime() -> void:
	var runtime := FakeObservabilityRuntime.new(123, 456, 7, 8, 8)
	var service := FoundryObservability.new(
			ObservabilityStartupSettings.new(),
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs",
			runtime,
		)
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, _ordinary_generation_config(true))).to_equal(Error.OK)

	Expect.that(service.capture_message("timed")).to_equal("memory:1")
	var event: ObservabilityEvent = provider.events()[0]
	Expect.that(event.timestamp_msec()).to_equal(456)
	Expect.that(event.engine_ticks_msec()).to_equal(123)
	service.shutdown()
	service.free()
```

Update automatic logger tests to pass the same fake runtime and assert its `monotonic_msec` rather than invoking `AutomaticCaptureTime.now`.

- [ ] **Step 2: Add the failing source contract**

Append to `scripts/test-foundry-script`:

```bash
if rg -n '\b(_processing_clock|_processing_frame|_clock: Callable|_frame: Callable|_owner: Callable)\b' \
	"$addon/FoundryObservability.fs" \
	"$addon/AutomaticObservabilityLogger.fs" \
	"$addon/ObservabilityProcessingPipeline.fs"; then
	fail "core runtime access must use ObservabilityRuntime"
fi
if rg -n '_frame_supplier' "$addon/AutomaticObservabilityLogger.fs"; then
	fail "automatic logger retains the unused frame supplier"
fi
```

- [ ] **Step 3: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because constructors still accept callables and the source contract finds callable runtime fields.

- [ ] **Step 4: Migrate the processing pipeline constructor**

Add:

```foundryscript
import foundry.observability.runtime

var _runtime: ObservabilityRuntime


func _init(runtime: ObservabilityRuntime) -> void:
	assert(runtime != null, "ObservabilityProcessingPipeline requires a runtime.")
	_runtime = runtime
```

Replace `_now_msec(clock)`, `_frame_index(frame)`, and `_owner_id()` with:

```foundryscript
func _now_msec() -> int:
	return _runtime.monotonic_time_msec()


func _frame_index() -> int:
	return _runtime.process_frame()


func _owner_id() -> int:
	return _runtime.caller_id()
```

Remove runtime callables from snapshot dictionaries and call these methods from `_admit`.

- [ ] **Step 5: Migrate the facade and logger**

Change the facade constructor tail to:

```foundryscript
		runtime: ObservabilityRuntime? = null,
) -> void:
	_runtime = runtime if runtime != null else SystemObservabilityRuntime.new()
```

Add:

```foundryscript
var _runtime: ObservabilityRuntime
```

Construct every pipeline as `ObservabilityProcessingPipeline.new(_runtime)`. Resolve capture times with:

```foundryscript
var capture_engine_ticks_msec: int = _runtime.monotonic_time_msec()
var capture_unix_msec: int = _runtime.unix_time_msec()
```

Use `_runtime.caller_id()` in state failure scopes and automatic capture ownership.

Change the logger constructor to:

```foundryscript
func _init(
		service: FoundryObservability,
		config: ObservabilityConfig,
		runtime: ObservabilityRuntime,
) -> void:
	assert(runtime != null, "AutomaticObservabilityLogger requires a runtime.")
	_service = service
	_config = config
	_runtime = runtime
```

Use `_runtime.monotonic_time_msec()` and delete `_now_msec()`, `_clock`, and `_frame_supplier`.

- [ ] **Step 6: Remove obsolete test seams and update all constructor calls**

Delete `automatic_capture_time.notest.fs*`. Replace every processing/service/logger callable constructor argument with a shared `FakeObservabilityRuntime`. Remove `_service_processing_clock`, `_service_processing_frame`, `_processing_clock`, `_processing_frame`, and their call counters from the core test.

- [ ] **Step 7: Verify GREEN**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all commands pass; no runtime callable seam is reported.

- [ ] **Step 8: Commit**

```bash
git add addons/FoundryObservability test_project/tests scripts/test-foundry-script
git commit -m "refactor: inject core observability runtime"
```

### Task 3: Add named processing enums and typed admission decisions

**Files:**

- Create: `addons/FoundryObservability/processing/ObservabilitySignal.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProcessingOutcome.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProcessingReason.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityLimitKind.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityAdmissionDecision.fs`
- Create: UID companions for all five files
- Modify: `addons/FoundryObservability/ObservabilitySignalLimiter.fs`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing typed-admission tests**

Add:

```foundryscript
func test_signal_limiter_returns_typed_admission_decisions() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0,
			ObservabilitySignalLimits.new(1, 0, 0, 0),
		)

	var accepted: ObservabilityAdmissionDecision = limiter.admit("one", 10, 1)
	var dropped: ObservabilityAdmissionDecision = limiter.admit("two", 10, 1)

	Expect.that(accepted.accepted()).to_be_true()
	Expect.that(accepted.reason()).to_equal(ObservabilityProcessingReason.NONE)
	Expect.that(accepted.limit_kind()).to_equal(ObservabilityLimitKind.NONE)
	Expect.that(dropped.accepted()).to_be_false()
	Expect.that(dropped.reason()).to_equal(ObservabilityProcessingReason.RATE_LIMITED)
	Expect.that(dropped.limit_kind()).to_equal(ObservabilityLimitKind.PER_FRAME)
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing processing enums and `ObservabilityAdmissionDecision`.

- [ ] **Step 3: Create the named enum files**

Create `ObservabilitySignal.fs`:

```foundryscript
namespace foundry.observability.processing

## Closed provider-neutral signal families.
enum_name ObservabilitySignal:
	EVENT = 0
	LOG = 1
	METRIC = 2
	STATE = 3
```

Create `ObservabilityProcessingOutcome.fs`:

```foundryscript
namespace foundry.observability.processing

## Closed processing outcomes.
enum_name ObservabilityProcessingOutcome:
	ACCEPTED = 0
	DROPPED = 1
	FAILED = 2
```

Create `ObservabilityProcessingReason.fs`:

```foundryscript
namespace foundry.observability.processing

## Stable payload-free processing reasons.
enum_name ObservabilityProcessingReason:
	NONE = 0
	PROCESSOR = 1
	SAMPLED = 2
	RATE_LIMITED = 3
	RECURSIVE = 4
	INVALID_PROCESSOR_RESULT = 5
	REDACTION_FAILED = 6
	INVALID_PAYLOAD = 7
	PROVIDER_REJECTED = 8
	STALE_GENERATION = 9
```

Create `ObservabilityLimitKind.fs`:

```foundryscript
namespace foundry.observability.processing

## Stable signal admission limit families.
enum_name ObservabilityLimitKind:
	NONE = 0
	PER_FRAME = 1
	REPEATED = 2
	WINDOW = 3
	LEGACY_LOG_WINDOW = 4
```

- [ ] **Step 4: Create the immutable admission value**

Create `ObservabilityAdmissionDecision.fs`:

```foundryscript
namespace foundry.observability.processing

## Immutable payload-free admission decision.
final class_name ObservabilityAdmissionDecision extends RefCounted

final var _accepted: bool
final var _reason: ObservabilityProcessingReason
final var _limit_kind: ObservabilityLimitKind


func _init(
		accepted: bool,
		reason: ObservabilityProcessingReason,
		limit_kind: ObservabilityLimitKind,
) -> void:
	_accepted = accepted
	_reason = reason
	_limit_kind = limit_kind


static func accepted_decision() -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.new(
			true,
			ObservabilityProcessingReason.NONE,
			ObservabilityLimitKind.NONE,
		)


static func dropped(
		reason: ObservabilityProcessingReason,
		limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
) -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.new(false, reason, limit_kind)


func accepted() -> bool:
	return _accepted


func reason() -> ObservabilityProcessingReason:
	return _reason


func limit_kind() -> ObservabilityLimitKind:
	return _limit_kind
```

Add the allocated UID companions and import `foundry.observability.processing` where required.

- [ ] **Step 5: Migrate the limiter**

Change `admit()` and `_dropped()` signatures:

```foundryscript
func admit(
		identity: String,
		now_msec: int,
		frame_index: int,
) -> ObservabilityAdmissionDecision:
```

Map each branch to the matching enums and finish with:

```foundryscript
return ObservabilityAdmissionDecision.accepted_decision()
```

Use:

```foundryscript
func _dropped(
		reason: ObservabilityProcessingReason,
		limit_kind: ObservabilityLimitKind,
) -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.dropped(reason, limit_kind)
```

- [ ] **Step 6: Update pipeline consumers**

Use:

```foundryscript
var admission: ObservabilityAdmissionDecision = limiter.admit(
		identity,
		_now_msec(),
		_frame_index(),
	)
if not admission.accepted():
	return _finish_drop(
			snapshot,
			p_signal,
			admission.reason(),
			-1,
			-1,
			admission.limit_kind(),
			Error.OK,
		)
```

Temporarily convert enum diagnostics to existing `StringName` values in one mapping function until Task 7 migrates the diagnostic class.

- [ ] **Step 7: Run GREEN and UID checks**

Run:

```bash
git add addons/FoundryObservability/processing
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all commands pass.

- [ ] **Step 8: Commit**

```bash
git add addons/FoundryObservability/ObservabilitySignalLimiter.fs test_project/tests/observability-core.test.fs
git commit -m "refactor: type signal admission decisions"
```

### Task 4: Add generic redaction and processing result values

**Files:**

- Create: `addons/FoundryObservability/processing/ObservabilityRedactionResult.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityRedactionResult.fs.uid`
- Create: `addons/FoundryObservability/processing/ObservabilityProcessingResult.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProcessingResult.fs.uid`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing generic result tests**

Add:

```foundryscript
func test_redaction_result_preserves_typed_success_and_failure() -> void:
	var event := ObservabilityEvent.new(p_kind = &"message", p_message = "safe")
	var success: ObservabilityRedactionResult[ObservabilityEvent] = (
			ObservabilityRedactionResult[ObservabilityEvent].success(event)
		)
	var failure: ObservabilityRedactionResult[ObservabilityEvent] = (
			ObservabilityRedactionResult[ObservabilityEvent].failure(3)
		)

	Expect.that(success.valid()).to_be_true()
	Expect.that(success.value()).to_equal(event)
	Expect.that(success.failed_rule_index()).to_equal(-1)
	Expect.that(failure.valid()).to_be_false()
	Expect.that(failure.value()).to_equal(null)
	Expect.that(failure.failed_rule_index()).to_equal(3)
	Expect.that(failure.error()).to_equal(Error.ERR_INVALID_DATA)


func test_processing_result_enforces_accepted_and_dropped_shapes() -> void:
	var event := ObservabilityEvent.new(p_kind = &"message", p_message = "accepted")
	var accepted: ObservabilityProcessingResult[ObservabilityEvent] = (
			ObservabilityProcessingResult[ObservabilityEvent].accepted(
					ObservabilitySignal.EVENT,
					event,
					17,
				)
		)
	var dropped: ObservabilityProcessingResult[ObservabilityEvent] = (
			ObservabilityProcessingResult[ObservabilityEvent].dropped(
					ObservabilitySignal.EVENT,
					ObservabilityProcessingReason.SAMPLED,
					ObservabilityLimitKind.NONE,
				)
		)

	Expect.that(accepted.outcome()).to_equal(ObservabilityProcessingOutcome.ACCEPTED)
	Expect.that(accepted.value()).to_equal(event)
	Expect.that(accepted.operation_token()).to_equal(17)
	Expect.that(dropped.outcome()).to_equal(ObservabilityProcessingOutcome.DROPPED)
	Expect.that(dropped.value()).to_equal(null)
	Expect.that(dropped.operation_token()).to_equal(-1)
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing generic result classes.

- [ ] **Step 3: Implement `ObservabilityRedactionResult[T]`**

Create:

```foundryscript
namespace foundry.observability.processing

## Immutable typed redaction outcome.
final class_name ObservabilityRedactionResult[T] extends RefCounted

final var _valid: bool
final var _value: T?
final var _failed_rule_index: int
final var _error: int


func _init(
		valid: bool,
		value: T?,
		failed_rule_index: int,
		error: int,
) -> void:
	_valid = valid
	_value = value
	_failed_rule_index = failed_rule_index
	_error = error


static func success(value: T) -> ObservabilityRedactionResult[T]:
	return ObservabilityRedactionResult[T].new(true, value, -1, Error.OK)


static func failure(
		failed_rule_index: int = -1,
) -> ObservabilityRedactionResult[T]:
	return ObservabilityRedactionResult[T].new(
			false,
			null,
			failed_rule_index,
			Error.ERR_INVALID_DATA,
		)


func valid() -> bool:
	return _valid


func value() -> T?:
	return _value


func failed_rule_index() -> int:
	return _failed_rule_index


func error() -> int:
	return _error
```

- [ ] **Step 4: Implement `ObservabilityProcessingResult[T]`**

Create:

```foundryscript
namespace foundry.observability.processing

## Immutable typed processing outcome.
final class_name ObservabilityProcessingResult[T] extends RefCounted

final var _outcome: ObservabilityProcessingOutcome
final var _signal: ObservabilitySignal
final var _value: T?
final var _operation_token: int
final var _reason: ObservabilityProcessingReason
final var _processor_index: int
final var _redaction_rule_index: int
final var _limit_kind: ObservabilityLimitKind
final var _error: int


func _init(
		outcome: ObservabilityProcessingOutcome,
		signal: ObservabilitySignal,
		value: T?,
		operation_token: int,
		reason: ObservabilityProcessingReason,
		processor_index: int,
		redaction_rule_index: int,
		limit_kind: ObservabilityLimitKind,
		error: int,
) -> void:
	_outcome = outcome
	_signal = signal
	_value = value
	_operation_token = operation_token
	_reason = reason
	_processor_index = processor_index
	_redaction_rule_index = redaction_rule_index
	_limit_kind = limit_kind
	_error = error


static func accepted(
		signal: ObservabilitySignal,
		value: T,
		operation_token: int,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.ACCEPTED,
			signal,
			value,
			operation_token,
			ObservabilityProcessingReason.NONE,
			-1,
			-1,
			ObservabilityLimitKind.NONE,
			Error.OK,
		)


static func dropped(
		signal: ObservabilitySignal,
		reason: ObservabilityProcessingReason,
		limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
		processor_index: int = -1,
		redaction_rule_index: int = -1,
		error: int = Error.OK,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.DROPPED,
			signal,
			null,
			-1,
			reason,
			processor_index,
			redaction_rule_index,
			limit_kind,
			error,
		)


static func failed(
		signal: ObservabilitySignal,
		reason: ObservabilityProcessingReason,
		error: int,
		processor_index: int = -1,
		redaction_rule_index: int = -1,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.FAILED,
			signal,
			null,
			-1,
			reason,
			processor_index,
			redaction_rule_index,
			ObservabilityLimitKind.NONE,
			error,
		)


func outcome() -> ObservabilityProcessingOutcome:
	return _outcome


func signal() -> ObservabilitySignal:
	return _signal


func value() -> T?:
	return _value


func operation_token() -> int:
	return _operation_token


func reason() -> ObservabilityProcessingReason:
	return _reason


func processor_index() -> int:
	return _processor_index


func redaction_rule_index() -> int:
	return _redaction_rule_index


func limit_kind() -> ObservabilityLimitKind:
	return _limit_kind


func error() -> int:
	return _error
```

- [ ] **Step 5: Verify GREEN**

Run:

```bash
git add addons/FoundryObservability/processing/Observability*Result.fs*
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all commands pass and generic nullable result types analyze successfully.

- [ ] **Step 6: Commit**

```bash
git add test_project/tests/observability-core.test.fs
git commit -m "feat: add typed processing result models"
```

### Task 5: Add focused immutable configuration types

**Files:**

- Create: `addons/FoundryObservability/ObservabilityProcessingConfig.fs`
- Create: `addons/FoundryObservability/ObservabilityAutomaticCaptureConfig.fs`
- Create: `addons/FoundryObservability/ObservabilityAttachmentConfig.fs`
- Create: `addons/FoundryObservability/ObservabilityStackTraceConfig.fs`
- Create: `addons/FoundryObservability/ObservabilityMobileDiagnosticsConfig.fs`
- Create: UID companions for all five files
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing focused-configuration tests**

Add:

```foundryscript
func test_focused_configuration_defaults_match_current_behavior() -> void:
	var processing := ObservabilityProcessingConfig.new()
	var automatic := ObservabilityAutomaticCaptureConfig.new()
	var attachments := ObservabilityAttachmentConfig.new()
	var stack_traces := ObservabilityStackTraceConfig.new()
	var mobile := ObservabilityMobileDiagnosticsConfig.new()

	Expect.that(processing.logs_enabled()).to_be_true()
	Expect.that(processing.log_minimum_level()).to_equal(ObservabilityLevel.TRACE)
	Expect.that(processing.metrics_enabled()).to_be_true()
	Expect.that(processing.event_sample_rate()).to_equal(1.0)
	Expect.that(processing.log_sample_rate()).to_equal(1.0)
	Expect.that(processing.metric_sample_rate()).to_equal(1.0)
	Expect.that(processing.metric_filter()).to_equal(null)
	Expect.that(processing.event_processors()).to_have_size(0)
	Expect.that(processing.event_limits().per_frame()).to_equal(5)
	Expect.that(processing.event_limits().repeated_window_msec()).to_equal(1000)
	Expect.that(processing.event_limits().window_count()).to_equal(20)
	Expect.that(processing.event_limits().window_msec()).to_equal(10000)
	Expect.that(automatic.enabled()).to_be_true()
	Expect.that(automatic.event_mask()).to_equal(ObservabilityCaptureMask.DEFAULT_EVENTS)
	Expect.that(automatic.breadcrumb_mask()).to_equal(
			ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
		)
	Expect.that(automatic.log_mask()).to_equal(ObservabilityCaptureMask.NONE)
	Expect.that(automatic.max_breadcrumbs()).to_equal(100)
	Expect.that(attachments.max_bytes()).to_equal(20 * 1024 * 1024)
	Expect.that(attachments.attach_game_log()).to_be_false()
	Expect.that(attachments.attach_screenshot()).to_be_false()
	Expect.that(attachments.attach_scene_tree()).to_be_false()
	Expect.that(stack_traces.source_context_enabled()).to_be_true()
	Expect.that(stack_traces.variables_enabled()).to_be_false()
	Expect.that(mobile.application_hang_detection_enabled()).to_be_true()
	Expect.that(mobile.application_hang_timeout_msec()).to_equal(5000)
	Expect.that(mobile.android_anr_detection_enabled()).to_be_true()
	Expect.that(mobile.android_anr_timeout_msec()).to_equal(5000)
	Expect.that(mobile.android_anr_attach_thread_dump()).to_be_false()


func test_processing_configuration_uses_exact_callable_types_and_copies_arrays() -> void:
	var event_processors: Array[
			Callable[[ObservabilityEvent], ObservabilityEvent?]
		] = [func(event: ObservabilityEvent) -> ObservabilityEvent?: return event]
	var metric_processors: Array[
			Callable[[ObservabilityMetric], ObservabilityMetric?]
		] = [func(metric: ObservabilityMetric) -> ObservabilityMetric?: return metric]
	var metric_filter: Callable[[ObservabilityMetric], bool]? = (
			func(_metric: ObservabilityMetric) -> bool: return true
		)
	var config := ObservabilityProcessingConfig.new(
			p_event_processors = event_processors,
			p_metric_processors = metric_processors,
			p_metric_filter = metric_filter,
		)

	event_processors.clear()
	metric_processors.clear()
	Expect.that(config.event_processors()).to_have_size(1)
	Expect.that(config.metric_processors()).to_have_size(1)
	Expect.that(config.metric_filter()).to_not_equal(null)
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing focused configuration classes.

- [ ] **Step 3: Implement scalar focused configurations**

Create `ObservabilityAttachmentConfig.fs` as a final class with final fields, constructor normalization, and accessors:

```foundryscript
namespace foundry.observability

## Immutable diagnostic attachment configuration.
final class_name ObservabilityAttachmentConfig extends RefCounted

final var _max_bytes: int
final var _attach_game_log: bool
final var _attach_screenshot: bool
final var _attach_scene_tree: bool


func _init(
		p_max_bytes: int = 20 * 1024 * 1024,
		p_attach_game_log: bool = false,
		p_attach_screenshot: bool = false,
		p_attach_scene_tree: bool = false,
) -> void:
	_max_bytes = maxi(0, p_max_bytes)
	_attach_game_log = p_attach_game_log
	_attach_screenshot = p_attach_screenshot
	_attach_scene_tree = p_attach_scene_tree


func max_bytes() -> int:
	return _max_bytes


func attach_game_log() -> bool:
	return _attach_game_log


func attach_screenshot() -> bool:
	return _attach_screenshot


func attach_scene_tree() -> bool:
	return _attach_scene_tree
```

Create `ObservabilityStackTraceConfig.fs` with final booleans `_source_context_enabled` and `_variables_enabled`, constructor defaults `true` and `false`, and same-named accessors.

Create `ObservabilityMobileDiagnosticsConfig.fs` with final fields and this constructor:

```foundryscript
func _init(
		p_application_hang_detection_enabled: bool = true,
		p_application_hang_timeout_msec: int = 5000,
		p_android_anr_detection_enabled: bool = true,
		p_android_anr_timeout_msec: int = 5000,
		p_android_anr_attach_thread_dump: bool = false,
) -> void:
	_application_hang_detection_enabled = p_application_hang_detection_enabled
	_application_hang_timeout_msec = maxi(1000, p_application_hang_timeout_msec)
	_android_anr_detection_enabled = p_android_anr_detection_enabled
	_android_anr_timeout_msec = maxi(1000, p_android_anr_timeout_msec)
	_android_anr_attach_thread_dump = p_android_anr_attach_thread_dump
```

Add accessors with the exact field names without underscores.

- [ ] **Step 4: Implement automatic capture configuration**

Create:

```foundryscript
namespace foundry.observability

## Immutable engine logger routing configuration.
final class_name ObservabilityAutomaticCaptureConfig extends RefCounted

final var _enabled: bool
final var _event_mask: int
final var _breadcrumb_mask: int
final var _log_mask: int
final var _max_breadcrumbs: int
final var _message_filter_prefixes: PackedStringArray


func _init(
		p_enabled: bool = true,
		p_event_mask: int = ObservabilityCaptureMask.DEFAULT_EVENTS,
		p_breadcrumb_mask: int = ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
		p_log_mask: int = ObservabilityCaptureMask.NONE,
		p_max_breadcrumbs: int = 100,
		p_message_filter_prefixes: PackedStringArray = PackedStringArray(
				["FoundryObservability: "],
			),
) -> void:
	_enabled = p_enabled
	_event_mask = p_event_mask
	_breadcrumb_mask = p_breadcrumb_mask
	_log_mask = p_log_mask
	_max_breadcrumbs = maxi(0, p_max_breadcrumbs)
	_message_filter_prefixes = p_message_filter_prefixes.duplicate()


func enabled() -> bool:
	return _enabled


func event_mask() -> int:
	return _event_mask


func breadcrumb_mask() -> int:
	return _breadcrumb_mask


func log_mask() -> int:
	return _log_mask


func max_breadcrumbs() -> int:
	return _max_breadcrumbs


func message_filter_prefixes() -> PackedStringArray:
	return _message_filter_prefixes.duplicate()
```

- [ ] **Step 5: Implement exact callable processing configuration**

Create `ObservabilityProcessingConfig.fs` with:

```foundryscript
namespace foundry.observability

import foundry.observability.processing

## Immutable provider-neutral processing configuration.
final class_name ObservabilityProcessingConfig extends RefCounted

final var _logs_enabled: bool
final var _log_minimum_level: int
final var _log_rate_limit_per_second: int
final var _metrics_enabled: bool
final var _event_sample_rate: float
final var _log_sample_rate: float
final var _metric_sample_rate: float
final var _metric_filter: Callable[[ObservabilityMetric], bool]?
final var _event_processors: Array[
		Callable[[ObservabilityEvent], ObservabilityEvent?]
	]
final var _log_processors: Array[
		Callable[[ObservabilityEvent], ObservabilityEvent?]
	]
final var _metric_processors: Array[
		Callable[[ObservabilityMetric], ObservabilityMetric?]
	]
final var _event_limits: ObservabilitySignalLimits
final var _log_limits: ObservabilitySignalLimits
final var _metric_limits: ObservabilitySignalLimits
final var _redaction_policy: ObservabilityRedactionPolicy
```

Use this constructor:

```foundryscript
func _init(
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0,
		p_metrics_enabled: bool = true,
		p_event_sample_rate: float = 1.0,
		p_log_sample_rate: float = 1.0,
		p_metric_sample_rate: float = 1.0,
		p_metric_filter: Callable[[ObservabilityMetric], bool]? = null,
		p_event_processors: Array[
				Callable[[ObservabilityEvent], ObservabilityEvent?]
			] = [],
		p_log_processors: Array[
				Callable[[ObservabilityEvent], ObservabilityEvent?]
			] = [],
		p_metric_processors: Array[
				Callable[[ObservabilityMetric], ObservabilityMetric?]
			] = [],
		p_event_limits: ObservabilitySignalLimits? = null,
		p_log_limits: ObservabilitySignalLimits? = null,
		p_metric_limits: ObservabilitySignalLimits? = null,
		p_redaction_policy: ObservabilityRedactionPolicy? = null,
) -> void:
```

The body clamps only the fixed log rate to zero, copies each typed processor array with a typed loop, duplicates limits/policy, and uses these defaults:

```foundryscript
ObservabilitySignalLimits.new(5, 1000, 20, 10000)
ObservabilitySignalLimits.new()
ObservabilitySignalLimits.new()
ObservabilityRedactionPolicy.new()
```

Add exact scalar accessors and defensive-copy accessors for processor arrays, limits, and redaction policy.

- [ ] **Step 6: Add UIDs and run GREEN**

Run:

```bash
git add addons/FoundryObservability/Observability*Config.fs*
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all focused configuration tests pass.

- [ ] **Step 7: Commit**

```bash
git add test_project/tests/observability-core.test.fs
git commit -m "feat: add focused observability configuration"
```

### Task 6: Hard-cut the root configuration aggregate

**Files:**

- Rewrite: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `addons/FoundryObservability/ObservabilityStartupSettings.fs`
- Modify: all first-party `.fs` call sites of `ObservabilityConfig`
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Add failing aggregate and source-contract tests**

Add:

```foundryscript
func test_root_configuration_is_an_immutable_aggregate() -> void:
	var processing := ObservabilityProcessingConfig.new(p_metrics_enabled = false)
	var automatic := ObservabilityAutomaticCaptureConfig.new(p_max_breadcrumbs = 7)
	var attachments := ObservabilityAttachmentConfig.new(p_attach_game_log = true)
	var stack_traces := ObservabilityStackTraceConfig.new(p_variables_enabled = true)
	var mobile := ObservabilityMobileDiagnosticsConfig.new(
			p_android_anr_timeout_msec = 6400,
		)
	var config := ObservabilityConfig.new(
			p_enabled = true,
			p_environment = "production",
			p_release = "game@1",
			p_dist = "arm64",
			p_global_attributes = {"region": "iad"},
			p_provider_options = {"dsn": "test"},
			p_processing = processing,
			p_automatic_capture = automatic,
			p_attachments = attachments,
			p_stack_traces = stack_traces,
			p_mobile_diagnostics = mobile,
		)

	Expect.that(config.processing().metrics_enabled()).to_be_false()
	Expect.that(config.automatic_capture().max_breadcrumbs()).to_equal(7)
	Expect.that(config.attachments().attach_game_log()).to_be_true()
	Expect.that(config.stack_traces().variables_enabled()).to_be_true()
	Expect.that(config.mobile_diagnostics().android_anr_timeout_msec()).to_equal(6400)
	Expect.that(config.global_attributes()).to_equal({"region": "iad"})
	Expect.that(config.provider_options()).to_equal({"dsn": "test"})
```

Add to `scripts/test-foundry-script`:

```bash
config_constructor=$(
	sed -n '/^func _init($/,/^) -> void:$/p' "$addon/ObservabilityConfig.fs"
)
for focused_parameter in \
	'p_processing: ObservabilityProcessingConfig' \
	'p_automatic_capture: ObservabilityAutomaticCaptureConfig' \
	'p_attachments: ObservabilityAttachmentConfig' \
	'p_stack_traces: ObservabilityStackTraceConfig' \
	'p_mobile_diagnostics: ObservabilityMobileDiagnosticsConfig'; do
	rg -Fq "$focused_parameter" <<<"$config_constructor" \
		|| fail "root configuration is missing $focused_parameter"
done
if rg -n 'p_(logs_enabled|metric_filter|automatic_event_mask|attach_screenshot|android_anr_timeout)' \
	<<<"$config_constructor"; then
	fail "root configuration retains focused positional settings"
fi
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because `ObservabilityConfig` still owns all focused settings.

- [ ] **Step 3: Rewrite the root configuration**

Use final fields and this constructor:

```foundryscript
func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_processing: ObservabilityProcessingConfig? = null,
		p_automatic_capture: ObservabilityAutomaticCaptureConfig? = null,
		p_attachments: ObservabilityAttachmentConfig? = null,
		p_stack_traces: ObservabilityStackTraceConfig? = null,
		p_mobile_diagnostics: ObservabilityMobileDiagnosticsConfig? = null,
) -> void:
	_enabled = p_enabled
	_environment = p_environment
	_release = p_release
	_dist = p_dist
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)
	_processing = (
			p_processing
			if p_processing != null
			else ObservabilityProcessingConfig.new()
		)
	_automatic_capture = (
			p_automatic_capture
			if p_automatic_capture != null
			else ObservabilityAutomaticCaptureConfig.new()
		)
	_attachments = (
			p_attachments
			if p_attachments != null
			else ObservabilityAttachmentConfig.new()
		)
	_stack_traces = (
			p_stack_traces
			if p_stack_traces != null
			else ObservabilityStackTraceConfig.new()
		)
	_mobile_diagnostics = (
			p_mobile_diagnostics
			if p_mobile_diagnostics != null
			else ObservabilityMobileDiagnosticsConfig.new()
		)
```

Add accessors `enabled`, `environment`, `release`, `dist`, `global_attributes`, `provider_options`, `processing`, `automatic_capture`, `attachments`, `stack_traces`, and `mobile_diagnostics`. Return immutable nested objects directly and duplicate the two dictionaries.

- [ ] **Step 4: Migrate startup construction**

Change `ObservabilityStartupSettings.observability_config()` to construct:

```foundryscript
return ObservabilityConfig.new(
		p_enabled = _enabled,
		p_environment = _environment,
		p_release = _release,
		p_dist = _dist,
		p_global_attributes = {},
		p_provider_options = _provider_options,
		p_processing = ObservabilityProcessingConfig.new(),
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(),
		p_attachments = ObservabilityAttachmentConfig.new(),
		p_stack_traces = ObservabilityStackTraceConfig.new(),
		p_mobile_diagnostics = ObservabilityMobileDiagnosticsConfig.new(),
	)
```

- [ ] **Step 5: Migrate every field access using the fixed map**

Apply this exact ownership map throughout core, memory/null providers, Sentry, and tests:

```text
config.enabled                                  config.enabled()
config.environment                              config.environment()
config.release                                  config.release()
config.dist                                     config.dist()
config.logs_enabled                             config.processing().logs_enabled()
config.log_minimum_level                        config.processing().log_minimum_level()
config.log_rate_limit_per_second                config.processing().log_rate_limit_per_second()
config.metrics_enabled                          config.processing().metrics_enabled()
config.event_sample_rate                        config.processing().event_sample_rate()
config.log_sample_rate                          config.processing().log_sample_rate()
config.metric_sample_rate                       config.processing().metric_sample_rate()
config.metric_filter                            config.processing().metric_filter()
config.event_processors()                       config.processing().event_processors()
config.log_processors()                         config.processing().log_processors()
config.metric_processors()                      config.processing().metric_processors()
config.event_limits()                           config.processing().event_limits()
config.log_limits()                             config.processing().log_limits()
config.metric_limits()                          config.processing().metric_limits()
config.redaction_policy()                       config.processing().redaction_policy()
config.automatic_capture_enabled                config.automatic_capture().enabled()
config.automatic_event_mask                     config.automatic_capture().event_mask()
config.automatic_breadcrumb_mask                config.automatic_capture().breadcrumb_mask()
config.automatic_log_mask                       config.automatic_capture().log_mask()
config.max_breadcrumbs                          config.automatic_capture().max_breadcrumbs()
config.automatic_message_filter_prefixes()      config.automatic_capture().message_filter_prefixes()
config.max_attachment_bytes                     config.attachments().max_bytes()
config.attach_game_log                          config.attachments().attach_game_log()
config.attach_screenshot                        config.attachments().attach_screenshot()
config.attach_scene_tree                        config.attachments().attach_scene_tree()
config.stack_trace_source_context_enabled       config.stack_traces().source_context_enabled()
config.stack_trace_variables_enabled            config.stack_traces().variables_enabled()
config.application_hang_detection_enabled       config.mobile_diagnostics().application_hang_detection_enabled()
config.application_hang_timeout_msec             config.mobile_diagnostics().application_hang_timeout_msec()
config.android_anr_detection_enabled             config.mobile_diagnostics().android_anr_detection_enabled()
config.android_anr_timeout_msec                  config.mobile_diagnostics().android_anr_timeout_msec()
config.android_anr_attach_thread_dump            config.mobile_diagnostics().android_anr_attach_thread_dump()
```

Replace every old constructor with named root and focused constructor arguments. Delete positional-constructor compatibility tests instead of translating them.

- [ ] **Step 6: Verify the hard cut**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-project
rg -n 'p_(logs_enabled|metric_filter|automatic_event_mask|attach_screenshot|android_anr_timeout)' \
	addons/FoundryObservability/ObservabilityConfig.fs
git diff --check
```

Expected: the test commands pass, the `rg` command prints nothing, and whitespace check prints nothing.

- [ ] **Step 7: Commit**

```bash
git add addons test_project/tests scripts/test-foundry-script
git commit -m "refactor!: decompose observability configuration"
```

### Task 7: Extract bounded value traversal and type redaction results

**Files:**

- Create: `addons/FoundryObservability/processing/ObservabilityValueVisitDecision.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityValuePolicy.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityValueWalker.fs`
- Create: UID companions for all three files
- Move: `addons/FoundryObservability/ObservabilityRedactor.fs*` to `addons/FoundryObservability/processing/`
- Modify: `addons/FoundryObservability/ObservabilityScope.fs`
- Modify: `addons/FoundryObservability/ObservabilityStartupSettings.fs`
- Modify: `addons/FoundryObservability/ObservabilityStackFrame.fs`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing typed-redactor tests**

Change representative assertions to:

```foundryscript
func test_redactor_returns_typed_event_result() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.sensitive_key("password"),
	])
	var redactor := ObservabilityRedactor.new(policy)
	var event := ObservabilityEvent.new(
			p_kind = &"message",
			p_attributes = {"password": "secret", "safe": true},
		)

	var result: ObservabilityRedactionResult[ObservabilityEvent] = (
			redactor.redact_event(event, ObservabilitySignal.EVENT)
		)

	Expect.that(result.valid()).to_be_true()
	Expect.that(result.failed_rule_index()).to_equal(-1)
	Expect.that(result.value().attributes()).to_equal({"safe": true})


func test_shared_value_walker_rejects_cycles_without_aliasing() -> void:
	var cyclic: Dictionary = {}
	cyclic["self"] = cyclic
	var policy := RejectingStructuredValuePolicy.new()
	var walker := ObservabilityValueWalker.new(8, 32)

	var result := walker.walk(cyclic, policy)

	Expect.that(result.valid()).to_be_false()
	Expect.that(result.value()).to_equal(null)
```

Add this nested test policy:

```foundryscript
class RejectingStructuredValuePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	func visit(
			_path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		if value == null or value is bool or value is int \
				or value is float or value is String or value is StringName:
			return ObservabilityValueVisitDecision.keep(value)
		return ObservabilityValueVisitDecision.reject()
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing traversal types and dictionary-returning redactor signatures.

- [ ] **Step 3: Implement traversal contracts**

`ObservabilityValuePolicy` declares:

```foundryscript
abstract func visit(
		path: PackedStringArray,
		value: Variant,
) -> ObservabilityValueVisitDecision
```

`ObservabilityValueVisitDecision` is final and has a named inner enum:

```foundryscript
enum Action:
	KEEP = 0
	DESCEND = 1
	REJECT = 2
```

It stores final `Action` and `Variant` fields, with `keep`, `descend`, and `reject` factories.

`ObservabilityValueWalker._init(max_depth: int, max_items: int)` normalizes both bounds to zero. `walk(value, policy)` initializes a remaining-item counter and active-container array, then recursively:

1. consumes one budget item;
2. asks the policy for a typed decision;
3. returns a typed failure on `REJECT`;
4. defensively copies a leaf on `KEEP`;
5. checks depth and active-container identity on `DESCEND`;
6. rebuilds dictionaries/arrays without aliasing;
7. removes the container from active state on every exit.

The walker returns `ObservabilityRedactionResult[Variant]`; the Task 4 analyzer test has already established nullable generic result support. Do not weaken the public generic result APIs.

- [ ] **Step 4: Migrate public redactor entry points**

Move the redactor and preserve UID `uid://d13rdctcore1`. Import the processing namespace from callers. Use exact signatures:

```foundryscript
func redact_event(
		event: ObservabilityEvent,
		signal: ObservabilitySignal,
) -> ObservabilityRedactionResult[ObservabilityEvent]

func redact_metric(
		metric: ObservabilityMetric,
) -> ObservabilityRedactionResult[ObservabilityMetric]

func redact_contexts(
		contexts: Dictionary,
) -> ObservabilityRedactionResult[Dictionary]

func redact_user(
		user: ObservabilityUser,
) -> ObservabilityRedactionResult[ObservabilityUser]

func redact_breadcrumb(
		breadcrumb: ObservabilityBreadcrumb,
) -> ObservabilityRedactionResult[ObservabilityBreadcrumb]

func redact_attachment(
		attachment: ObservabilityAttachment,
) -> ObservabilityRedactionResult[ObservabilityAttachment]

func redact_attachment_payload(
		payload: Dictionary,
) -> ObservabilityRedactionResult[Dictionary]
```

Replace `_success`, `_failure`, `_typed_failure`, and caller dictionary indexing with typed factories/accessors. Replace `event`/`log` string comparisons with `ObservabilitySignal`.

- [ ] **Step 5: Reuse traversal mechanics without weakening policies**

Extract only depth, item-budget, cycle, and reconstruction mechanics from:

- `ObservabilityScope._normalized_*`;
- `ObservabilityStartupSettings._is_valid_provider_option*`;
- `ObservabilityStackFrame._bounded_*`;
- `ObservabilityRedactor._validate_source_tree` and recursive rebuilding.

Give each caller a separate policy implementation retaining its current allowed scalar types, maximum depth/items, truncation behavior, and failure behavior. Keep existing focused tests for all four callers and add the cycle test above.

- [ ] **Step 6: Run GREEN and UID checks**

Run:

```bash
git add addons/FoundryObservability/processing
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all commands pass; existing scope/startup/stack/redaction bounds remain green.

Update existing `scripts/test-foundry-script` redactor path checks from the
addon root to `processing/ObservabilityRedactor.fs`.

- [ ] **Step 7: Commit**

```bash
git add addons/FoundryObservability test_project/tests/observability-core.test.fs
git commit -m "refactor: type redaction and shared value traversal"
```

### Task 8: Type the processing lease, pipeline, and diagnostic

**Files:**

- Create: `addons/FoundryObservability/processing/ObservabilityProcessingLease.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProcessingLease.fs.uid`
- Move: `addons/FoundryObservability/ObservabilityProcessingPipeline.fs*` to `addons/FoundryObservability/processing/`
- Move: `addons/FoundryObservability/ObservabilitySignalLimiter.fs*` to `addons/FoundryObservability/processing/`
- Modify: `addons/FoundryObservability/ObservabilityProcessingDiagnostic.fs`
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `scripts/test-foundry-script`
- Modify: `scripts/test-package`

- [ ] **Step 1: Convert pipeline tests to typed results first**

Change representative event and metric assertions to:

```foundryscript
var event_result: ObservabilityProcessingResult[ObservabilityEvent] = (
		pipeline.process_event(event)
	)
Expect.that(event_result.outcome()).to_equal(
		ObservabilityProcessingOutcome.ACCEPTED,
	)
Expect.that(event_result.signal()).to_equal(ObservabilitySignal.EVENT)
Expect.that(event_result.value().message()).to_equal("processed")
Expect.that(event_result.operation_token()).to_be_greater_than(0)

var metric_result: ObservabilityProcessingResult[ObservabilityMetric] = (
		pipeline.process_metric(metric)
	)
Expect.that(metric_result.value().name()).to_equal("combat.damage")
```

Update diagnostic assertions to enum accessors:

```foundryscript
Expect.that(diagnostic.signal()).to_equal(ObservabilitySignal.EVENT)
Expect.that(diagnostic.outcome()).to_equal(ObservabilityProcessingOutcome.DROPPED)
Expect.that(diagnostic.reason()).to_equal(ObservabilityProcessingReason.RECURSIVE)
Expect.that(diagnostic.limit_kind()).to_equal(ObservabilityLimitKind.NONE)
```

- [ ] **Step 2: Add failing source contracts**

Add:

```bash
pipeline="$addon/processing/ObservabilityProcessingPipeline.fs"
for signature in \
	'func process_event(event: ObservabilityEvent) -> ObservabilityProcessingResult[ObservabilityEvent]:' \
	'func process_metric(metric: ObservabilityMetric) -> ObservabilityProcessingResult[ObservabilityMetric]:'; do
	rg -Fq "$signature" "$pipeline" \
		|| fail "processing pipeline is missing typed signature: $signature"
done
if rg -n 'snapshot\["|result\["(accepted|value|operation_token)"\]' "$pipeline"; then
	fail "processing pipeline retains dictionary protocols"
fi
```

- [ ] **Step 3: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because the processing namespace files have not moved and pipeline returns dictionaries.

- [ ] **Step 4: Implement the immutable generic lease**

Create `ObservabilityProcessingLease[T]` with final fields:

```foundryscript
final var _generation: int
final var _operation_token: int
final var _owner_id: int
final var _signal: ObservabilitySignal
final var _processors: Array[Callable[[T], T?]]
final var _redactor: ObservabilityRedactor
final var _limiter: ObservabilitySignalLimiter
final var _limiter_mutex: Mutex
final var _runtime: ObservabilityRuntime
```

Its constructor snapshots the typed processor array. Add exact accessors and return a copied processor array.

- [ ] **Step 5: Migrate pipeline state and methods**

Use exact fields:

```foundryscript
var _event_processors: Array[
		Callable[[ObservabilityEvent], ObservabilityEvent?]
	] = []
var _log_processors: Array[
		Callable[[ObservabilityEvent], ObservabilityEvent?]
	] = []
var _metric_processors: Array[
		Callable[[ObservabilityMetric], ObservabilityMetric?]
	] = []
var _metric_filter: Callable[[ObservabilityMetric], bool]?
```

Make reserve paths return typed event or metric leases. Keep active-operation and pending-result dictionaries because they are bounded keyed tables, but store typed internal record values rather than magic-key dictionaries.

Make every finish method return `ObservabilityProcessingResult[T]`. Remove `_rejected`, `_rule_index`, `_int_value`, and dictionary snapshot casts. Invoke typed processors directly and test nullable replacement values without `Variant` type probing.

- [ ] **Step 6: Type diagnostics**

Change `ObservabilityProcessingDiagnostic` fields, constructor parameters, and accessors to `ObservabilitySignal`, `ObservabilityProcessingOutcome`, `ObservabilityProcessingReason`, and `ObservabilityLimitKind`. Preserve sequence, processor index, redaction rule index, and error.

- [ ] **Step 7: Move namespace files and update recursive packaging**

Move the pipeline and limiter with their existing UIDs, change their namespace
to `foundry.observability.processing`, and add imports to root and Sentry
callers. Keep `ObservabilityProcessingDiagnostic`,
`ObservabilityRedactionPolicy`, `ObservabilityRedactionRule`, and
`ObservabilitySignalLimits` in `foundry.observability`.

Update all existing source-contract path variables for the moved pipeline,
limiter, and redactor to their `processing/` paths.

Change package/source loops that currently use `-maxdepth 1` for core Foundry Script resources to recursive `find`/copy behavior. Keep binary artifact handling unchanged.

- [ ] **Step 8: Verify GREEN**

Run:

```bash
git add addons/FoundryObservability/processing scripts
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
./scripts/test-package
git diff --check
```

Expected: all commands pass, typed pipeline assertions pass, and recursive processing files are packaged.

- [ ] **Step 9: Commit**

```bash
git add addons test_project/tests scripts
git commit -m "refactor!: type the observability processing pipeline"
```

### Task 9: Extract normalization and provider-session collaborators

**Files:**

- Create: `addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs.uid`
- Create: `addons/FoundryObservability/processing/ObservabilityNormalizer.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityNormalizer.fs.uid`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderSnapshot.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderSnapshot.fs.uid`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderCall.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderCall.fs.uid`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderSession.fs`
- Create: `addons/FoundryObservability/processing/ObservabilityProviderSession.fs.uid`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing normalizer tests**

Add:

```foundryscript
func test_normalizer_resolves_capture_time_once_from_runtime() -> void:
	var runtime := FakeObservabilityRuntime.new(123, 456, 7, 8, 8)
	var normalizer := ObservabilityNormalizer.new(runtime)
	var config := ObservabilityConfig.new()
	var source := ObservabilityEvent.new(
			p_kind = &"message",
			p_message = "hello",
		)

	var result: ObservabilityNormalizationResult[ObservabilityEvent] = (
			normalizer.normalize_event(source, config)
		)

	Expect.that(result.valid()).to_be_true()
	Expect.that(result.error()).to_equal(Error.OK)
	Expect.that(result.value().timestamp_msec()).to_equal(456)
	Expect.that(result.value().engine_ticks_msec()).to_equal(123)


func test_normalizer_rejects_invalid_metric_without_session_state() -> void:
	var runtime := FakeObservabilityRuntime.new()
	var normalizer := ObservabilityNormalizer.new(runtime)
	var metric := ObservabilityMetric.new(
			ObservabilityMetricType.GAUGE,
			"",
			1.0,
		)

	var result: ObservabilityNormalizationResult[ObservabilityMetric] = (
			normalizer.normalize_metric(metric, ObservabilityConfig.new())
		)

	Expect.that(result.valid()).to_be_false()
	Expect.that(result.value()).to_equal(null)
	Expect.that(result.error()).to_equal(Error.ERR_INVALID_PARAMETER)
```

- [ ] **Step 2: Write failing provider-session tests**

Add:

```foundryscript
func test_provider_session_commits_provider_config_and_pipeline_together() -> void:
	var runtime := FakeObservabilityRuntime.new()
	var session := ObservabilityProviderSession.new(runtime)
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new()
	var pipeline := ObservabilityProcessingPipeline.new(runtime)
	Expect.that(pipeline.configure(config)).to_equal(Error.OK)

	Expect.that(session.replace(provider, config, pipeline)).to_equal(Error.OK)
	var snapshot: ObservabilityProviderSnapshot = session.snapshot()

	Expect.that(snapshot.provider()).to_equal(provider)
	Expect.that(snapshot.config()).to_equal(config)
	Expect.that(snapshot.pipeline()).to_equal(pipeline)
	Expect.that(snapshot.generation()).to_equal(1)
	Expect.that(snapshot.enabled()).to_be_true()


func test_provider_session_pins_and_balances_provider_calls() -> void:
	var runtime := FakeObservabilityRuntime.new()
	var session := ObservabilityProviderSession.new(runtime)
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new()
	var pipeline := ObservabilityProcessingPipeline.new(runtime)
	Expect.that(pipeline.configure(config)).to_equal(Error.OK)
	Expect.that(session.replace(provider, config, pipeline)).to_equal(Error.OK)

	var call: ObservabilityProviderCall = session.begin_call()
	Expect.that(call.accepted()).to_be_true()
	Expect.that(call.provider()).to_equal(provider)
	Expect.that(call.generation()).to_equal(1)
	session.end_call(call)
	Expect.that(session.in_flight_call_count()).to_equal(0)
```

- [ ] **Step 3: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing normalizer and provider-session types.

- [ ] **Step 4: Implement the generic normalization result**

Create a final generic class with final `_valid: bool`, `_value: T?`, and `_error: int` fields. Implement:

```foundryscript
static func success(value: T) -> ObservabilityNormalizationResult[T]:
	return ObservabilityNormalizationResult[T].new(true, value, Error.OK)


static func failure(error: int) -> ObservabilityNormalizationResult[T]:
	return ObservabilityNormalizationResult[T].new(false, null, error)
```

Add `valid()`, `value()`, and `error()` accessors.

- [ ] **Step 5: Extract `ObservabilityNormalizer`**

Move these exact concerns from `FoundryObservability` without changing behavior:

- `_resolved_event_timestamp`;
- `_normalized_metric` and metric validators;
- `_normalized_exception_event`;
- `_normalized_exception`;
- `_normalized_stack_frame`;
- feedback validation;
- control-character and whitespace validation used only by normalization.

Use this public surface:

```foundryscript
func normalize_event(
		event: ObservabilityEvent,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityEvent]

func normalize_metric(
		metric: ObservabilityMetric,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]

func normalize_feedback(
		feedback: ObservabilityFeedback,
) -> ObservabilityNormalizationResult[ObservabilityFeedback]

func counter(
		metric_name: String,
		value: int,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]

func gauge(
		metric_name: String,
		value: float,
		unit: String,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]

func distribution(
		metric_name: String,
		value: float,
		unit: String,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]
```

The constructor requires `ObservabilityRuntime`. `normalize_event` snapshots monotonic and Unix time once and passes those values through all event/exception rebuilds.

- [ ] **Step 6: Implement typed provider snapshot and call values**

`ObservabilityProviderSnapshot` stores final provider, config, pipeline, generation, and enabled fields.

`ObservabilityProviderCall` stores final accepted, pinned `ObservabilityProviderSnapshot?`, and error fields. Its provider/config/pipeline/generation accessors delegate to the pinned snapshot after asserting the call is accepted. Implement accepted/rejected factories:

```foundryscript
static func begin(
		snapshot: ObservabilityProviderSnapshot,
) -> ObservabilityProviderCall

static func rejected(error: int) -> ObservabilityProviderCall
```

- [ ] **Step 7: Extract `ObservabilityProviderSession`**

Move active provider/config/generation, in-flight call count, configuration-in-progress state, shutdown request, shutdown completion, flush, and null-provider restoration from the facade.

The session owns one mutex and these methods:

```foundryscript
func snapshot() -> ObservabilityProviderSnapshot
func replace(
		provider: ObservabilityProvider,
		config: ObservabilityConfig,
		pipeline: ObservabilityProcessingPipeline,
) -> int
func begin_call() -> ObservabilityProviderCall
func end_call(call: ObservabilityProviderCall) -> void
func finish_call(call: ObservabilityProviderCall, error: int) -> void
func flush(timeout_msec: int = 2000) -> int
func shutdown() -> void
func shutdown_requested() -> bool
func in_flight_call_count() -> int
```

`replace` receives an already validated pipeline, configures the candidate provider without holding the mutex, and atomically commits provider/config/pipeline/generation. It preserves the active snapshot on failure and shuts down rejected replacement providers exactly once.

- [ ] **Step 8: Verify collaborators independently**

Run:

```bash
git add addons/FoundryObservability/processing
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: collaborator tests pass while the facade still uses its original internal paths.

- [ ] **Step 9: Commit**

```bash
git add test_project/tests/observability-core.test.fs
git commit -m "feat: add typed normalization and provider session"
```

### Task 10: Reduce the facade and automatic logger to orchestration

**Files:**

- Rewrite: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/AutomaticObservabilityLogger.fs`
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs`
- Modify: `addons/FoundryObservability/NullObservabilityProvider.fs`
- Modify: `addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs`
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `test_project/tests/observability-foundrylib.test.fs`
- Modify: `test_project/tests/support/recording_observability_api.notest.fs`

- [ ] **Step 1: Add failing facade-delegation assertions**

Add:

```foundryscript
func test_facade_uses_typed_session_pipeline_and_normalizer() -> void:
	var runtime := FakeObservabilityRuntime.new(100, 200, 3, 4, 4)
	var service := FoundryObservability.new(
			ObservabilityStartupSettings.new(),
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs",
			runtime,
		)
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new()

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_message("delegated")).to_equal("memory:1")
	Expect.that(provider.events()[0].timestamp_msec()).to_equal(200)
	Expect.that(provider.events()[0].engine_ticks_msec()).to_equal(100)
	Expect.that(service.last_processing_diagnostic().signal()).to_equal(
			ObservabilitySignal.EVENT,
		)
	service.shutdown()
	service.free()
```

Add a source-size responsibility contract:

```bash
facade_lines=$(wc -l <"$addon/FoundryObservability.fs" | tr -d ' ')
(( facade_lines <= 900 )) \
	|| fail "FoundryObservability remains oversized after collaborator extraction"
for collaborator in _runtime _normalizer _session; do
	rg -q "^var ${collaborator}:" "$addon/FoundryObservability.fs" \
		|| fail "facade is missing collaborator ${collaborator}"
done
```

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because the facade remains larger than 900 lines and lacks typed collaborators.

- [ ] **Step 3: Compose collaborators in `_init`**

Use:

```foundryscript
var _runtime: ObservabilityRuntime
var _normalizer: ObservabilityNormalizer
var _session: ObservabilityProviderSession
var _automatic_logger: AutomaticObservabilityLogger?


func _init(
		startup_settings: ObservabilityStartupSettings? = null,
		startup_provider_path: String = _SENTRY_PROVIDER_PATH,
		runtime: ObservabilityRuntime? = null,
) -> void:
	_runtime = runtime if runtime != null else SystemObservabilityRuntime.new()
	_normalizer = ObservabilityNormalizer.new(_runtime)
	_session = ObservabilityProviderSession.new(_runtime)
	_startup_provider_path = startup_provider_path
	if startup_settings == null:
		initialize_from_project_settings()
	else:
		_initialize_startup(startup_settings)
```

- [ ] **Step 4: Delegate configuration transaction**

`configure` builds/configures a candidate pipeline first, then passes provider, immutable config, and candidate pipeline to `_session.replace`. On success it refreshes the automatic logger from the committed snapshot. On failure it preserves the session and active logger.

- [ ] **Step 5: Delegate capture paths**

For events and metrics:

1. call `_normalizer`;
2. begin one typed provider call;
3. process through `call.snapshot().pipeline()`;
4. dispatch the typed accepted value;
5. record provider result with enum signal/token;
6. finish the provider call exactly once.

State operations use typed redaction results and provider capability traits. Delete dictionary state snapshots, normalization helpers, provider counters, and shutdown/configuration state that moved to collaborators.

- [ ] **Step 6: Finish automatic logger hard cut**

Store `ObservabilityAutomaticCaptureConfig`, not the root configuration. Replace mask/prefix accesses with focused accessors. Construct with service, automatic configuration, and shared runtime. Keep registration/removal and engine callback signatures unchanged.

- [ ] **Step 7: Update provider and adapter configuration access**

Use focused configuration accessors in memory/null providers and FoundryLib sink tests. Update `FoundryObservabilityApi.last_processing_diagnostic()` documentation to the enum-typed diagnostic.

- [ ] **Step 8: Run the concurrency regression suite**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all existing replacement, reconfiguration, recursion, shutdown, automatic capture, and stale-generation tests pass through the collaborators.

- [ ] **Step 9: Commit**

```bash
git add addons/FoundryObservability test_project/tests scripts/test-foundry-script
git commit -m "refactor!: decompose the observability facade"
```

### Task 11: Replace Sentry probes with typed source traits

**Files:**

- Create: `addons/FoundryObservabilitySentry/SentryRuntimeSnapshot.fs`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeSnapshot.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextSource.fs`
- Create: `addons/FoundryObservabilitySentry/SentryRuntimeContextSource.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SystemSentryRuntimeContextSource.fs`
- Create: `addons/FoundryObservabilitySentry/SystemSentryRuntimeContextSource.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SentryAttachmentSource.fs`
- Create: `addons/FoundryObservabilitySentry/SentryAttachmentSource.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SystemSentryAttachmentSource.fs`
- Create: `addons/FoundryObservabilitySentry/SystemSentryAttachmentSource.fs.uid`
- Create: `addons/FoundryObservabilitySentry/SentryAttachmentCollection.fs`
- Create: `addons/FoundryObservabilitySentry/SentryAttachmentCollection.fs.uid`
- Modify: `addons/FoundryObservabilitySentry/SentryRuntimeContextCollector.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryBuiltInAttachmentCollector.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Delete: `addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs`
- Delete: `addons/FoundryObservabilitySentry/SentryRuntimeContextProbe.fs.uid`
- Delete: `addons/FoundryObservabilitySentry/SentryAttachmentRuntimeProbe.fs`
- Delete: `addons/FoundryObservabilitySentry/SentryAttachmentRuntimeProbe.fs.uid`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Convert test fakes to trait conformances**

Replace `FakeRuntimeContextProbe` with `FakeRuntimeContextSource uses SentryRuntimeContextSource`. Replace its dictionary-returning methods with:

```foundryscript
func stable_snapshot() -> SentryRuntimeSnapshot:
	return _snapshot(false)


func volatile_snapshot() -> SentryRuntimeSnapshot:
	memory_call_count += 1
	return _snapshot(true)
```

Replace `FakeAttachmentRuntimeProbe` with `FakeAttachmentSource uses SentryAttachmentSource` and exact typed methods:

```foundryscript
func is_main_thread() -> bool
func is_headless() -> bool
func frames_drawn() -> int
func scene_root() -> Node?
func screenshot_png() -> PackedByteArray
func game_log_path() -> String
```

- [ ] **Step 2: Add failing collection assertions**

Use:

```foundryscript
var collection: SentryAttachmentCollection = collector.collect(event, config)
Expect.that(collection.attachments()).to_have_size(1)
Expect.that(collection.failures()).to_have_size(0)
```

Add a source contract:

```bash
if rg -n '_probe: Object|_probe\.call\(' \
	"$sentry_addon/SentryRuntimeContextCollector.fs" \
	"$sentry_addon/SentryBuiltInAttachmentCollector.fs"; then
	fail "Sentry collectors retain dynamic probes"
fi
```

- [ ] **Step 3: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL with missing source traits/snapshots and dynamic probe calls.

- [ ] **Step 4: Implement `SentryRuntimeSnapshot`**

Use one final aggregate with nested final component classes:

```foundryscript
final class Application extends RefCounted
final class EngineRuntime extends RefCounted
final class Device extends RefCounted
final class Display extends RefCounted
final class Gpu extends RefCounted
final class Runtime extends RefCounted
final class Privacy extends RefCounted
```

Use these exact final fields:

```text
Application:
  name: String
  version: String
  start_time: String
  architecture: String
EngineRuntime:
  version: String
  version_commit: String
  architecture: String
  editor: bool
  debug_build: bool
  headless: bool
  dedicated_server: bool
Device:
  model: String
  processor_name: String
  processor_count: int
  physical_memory: int
  free_memory: int
  usable_memory: int
Display:
  server: String
  screen_count: int
  touchscreen_available: bool
  primary_width_pixels: int
  primary_height_pixels: int
  primary_dpi: int
  primary_refresh_rate: float
  primary_orientation: String
Gpu:
  name: String
  vendor_name: String
  api_version: String
  device_type: String
  driver_name: String
  driver_version: String
  rendering_method: String
Runtime:
  sandboxed: bool
  userfs_persistent: bool
Privacy:
  unique_identifier: String
  locale: String
  timezone: String
SentryRuntimeSnapshot:
  platform_name: String
  application: Application
  engine: EngineRuntime
  device: Device
  display: Display
  gpu: Gpu
  runtime: Runtime
  privacy: Privacy
  free_storage: int
```

Each nested constructor accepts its fields in the listed order and assigns them one-to-one. The head constructor accepts its listed fields in order. All scalar defaults are empty string, `false`, `0`, or `-1` for unavailable byte counts; component defaults are newly constructed empty components. Public final fields are read directly by the collector, so no dynamic keys or accessors are needed.

- [ ] **Step 5: Implement runtime source trait and system source**

The trait declares:

```foundryscript
abstract func stable_snapshot() -> SentryRuntimeSnapshot
abstract func volatile_snapshot() -> SentryRuntimeSnapshot
```

Move existing engine reads from `SentryRuntimeContextProbe` into `SystemSentryRuntimeContextSource`. Stable snapshot reads application/engine/device/display/GPU/runtime/privacy facts. Volatile snapshot reads memory, free storage, and orientation and leaves unrelated fields at documented empty defaults.

- [ ] **Step 6: Implement attachment source trait and system source**

Use the exact fake method signatures from Step 1. The system source returns the main scene root directly, preserving current null/headless/main-thread checks and PNG generation.

- [ ] **Step 7: Implement immutable attachment collection**

Create:

```foundryscript
namespace foundry.observability.sentry

import foundry.observability

## Isolated native attachment payloads and typed failures.
final class_name SentryAttachmentCollection extends RefCounted

final var _attachments: Array[Dictionary]
final var _failures: Array[ObservabilityAttachmentFailure]
```

The constructor deep-copies every attachment dictionary and duplicates every failure. Both accessors return new isolated arrays.

- [ ] **Step 8: Migrate collectors and delete probes**

Collectors require their source traits in constructors and call methods directly. Runtime collector maps explicit snapshot fields into existing context dictionary keys. Attachment collector returns `SentryAttachmentCollection`.

Delete old probe files and update provider construction to use the new system source class names.

- [ ] **Step 9: Verify GREEN and UIDs**

Run:

```bash
git add addons/FoundryObservabilitySentry test_project/tests
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
git diff --check
```

Expected: all Sentry collector tests pass and source contracts find no dynamic probe calls.

- [ ] **Step 10: Commit**

```bash
git commit -m "refactor!: type Sentry runtime sources"
```

### Task 12: Isolate the native Sentry bridge behind a trait adapter

**Files:**

- Create: `addons/FoundryObservabilitySentry/SentryNativeBridge.fs`
- Create: `addons/FoundryObservabilitySentry/SentryNativeBridge.fs.uid`
- Create: `addons/FoundryObservabilitySentry/DynamicSentryNativeBridgeAdapter.fs`
- Create: `addons/FoundryObservabilitySentry/DynamicSentryNativeBridgeAdapter.fs.uid`
- Create: `test_project/tests/support/fake_sentry_native_bridge.notest.fs`
- Create: `test_project/tests/support/fake_sentry_native_bridge.notest.fs.uid`
- Rewrite: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`
- Delete: obsolete partial dynamic bridge support fixtures after their capability cases move to the typed fake

- [ ] **Step 1: Write failing typed bridge tests**

Add:

```foundryscript
func test_sentry_provider_accepts_typed_bridge() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_provider_options = {"dsn": "https://public@example.invalid/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"message",
			p_message = "typed bridge",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	provider.shutdown()


func test_dynamic_bridge_adapter_rejects_missing_required_contract() -> void:
	var adapter := DynamicSentryNativeBridgeAdapter.new(RefCounted.new())

	Expect.that(adapter.lifecycle_version()).to_equal(-1)
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(adapter.is_available("owner")).to_be_false()
```

- [ ] **Step 2: Add the failing dynamic-boundary contract**

Append:

```bash
while IFS= read -r dynamic_use; do
	case "$dynamic_use" in
		*DynamicSentryNativeBridgeAdapter.fs:*) ;;
		*) fail "native bridge dynamic call escaped its adapter: $dynamic_use" ;;
	esac
done < <(
	rg -n 'bridge\.(call|has_method)\(|_bridge\.(call|has_method)\(' \
		"$sentry_addon" --glob '*.fs' || true
)
rg -q '^var _bridge: SentryNativeBridge' \
	"$sentry_addon/SentryObservabilityProvider.fs" \
	|| fail "Sentry provider must depend on SentryNativeBridge"
```

- [ ] **Step 3: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because the provider stores `Object?` and calls the native object directly.

- [ ] **Step 4: Define the bridge trait**

Declare exact typed methods:

```foundryscript
abstract func lifecycle_version() -> int
abstract func configure(payload: Dictionary) -> int
abstract func is_available(owner: String) -> bool
abstract func capture(payload: Dictionary) -> String
abstract func capture_log(payload: Dictionary) -> String
abstract func supports_scope() -> bool
abstract func apply_scope(payload: Dictionary) -> bool
abstract func supports_breadcrumbs() -> bool
abstract func capture_breadcrumb(payload: Dictionary) -> bool
abstract func clear_breadcrumbs() -> bool
abstract func supports_feedback() -> bool
abstract func capture_feedback(payload: Dictionary) -> String
abstract func supports_metrics() -> bool
abstract func capture_metric(payload: Dictionary) -> bool
abstract func supports_attachments() -> bool
abstract func replace_attachments(payloads: Array[Dictionary]) -> bool
abstract func capture_with_attachments(payload: Dictionary) -> String
abstract func flush(owner: String, timeout_msec: int) -> int
abstract func shutdown(owner: String) -> void
```

- [ ] **Step 5: Implement the dynamic adapter**

The adapter stores `Object?`. Every method:

1. checks the exact native method name;
2. calls it only inside this file;
3. validates the `Variant` result type;
4. returns the trait's fail-closed fallback on missing/malformed results.

Use native name mappings:

```text
lifecycle_version          lifecycleVersion
configure                  configure
is_available               isAvailable
capture                    capture
capture_log                captureLog
apply_scope                applyScope
capture_breadcrumb         captureBreadcrumb
clear_breadcrumbs          clearBreadcrumbs
capture_feedback           captureFeedback
capture_metric             captureMetric
replace_attachments        replaceAttachments
capture_with_attachments   captureWithAttachments
flush                      flush
shutdown                   shutdown
```

Capability methods report whether all methods needed by that family exist.

- [ ] **Step 6: Convert the full fake to the trait**

Move behavior and state from `fake_sentry_bridge.notest.fs` into `FakeSentryNativeBridge uses SentryNativeBridge`, renaming camelCase methods to trait names and replacing malformed `Variant` result tests with dedicated dynamic-adapter tests.

Partial capability fixtures become flag combinations on the typed fake. Keep raw malformed-object fixtures only for adapter validation.

- [ ] **Step 7: Rewrite the provider against the trait**

Store:

```foundryscript
var _bridge: SentryNativeBridge?
```

Constructor injection accepts `SentryNativeBridge?`. Normal native resolution wraps the extension object once in `DynamicSentryNativeBridgeAdapter`. Replace all `has_method`, `call`, and camelCase method names with trait methods/capability queries.

- [ ] **Step 8: Verify GREEN**

Run:

```bash
git add addons/FoundryObservabilitySentry test_project/tests
./scripts/test-foundry-uids
./scripts/test-foundry-script
./scripts/test-project
./scripts/test-sentry-ios-build-contract
./scripts/test-sentry-android-build-contract
git diff --check
```

Expected: Foundry Script and native build-contract tests pass; dynamic-boundary scan reports no escape.

- [ ] **Step 9: Commit**

```bash
git commit -m "refactor!: isolate the native Sentry bridge"
```

### Task 13: Enforce the hard-cut language and namespace contracts

**Files:**

- Modify: `scripts/test-foundry-script`
- Modify: `scripts/test-package`
- Modify: `scripts/test-foundry-uids`
- Modify: imports in all addon and test `.fs` files
- Modify: `CONTRIBUTING.md`

- [ ] **Step 1: Add the final failing source rules**

Add:

```bash
first_party_sources=(
	"$addon"
	"$sentry_addon"
)
if rg -n --glob '*.fs' \
	'(:|->|Array\[)[[:space:]]*Callable([[:space:]=,)\]]|$)' \
	"${first_party_sources[@]}"; then
	fail "first-party addon source contains a bare Callable"
fi
if rg -n --glob '*.fs' 'Callable\(\)' "${first_party_sources[@]}"; then
	fail "first-party addon source contains an invalid Callable sentinel"
fi
if rg -n --glob '*.fs' '_probe: Object|_probe\.call\(' "$sentry_addon"; then
	fail "Sentry addon contains a dynamic probe"
fi
if rg -n '_frame_supplier|positional compatibility seam' "$addon"; then
	fail "first-party source contains compatibility-only constructor baggage"
fi
```

Add a namespace-directory loop that checks:

- core `runtime/` files declare `foundry.observability.runtime`;
- core `processing/` files declare `foundry.observability.processing`;
- core `foundrylib/` files declare `foundry.observability.foundrylib`;
- Sentry root files declare `foundry.observability.sentry`.

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL and print every remaining bare callable, sentinel, stale import, or namespace mismatch.

- [ ] **Step 3: Remove every remaining violation**

Type legitimate callables with full signatures. Replace absence with `null`. Remove compatibility wording. Add explicit imports for runtime and processing namespaces. Do not change historical documents under `docs/superpowers/specs` or `docs/superpowers/plans`.

- [ ] **Step 4: Make source/UID/package traversal recursive**

Ensure all three scripts recursively discover `.fs` and `.fs.uid` under addon roots. Preserve symlink restoration and binary artifact behavior in `scripts/test-project`.

- [ ] **Step 5: Document contributor rules**

Add to `CONTRIBUTING.md`:

```markdown
## Foundry Script modeling

- Use complete `Callable[[ArgumentType], ResultType]` or
  `AsyncCallable[[ArgumentType], ResultType]`
  signatures for functional extension points.
- Use traits for injected dependencies with named operations.
- Use `null` for an absent nullable callback; do not use `Callable()` as a
  sentinel.
- Keep dynamic native calls inside the owning bridge adapter.
- Use `async` only when a function has a real suspension point.
- Match implementation subnamespace suffixes to source directories.
```

- [ ] **Step 6: Verify GREEN**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-foundry-uids
./scripts/test-package
git diff --check
```

Expected: all commands pass and scans print no violations.

- [ ] **Step 7: Commit**

```bash
git add addons test_project scripts CONTRIBUTING.md
git commit -m "chore: enforce Foundry Script modeling contracts"
```

### Task 14: Rewrite public documentation for the breaking API

**Files:**

- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `BUILD.md`
- Modify: `CHANGELOG.md`
- Modify: inline `##` documentation in all new/moved public `.fs` files
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Add failing documentation contracts**

Require these exact snippets:

```bash
for required_text in \
	'trait_name ObservabilityRuntime' \
	'Callable[[ObservabilityEvent], ObservabilityEvent?]' \
	'Callable[[ObservabilityMetric], ObservabilityMetric?]' \
	'Callable[[ObservabilityMetric], bool]?' \
	'ObservabilityProcessingConfig.new(' \
	'ObservabilityAutomaticCaptureConfig.new(' \
	'ObservabilityAttachmentConfig.new(' \
	'ObservabilityStackTraceConfig.new(' \
	'ObservabilityMobileDiagnosticsConfig.new(' \
	'Foundry Script `async` is cooperative'; do
	require_doc_text \
		"docs/API.md" \
		"$required_text" \
		"API documentation is missing language-model contract: $required_text"
done
```

Reject old root constructor parameters and `Callable()` from current docs.

- [ ] **Step 2: Run RED**

Run:

```bash
./scripts/test-foundry-script
```

Expected: FAIL because current docs show the old root configuration and untyped callables.

- [ ] **Step 3: Rewrite the README quick start**

Show:

```foundryscript
var processing := ObservabilityProcessingConfig.new(
		p_event_processors = [
			func(event: ObservabilityEvent) -> ObservabilityEvent?:
				if event.level() < ObservabilityLevel.WARN:
					return null
				return event,
		],
		p_event_limits = ObservabilitySignalLimits.new(5, 1000, 20, 10000),
	)
var config := ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "production",
		p_release = "1.0.0",
		p_processing = processing,
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(),
		p_attachments = ObservabilityAttachmentConfig.new(),
		p_stack_traces = ObservabilityStackTraceConfig.new(),
		p_mobile_diagnostics = ObservabilityMobileDiagnosticsConfig.new(),
	)
```

- [ ] **Step 4: Rewrite the API reference**

Document:

- all focused configuration constructors/defaults/accessors;
- runtime trait and system implementation;
- exact processor/filter signatures and null semantics;
- signal/outcome/reason/limit enums;
- typed admission, redaction, processing, and diagnostic accessors;
- provider session and processing internals only where needed to explain behavior;
- Sentry source/bridge traits and dynamic adapter boundary;
- synchronous capture flow and explicit rationale for not using `async`;
- hard-cut migration examples from old constructor fields to focused configs.

- [ ] **Step 5: Update build and changelog**

`BUILD.md` documents recursive source layout and the complete validation commands. `CHANGELOG.md` adds one breaking-change entry covering traits, typed callables/results, focused config, namespaces, and Sentry adapter isolation.

- [ ] **Step 6: Verify GREEN**

Run:

```bash
./scripts/test-foundry-script
./scripts/test-package
git diff --check
```

Expected: documentation contracts and package validation pass.

- [ ] **Step 7: Commit**

```bash
git add README.md docs/API.md BUILD.md CHANGELOG.md addons scripts/test-foundry-script
git commit -m "docs: document typed Foundry Script observability API"
```

### Task 15: Run complete verification

**Files:**

- Modify only files required to fix verification failures caused by this refactor.

- [ ] **Step 1: Run core Foundry Script validation**

```bash
./scripts/test-foundry-script
./scripts/test-project
./scripts/test-foundry-uids
./scripts/test-package
```

Expected: every command exits zero; Foundry analysis reports zero diagnostics; project tests report zero failures.

- [ ] **Step 2: Run adapter and workflow contracts**

```bash
./scripts/test-ci-workflows
./scripts/test-sentry-ios-build-contract
./scripts/test-sentry-android-build-contract
```

Expected: every contract script exits zero.

- [ ] **Step 3: Run available native unit suites**

Run:

```bash
task test:sentry-swift
task test:sentry-java
```

Expected: both native unit suites pass. If Swift or the Android/Java toolchain is unavailable, record the exact failed command and missing prerequisite in the handoff; do not report that suite as passing.

- [ ] **Step 4: Audit the implementation against the design**

Check every acceptance criterion in `docs/superpowers/specs/2026-07-27-foundry-script-language-modeling-design.md`. Verify:

```bash
rg -n --glob '*.fs' \
	'(:|->|Array\[)[[:space:]]*Callable([[:space:]=,)\]]|$)|Callable\(\)|_probe\.call\(|_frame_supplier' \
	addons/FoundryObservability addons/FoundryObservabilitySentry
git diff --check
git status --short
```

Expected: `rg` and `git diff --check` print nothing. `git status --short` lists only intentional implementation changes, or is clean after the final commit.

- [ ] **Step 5: Commit verification fixes**

If verification required changes:

```bash
git add addons test_project scripts README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md
git commit -m "fix: complete Foundry Script model verification"
```

If no changes were required, do not create an empty commit.

- [ ] **Step 6: Capture final evidence**

Run again after the final commit:

```bash
./scripts/test-foundry-script
./scripts/test-project
./scripts/test-foundry-uids
./scripts/test-package
git diff --check
git status --short
```

Expected: all validation commands exit zero, checks print no failures, and the worktree is clean.
