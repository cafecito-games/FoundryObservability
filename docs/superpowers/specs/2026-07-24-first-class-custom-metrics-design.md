# First-Class Custom Metrics Design

## Goal

Add provider-neutral counters, gauges, and distributions to
FoundryObservability for gameplay and runtime measurements on macOS, iOS, and
Android. The core API will validate, filter, sample, and normalize metrics,
while providers retain responsibility for aggregation, batching, transport,
and queue draining.

The public shape follows the current Sentry Godot metrics model: counters carry
non-negative integral values, gauges and distributions carry finite numeric
values, gauges and distributions may specify a unit, and all three types may
carry structured attributes.

## Scope

This slice adds:

- A provider-neutral metric value and metric-type constants.
- A public low-level metric capture method plus counter, gauge, and
  distribution convenience methods.
- Independent metric enablement, deterministic sampling, and an optional
  application filter.
- Optional provider capability detection so providers without metric support
  continue handling events, logs, feedback, flush, and shutdown.
- Deterministic in-memory capture for tests.
- Native Sentry metric mapping through Sentry Cocoa and Sentry Android.
- Documentation and package/build contract coverage.

The core addon will not create a metric queue or aggregation worker. Sentry's
native SDKs already aggregate and batch metrics, and the existing provider
`flush()` operation drains their queued work.

## Public metric model

Add `ObservabilityMetricType` in the `foundry.observability` namespace with:

```text
COUNTER = 0
GAUGE = 1
DISTRIBUTION = 2
```

Add `ObservabilityMetric`, a `RefCounted` immutable-style value with:

```text
type: int
name: String
value: float
unit: String
attributes: Dictionary
```

The constructor follows the repository's `p_` parameter convention and deep
copies attributes. Accessors return the stored scalar fields and a deep copy of
attributes. Counters use the same numeric field as the other metric types, but
core validation requires their value to be a non-negative integer.

Extend `FoundryObservabilityApi` with:

```text
capture_metric(metric: ObservabilityMetric) -> bool
capture_counter(name: String, value: int = 1, attributes: Dictionary = {}) -> bool
capture_gauge(
    name: String,
    value: float,
    unit: String = "",
    attributes: Dictionary = {},
) -> bool
capture_distribution(
    name: String,
    value: float,
    unit: String = "",
    attributes: Dictionary = {},
) -> bool
```

`true` means the active provider accepted the metric into its local SDK or
in-memory store. `false` covers disabled, filtered, sampled-out, invalid,
unsupported, or rejected metrics. Metrics do not have provider event IDs, so
the API does not synthesize IDs.

## Optional provider capability

Metrics are an optional capability rather than a new required method on
`ObservabilityProvider`. Add a focused `ObservabilityMetricsProvider` trait:

```text
capture_metric(metric: ObservabilityMetric) -> bool
```

The service checks for `capture_metric` before calling it. The memory and Sentry
providers implement the capability. The null provider may implement it as a
documented no-op, but providers that only implement `ObservabilityProvider`
remain valid and continue capturing ordinary events.

This boundary avoids making issue #14 a source-breaking change for external
providers and supplies the required safe no-op path for unsupported
capabilities.

## Configuration

Extend `ObservabilityConfig` with:

```text
metrics_enabled: bool = true
metric_sample_rate: float = 1.0
metric_filter: Callable = Callable()
```

The constructor accepts matching `p_` parameters. A sample rate must be finite
and within the inclusive range `0.0...1.0`. Invalid metric configuration makes
`FoundryObservability.configure()` return `Error.ERR_INVALID_PARAMETER` without
replacing or reconfiguring the active provider.

When present, `metric_filter` receives the normalized `ObservabilityMetric`
after global/per-metric attribute merging and validation. A boolean `true`
keeps the metric and `false` drops it. A non-boolean result is invalid filter
output: capture returns `false` and records `Error.ERR_INVALID_PARAMETER`.

Disabled, filter-dropped, and sample-dropped metrics are intentional no-ops and
do not change `last_error()`.

## Validation and normalization

Core validation runs before filters, sampling, or provider calls.

Metric names:

- Must contain between 1 and 200 Unicode characters.
- Must not have leading or trailing whitespace.
- Must not contain control characters.
- Lowercase dot-delimited names are recommended but not required, matching
  Sentry's guidance without imposing a provider-specific grammar.

Metric values:

- All values must be finite.
- Counter values must be whole, non-negative numbers representable by the
  FoundryScript `int` type.
- Gauges and distributions accept any finite signed numeric value.

Units:

- Counters must use an empty unit.
- Gauge and distribution units are optional.
- A non-empty unit must contain at most 64 Unicode characters and may not
  contain whitespace or control characters.
- Known Sentry units and custom provider-neutral units are both accepted.

Attributes:

- Keys must be `String` or `StringName`, contain 1 to 200 characters after
  conversion, have no leading/trailing whitespace, and contain no control
  characters.
- Values may be `bool`, `int`, finite `float`, `String`, or `StringName`.
- Null, nested dictionaries, objects, callables, and arrays are unsupported.
- Any invalid key or value rejects the metric as
  `Error.ERR_INVALID_PARAMETER`; invalid values are not silently stringified.

The service deep-copies global attributes first, then applies per-metric
attributes so per-metric values win on duplicate keys. It creates a normalized
metric containing the merged dictionary and passes only that value to filters
and providers. Invalid global metric attributes reject metrics without
changing event, log, or feedback capture behavior.

## Filtering and sampling flow

Metric capture follows this order:

1. Reject a null or invalid metric and set
   `Error.ERR_INVALID_PARAMETER`.
2. Return `false` without an error when the service or metrics are disabled.
3. Merge and validate global and per-metric attributes.
4. Invoke the optional filter; return `false` without an error when it drops
   the metric.
5. Apply deterministic sampling.
6. Return `false` with `Error.ERR_UNAVAILABLE` when the active provider does not
   implement the optional metric capability.
7. Call the provider. A provider rejection returns `false` and records
   `Error.FAILED`.

Sampling uses an accumulator reset by successful configuration and shutdown.
For each otherwise accepted metric, add `metric_sample_rate`; accept when the
accumulator reaches at least `1.0`, then subtract `1.0`. Therefore `0.0`
accepts none, `1.0` accepts all, and `0.25` deterministically accepts every
fourth eligible metric. Disabled or filtered metrics do not consume sampling
state.

Successful metric capture sets `last_error()` to `Error.OK`. Intentional
drops preserve the prior error so callers can distinguish filtering from a new
failure without high-frequency error churn.

## Core providers

`MemoryObservabilityProvider` stores normalized metrics in a separate
`Array[ObservabilityMetric]`, exposes a defensive `metrics()` copy, provides
`clear_metrics()`, and returns `true` when enabled and not shut down.

`NullObservabilityProvider` returns `false` without allocating or retaining a
metric.

Metrics never enter the event or feedback collections, never consume the
structured-log rate limit, and never affect sequential event or feedback IDs.

## Sentry FoundryScript provider

`SentryObservabilityProvider.configure()` forwards `metrics_enabled` in its
native configuration payload. Missing `captureMetric` support does not fail
configuration because metrics are optional and ordinary event capture must
remain operational.

Its metric capability method:

- Returns `false` when disabled, shut down, unavailable, or missing the native
  method.
- Sends `type`, `name`, `value`, `unit`, and normalized `attributes`.
- Calls `captureMetric` and accepts only a boolean return value.

The Sentry provider performs no additional sampling, aggregation, or batching.

## Apple native mapping

The Swift bridge stores `metricsEnabled` from configuration and sets
`Options.enableMetrics` before starting Sentry.

`captureMetric` validates the dynamic payload defensively and maps supported
attribute scalars to `[String: SentryAttributeValue]`. It then calls:

- `SentrySDK.metrics.count(key:value:attributes:)` for counters.
- `SentrySDK.metrics.gauge(key:value:unit:attributes:)` for gauges.
- `SentrySDK.metrics.distribution(key:value:unit:attributes:)` for
  distributions.

Non-empty unit strings use `SentryUnit(rawValue:)`, which preserves both known
and custom units. The bridge returns `true` after handing the metric to the SDK
and `false` for unavailable, disabled, malformed, or unsupported payloads.

## Android native mapping

The Android bridge stores `metricsEnabled` and configures
`options.getMetrics().setEnabled(metricsEnabled)`.

`captureMetric` validates the payload, creates
`SentryMetricsParameters` from `SentryAttributes.fromMap(attributes)`, and
calls the corresponding method on `Sentry.metrics()`:

- `count(name, value, unit, parameters)`
- `gauge(name, value, unit, parameters)`
- `distribution(name, value, unit, parameters)`

The counter unit remains empty by core contract. Gauges and distributions pass
their optional raw unit string. The method returns `true` after handing the
metric to Sentry and `false` when unavailable, disabled, malformed, or
unsupported.

Both native SDKs own their metric batch processors and transport queues.

## Flush and shutdown

No new flush API is introduced. `FoundryObservability.flush(timeout_msec)`
continues calling the provider's existing flush method. For Sentry this drains
events, logs, feedback, and metric batches within the SDK's platform-specific
timeout behavior.

Shutdown retains the existing flush-then-close sequence. Repeated shutdown
remains safe, and metric sampling state is reset when the service returns to
its disabled null-provider state.

## Error isolation

Invalid or rejected metrics never enter `capture_event()` and do not mutate
event, log, or feedback collections. Unsupported metrics do not prevent
provider configuration. A metric filter can only affect metrics.

Native conversion failures return `false` rather than throwing through the
Foundry boundary. Ordinary event capture remains usable after any metric
failure.

## Testing strategy

FoundryScript tests will cover:

- Metric type constants and defensive value storage.
- Counter, gauge, and distribution convenience methods.
- Name, value, unit, key, and attribute-value validation.
- Global/per-metric attribute precedence.
- Independent metric disablement.
- Filter keep/drop behavior and invalid filter results.
- Deterministic `0.0`, `0.25`, and `1.0` sampling.
- Disabled, null, unsupported, rejecting, and shut-down providers.
- Separation from events, feedback, and structured-log limiting.
- Memory-provider clearing and existing flush forwarding.
- Sentry bridge configuration and normalized payload forwarding.
- Missing native metric capability without loss of event capture.

Swift tests will cover type routing, known/custom units, scalar attribute
conversion, malformed payload rejection, and configuration enablement.

Android unit tests will cover the same mapping contract using a dedicated
metric mapper where practical. Build-contract tests will assert the pinned
native APIs and configuration flags without requiring network delivery.

The complete `task test` gate will be rerun after focused FoundryScript, Swift,
and Android checks.

## Documentation and packaging

Update:

- `README.md` with custom metrics in status and quick-start examples.
- `docs/API.md` with the complete metric model, validation, filtering,
  sampling, batching ownership, and flush semantics.
- `CHANGELOG.md` with the new provider-neutral and Sentry metric capability.
- FoundryScript and UID contract scripts for new public source files.
- Apple and Android Sentry build-contract scripts for metric bridge APIs.

The core and Sentry package boundaries remain unchanged; only new source/UID
files inside the existing addon roots are added.

## Non-goals

This issue does not add:

- A core metric queue, timer, worker, or persistence format.
- Core aggregation, histogram computation, or retry storage.
- Sets, timers, histograms, summaries, or observable callbacks.
- Automatic engine or device metrics.
- A provider-specific Sentry API in the public core namespace.
- Delivery acknowledgements or metric event IDs.

## References

- [Sentry Godot metric value](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/sentry_metric.h)
- [Sentry Godot metrics API](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/sentry_metrics.cpp)
- [Sentry Godot Cocoa metric mapping](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/cocoa/cocoa_sdk.mm)
- [Sentry Godot Android metric mapping](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/android/android_sdk.cpp)
