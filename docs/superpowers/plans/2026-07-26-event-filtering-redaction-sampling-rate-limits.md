# Event Filtering, Redaction, Sampling, and Rate Limits Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one provider-neutral, fail-closed processing pipeline for redaction, ordered replacement-or-drop event, structured log, and metric processors, deterministic sampling, independent event/log/metric limits, recursion protection, and payload-free diagnostics.

**Architecture:** Add immutable public policy/diagnostic DTOs, a shared redactor, one bounded limiter per signal, and a coordinator that runs the approved processing order. `FoundryObservability` commits a candidate pipeline atomically with provider configuration and delegates all Foundry-originated signals and provider-owned state through it; the Sentry provider reuses the redactor only for Foundry-owned runtime contexts and built-in attachment metadata created after the core boundary.

**Tech Stack:** FoundryScript, Foundry testlib, Godot `RegEx`, `Mutex`, `Time`, and `Engine` APIs, Bash contract tests, Swift/XCTest, Java/JUnit, Task, Prek, and tracked FoundryScript UID companions.

---

## File structure

Create these provider-neutral resources and matching `.fs.uid` companions:

- `addons/FoundryObservability/ObservabilitySignalLimits.fs`: immutable public
  per-frame, repeated, and sliding-window settings.
- `addons/FoundryObservability/ObservabilityRedactionRule.fs`: immutable public
  canonical-path redaction rule.
- `addons/FoundryObservability/ObservabilityRedactionPolicy.fs`: immutable
  ordered rule collection.
- `addons/FoundryObservability/ObservabilityProcessingDiagnostic.fs`: immutable
  payload-free processing outcome.
- `addons/FoundryObservability/ObservabilityRedactor.fs`: shared rule matching,
  typed DTO traversal, reconstruction, and provider-state redaction.
- `addons/FoundryObservability/ObservabilitySignalLimiter.fs`: bounded sampler,
  frame counter, repeated digest cache, and sliding window for one signal.
- `addons/FoundryObservability/ObservabilityProcessingPipeline.fs`: candidate
  configuration, processor ordering, recursion guard, signal coordination, and
  diagnostic publication.

Modify these provider-neutral resources:

- `addons/FoundryObservability/ObservabilityConfig.fs`: append processing
  options, copy callables and policy values, and map compatibility inputs.
- `addons/FoundryObservability/FoundryObservabilityApi.fs`: expose the latest
  processing diagnostic.
- `addons/FoundryObservability/FoundryObservability.fs`: commit candidate
  pipelines atomically and delegate signal/state processing.
- `addons/FoundryObservability/ObservabilityMetric.fs`: make its private fields
  formally immutable.
- `addons/FoundryObservability/ObservabilityAttachmentFailure.fs`: add the
  payload-free redaction omission reason.
- `addons/FoundryObservability/AutomaticObservabilityLogger.fs`: remove
  automatic-only event admission state.

Modify the Sentry adapter:

- `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`: commit the
  shared redactor with configuration and apply it to runtime contexts and
  provider-built attachment metadata.

Modify tests and contracts:

- `test_project/tests/observability-core.test.fs`
- `test_project/tests/observability-sentry.test.fs`
- `scripts/test-foundry-script`
- `scripts/test-package`
- `README.md`
- `docs/API.md`
- `CHANGELOG.md`

No Swift, Java, native bridge API, workflow, or packaging script source changes
are planned. Their complete tests remain required boundary verification.

### Task 1: Public processing contracts and configuration

**Files:**

- Create: `addons/FoundryObservability/ObservabilitySignalLimits.fs`
- Create: `addons/FoundryObservability/ObservabilitySignalLimits.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityRedactionRule.fs`
- Create: `addons/FoundryObservability/ObservabilityRedactionRule.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityRedactionPolicy.fs`
- Create: `addons/FoundryObservability/ObservabilityRedactionPolicy.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityProcessingDiagnostic.fs`
- Create: `addons/FoundryObservability/ObservabilityProcessingDiagnostic.fs.uid`
- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `addons/FoundryObservability/ObservabilityMetric.fs`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Add failing resource and immutable-value contracts**

Extend the required-resource section of `scripts/test-foundry-script` with:

```bash
for processing_resource in \
	ObservabilitySignalLimits \
	ObservabilityRedactionRule \
	ObservabilityRedactionPolicy \
	ObservabilityProcessingDiagnostic; do
	[[ -f "$addon/${processing_resource}.fs" ]] \
		|| fail "${processing_resource}.fs is missing"
	[[ -s "$addon/${processing_resource}.fs.uid" ]] \
		|| fail "${processing_resource}.fs.uid is missing or empty"
done

for field in _type _name _value _unit _attributes; do
	rg -q "^final var ${field}:" "$addon/ObservabilityMetric.fs" \
		|| fail "ObservabilityMetric field must be immutable: ${field}"
done
```

Add these tests beside the existing config and DTO cases:

```foundryscript
func test_processing_value_types_copy_inputs_and_normalize_limits() -> void:
	var limits := ObservabilitySignalLimits.new(-1, 250, 3, 1000)
	Expect.that(limits.per_frame()).to_equal(0)
	Expect.that(limits.repeated_window_msec()).to_equal(250)
	Expect.that(limits.window_count()).to_equal(3)
	Expect.that(limits.window_msec()).to_equal(1000)

	var path := PackedStringArray(["event", "**", "password"])
	var rule := ObservabilityRedactionRule.replace_text(
			path,
			"[A-Z0-9._%+-]+@[A-Z0-9.-]+",
			"[redacted-email]",
		)
	path[2] = "changed"
	Expect.that(rule.path()).to_equal(
			PackedStringArray(["event", "**", "password"]),
		)
	Expect.that(rule.action()).to_equal(
			ObservabilityRedactionRule.REPLACE_TEXT,
		)

	var source_rules: Array[ObservabilityRedactionRule] = [rule]
	var policy := ObservabilityRedactionPolicy.new(source_rules)
	source_rules.clear()
	Expect.that(policy.rules().size()).to_equal(1)

	var diagnostic := ObservabilityProcessingDiagnostic.new(
			7,
			&"event",
			&"dropped",
			&"processor",
			1,
			-1,
			&"",
			Error.OK,
		)
	Expect.that(diagnostic.sequence()).to_equal(7)
	Expect.that(diagnostic.signal()).to_equal(&"event")
	Expect.that(diagnostic.reason()).to_equal(&"processor")


func test_processing_config_defaults_and_compatibility_are_signal_local() -> void:
	var defaults := ObservabilityConfig.new()
	Expect.that(defaults.event_sample_rate).to_equal(1.0)
	Expect.that(defaults.log_sample_rate).to_equal(1.0)
	Expect.that(defaults.metric_sample_rate).to_equal(1.0)
	Expect.that(defaults.event_limits().per_frame()).to_equal(5)
	Expect.that(defaults.event_limits().repeated_window_msec()).to_equal(1000)
	Expect.that(defaults.event_limits().window_count()).to_equal(20)
	Expect.that(defaults.event_limits().window_msec()).to_equal(10000)
	Expect.that(defaults.log_limits().per_frame()).to_equal(0)
	Expect.that(defaults.metric_limits().per_frame()).to_equal(0)
	Expect.that(defaults.event_processors().is_empty()).to_be_true()
	Expect.that(defaults.log_processors().is_empty()).to_be_true()
	Expect.that(defaults.metric_processors().is_empty()).to_be_true()

	var compatibility := ObservabilityConfig.new(
			p_automatic_events_per_frame = 2,
			p_automatic_repeated_error_window_msec = 3,
			p_automatic_event_throttle_count = 4,
			p_automatic_event_throttle_window_msec = 5,
		)
	Expect.that(compatibility.event_limits().per_frame()).to_equal(2)
	Expect.that(compatibility.event_limits().repeated_window_msec()).to_equal(3)
	Expect.that(compatibility.event_limits().window_count()).to_equal(4)
	Expect.that(compatibility.event_limits().window_msec()).to_equal(5)
```

- [ ] **Step 2: Run the contract and project tests to verify RED**

Run:

```bash
scripts/test-foundry-script
```

Expected: FAIL with `ObservabilitySignalLimits.fs is missing`.

Run:

```bash
scripts/test-project
```

Expected: FAIL during source analysis because the four public classes and new
configuration methods do not exist.

- [ ] **Step 3: Implement the four immutable public DTOs**

Create `ObservabilitySignalLimits.fs` with this complete public shape:

```foundryscript
namespace foundry.observability

## Immutable limits for one observability signal.
class_name ObservabilitySignalLimits
extends RefCounted

final var _per_frame: int
final var _repeated_window_msec: int
final var _window_count: int
final var _window_msec: int


func _init(
		p_per_frame: int = 0,
		p_repeated_window_msec: int = 0,
		p_window_count: int = 0,
		p_window_msec: int = 0,
) -> void:
	_per_frame = maxi(0, p_per_frame)
	_repeated_window_msec = maxi(0, p_repeated_window_msec)
	_window_count = maxi(0, p_window_count)
	_window_msec = maxi(0, p_window_msec)


func per_frame() -> int:
	return _per_frame


func repeated_window_msec() -> int:
	return _repeated_window_msec


func window_count() -> int:
	return _window_count


func window_msec() -> int:
	return _window_msec


func duplicate() -> ObservabilitySignalLimits:
	return ObservabilitySignalLimits.new(
			_per_frame,
			_repeated_window_msec,
			_window_count,
			_window_msec,
		)
```

Create `ObservabilityRedactionRule.fs` with constants `REMOVE_FIELD = 0`,
`REPLACE_VALUE = 1`, and `REPLACE_TEXT = 2`; final `_path`, `_action`,
`_pattern`, and `_replacement` fields; defensive accessors; `duplicate()`; and
these named factories:

```foundryscript
static func remove_field(path: PackedStringArray) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(path, REMOVE_FIELD)


static func replace_value(
		path: PackedStringArray,
		replacement: Variant,
) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(path, REPLACE_VALUE, "", replacement)


static func replace_text(
		path: PackedStringArray,
		pattern: String = "",
		replacement: String = "[REDACTED]",
) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(
			path,
			REPLACE_TEXT,
			pattern,
			replacement,
		)


static func sensitive_key(
		key: String,
		replacement: String = "[REDACTED]",
) -> ObservabilityRedactionRule:
	return replace_text(PackedStringArray(["**", key]), "", replacement)
```

Its `is_valid()` must reject an empty path, empty ordinary path segments,
unknown actions, non-string text replacements, invalid `RegEx` patterns, and
`REMOVE_FIELD` or `REPLACE_VALUE` rules carrying a text pattern.

Create `ObservabilityRedactionPolicy.fs` with a final typed rule array,
constructor validation through `is_valid()`, copied `rules()`, and `duplicate()`.

Create `ObservabilityProcessingDiagnostic.fs` with the eight final fields and
accessors from the test. Add constants for every approved signal, outcome,
reason, and limit kind so call sites do not use unscoped string literals.

Create these UID companions:

```text
ObservabilitySignalLimits.fs.uid: uid://d13sgnllmt1
ObservabilityRedactionRule.fs.uid: uid://d13rdctrule1
ObservabilityRedactionPolicy.fs.uid: uid://d13rdctplcy1
ObservabilityProcessingDiagnostic.fs.uid: uid://d13prcsdiag1
```

- [ ] **Step 4: Append configuration without breaking positional callers**

Make `ObservabilityMetric`'s five private fields `final`.

Add `event_sample_rate` and `log_sample_rate` public fields to
`ObservabilityConfig`. Store private copied arrays and values:

```foundryscript
var event_sample_rate: float = 1.0
var log_sample_rate: float = 1.0
var _event_processors: Array[Callable] = []
var _log_processors: Array[Callable] = []
var _metric_processors: Array[Callable] = []
var _event_limits: ObservabilitySignalLimits
var _log_limits: ObservabilitySignalLimits
var _metric_limits: ObservabilitySignalLimits
var _redaction_policy: ObservabilityRedactionPolicy
```

Append, after `p_attach_scene_tree`, the exact constructor parameters:

```foundryscript
		p_event_sample_rate: float = 1.0,
		p_log_sample_rate: float = 1.0,
		p_event_processors: Array[Callable] = [],
		p_log_processors: Array[Callable] = [],
		p_metric_processors: Array[Callable] = [],
		p_event_limits: ObservabilitySignalLimits? = null,
		p_log_limits: ObservabilitySignalLimits? = null,
		p_metric_limits: ObservabilitySignalLimits? = null,
		p_redaction_policy: ObservabilityRedactionPolicy? = null,
```

Copy processor arrays item by item, preserving invalid callables for atomic
configuration validation. Resolve values with:

```foundryscript
	event_sample_rate = p_event_sample_rate
	log_sample_rate = p_log_sample_rate
	_event_processors = p_event_processors.duplicate()
	_log_processors = p_log_processors.duplicate()
	_metric_processors = p_metric_processors.duplicate()
	_event_limits = (
			p_event_limits.duplicate()
			if p_event_limits != null
			else ObservabilitySignalLimits.new(
					automatic_events_per_frame,
					automatic_repeated_error_window_msec,
					automatic_event_throttle_count,
					automatic_event_throttle_window_msec,
				)
		)
	_log_limits = (
			p_log_limits.duplicate()
			if p_log_limits != null
			else ObservabilitySignalLimits.new()
		)
	_metric_limits = (
			p_metric_limits.duplicate()
			if p_metric_limits != null
			else ObservabilitySignalLimits.new()
		)
	_redaction_policy = (
			p_redaction_policy.duplicate()
			if p_redaction_policy != null
			else ObservabilityRedactionPolicy.new()
		)
```

Add defensive `event_processors()`, `log_processors()`,
`metric_processors()`, `event_limits()`, `log_limits()`, `metric_limits()`, and
`redaction_policy()` accessors.

- [ ] **Step 5: Run GREEN tests and UID verification**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: both PASS, including the new DTO/config cases.

Run:

```bash
git add addons/FoundryObservability scripts/test-foundry-script \
  test_project/tests/observability-core.test.fs
git commit -m "feat: define observability processing contracts"
```

### Task 2: Canonical redaction engine

**Files:**

- Create: `addons/FoundryObservability/ObservabilityRedactor.fs`
- Create: `addons/FoundryObservability/ObservabilityRedactor.fs.uid`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Add failing redaction behavior tests**

Add a required immutable resource check for `ObservabilityRedactor` and these
focused cases:

```foundryscript
func test_redactor_applies_recursive_keys_paths_and_text_patterns() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.sensitive_key("password"),
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["event", "attributes", "delete_me"]),
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["event", "**"]),
				"[0-9]{3}-[0-9]{2}-[0-9]{4}",
				"[ssn]",
			),
	])
	var redactor := ObservabilityRedactor.new(policy)
	var result: Dictionary = redactor.redact_event(ObservabilityEvent.new(
			p_message = "customer 123-45-6789",
			p_attributes = {
				"password": "secret",
				"nested": {"PASSWORD": "second"},
				"delete_me": "gone",
			},
		), &"event")
	Expect.that(result["valid"]).to_be_true()
	var event: ObservabilityEvent = result["value"]
	Expect.that(event.message()).to_equal("customer [ssn]")
	Expect.that(event.attributes()["password"]).to_equal("[REDACTED]")
	Expect.that(event.attributes()["nested"]["PASSWORD"]).to_equal("[REDACTED]")
	Expect.that(event.attributes().has("delete_me")).to_be_false()


func test_redactor_rebuilds_every_provider_owned_value_type() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["**"]),
				"secret",
				"safe",
			),
	])
	var redactor := ObservabilityRedactor.new(policy)
	var metric_result := redactor.redact_metric(ObservabilityMetric.new(
			ObservabilityMetricType.GAUGE,
			"secret.metric",
			1.0,
			"secret",
			{"token": "secret"},
		))
	Expect.that(metric_result["value"].name()).to_equal("safe.metric")
	Expect.that(metric_result["value"].attributes()["token"]).to_equal("safe")

	var user_result := redactor.redact_user(ObservabilityUser.new(
			"secret-id",
			"secret-name",
			"secret@example.invalid",
		))
	Expect.that(user_result["value"].application_user_id()).to_equal("safe-id")

	var breadcrumb_result := redactor.redact_breadcrumb(ObservabilityBreadcrumb.new(
			"secret message",
			ObservabilityLevel.INFO,
			&"secret.category",
			1,
			{"token": "secret"},
		))
	Expect.that(breadcrumb_result["value"].message()).to_equal("safe message")

	var attachment_result := redactor.redact_attachment(
			ObservabilityAttachment.from_path(
					"user://private.log",
					"secret.log",
					"text/plain",
				),
		)
	Expect.that(attachment_result["value"].path()).to_equal("user://private.log")
	Expect.that(attachment_result["value"].filename()).to_equal("safe.log")


func test_redactor_fails_closed_on_incompatible_runtime_matches() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_value(
				PackedStringArray(["event", "level"]),
				"not-an-integer",
			),
	])
	var result := ObservabilityRedactor.new(policy).redact_event(
			ObservabilityEvent.new(),
			&"event",
		)
	Expect.that(result["valid"]).to_be_false()
	Expect.that(result["rule_index"]).to_equal(0)
```

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: the contract fails because `ObservabilityRedactor.fs` is missing; after
adding an empty resource shell, the project test fails because typed redaction
methods are absent.

- [ ] **Step 3: Implement deterministic path matching and tree traversal**

Create `ObservabilityRedactor.fs` with:

```foundryscript
namespace foundry.observability

## Applies a committed provider-neutral redaction policy.
class_name ObservabilityRedactor
extends RefCounted

final var _policy: ObservabilityRedactionPolicy
final var _compiled_patterns: Array[RegEx] = []


func _init(policy: ObservabilityRedactionPolicy? = null) -> void:
	_policy = (
			policy.duplicate()
			if policy != null
			else ObservabilityRedactionPolicy.new()
		)
	for rule: ObservabilityRedactionRule in _policy.rules():
		var compiled := RegEx.new()
		if rule.action() == ObservabilityRedactionRule.REPLACE_TEXT \
				and not rule.pattern().is_empty():
			compiled.compile(rule.pattern())
		_compiled_patterns.append(compiled)
```

Implement `_path_matches(pattern, path, pattern_index, path_index)` with these
exact recursive rules:

```foundryscript
func _path_matches(
		pattern: PackedStringArray,
		path: PackedStringArray,
		pattern_index: int = 0,
		path_index: int = 0,
) -> bool:
	if pattern_index == pattern.size():
		return path_index == path.size()
	var segment: String = pattern[pattern_index]
	if segment == "**":
		if _path_matches(pattern, path, pattern_index + 1, path_index):
			return true
		return path_index < path.size() and _path_matches(
				pattern,
				path,
				pattern_index,
				path_index + 1,
			)
	if path_index == path.size():
		return false
	if segment != "*" and segment.to_lower() != path[path_index].to_lower():
		return false
	return _path_matches(pattern, path, pattern_index + 1, path_index + 1)
```

Implement `_redact_value(value, path, parent_is_dictionary)` to:

1. apply matching rules in order;
2. return `{"valid": true, "removed": true}` only for `REMOVE_FIELD` on a
   dictionary child;
3. reject incompatible `REPLACE_VALUE`;
4. use `RegEx.sub(str(value), replacement, true)` for patterned text and the
   complete replacement for an empty pattern;
5. recursively rebuild arrays and dictionaries after rule application;
6. return the first failing rule index without returning the source value.

Never mutate the source array, dictionary, or DTO.

- [ ] **Step 4: Implement exact typed conversion boundaries**

Implement `redact_event(event, signal)`, `redact_metric(metric)`,
`redact_contexts(contexts)`, `redact_user(user)`,
`redact_breadcrumb(breadcrumb)`, `redact_attachment(attachment)`, and
`redact_attachment_payload(payload)` by converting to canonical dictionaries,
calling `_redact_value`, validating the resulting types, and reconstructing the
existing DTO constructors.

Use `event` or `log` as the event root. Include event exception fields, every
structured frame field, frame variables, and event-local scope. Use the
`contexts`, `user`, `breadcrumbs`, and `attachments` roots for provider state.
`redact_attachment()` must always reconstruct with the original `path()` or
`bytes()` source and only redacted filename, content type, and category.

Create:

```text
addons/FoundryObservability/ObservabilityRedactor.fs.uid
```

with:

```text
uid://d13rdctcore1
```

- [ ] **Step 5: Run GREEN and commit**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: PASS with the redaction cases proving input isolation, path grammar,
case-insensitive matching, full text substitution, typed reconstruction, and
fail-closed incompatibility.

Run:

```bash
git add addons/FoundryObservability/ObservabilityRedactor.fs \
  addons/FoundryObservability/ObservabilityRedactor.fs.uid \
  scripts/test-foundry-script test_project/tests/observability-core.test.fs
git commit -m "feat: add provider-neutral payload redaction"
```

### Task 3: Bounded deterministic signal limiter

**Files:**

- Create: `addons/FoundryObservability/ObservabilitySignalLimiter.fs`
- Create: `addons/FoundryObservability/ObservabilitySignalLimiter.fs.uid`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Add failing sampler and limiter tests**

Require the new immutable resource and add:

```foundryscript
func test_signal_limiter_sampling_is_deterministic_and_precedes_limits() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			0.25,
			ObservabilitySignalLimits.new(1, 0, 0, 0),
		)
	for index: int in range(3):
		var sampled := limiter.admit("sample-%s" % index, 100, 1)
		Expect.that(sampled["reason"]).to_equal(&"sampled")
	var accepted := limiter.admit("sample-3", 100, 1)
	Expect.that(accepted["accepted"]).to_be_true()
	var limited := limiter.admit("sample-4", 100, 1)
	Expect.that(limited["reason"]).to_equal(&"sampled")


func test_signal_limiter_checks_frame_repeat_and_window_atomically() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0,
			ObservabilitySignalLimits.new(2, 100, 2, 1000),
		)
	Expect.that(limiter.admit("a", 0, 1)["accepted"]).to_be_true()
	Expect.that(limiter.admit("a", 1, 1)["limit_kind"]).to_equal(&"repeated")
	Expect.that(limiter.admit("b", 2, 1)["accepted"]).to_be_true()
	Expect.that(limiter.admit("c", 3, 1)["limit_kind"]).to_equal(&"per_frame")
	Expect.that(limiter.admit("c", 4, 2)["limit_kind"]).to_equal(&"window")
	Expect.that(limiter.admit("c", 1001, 2)["accepted"]).to_be_true()


func test_signal_limiter_bounds_repeated_identity_state() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0,
			ObservabilitySignalLimits.new(0, 100000, 0, 0),
		)
	for index: int in range(1025):
		Expect.that(limiter.admit("identity-%s" % index, index, index)["accepted"]) \
				.to_be_true()
	Expect.that(limiter.admit("identity-0", 2000, 2000)["accepted"]).to_be_true()
	Expect.that(limiter.admit("identity-1024", 2001, 2001)["limit_kind"]) \
			.to_equal(&"repeated")


func test_signal_limiter_reserves_legacy_log_window_atomically() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0,
			ObservabilitySignalLimits.new(1, 0, 0, 0),
			1,
		)
	Expect.that(limiter.admit("first", 0, 1)["accepted"]).to_be_true()
	Expect.that(limiter.admit("legacy-drop", 500, 2)["limit_kind"]) \
			.to_equal(&"legacy_log_window")
	Expect.that(limiter.admit("next-second", 1000, 2)["accepted"]).to_be_true()
```

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: FAIL because `ObservabilitySignalLimiter` does not exist.

- [ ] **Step 3: Implement admission without partial capacity consumption**

Create `ObservabilitySignalLimiter.fs` with `MAX_IDENTITIES = 1024`, copied
limits, an optional legacy fixed one-second count, sample accumulator, current
frame/count, accepted timepoints, legacy second/count, digest records, and
monotonic sequence. The third constructor parameter is
`p_legacy_limit_per_second: int = 0`.

Implement:

```foundryscript
func admit(identity: String, now_msec: int, frame_index: int) -> Dictionary:
	_sample_accumulator += _sample_rate
	if _sample_accumulator < 1.0:
		return _drop(&"sampled", &"")
	_sample_accumulator -= 1.0

	var candidate_frame_count: int = _frame_count
	if frame_index != _current_frame:
		candidate_frame_count = 0
	if _limits.per_frame() > 0 \
			and candidate_frame_count >= _limits.per_frame():
		return _drop(&"rate_limited", &"per_frame")

	_prune_window(now_msec)
	var digest: String = identity.sha256_text()
	var previous: Variant = _identities.get(digest)
	if _limits.repeated_window_msec() > 0 and previous is Dictionary:
		var previous_record: Dictionary = previous
		if now_msec - int(previous_record["time"]) \
				< _limits.repeated_window_msec():
			return _drop(&"rate_limited", &"repeated")

	if _limits.window_count() > 0 and _limits.window_msec() > 0 \
			and _timepoints.size() >= _limits.window_count():
		return _drop(&"rate_limited", &"window")

	var candidate_legacy_count: int = _legacy_count
	var legacy_second: int = floori(float(now_msec) / 1000.0)
	if legacy_second != _legacy_second:
		candidate_legacy_count = 0
	if _legacy_limit_per_second > 0 \
			and candidate_legacy_count >= _legacy_limit_per_second:
		return _drop(&"rate_limited", &"legacy_log_window")

	_current_frame = frame_index
	_frame_count = candidate_frame_count + 1
	if _limits.window_count() > 0 and _limits.window_msec() > 0:
		_timepoints.append(now_msec)
	if _limits.repeated_window_msec() > 0:
		_identity_sequence += 1
		_identities[digest] = {"time": now_msec, "sequence": _identity_sequence}
		_evict_oldest_identity()
	_legacy_second = legacy_second
	if _legacy_limit_per_second > 0:
		_legacy_count = candidate_legacy_count + 1
	return {"accepted": true, "reason": &"", "limit_kind": &""}
```

`_prune_window()` removes timestamps whose age is greater than or equal to the
configured window. `_evict_oldest_identity()` scans the bounded dictionary for
the smallest sequence and erases it only when size exceeds 1,024. No frame,
repeat, sliding, or legacy state commits until every applicable limit accepts.
`reset()` clears every field. Constructor validation clamps the sample rate only
after callers have validated it.

Create the UID:

```text
uid://d13sgnlmit1
```

- [ ] **Step 4: Run GREEN and commit**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: PASS with deterministic sampling and every limiter mode.

Run:

```bash
git add addons/FoundryObservability/ObservabilitySignalLimiter.fs \
  addons/FoundryObservability/ObservabilitySignalLimiter.fs.uid \
  scripts/test-foundry-script test_project/tests/observability-core.test.fs
git commit -m "feat: add bounded signal sampling and limits"
```

### Task 4: Processing coordinator, processors, recursion, and diagnostics

**Files:**

- Create: `addons/FoundryObservability/ObservabilityProcessingPipeline.fs`
- Create: `addons/FoundryObservability/ObservabilityProcessingPipeline.fs.uid`
- Modify: `scripts/test-foundry-script`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Add failing coordinator tests and processor helpers**

Add test fields and helpers:

```foundryscript
var _processing_order: Array[String] = []
var _recursive_pipeline: ObservabilityProcessingPipeline


func _processing_replace_first(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("first")
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message() + "-first",
			p_source = event.source(),
			p_timestamp_msec = event.timestamp_msec(),
			p_attributes = event.attributes(),
			p_exception = event.exception(),
			p_engine_ticks_msec = event.engine_ticks_msec(),
			p_scope = event.scope(),
		)


func _processing_replace_second(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("second")
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message() + "-second",
			p_source = event.source(),
			p_timestamp_msec = event.timestamp_msec(),
			p_attributes = event.attributes(),
			p_exception = event.exception(),
			p_engine_ticks_msec = event.engine_ticks_msec(),
			p_scope = event.scope(),
		)


func _processing_drop(_event: ObservabilityEvent) -> Variant:
	return null


func _processing_wrong_type(_event: ObservabilityEvent) -> Variant:
	return ObservabilityMetric.new()


func _processing_reenter(event: ObservabilityEvent) -> ObservabilityEvent:
	_recursive_pipeline.process_event(event)
	return event
```

Add:

```foundryscript
func test_pipeline_redacts_chains_replacements_and_redacts_again() -> void:
	_processing_order.clear()
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 2,
		)
	var result := pipeline.configure(ObservabilityConfig.new(
			p_event_processors = [
				Callable(self, "_processing_replace_first"),
				Callable(self, "_processing_replace_second"),
			],
			p_event_limits = ObservabilitySignalLimits.new(),
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_text(
						PackedStringArray(["event", "message"]),
						"secret",
						"safe",
					),
			]),
		))
	Expect.that(result).to_equal(Error.OK)
	var processed := pipeline.process_event(ObservabilityEvent.new(
			p_message = "secret",
		))
	Expect.that(processed["accepted"]).to_be_true()
	Expect.that(processed["value"].message()).to_equal("safe-first-second")
	Expect.that(_processing_order).to_equal(["first", "second"])


func test_pipeline_reports_processor_drop_and_invalid_result_without_payload() -> void:
	var dropped := ObservabilityProcessingPipeline.new()
	Expect.that(dropped.configure(ObservabilityConfig.new(
			p_event_processors = [Callable(self, "_processing_drop")],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)
	Expect.that(dropped.process_event(ObservabilityEvent.new())["accepted"]) \
			.to_be_false()
	Expect.that(dropped.last_diagnostic().reason()).to_equal(&"processor")
	Expect.that(dropped.last_diagnostic().error()).to_equal(Error.OK)

	var invalid := ObservabilityProcessingPipeline.new()
	Expect.that(invalid.configure(ObservabilityConfig.new(
			p_event_processors = [Callable(self, "_processing_wrong_type")],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)
	Expect.that(invalid.process_event(ObservabilityEvent.new())["accepted"]) \
			.to_be_false()
	Expect.that(invalid.last_diagnostic().reason()) \
			.to_equal(&"invalid_processor_result")
	Expect.that(invalid.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)


func test_pipeline_blocks_recursive_processor_capture() -> void:
	_recursive_pipeline = ObservabilityProcessingPipeline.new()
	Expect.that(_recursive_pipeline.configure(ObservabilityConfig.new(
			p_event_processors = [Callable(self, "_processing_reenter")],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)
	var result := _recursive_pipeline.process_event(ObservabilityEvent.new())
	Expect.that(result["accepted"]).to_be_true()
	Expect.that(_recursive_pipeline.recursive_drop_count()).to_equal(1)
```

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: FAIL because the coordinator class is missing.

- [ ] **Step 3: Implement candidate validation and signal-local state**

Create `ObservabilityProcessingPipeline.fs` with:

```foundryscript
namespace foundry.observability

## Coordinates provider-neutral processing before provider delivery.
class_name ObservabilityProcessingPipeline
extends RefCounted

var _clock: Callable
var _frame: Callable
var _redactor: ObservabilityRedactor
var _event_processors: Array[Callable] = []
var _log_processors: Array[Callable] = []
var _metric_processors: Array[Callable] = []
var _metric_filter: Callable
var _event_limiter: ObservabilitySignalLimiter
var _log_limiter: ObservabilitySignalLimiter
var _metric_limiter: ObservabilitySignalLimiter
var _processing_depth: int = 0
var _recursive_drops: int = 0
var _diagnostic_sequence: int = 0
var _last_diagnostic: ObservabilityProcessingDiagnostic?
var _state_mutex: Mutex = Mutex.new()
```

The constructor installs `Time.get_ticks_msec()` and
`Engine.get_process_frames()` lambdas when callables are absent.

`configure(config)` must validate all three finite sample rates, every callable,
the policy, and every limit DTO before replacing local state. It constructs one
limiter per signal and an `ObservabilityRedactor`, copies processors, passes
`config.log_rate_limit_per_second` as the log limiter's third constructor
argument, stores the metric predicate, then clears diagnostics and recursion
counts.

- [ ] **Step 4: Implement ordered processing and diagnostics**

Implement:

```foundryscript
func process_event(event: ObservabilityEvent) -> Dictionary:
	var signal: StringName = &"log" if event != null and event.kind() == &"log" else &"event"
	return _process_event_signal(event, signal)


func process_metric(metric: ObservabilityMetric) -> Dictionary:
	return _process_metric_signal(metric)
```

For each signal:

1. reject recursive entry before calling redaction or processors;
2. pre-redact and validate;
3. run only that signal's processors in array order;
4. treat `null` as reason `processor`;
5. reject the wrong type or a log/event kind change with
   `invalid_processor_result`;
6. post-redact and validate;
7. run the legacy metric predicate after pre-redaction and before metric
   processors;
8. derive the approved non-attribute identity;
9. call the signal-local limiter with injected time/frame;
10. return `{"accepted": true, "value": replacement, "signal": signal}` without
    publishing provider acceptance yet.

Add `record_provider_result(signal, accepted, error)` so the service publishes
`accepted` or `provider_rejected` after dispatch. Every drop path publishes the
approved payload-free diagnostic. `last_diagnostic()` returns the immutable
value. `recursive_drop_count()` is a stable diagnostic counter, not a payload or
test-only mutation API.

Never hold `_state_mutex` across a redactor call, processor callable, metric
predicate, or provider call. Use it only to reserve/release `_processing_depth`
and publish/copy state.

Create:

```text
addons/FoundryObservability/ObservabilityProcessingPipeline.fs.uid
```

with:

```text
uid://d13prcspipe1
```

- [ ] **Step 5: Run GREEN and commit**

Run:

```bash
scripts/test-foundry-script
scripts/test-project
```

Expected: PASS for replacement order, drop/error semantics, two-pass redaction,
signal separation, and recursion.

Run:

```bash
git add addons/FoundryObservability/ObservabilityProcessingPipeline.fs \
  addons/FoundryObservability/ObservabilityProcessingPipeline.fs.uid \
  scripts/test-foundry-script test_project/tests/observability-core.test.fs
git commit -m "feat: coordinate observability signal processing"
```

### Task 5: Integrate event, log, and metric delivery atomically

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Test: `test_project/tests/observability-core.test.fs`
- Test: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing service-level processor and diagnostic tests**

Add service tests proving:

```foundryscript
func test_service_processors_replace_or_drop_before_provider_delivery() -> void:
	var provider := MemoryObservabilityProvider.new()
	var service := FoundryObservability.new(
			ObservabilityStartupSettings.new(p_capture_enabled = false),
			"",
			func() -> int: return 100,
			func() -> int: return 1,
		)
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_event_processors = [Callable(self, "_processing_replace_first")],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)
	Expect.that(service.capture_message("original")).to_equal("memory:1")
	Expect.that(provider.events()[0].message()).to_equal("original-first")
	Expect.that(service.last_processing_diagnostic().outcome()).to_equal(&"accepted")

	var dropping_provider := MemoryObservabilityProvider.new()
	var dropping_service := FoundryObservability.new(
			ObservabilityStartupSettings.new(p_capture_enabled = false),
		)
	Expect.that(dropping_service.configure(
			dropping_provider,
			ObservabilityConfig.new(
					p_event_processors = [Callable(self, "_processing_drop")],
					p_event_limits = ObservabilitySignalLimits.new(),
				),
		)).to_equal(Error.OK)
	Expect.that(dropping_service.capture_message("drop")).to_equal("")
	Expect.that(dropping_provider.events().is_empty()).to_be_true()
	Expect.that(dropping_service.last_error()).to_equal(Error.OK)
	Expect.that(dropping_service.last_processing_diagnostic().reason()) \
			.to_equal(&"processor")


func test_service_keeps_event_log_and_metric_capacity_independent() -> void:
	var provider := MemoryObservabilityProvider.new()
	var service := FoundryObservability.new(
			ObservabilityStartupSettings.new(p_capture_enabled = false),
			"",
			func() -> int: return 10,
			func() -> int: return 1,
		)
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_event_limits = ObservabilitySignalLimits.new(1),
			p_log_limits = ObservabilitySignalLimits.new(1),
			p_metric_limits = ObservabilitySignalLimits.new(1),
		))).to_equal(Error.OK)
	Expect.that(service.capture_message("event")).not_to_equal("")
	Expect.that(service.capture_log("log")).not_to_equal("")
	Expect.that(service.capture_counter("metric")).to_be_true()
	Expect.that(service.capture_message("event-2")).to_equal("")
	Expect.that(service.capture_log("log-2")).to_equal("")
	Expect.that(service.capture_counter("metric-2")).to_be_false()
```

Add a Sentry provider test showing only the replacement event reaches
`fake_sentry_bridge.notest.fs`.

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because `FoundryObservability` does not accept the injected
processing clock/frame, does not expose diagnostics, and bypasses the pipeline.

- [ ] **Step 3: Commit candidate pipelines with provider configuration**

Append `processing_clock: Callable = Callable()` and
`processing_frame: Callable = Callable()` to `FoundryObservability._init()`.
Store them and initialize a disabled pipeline.

In `configure()`:

```foundryscript
	var candidate_pipeline := ObservabilityProcessingPipeline.new(
			_processing_clock,
			_processing_frame,
		)
	var pipeline_result: int = candidate_pipeline.configure(candidate_config)
	if pipeline_result != Error.OK:
		_last_error = pipeline_result
		return pipeline_result
```

Create the candidate before `provider.configure()`. Commit `_pipeline =
candidate_pipeline` only after provider configuration and any old-provider
shutdown succeed. A same-provider successful reconfiguration also replaces the
pipeline. Failed provider candidates preserve the active pipeline.

Remove `_metric_sample_accumulator`, `_accept_metric_sample()`,
`_reset_metric_sampling()`, `_log_window_second`, `_log_window_count`,
`_accept_log()`, and `_reset_log_rate_limit()` after all call sites delegate to
the pipeline.

- [ ] **Step 4: Delegate signal delivery and expose diagnostics**

Add to `FoundryObservabilityApi`:

```foundryscript
## Returns a payload-free snapshot of the latest signal processing outcome.
abstract func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?
```

In `capture_event()`, preserve disabled and timestamp normalization gates, then
call `_pipeline.process_event(normalized)`. If rejected, set `_last_error` from
the diagnostic and return empty. Dispatch only `result["value"]`. Record
provider acceptance/rejection after `_capture_event()`.

In `capture_metric()`, retain existing normalization and capability validation,
then pass the normalized metric through `_pipeline.process_metric()`. Move the
legacy metric filter into the pipeline. Dispatch only the replacement and record
the provider result.

Do not run event processors for kind `log`; the pipeline selects log state from
the normalized kind. Disabled logs and minimum-level rejection remain pre-
pipeline no-ops.

Implement:

```foundryscript
func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?:
	return _pipeline.last_diagnostic()
```

Set `_last_error = Error.OK` for explicit processor, sampling, rate-limit, and
recursive drops; preserve `ERR_INVALID_DATA` for invalid/redaction drops and
existing provider errors for dispatch rejection.

- [ ] **Step 5: Run focused integration tests and commit**

Run:

```bash
scripts/test-project
```

Expected: PASS for all core and Sentry FoundryScript cases, including exact
sampling sequences, legacy metric-filter behavior, legacy log limits, failed
configuration preservation, and provider replacement.

Run:

```bash
git add addons/FoundryObservability/FoundryObservabilityApi.fs \
  addons/FoundryObservability/FoundryObservability.fs \
  test_project/tests/observability-core.test.fs \
  test_project/tests/observability-sentry.test.fs
git commit -m "feat: process signals before provider delivery"
```

### Task 6: Redact provider-owned state and Sentry-created metadata

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/ObservabilityAttachmentFailure.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Test: `test_project/tests/observability-core.test.fs`
- Test: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing state and provider-created metadata tests**

Add core tests that configure a policy matching `secret` everywhere, then call
`set_context`, `set_user`, `capture_breadcrumb`, and `add_attachment`. Assert
through `MemoryObservabilityProvider` snapshots that:

```foundryscript
Expect.that(provider.captured_scopes()[0]["contexts"]["account"]["token"]) \
		.to_equal("safe")
Expect.that(provider.captured_scopes()[0]["user"]["contact_email"]) \
		.to_equal("safe@example.invalid")
Expect.that(provider.breadcrumbs()[0].message()).to_equal("safe")
Expect.that(provider.captured_attachments()[0][0]["filename"]) \
		.to_equal("safe.log")
```

Add failure tests where a rule replaces a required typed field incompatibly.
Assert the provider mutation is not called, `last_error()` is
`ERR_INVALID_DATA`, and diagnostic reason is `redaction_failed`.

Add Sentry tests that make the runtime collector and built-in collector produce
matching metadata, then assert the fake bridge receives only redacted runtime
contexts and filenames. Add an attachment rule that invalidates a filename and
assert the event still captures while `last_attachment_failures()` contains the
new redaction reason.

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because provider-owned state and provider-created Sentry values
bypass the redactor.

- [ ] **Step 3: Redact core provider-state candidates**

Expose internal typed redaction wrappers from
`ObservabilityProcessingPipeline`:

```foundryscript
func redact_contexts(contexts: Dictionary) -> Dictionary:
	return _redactor.redact_contexts(contexts)

func redact_user(user: ObservabilityUser) -> Dictionary:
	return _redactor.redact_user(user)

func redact_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> Dictionary:
	return _redactor.redact_breadcrumb(breadcrumb)

func redact_attachment(attachment: ObservabilityAttachment) -> Dictionary:
	return _redactor.redact_attachment(attachment)
```

Each wrapper reserves processing recursion, records `redaction_failed` with
signal `state` on failure, and returns only the rebuilt value on success.

In `FoundryObservability`, rebuild a one-name context dictionary before
`_call_scope_operation`, redact users before `set_user`, redact both manual and
automatic breadcrumbs before provider calls, and redact attachments before
capability dispatch. Never retain the input after the call.

Add:

```foundryscript
const REDACTED: StringName = &"redacted"
```

to `ObservabilityAttachmentFailure`.

- [ ] **Step 4: Commit the shared redactor inside the Sentry provider**

Add `_redactor: ObservabilityRedactor` to `SentryObservabilityProvider`.
Construct `candidate_redactor` from `config.redaction_policy()` before runtime
context and built-in attachment candidates. Apply:

```foundryscript
	var stable_result: Dictionary = candidate_redactor.redact_contexts(
			candidate_stable_contexts,
		)
	if not stable_result["valid"]:
		return Error.ERR_INVALID_DATA
	candidate_stable_contexts = stable_result["value"]
```

Redact every built-in persistent and capture-local attachment dictionary with
`redact_attachment_payload()`. Omit invalid attachment metadata and append an
`ObservabilityAttachmentFailure` using reason `REDACTED` and
`Error.ERR_INVALID_DATA`. Apply the committed redactor to volatile
`contexts_for_capture()` before adding them to the event payload.

Commit `_redactor = candidate_redactor` only at the same point where
`_stable_contexts`, attachment configuration, and `_last_config_payload` commit.
Rollback and fail-closed paths preserve or reset the matching redactor with the
rest of the session.

- [ ] **Step 5: Run GREEN and commit**

Run:

```bash
scripts/test-project
```

Expected: PASS for all core state, attachment partial-failure, Sentry runtime
context, built-in attachment, rollback, and session-boundary tests.

Run:

```bash
git add addons/FoundryObservability/FoundryObservability.fs \
  addons/FoundryObservability/ObservabilityAttachmentFailure.fs \
  addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs \
  test_project/tests/observability-core.test.fs \
  test_project/tests/observability-sentry.test.fs
git commit -m "feat: redact observability state and metadata"
```

### Task 7: Move automatic errors onto the shared event limiter

**Files:**

- Modify: `addons/FoundryObservability/AutomaticObservabilityLogger.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Test: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Rewrite automatic admission tests to assert shared-pipeline semantics**

Add a test using a real `FoundryObservability` with injected
`AutomaticCaptureTime` callables. Configure one event per frame, a repeated
window, two events per sliding window, breadcrumbs, and logs. Call
`AutomaticObservabilityLogger._capture_error()` directly and assert:

```foundryscript
Expect.that(provider.events().filter(
		func(event: ObservabilityEvent) -> bool:
			return event.kind() != &"log"
	).size()).to_equal(1)
Expect.that(provider.breadcrumbs().size()).to_equal(3)
Expect.that(provider.events().filter(
		func(event: ObservabilityEvent) -> bool:
			return event.kind() == &"log"
	).size()).to_equal(3)
```

Advance frame/time and prove a manual `capture_message()` consumes the same
event capacity as the next automatic error. Configure separate log limits and
prove event drops do not consume log capacity.

Add a processor that calls `service.capture_message("nested")`; assert the
nested capture is dropped as `recursive`, the outer event is accepted once, and
the provider sees no nested payload.

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-project
```

Expected: FAIL because `AutomaticObservabilityLogger` still suppresses all
destinations with its own repeated-error state and does not share manual event
capacity.

- [ ] **Step 3: Remove automatic-only admission state**

Delete `_state_mutex`, `_error_timepoints`, `_event_timepoints`,
`_current_frame`, `_frame_event_count`, `_prune_event_timepoints()`, and every
pre-dispatch automatic event limit check.

Keep normalization, category masks, deterministic clock use for event
timestamps, and destination routing. After the change `_capture_error()`:

1. builds attributes once;
2. independently calls `capture_event`, `_capture_automatic_breadcrumb`, and
   `capture_log` according to masks;
3. never stores an error message or identity;
4. returns only after all enabled destinations have attempted delivery.

Retain `try_begin_automatic_capture()`/`end_automatic_capture()` around logger
callbacks because they block provider-generated logger feedback. Processor
recursion is independently handled inside the processing pipeline.

- [ ] **Step 4: Run GREEN, regression tests, and commit**

Run:

```bash
scripts/test-project
```

Expected: PASS for automatic/manual shared event capacity, independent
breadcrumbs/logs, processor recursion, provider recursion, masks, timestamps,
and logger registration lifecycle.

Run:

```bash
git add addons/FoundryObservability/AutomaticObservabilityLogger.fs \
  addons/FoundryObservability/FoundryObservability.fs \
  test_project/tests/observability-core.test.fs
git commit -m "feat: share event limits with automatic capture"
```

### Task 8: Package contracts and public documentation

**Files:**

- Modify: `scripts/test-foundry-script`
- Modify: `scripts/test-package`
- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add failing package and documentation contracts**

In `scripts/test-package`, require every new `.fs` and `.fs.uid` under the core
archive:

```bash
for processing_resource in \
	ObservabilitySignalLimits \
	ObservabilityRedactionRule \
	ObservabilityRedactionPolicy \
	ObservabilityProcessingDiagnostic \
	ObservabilityRedactor \
	ObservabilitySignalLimiter \
	ObservabilityProcessingPipeline; do
	resource_path="addons/FoundryObservability/${processing_resource}.fs"
	grep -qx "$resource_path" <<<"$listing" \
		|| fail "package is missing $resource_path"
	grep -qx "$resource_path.uid" <<<"$listing" \
		|| fail "package is missing $resource_path.uid"
done
```

In `scripts/test-foundry-script`, require exact public API text for
`last_processing_diagnostic`, the replacement-or-null processor contract,
redaction ordering, payload-free diagnostics, and the three independent limit
groups in `docs/API.md`.

- [ ] **Step 2: Run RED**

Run:

```bash
scripts/test-foundry-script
scripts/test-package
```

Expected: the documentation contract fails because the public guide does not
yet describe processing; package checks pass only after all new resources are
tracked and included.

- [ ] **Step 3: Document configuration, ordering, privacy, and diagnostics**

Add a provider-neutral example to `README.md`:

```foundryscript
var policy := ObservabilityRedactionPolicy.new([
	ObservabilityRedactionRule.sensitive_key("password"),
	ObservabilityRedactionRule.replace_text(
			PackedStringArray(["**"]),
			"[0-9]{3}-[0-9]{2}-[0-9]{4}",
			"[ssn]",
		),
])
var config := ObservabilityConfig.new(
		p_event_processors = [func(event: ObservabilityEvent) -> Variant:
			if event.level() < ObservabilityLevel.WARNING:
				return null
			return event
		],
		p_event_limits = ObservabilitySignalLimits.new(5, 1000, 20, 10000),
		p_log_limits = ObservabilitySignalLimits.new(100, 0, 1000, 10000),
		p_metric_limits = ObservabilitySignalLimits.new(100, 0, 1000, 10000),
		p_redaction_policy = policy,
	)
```

In `docs/API.md`, document:

- immutable replacement-or-null semantics;
- event/log/metric processor separation and order;
- the two redaction passes;
- canonical redaction roots and wildcards;
- attachment source-path privacy and metadata behavior;
- deterministic accumulator sampling;
- per-frame, repeated, sliding, and legacy log limits;
- default event limits and disabled log/metric limits;
- recursion behavior;
- every diagnostic reason and `last_error()` mapping;
- configuration/reconfiguration/shutdown state boundaries;
- provider-created Foundry runtime context handling;
- the native SDK-owned data non-goal.

Add an `Unreleased` changelog entry summarizing issue #13 without backend-
specific API claims.

- [ ] **Step 4: Run contract GREEN and commit**

Run:

```bash
scripts/test-foundry-script
scripts/test-package
```

Expected: both PASS.

Run:

```bash
git add scripts/test-foundry-script scripts/test-package \
  README.md docs/API.md CHANGELOG.md
git commit -m "docs: explain observability processing controls"
```

### Task 9: Full verification and requirement audit

**Files:**

- Verify all changed files against:
  `docs/superpowers/specs/2026-07-26-event-filtering-redaction-sampling-rate-limits-design.md`

- [ ] **Step 1: Run static diff checks**

Run:

```bash
git diff --check origin/main...HEAD
prek run --all-files
```

Expected: exit 0 with every hook passing.

- [ ] **Step 2: Run the complete repository gate**

Ensure the ignored Android worktree file still contains:

```text
sdk.dir=/Users/christian/Library/Android/sdk
```

Run:

```bash
task test
```

Expected:

- Foundry project suite reports every case passed and zero failures;
- Swift reports 59 existing tests plus any added mapper/provider-boundary tests
  passed;
- Gradle reports `BUILD SUCCESSFUL`;
- iOS and Android contract tests pass;
- FoundryScript, UID, package, CI, and Prek checks pass;
- command exits 0.

- [ ] **Step 3: Audit every issue acceptance criterion**

Read issue #13 and verify in the diff and fresh test output:

```text
[ ] replacement event reaches provider
[ ] null processor result prevents provider transmission
[ ] payload-free diagnostic reports each drop
[ ] event, log, and metric sampling is deterministic
[ ] all three limiter modes are deterministic
[ ] automatic and manual events share event processing
[ ] event, log, and metric state cannot starve another signal
[ ] message, attributes, contexts, user, breadcrumbs, and attachment metadata redact
[ ] processor and redaction ordering is explicit and tested
[ ] recursive processing is blocked
[ ] filtering failures never print payload content
[ ] unsupported provider capabilities remain isolated and observable
[ ] README, API docs, and changelog describe the final contract
```

If any box cannot be tied to a named test and a changed implementation path,
add that missing test first, watch it fail, implement the smallest correction,
and rerun `task test`.

- [ ] **Step 4: Record final branch state**

Run:

```bash
git status --short --branch
git log --oneline --decorate origin/main..HEAD
```

Expected: clean `issue-13` worktree with the design, plan, focused implementation
commits, and no generated or ignored artifacts staged.
