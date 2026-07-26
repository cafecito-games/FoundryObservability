# Event Filtering, Redaction, Sampling, and Rate Limits Design

## Context

Issue #13 requires provider-neutral controls that can modify or drop events,
structured logs, and metrics before transmission. It also requires deterministic
sampling, bounded flood protection, recursive-reporting protection, consistent
redaction, stable diagnostics, and behavior shared by macOS, iOS, and Android.

The current core already has several narrower mechanisms:

- immutable event and breadcrumb DTOs with defensive structured-data copies;
- a metric predicate and deterministic metric sample accumulator;
- a fixed one-second structured-log limit;
- automatic-error per-frame, repeated-error, and sliding-window limits;
- provider-call tracking that prevents automatic logger recursion;
- provider-owned global scope, user, breadcrumb, and attachment state.

Those mechanisms are distributed across `FoundryObservability` and
`AutomaticObservabilityLogger`, apply to different signal subsets, and cannot
report a stable reason for an intentional drop.

Sentry Godot provides useful guidance: it runs ordered event processors followed
by a final before-send callback, allows `null` to drop a payload, provides
parallel log and metric hooks, guards recursive event processing, and uses
per-frame, repeated-error, and sliding-window logger limits. This design keeps
those useful semantics while adapting them to FoundryObservability's immutable,
provider-neutral DTOs.

## Goals

- Let ordered event, log, and metric processors return an immutable replacement
  or `null` to drop the signal.
- Apply the same event pipeline to manual events and automatically captured
  engine errors.
- Give events, logs, and metrics independent sampling and limit state.
- Provide configurable, provider-neutral redaction for all Foundry-owned
  transmitted fields.
- Fail closed when processing or redaction produces an unsafe or invalid
  payload.
- Report accepted and dropped outcomes without storing or printing payload
  contents.
- Preserve existing provider boundaries and native bridge payload formats.
- Keep all time-, frame-, and sampling-dependent behavior deterministic in
  tests.

## Non-goals

- A general-purpose policy language beyond filtering and redaction.
- Remote or dynamically downloaded processing rules.
- Backend-specific Sentry event processors in the provider-neutral API.
- Calling FoundryScript processors from native crash handlers.
- Replacing native SDK privacy controls for SDK-owned crash, hang, ANR, device,
  or network data that does not originate in FoundryObservability.
- Persisting rate-limit or sample state across process launches.
- Adding runtime processor mutation APIs; processors are committed through a
  successful configuration.

## Approved approach

The core owns a single processing pipeline. Providers receive only accepted,
validated Foundry-originated payloads. The pipeline executes:

1. normalize and validate;
2. apply configured redaction;
3. run ordered signal processors;
4. apply redaction again to the replacement;
5. validate the final replacement;
6. sample using signal-local deterministic state;
7. reserve signal-local rate-limit capacity;
8. dispatch to the provider;
9. publish a payload-free diagnostic.

Pre-processor redaction prevents callbacks from inspecting configured sensitive
fields. Post-processor redaction prevents a processor from accidentally
reintroducing them.

Logs remain `ObservabilityEvent` instances with kind `log`. Log processors
receive and return `ObservabilityEvent`; ordinary event processors never receive
logs. Metric processors receive and return `ObservabilityMetric`.

## Public value types

### ObservabilitySignalLimits

Add an immutable, defensively copied configuration DTO:

```text
ObservabilitySignalLimits.new(
    p_per_frame = 0,
    p_repeated_window_msec = 0,
    p_window_count = 0,
    p_window_msec = 0,
)
```

Negative values normalize to zero. Zero disables the corresponding limit.
`window_count` and `window_msec` enable the sliding-window limit only when both
are positive.

The default event limits are 5 events per processed frame, one matching event
per 1,000 milliseconds, and 20 events per 10,000 milliseconds. These preserve
the existing automatic-error safeguards but apply to all events. Log and metric
limits default to disabled so existing structured logging and metric volume do
not change unexpectedly. Their configured state is completely independent from
the event limiter and from each other.

### ObservabilityRedactionRule

Add an immutable rule with:

- a nonempty `PackedStringArray` canonical path;
- an action: `REMOVE_FIELD`, `REPLACE_VALUE`, or `REPLACE_TEXT`;
- an optional text pattern;
- a replacement value.

Path segments use case-insensitive exact matching. `*` matches one segment and
`**` matches zero or more segments. A convenience constructor for a sensitive
key creates the equivalent of `["**", key]`.

`REMOVE_FIELD` removes a matching dictionary entry or optional DTO field.
`REPLACE_VALUE` replaces the complete matching value with a type-compatible
configured value. `REPLACE_TEXT` replaces either a complete string or every
match of its compiled regular expression. Rules execute in declaration order;
removing a field ends traversal for that field while later rules see earlier
replacements.

Invalid paths, patterns, action/value combinations, or replacements reject
configuration. A wildcard that resolves to an incompatible target at runtime
fails closed for that payload.

### ObservabilityRedactionPolicy

Add an immutable ordered collection of redaction rules. It snapshots every rule
and compiles text patterns during configuration. An empty policy is a no-op.

Canonical roots are:

- `event`: kind, level, message, source, attributes, exception, and local scope;
- `log`: the same event shape for structured logs;
- `metric`: type, name, value, unit, and attributes;
- `contexts`: provider-owned global structured contexts;
- `user`: application user ID, display name, and contact email;
- `breadcrumbs`: message, level, category, type, timestamp, and attributes;
- `attachments`: outbound filename, content type, and category.

The attachment's local source path is not an outbound metadata field. It remains
available only to load the file. Providers that synthesize Foundry-owned
runtime contexts or built-in attachment metadata after the core boundary use
the same shared redactor before constructing the native payload.

### ObservabilityProcessingDiagnostic

Add an immutable payload-free diagnostic containing:

- monotonically increasing local sequence;
- signal: `event`, `log`, `metric`, or `state`;
- outcome: `accepted` or `dropped`;
- stable reason;
- processor index, or `-1`;
- redaction rule index, or `-1`;
- limit kind, or an empty name;
- engine `Error` value.

Stable drop reasons are:

- `processor`;
- `sampled`;
- `rate_limited`;
- `recursive`;
- `invalid_processor_result`;
- `redaction_failed`;
- `invalid_payload`;
- `provider_rejected`.

Stable limit kinds are `per_frame`, `repeated`, `window`, and
`legacy_log_window`.

The diagnostic never stores payload objects, messages, attribute keys or
values, context names, user fields, attachment paths or filenames, callable
error strings, processor identities, or redaction patterns.

Expose:

```text
FoundryObservability.last_processing_diagnostic()
    -> ObservabilityProcessingDiagnostic?
```

The returned value is an isolated snapshot. Disabled no-op calls that never
enter a signal pipeline preserve the previous diagnostic, matching existing
`last_error()` no-op behavior.

## Configuration

Append processing parameters to `ObservabilityConfig._init()` so every existing
positional argument retains its meaning:

```text
p_event_sample_rate = 1.0
p_log_sample_rate = 1.0
p_event_processors = []
p_log_processors = []
p_metric_processors = []
p_event_limits = null
p_log_limits = null
p_metric_limits = null
p_redaction_policy = null
```

Processor collections accept only valid `Callable` values and are copied.
Signal limits and redaction policy are defensively duplicated. Every sample rate
must be finite and inside `[0.0, 1.0]`; invalid committed configuration rejects
the candidate and preserves the active provider and pipeline state.

Compatibility behavior is:

- when `p_event_limits` is absent, the existing
  `p_automatic_events_per_frame`,
  `p_automatic_repeated_error_window_msec`,
  `p_automatic_event_throttle_count`, and
  `p_automatic_event_throttle_window_msec` values seed the effective event
  limits;
- an explicit `p_event_limits` value wins over those compatibility inputs;
- `metric_filter` remains supported and runs on the pre-redacted, normalized
  metric before the metric processor array;
- `metric_sample_rate` remains the metric sample-rate source;
- `log_rate_limit_per_second` remains an additional fixed one-second log limit
  after the new signal-local limiter, preserving its current behavior;
- the automatic logger no longer owns event admission state.

A successful configuration resets processor recursion state, diagnostics,
sampling accumulators, frame counters, repeated-identity state, and sliding
windows. A failed configuration preserves the live pipeline exactly. Shutdown
clears callables and all processing state.

## Processing semantics

### Processor ordering and replacement

Processors run in array order. Each receives the previous accepted immutable
DTO and may return:

- a valid replacement of the exact expected DTO type; or
- `null`, which deliberately drops the signal.

Returning the original object is valid. Returning a different immutable object
is also valid. Returning another type, returning an event with the wrong signal
kind, or returning a payload that fails normalization is an
`invalid_processor_result` drop.

Because FoundryScript callable failures are not catchable as language
exceptions, a callable failure that yields `null` has the same fail-closed
delivery behavior as an explicit drop. FoundryObservability does not print the
callable error or payload.

### Sampling

Sampling runs after redaction and processors, so rejected or deliberately
dropped payloads do not consume sample state. Each signal owns an accumulator
initialized to zero. The sample rate is added for every otherwise eligible
payload; values below one drop the payload, and reaching one accepts it and
subtracts one. For example, `0.25` accepts every fourth eligible payload.

This extends the existing deterministic metric behavior and avoids random
seeding, global RNG coupling, and flaky tests.

### Rate limits

Admission is evaluated in this order:

1. per-frame limit;
2. repeated-identity window;
3. sliding time window;
4. legacy fixed one-second log limit.

Capacity is committed atomically only after every applicable limit accepts the
payload. A failure in one limit does not partially consume another limit.
Provider rejection does not roll back committed capacity because the pipeline
already admitted and attempted the signal.

The pipeline derives a stable digest from the final normalized signal identity.
It stores only the digest and the last accepted monotonic time. Event identity
uses kind, source, level, message, and exception identity. Log identity uses
source, level, and message. Metric identity uses type, name, and unit. Attributes
are excluded so unbounded or sensitive structures are never retained by the
limiter.

Repeated-identity state is capped at 1,024 entries per signal with oldest-entry
eviction. Sliding-window timestamps are pruned before evaluation and never
exceed the configured count. Frame counters keep only the current frame number
and count.

### Recursion and concurrency

Entering redaction or processor execution reserves the signal pipeline. A
capture attempted reentrantly by a redaction or processor callable is dropped
as `recursive` before it can invoke another callable, sample, consume limit
capacity, or call the provider.

The existing provider-call reservation continues to prevent automatic logger
feedback from provider diagnostics. Automatic capture and explicit capture use
the same admission pipeline. Pipeline state transitions are mutex-protected,
but user callables and provider calls execute without holding the state mutex so
they cannot deadlock the core.

An internal clock/frame seam supplies monotonic milliseconds and processed frame
numbers. Production uses `Time.get_ticks_msec()` and
`Engine.get_process_frames()`; tests inject deterministic values without
sleeping.

## Redaction across provider-owned state

Event-local scope is redacted as part of event reconstruction. Provider-owned
state is redacted before its mutation is forwarded:

- `set_context()` rebuilds the context candidate from the redacted dictionary;
- `set_user()` rebuilds the user from redacted fields;
- `capture_breadcrumb()` and automatic breadcrumbs rebuild the breadcrumb;
- `add_attachment()` preserves the private source but rebuilds outbound
  metadata.

The raw input is not retained in the service after the operation returns.
Successful reconfiguration already resets provider scope, user, breadcrumbs,
and attachments, so changing policy cannot leave an older unredacted session
snapshot alive.

Sentry runtime-context enrichment and built-in attachment construction occur
inside the provider after the core pipeline. `SentryObservabilityProvider` uses
the committed shared redactor for those Foundry-owned fields before calling its
existing bridge methods. The native bridge formats remain unchanged.

If redaction invalidates an event, log, metric, user, context, or breadcrumb,
the operation fails closed and records `redaction_failed`. If only attachment
metadata becomes invalid, the attachment is omitted. Event capture may continue
with a new payload-free `ObservabilityAttachmentFailure` redaction reason.

## Error and diagnostic behavior

Expected policy outcomes do not represent service failures:

- explicit processor drop;
- sampling drop;
- rate-limit drop;
- recursion prevention.

They set the processing diagnostic and leave `last_error()` at `Error.OK`.

Unsafe or malformed pipeline outcomes set an error:

- invalid replacement or final payload: `Error.ERR_INVALID_DATA`;
- redaction failure: `Error.ERR_INVALID_DATA`;
- provider rejection: existing provider-specific or `Error.FAILED` behavior.

Successful provider delivery records an `accepted` diagnostic with
`Error.OK`. No processing failure is reported through FoundryLib or engine
logging, avoiding recursive reporting and sensitive payload exposure.

An automatic logger callback has independent destinations. An event drop does
not suppress its breadcrumb or structured log, and a log drop does not consume
event capacity. The callback's final `last_error()` preservation rules continue
to prevent one successful optional destination from hiding another provider
failure.

## Components

Add focused core resources:

- `ObservabilitySignalLimits.fs`: immutable public limit configuration;
- `ObservabilityRedactionRule.fs`: immutable public rule and path/action
  validation;
- `ObservabilityRedactionPolicy.fs`: immutable public ordered policy;
- `ObservabilityProcessingDiagnostic.fs`: immutable payload-free outcome;
- `ObservabilityRedactor.fs`: shared defensive traversal and DTO
  reconstruction;
- `ObservabilitySignalLimiter.fs`: one bounded signal-local sampler/limiter;
- `ObservabilityProcessingPipeline.fs`: ordering, recursion, validation,
  diagnostics, and coordination.

Modify:

- `ObservabilityConfig.fs` for processing configuration and compatibility;
- `FoundryObservabilityApi.fs` for diagnostic access;
- `FoundryObservability.fs` to delegate signal and provider-state processing;
- `AutomaticObservabilityLogger.fs` to remove its event limiter and use the
  shared pipeline;
- `ObservabilityAttachmentFailure.fs` for the redaction omission reason;
- `SentryObservabilityProvider.fs` for shared redaction of provider-created
  runtime contexts and attachment metadata;
- repository resource, package, documentation, and test contracts.

No Swift, Java, Sentry Cocoa, or Sentry Android bridge API change is required.
Those layers continue receiving the same payload shapes after provider-neutral
processing.

## Testing

### Core deterministic tests

Add FoundryScript tests for:

- configuration copying, validation, compatibility inputs, and defaults;
- event/log/metric processor order and replacement chaining;
- original-object returns, replacement-object returns, and explicit drops;
- wrong types, wrong event kinds, invalid replacements, and fail-closed
  behavior;
- pre-processor redaction, post-processor redaction, rule ordering, wildcards,
  recursive key matching, removal, replacement, and text substitution;
- event attributes, exception data, local/global contexts, user fields,
  breadcrumbs, attachment metadata, and metric attributes;
- invalid redaction rules and payload-free failure diagnostics;
- exact deterministic sample sequences at zero, fractional, and one rates;
- independent event, log, and metric sampling state and configuration reset;
- per-frame, repeated, sliding-window, and legacy log limits;
- atomic limit reservation, bounded identity state, and oldest-entry eviction;
- strict signal independence so one type cannot starve another;
- recursive capture from processors and redaction;
- manual and automatic event parity;
- independent automatic event, breadcrumb, and log destinations;
- success, drop, invalid-result, redaction, recursion, limit, and provider
  diagnostics;
- failed reconfiguration preserving active pipeline state;
- successful reconfiguration and shutdown clearing state and callable
  references.

Use the injected clock/frame seam for every time-window test. Do not sleep.

### Provider and platform tests

The memory provider proves that only final redacted replacement payloads arrive
at a provider. Sentry FoundryScript tests prove that runtime contexts and
built-in attachment metadata use the committed policy before the existing
bridge calls.

Existing Swift and Android mapper tests remain the platform boundary proof
because the bridge payload schemas do not change. Run their complete suites to
catch accidental contract drift.

### Repository verification

Update:

- `scripts/test-foundry-script`;
- UID and package contracts;
- `README.md`;
- `docs/API.md`;
- `CHANGELOG.md`.

The completion gate is:

```sh
task test
```

It covers lint, FoundryScript contracts and UIDs, the 232-case baseline project
suite plus new tests, CI and package contracts, Swift/XCTest, Android/JUnit, and
both native build contracts.
