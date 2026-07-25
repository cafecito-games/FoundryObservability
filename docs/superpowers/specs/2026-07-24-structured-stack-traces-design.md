# Structured Stack Traces and Source Context Design

## Summary

Issue #5 extends the provider-neutral exception model with structured stack
frames while preserving the existing formatted stack-trace string. Each frame
can describe its source location, language, application ownership, surrounding
source text, and explicitly enabled variables. The Sentry provider maps those
frames into the Apple and Android SDKs' native stack-trace models.

The design follows Sentry Godot's separation between its stack-frame value
model and its capture policy:

- source context is enabled by default;
- variable capture is disabled by default;
- producers should avoid collecting variables when the option is disabled;
- non-empty structured frames are mapped to native Sentry stack traces rather
  than stored only as arbitrary event extras.

FoundryObservability keeps its existing provider-neutral boundary. No core type
or public method exposes Sentry-specific classes or field names.

## Goals

- Represent file, function, line, language, in-app status, source context, and
  optional variables in a typed provider-neutral frame.
- Preserve structured frames in `ObservabilityException` without breaking
  existing constructor calls or string-only providers.
- Apply source-context and variable privacy policy before provider dispatch.
- Map frames to native Sentry stack traces on macOS, iOS, and Android.
- Define deterministic behavior for partial, invalid, or empty frames.
- Defensively copy mutable arrays and dictionaries.
- Document frame ordering, privacy defaults, fallback behavior, and provider
  expectations.

## Non-goals

- Automatically intercepting engine or script errors.
- Adding a generic formatted-stack parser.
- Capturing native crash stacks, thread dumps, or exception mechanisms.
- Symbolication, source-map upload, or debug-file management.
- Exposing Sentry protocol objects through the core addon.
- Adding structured stack support to providers other than the existing Sentry,
  memory, and null providers.

## Public API

### ObservabilityStackFrame

Add `ObservabilityStackFrame` to the `foundry.observability` namespace:

```foundryscript
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
```

Accessors expose the same names without the `p_` prefix:

```foundryscript
func file() -> String
func function() -> String
func line() -> int
func language() -> String
func in_app() -> bool
func context_line() -> String
func pre_context() -> PackedStringArray
func post_context() -> PackedStringArray
func variables() -> Dictionary
```

`line = -1` means unknown. Positive lines are one-based. `pre_context` and
`post_context` are ordered from earlier to later source lines. Construction and
accessors defensively copy the arrays and variable dictionary.

Frames are supplied oldest-to-newest, matching the Sentry event protocol and
Sentry Godot's extracted-frame order.

### ObservabilityException

Append an optional frame array to the existing constructor:

```foundryscript
ObservabilityException.new(
		p_type_name: String = "Error",
		p_message: String = "",
		p_stack_trace: String = "",
		p_attributes: Dictionary = {},
		p_frames: Array[ObservabilityStackFrame] = [],
)
```

Add:

```foundryscript
func frames() -> Array[ObservabilityStackFrame]
```

Appending the argument preserves every current positional and named
constructor call. The formatted `stack_trace` remains independent of the frame
array and is never synthesized or overwritten by structured frames.

The constructor copies the containing frame array, and `frames()` returns
another array copy. Frame objects are immutable after construction through
their accessor-only API.

### ObservabilityConfig

Append two provider-neutral policy fields and constructor arguments:

```foundryscript
var stack_trace_source_context_enabled: bool = true
var stack_trace_variables_enabled: bool = false
```

```foundryscript
p_stack_trace_source_context_enabled: bool = true,
p_stack_trace_variables_enabled: bool = false,
```

These options apply to every provider dispatch. Source context follows Sentry
Godot's enabled default. Variables follow its disabled default.

Producers that acquire engine backtraces or inspect locals must use
`stack_trace_variables_enabled` before doing that potentially expensive and
privacy-sensitive work. The core also removes variables before dispatch when
the option is false, providing a second privacy boundary if a caller supplies
them accidentally.

## Capture Normalization

Before dispatch, `FoundryObservability` normalizes an attached exception:

1. Null frame entries are omitted.
2. A frame with an empty file, empty function, empty language, and line below
   one is omitted as unusable.
3. A non-empty partial frame is preserved. File, function, and language remain
   independently optional.
4. Non-positive lines normalize to `-1`.
5. Source context is removed when
   `stack_trace_source_context_enabled` is false.
6. When source context is enabled, `pre_context` and `post_context` are limited
   to the nearest five lines, matching Sentry Godot. Nearby lines are omitted
   when `context_line` is empty.
7. Variables are removed when `stack_trace_variables_enabled` is false.
8. Enabled variables retain only values supported by the existing recursive
   native bridge conversion: booleans, finite numbers, strings, arrays, and
   string-keyed dictionaries. Unsupported values and non-string keys are
   omitted without rejecting the exception.

Normalization creates a new exception only when an attached exception is
present. It preserves type, message, formatted stack string, and attributes.
The normalized exception is used for memory and production providers alike, so
privacy behavior does not vary by backend.

If every structured frame is omitted, capture continues with the existing
exception type, message, attributes, and formatted string. This is the safe,
observable fallback rather than a capture failure.

## Provider Payload

`SentryObservabilityProvider` adds a `frames` array to the exception dictionary
only when at least one normalized frame remains:

```text
exception: {
  type_name: String,
  message: String,
  stack_trace: String,
  attributes: Dictionary,
  frames: [
    {
      file: String,
      function: String,
      line: int,
      language: String,
      in_app: bool,
      context_line: String,       # only when present
      pre_context: Array[String], # only with context_line
      post_context: Array[String],# only with context_line
      variables: Dictionary       # only when enabled and non-empty
    }
  ]
}
```

The bridge payload remains provider-neutral. It uses the public model's terms
instead of Sentry's `filename`, `lineno`, `platform`, and `vars` names. Native
mappers own that final translation.

Older or custom providers continue receiving `ObservabilityException` and can
keep using `stack_trace()` without implementing structured frames. Adding the
accessor and final constructor argument is source compatible.

## Apple Mapping

Extend `FoundryExceptionPayload` with normalized frame payloads. For each valid
frame, create a Sentry Cocoa `Frame` and map:

| Foundry | Sentry Cocoa |
| --- | --- |
| `file` | `fileName` |
| `function` | `function` |
| positive `line` | `lineNumber` |
| `language` | `platform` |
| `in_app` | `inApp` |
| `context_line` | `contextLine` |
| `pre_context` | `preContext` |
| `post_context` | `postContext` |
| `variables` | `vars` |

Create a Sentry `Stacktrace` from the frames and assign it to the native
Sentry `Exception.stacktrace`. Direct exception attachment is the native SDK
equivalent needed here; unlike Sentry Godot's engine logger, this API does not
capture a concrete crashed/current engine thread that should be modeled as a
separate Sentry thread.

The formatted stack string remains in `foundry.stack_trace` extras for backward
compatibility and operator visibility.

## Android Mapping

For each valid frame, create `io.sentry.protocol.SentryStackFrame` and map:

| Foundry | Sentry Android |
| --- | --- |
| `file` | `filename` |
| `function` | `function` |
| positive `line` | `lineno` |
| `language` | `platform` |
| `in_app` | `isInApp` |
| `context_line` | `contextLine` |
| `pre_context` | `preContext` |
| `post_context` | `postContext` |
| `variables` | `vars` |

Put frames in `SentryStackTrace` and assign it to
`SentryException.setStacktrace`. Invalid bridge values are handled with checked
type conversions; no malformed frame may throw from `makeEvent`.

The existing `foundry.stack_trace` extra remains unchanged.

## Data Flow

```text
producer
  -> ObservabilityStackFrame values
  -> ObservabilityException(frames + formatted stack fallback)
  -> FoundryObservability policy normalization
  -> ObservabilityProvider
  -> Sentry bridge dictionary
  -> Apple Frame/Stacktrace/Exception
     or Android SentryStackFrame/SentryStackTrace/SentryException
```

Null and memory providers remain deterministic. The null provider discards the
normalized event safely. The memory provider stores it for assertions and
local inspection.

## Error and Compatibility Behavior

- Missing structured frames are not an error.
- Partial frames with at least one useful identity or location field survive.
- Empty and null frames are ignored.
- Unknown lines are omitted from native Sentry frames.
- Context arrays without a current context line are ignored.
- Unsupported variable entries are dropped individually.
- A malformed frame never prevents capture of the exception.
- A string-only exception behaves exactly as it does before this change.
- Native mappers never store structured frames only in extras; extras retain
  only the formatted fallback and existing metadata.
- Providers that do not understand frames can continue relying on
  `stack_trace()`.

## Testing

### FoundryScript core tests

- Construct a complete frame and verify every accessor.
- Verify array and dictionary defensive copies.
- Verify exception frame-array copying and formatted-string preservation.
- Verify default configuration keeps source context and removes variables.
- Verify explicit variable enablement preserves supported values.
- Verify source-context disablement removes current and nearby lines.
- Verify null, empty, partial, negative-line, excessive-context, and
  unsupported-variable normalization.
- Verify string-only memory-provider capture remains unchanged.

### FoundryScript Sentry provider tests

- Verify normalized frames are forwarded with stable provider-neutral keys.
- Verify source context and enabled variables reach the fake bridge.
- Verify disabled variables are absent.
- Verify an exception without frames retains its existing payload.

### Swift tests

- Verify all fields map to native Sentry `Frame` and `Stacktrace`.
- Verify context and variables map when present.
- Verify unknown lines and absent optional fields stay nil.
- Verify string-only exception mapping remains compatible.

### Android tests

- Verify all fields map to `SentryStackFrame` and `SentryStackTrace`.
- Verify context and variables map when present.
- Verify malformed bridge dictionaries are handled without exceptions.
- Verify string-only exception mapping remains compatible.

### Repository validation

Run focused tests during TDD, then the complete `task test` gate before review
and handoff.

## Documentation

Update `docs/API.md` with:

- the new frame constructor and accessors;
- oldest-to-newest ordering;
- one-based line semantics and the `-1` sentinel;
- five-line source-context limits;
- source-context and variable configuration defaults;
- the warning that producers must not acquire locals when variable capture is
  disabled;
- string fallback and partial-frame behavior;
- an exception-capture example with structured frames.

Update `README.md` to list structured exception stacks and source context among
the supported core/Sentry capabilities.
