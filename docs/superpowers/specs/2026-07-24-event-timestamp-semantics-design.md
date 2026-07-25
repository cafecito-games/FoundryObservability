# Event Timestamp Semantics Design

## Context

FoundryObservability currently uses `timestamp_msec` for a monotonic engine tick
from `Time.get_ticks_msec()`. That value is useful for elapsed-time diagnostics
and rate limiting, but it is not a wall-clock timestamp and cannot be used as a
backend event occurrence time.

The Apple and Android Sentry bridges currently preserve the engine tick as
`foundry.timestamp_msec` metadata while allowing the native SDK to timestamp
the event when the bridge sends it. This loses the distinction between when an
event occurred and when it was delivered. It also prevents a caller-supplied
custom event timestamp from becoming the native Sentry event timestamp.

The design follows the model used by `getsentry/sentry-godot`: event timestamps
are absolute wall-clock values relative to the Unix epoch. Monotonic engine
ticks remain separate diagnostic data.

## Goals

- Define `ObservabilityEvent.timestamp_msec()` as Unix epoch milliseconds.
- Preserve the existing `timestamp_msec` constructor parameter and accessor
  names while correcting their documented semantics.
- Add an optional monotonic `engine_ticks_msec` value without making it an event
  occurrence timestamp.
- Preserve caller-provided wall-clock timestamps through the provider-neutral,
  Apple, and Android mappings.
- Resolve a missing timestamp once at the provider-neutral capture boundary.
- Convert FoundryLib's monotonic log-record timestamps to wall-clock occurrence
  time using a contemporaneous clock pair.
- Use monotonic time, not wall-clock time, for structured-log rate limiting.
- Keep providers that do not support timestamp mapping safe and observable.
- Add deterministic tests for units, conversion, mapping, and UTC serialization.

## Non-goals

- Changing the provider-neutral timestamp unit from milliseconds.
- Introducing a provider-specific timestamp type into the core addon.
- Estimating timestamps across process restarts. Engine ticks and their
  wall-clock anchor are valid only within the current process.
- Replacing FoundryLib's monotonic `LogRecord.timestamp_msec` contract.
- Emulating a native structured-log timestamp when the pinned Sentry logging API
  does not expose one. The normalized occurrence timestamp remains available as
  reserved structured metadata in that case.

## Public API

### `ObservabilityEvent`

Keep `p_timestamp_msec` and `timestamp_msec()`, but define them as integer Unix
epoch milliseconds. Change the constructor default from `0` to `-1`, where a
negative value means "unspecified." Zero remains a valid explicit timestamp for
the Unix epoch.

Add a trailing constructor argument:

```foundryscript
p_engine_ticks_msec: int = -1
```

and a matching accessor:

```foundryscript
func engine_ticks_msec() -> int
```

The argument is appended after the existing arguments so positional callers
keep their current parameter order. A negative engine tick means the original
monotonic timestamp is unavailable.

All `ObservabilityEvent` backing fields are `final var` values assigned exactly
once by the constructor. The accessor-only event surface therefore has
compile-time-enforced immutability in addition to defensive dictionary copies.

### `capture_log`

Keep the existing `timestamp_msec` parameter name and position, but define it as
Unix epoch milliseconds. Add a trailing optional parameter:

```foundryscript
engine_ticks_msec: int = -1
```

The rules are:

1. An explicit non-negative `timestamp_msec` is authoritative.
2. If `timestamp_msec` is missing and `engine_ticks_msec` is present, convert
   the engine tick to wall time using the capture clock pair.
3. If both are missing, use capture wall time and capture engine ticks.

`FoundryObservabilityApi`, its concrete implementation, the FoundryLib sink, and
test doubles receive the same signature.

## Clock Model

At a capture boundary, read:

```text
capture_unix_msec
capture_engine_ticks_msec
```

For a source event containing only a monotonic tick, derive its occurrence time
with:

```text
event_unix_msec =
    capture_unix_msec
    + event_engine_ticks_msec
    - capture_engine_ticks_msec
```

The calculation uses integer milliseconds throughout. It does not apply local
timezone conversion, floating-point formatting, or clamping. Keeping the
conversion as a pure helper permits deterministic fixed-input tests.

The two capture clocks must be read together at the boundary where the fallback
or conversion is resolved. Millisecond scheduling skew between the reads is
acceptable and bounded; the resulting timestamp then becomes immutable for the
rest of the provider pipeline.

## Provider-neutral Capture Flow

`FoundryObservability.capture_event()` normalizes timestamps before filtering or
provider dispatch:

1. Read a capture wall-clock/engine-tick pair.
2. Preserve an explicit non-negative event wall-clock timestamp.
3. If the wall-clock timestamp is missing but an engine tick is present,
   convert it using the clock pair.
4. If both are missing, use the capture pair.
5. Create a normalized `ObservabilityEvent` copy only when values must be
   resolved; retain all original event fields and defensive-copy behavior.
6. Dispatch the normalized event to the active provider.

Convenience message and exception capture methods create events with the
current wall-clock timestamp and contemporaneous engine tick. Direct custom
events may provide either or both values.

Structured-log filtering uses a monotonic rate-limit timestamp:

- use the event's `engine_ticks_msec` when present;
- otherwise use the capture engine tick.

This prevents system clock changes and caller-supplied historical timestamps
from moving records between rate-limit windows.

## FoundryLib Integration

FoundryLib `LogRecord.timestamp_msec` remains a monotonic engine tick. The sink
must not pass it as the wall-clock `timestamp_msec` argument after the semantic
correction.

Instead, the sink calls `capture_log` with:

- `timestamp_msec = -1`;
- `engine_ticks_msec = record.timestamp_msec`.

The core converts the record tick using the capture clock pair, stores the
derived Unix epoch milliseconds as the event occurrence timestamp, and retains
the original record tick as diagnostics.

## Sentry Provider Payload

The FoundryScript Sentry provider sends:

```text
timestamp_msec       Unix epoch milliseconds
engine_ticks_msec    monotonic engine milliseconds, only when available
```

Reserved metadata is written after caller attributes:

```text
foundry.timestamp_msec
foundry.engine_ticks_msec
```

`foundry.timestamp_msec` therefore changes from the old ambiguous engine-tick
meaning to the corrected Unix epoch meaning. The new key preserves the
diagnostic value that the old implementation carried.

For ordinary message and exception events, both native bridges explicitly set
the native Sentry event timestamp from `timestamp_msec`.

For structured logs, the pinned Sentry Cocoa and Android logging APIs assign
their own transport record time. The bridge preserves the normalized occurrence
time and original engine tick in the reserved attributes above. This limitation
is documented rather than silently pretending the native log timestamp was
overridden.

## Apple Mapping

Add a pure conversion helper that maps integer Unix epoch milliseconds to
Foundation `Date`:

```swift
Date(timeIntervalSince1970: TimeInterval(timestampMsec) / 1_000.0)
```

`makeSentryEvent` accepts the wall-clock timestamp and optional engine tick,
sets `event.timestamp` explicitly, and adds both reserved metadata values when
available.

Epoch conversion is timezone-independent because `Date` stores an absolute
instant. Serialization tests use a UTC formatter and fixed input rather than
the host's locale or timezone.

The same Swift implementation is shared by macOS and iOS.

## Android Mapping

Add a pure conversion helper that maps integer Unix epoch milliseconds to
`java.util.Date` without changing units:

```java
new Date(timestampMsec)
```

`makeEvent` sets the resulting value on `SentryEvent`, then adds corrected wall
time and optional engine ticks to extras. Fixed-input tests assert the exact
epoch millisecond value while running with a non-UTC default timezone to prove
that local timezone does not affect conversion.

## Error Handling and Unsupported Capabilities

- A negative wall-clock timestamp is treated as missing, not forwarded to a
  native SDK.
- Zero and positive caller timestamps are preserved exactly.
- A negative engine tick means unavailable and is omitted from reserved native
  metadata.
- Integer arithmetic is used end to end so millisecond precision is not lost.
- Providers that ignore the new engine-tick accessor remain source-compatible
  consumers of `ObservabilityEvent`.
- Native structured logging's lack of an explicit timestamp setter is
  documented, with occurrence time retained in reserved attributes.
- Capture-time resolution happens before provider dispatch so providers never
  independently choose different fallback instants.

## Testing

### FoundryScript core

- Verify `ObservabilityEvent` stores wall-clock and engine-tick fields
  independently.
- Verify zero is a valid explicit Unix timestamp and negative means missing.
- Test the pure monotonic-to-wall-clock conversion with fixed values.
- Verify a custom explicit event timestamp reaches the memory provider
  unchanged.
- Verify a missing timestamp is resolved to a wall-clock value at capture.
- Verify convenience capture records both clocks.
- Verify rate limiting uses monotonic ticks rather than wall-clock timestamps.

### FoundryLib integration

- Verify the sink forwards a log record's timestamp as `engine_ticks_msec`, not
  as `timestamp_msec`.
- Verify the captured event retains the original tick and receives a derived
  wall-clock timestamp.

### FoundryScript Sentry adapter

- Verify ordinary event payloads contain corrected wall-clock timestamps and
  optional engine ticks.
- Verify structured-log payloads carry both values.
- Verify missing engine ticks are omitted or represented consistently.

### Apple native mapper

- Verify a fixed Unix millisecond value becomes the exact expected `Date`.
- Verify `makeSentryEvent` assigns that `Date` as the Sentry event timestamp.
- Verify custom timestamps and engine-tick metadata survive mapping.
- Verify UTC serialization is identical under a non-UTC default timezone.

### Android native mapper

- Verify a fixed Unix millisecond value becomes a `Date` with the same epoch
  milliseconds.
- Verify `makeEvent` assigns that date to `SentryEvent`.
- Verify custom timestamps and engine-tick metadata survive mapping.
- Verify changing the default timezone does not change the epoch value or UTC
  serialization.

### Validation

Run focused FoundryScript, Swift, and Android mapper tests during development,
then finish with the repository's full `task test` gate.

## Documentation and Release Notes

Update:

- `docs/API.md` timestamp conventions, constructors, accessors, convenience
  capture behavior, FoundryLib mapping, and Sentry mapping;
- `README.md` only if a quick-start timestamp example materially improves usage;
- `CHANGELOG.md` with the corrected timestamp semantics and the new diagnostic
  engine-tick field.

The documentation must call out the semantic correction for existing
`timestamp_msec` consumers: callers should now supply Unix epoch milliseconds,
while monotonic values belong in `engine_ticks_msec`.

## Alternatives Considered

### Add a second wall-clock field while leaving `timestamp_msec` monotonic

This avoids changing the old meaning but leaves the most natural timestamp name
attached to the wrong clock. It also forces precedence rules between two public
occurrence-time fields and diverges from Sentry's event model.

### Let every native SDK assign capture time

This is simple but loses custom and delayed event occurrence times, produces
provider-dependent fallbacks, and fails the issue's acceptance criteria.

### Selected approach

Correct `timestamp_msec` to Unix epoch milliseconds and preserve monotonic time
as `engine_ticks_msec`. This keeps the provider-neutral surface compact, aligns
with Sentry's timestamp model, and retains the diagnostic value of engine ticks.
