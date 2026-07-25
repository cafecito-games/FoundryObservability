# FoundryObservability API

FoundryObservability is the provider-neutral observability boundary for Foundry
games. It normalizes messages, exceptions, breadcrumbs, structured log records,
and custom metrics before dispatching them to a backend provider. It can also
capture engine diagnostics and output automatically after successful
configuration. Player feedback remains an explicit, separate path and is never
collected implicitly.

The public namespace is **foundry.observability**. The FoundryLib adapter lives
in **foundry.observability.foundrylib**. Global class and trait names are
unchanged by the namespace migration.

## Installation and setup

Install FoundryLib as a project package, then copy or install the
addons/FoundryObservability directory. Enable the FoundryObservability editor
plugin. The plugin registers the FoundryObservability autoload.

The addon currently requires FoundryLib because its core package includes the
FoundryLib LogSink adapter. The optional FoundryObservabilitySentry sibling
addon provides the first production backend for Apple and Android exports.
Apple projects using that provider must also install the FoundrySwift
`0.1.0-alpha.2` companion addon. Its `FoundrySwiftEmbed` extension owns the
single shared FoundrySwift runtime; the Sentry extension intentionally does not
declare or embed another copy. Android uses the Sentry Android bridge and does
not require FoundrySwift.

The smallest setup is:

~~~
import foundry.observability

var config := ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "production",
		p_release = "1.0.0",
)
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
var result: int = FoundryObservability.configure(provider, config)
if result != Error.OK:
	# Handle configuration failure.
	pass

FoundryObservability.capture_message("game started")
~~~

FoundryObservability is an autoload, so consumers use the registered global
service after the editor plugin has enabled it. The service starts with a safe
NullObservabilityProvider and a disabled configuration.

## Public API index

Core service and contracts:

- FoundryObservability: autoload service implementing the public API.
- FoundryObservabilityApi: trait implemented by the autoload service.
- ObservabilityProvider: trait implemented by backend providers.

Value types:

- ObservabilityLevel
- ObservabilityConfig
- ObservabilityException
- ObservabilityStackFrame
- ObservabilityEvent
- ObservabilityBreadcrumb
- ObservabilityCaptureMask
- ObservabilityFeedback
- ObservabilityMetricType
- ObservabilityMetric

Optional provider capabilities:

- ObservabilityMetricsProvider
- ObservabilityBreadcrumbsProvider

Built-in providers:

- NullObservabilityProvider
- MemoryObservabilityProvider

FoundryLib integration:

- foundry.observability.foundrylib.FoundryLibObservabilitySink

## Conventions

### Error values

Methods returning int use Foundry engine Error values. Error.OK means success.
A provider configuration or flush failure is returned to the caller and stored
by the service in last_error(). Capture methods return String event IDs rather
than Error values. An empty capture ID means the event was not accepted; the
service records Error.FAILED for an enabled provider that returns an empty ID.
Metric and breadcrumb capture methods return bool. Rejected invalid input stores
Error.ERR_INVALID_PARAMETER, a provider without the optional metrics capability
stores Error.ERR_UNAVAILABLE, and a provider rejection stores Error.FAILED.
The same unavailable/rejected behavior applies to optional breadcrumb capture.

### Timestamps

`timestamp_msec` is an integer Unix timestamp in milliseconds. The separate
`engine_ticks_msec` value is the monotonic engine tick associated with the
event and is used for elapsed-time behavior such as log rate limiting.

The service resolves missing timestamps once at the capture boundary. An
explicit nonnegative `timestamp_msec` is preserved. When only
`engine_ticks_msec` is supplied, the service converts it using a contemporaneous
wall-clock/engine-tick pair. When both are missing, the service records the
current values as a pair. Providers should map `timestamp_msec` to their
backend's native occurrence timestamp and preserve engine ticks as diagnostic
metadata when useful.

### Defensive copies

Configuration, event, breadcrumb, and metric dictionaries are copied deeply
when stored and when returned through accessors. This prevents callers from
mutating a payload after it has been handed to the observability service.
Memory provider list accessors copy the containing array; the payload objects
themselves are the captured objects.

## ObservabilityLevel

ObservabilityLevel defines the shared severity values used by events and log
adapters. Values increase with severity:

| Constant | Value | Meaning |
| --- | ---: | --- |
| TRACE | 10 | Most verbose diagnostic detail |
| DEBUG | 20 | Debugging information |
| INFO | 30 | Normal informational event |
| WARN | 40 | Something unusual but recoverable |
| ERROR | 50 | An operation or subsystem failed |
| FATAL | 60 | A severe failure requiring attention |

Static method:

~~~
static func name(level: int) -> String
~~~

Returns the uppercase constant name for a known value. Unknown values return
LEVEL(value), for example LEVEL(35).

## ObservabilityConfig

ObservabilityConfig contains provider-neutral deployment metadata and opaque
provider options.

Constructor:

~~~
ObservabilityConfig.new(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0,
		p_metrics_enabled: bool = true,
		p_metric_sample_rate: float = 1.0,
		p_metric_filter: Callable = Callable(),
		p_automatic_capture_enabled: bool = true,
		p_automatic_event_mask: int = ObservabilityCaptureMask.DEFAULT_EVENTS,
		p_automatic_breadcrumb_mask: int = ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
		p_automatic_log_mask: int = ObservabilityCaptureMask.NONE,
		p_automatic_events_per_frame: int = 5,
		p_automatic_repeated_error_window_msec: int = 1000,
		p_automatic_event_throttle_count: int = 20,
		p_automatic_event_throttle_window_msec: int = 10000,
		p_stack_trace_source_context_enabled: bool = true,
		p_stack_trace_variables_enabled: bool = false,
		p_automatic_message_filter_prefixes: PackedStringArray = PackedStringArray(
				["FoundryObservability: "],
		),
		p_application_hang_detection_enabled: bool = true,
		p_application_hang_timeout_msec: int = 5000,
		p_android_anr_detection_enabled: bool = true,
		p_android_anr_timeout_msec: int = 5000,
		p_android_anr_attach_thread_dump: bool = false,
)
~~~

Public fields:

| Field | Type | Meaning |
| --- | --- | --- |
| enabled | bool | Whether the service may capture events after configuration |
| environment | String | Deployment environment such as production or staging |
| release | String | Game release identifier |
| dist | String | Optional distribution variant |
| logs_enabled | bool | Whether structured logs are accepted; enabled by default |
| log_minimum_level | int | Lowest structured-log severity accepted; TRACE by default |
| log_rate_limit_per_second | int | Maximum accepted logs per monotonic engine-tick second; zero means unlimited |
| metrics_enabled | bool | Whether custom metrics are accepted; enabled by default |
| metric_sample_rate | float | Deterministic accepted fraction from 0.0 through 1.0 |
| metric_filter | Callable | Optional predicate receiving each normalized metric before sampling |
| automatic_capture_enabled | bool | Whether successful enabled configuration installs the automatic engine logger |
| automatic_event_mask | int | Categories routed to exception events |
| automatic_breadcrumb_mask | int | Categories routed to breadcrumbs |
| automatic_log_mask | int | Categories routed to structured logs |
| automatic_events_per_frame | int | Maximum automatic exception events per processed frame; zero is unlimited |
| automatic_repeated_error_window_msec | int | Duplicate-suppression window; zero disables suppression |
| automatic_event_throttle_count | int | Maximum automatic exception events in the sliding window; zero is unlimited |
| automatic_event_throttle_window_msec | int | Sliding-window duration; zero disables that limit |
| stack_trace_source_context_enabled | bool | Retain bounded stack-frame source context; enabled by default |
| stack_trace_variables_enabled | bool | Retain a bounded, type-filtered copy of stack-frame variables; disabled by default and requires explicit opt-in |
| application_hang_detection_enabled | bool | Enable native main-thread hang detection on macOS and iOS; enabled by default |
| application_hang_timeout_msec | int | Apple hang threshold in milliseconds; defaults to 5000 and normalizes to at least 1000 |
| android_anr_detection_enabled | bool | Enable native Android ANR detection; enabled by default |
| android_anr_timeout_msec | int | Pre-Android-11 watchdog threshold in milliseconds; defaults to 5000 and normalizes to at least 1000 |
| android_anr_attach_thread_dump | bool | Request an Android 11+ ANR thread dump when available; disabled by default |

Accessors:

~~~
func global_attributes() -> Dictionary
func provider_options() -> Dictionary
func automatic_message_filter_prefixes() -> PackedStringArray
~~~

Dictionary accessors return deep copies. global_attributes are shared metadata
for a provider integration. provider_options are opaque to the core and are
passed to provider implementations through the config object.
automatic_message_filter_prefixes returns a copied list of ordinary output
prefixes excluded from automatic capture.

Structured logs are enabled by default independently of messages and
exceptions. The core applies log_minimum_level and log_rate_limit_per_second
before dispatch. The rate limit uses each record's monotonic
engine_ticks_msec in a fixed one-second window; a zero limit is unlimited. A
disabled log configuration or a record below the configured level returns an
empty ID without calling the provider.

Metrics are independently enabled by default. metric_sample_rate must be finite
and between 0.0 and 1.0 inclusive; invalid configuration returns
Error.ERR_INVALID_PARAMETER without replacing the active provider. A valid
metric_filter must return bool. Returning false drops the metric normally;
returning another type stores Error.ERR_INVALID_PARAMETER. Filtering happens
before deterministic accumulator-based sampling, and the sampling sequence
resets on successful configuration and shutdown.

### Automatic engine capture

Automatic capture activates only after a provider completes enabled
configuration successfully. Reconfiguring the active provider updates and
resets the logger without adding a duplicate. Provider replacement removes the
old logger before shutting down the previous provider; failed replacement
leaves the active provider and logger unchanged. Disabling the service,
disabling automatic capture, or calling shutdown removes the logger.

The three destination masks are independent:

| Destination | Default mask | Default behavior |
| --- | --- | --- |
| Events | `ERROR | SCRIPT | SHADER` | Capture engine/native errors, `push_error`, script errors, and shader errors |
| Breadcrumbs | `ALL` | Capture every error category plus ordinary output messages |
| Structured logs | `NONE` | No automatic structured logs until explicitly enabled |

`ObservabilityCaptureMask` categories map as follows:

| Category | Engine input | Normalized level |
| --- | --- | --- |
| ERROR | Engine/native errors and `push_error` | ERROR |
| ERROR | Fatal diagnostics | FATAL |
| WARNING | Warnings and `push_warning` | WARN |
| SCRIPT | Script runtime errors | ERROR |
| SHADER | Shader errors | ERROR |
| MESSAGE | Ordinary output; error-stream output uses ERROR, other output uses INFO | ERROR or INFO |

MESSAGE never creates an exception event. It may create a breadcrumb and/or
structured log when its destination masks include MESSAGE. Before routing an
ordinary message, the logger strips ANSI escape sequences and control
characters, drops empty results, and applies
automatic_message_filter_prefixes. The default prefix prevents the
observability addon from collecting its own output.

Automatic error records preserve the callback's function, file, line, code,
rationale, diagnostic type, editor-notify flag, and serialized script
backtraces. These fields use `error.*` attribute names. Exception events also
carry a printable stack trace; every automatic record includes
`observability.origin = "auto.log.foundry"` and uses source
`foundry.engine`. Ordinary output includes its error-stream flag.
Automatic throttling uses the diagnostic's monotonic engine tick. Exception
events and structured logs pass that value as `engine_ticks_msec`, allowing the
core capture boundary to derive the Unix occurrence timestamp. Breadcrumbs
retain the engine tick in their provider-neutral `timestamp_msec`; native
Sentry breadcrumbs use wall-clock receipt time.

Duplicate identity is `(message, file, line, diagnostic type)`. A duplicate
inside automatic_repeated_error_window_msec is suppressed from every
destination. The identity table is bounded and periodically cleared.
automatic_events_per_frame and the sliding
automatic_event_throttle_count/automatic_event_throttle_window_msec pair limit
only exception events; breadcrumbs and automatic structured logs still flow.
All non-negative limit values are accepted, zero disables the corresponding
limit, and successful reconfiguration resets accumulated limit state.

Provider calls are guarded so an error emitted by provider configuration,
capture, flush, or shutdown cannot recursively enter automatic capture.
Providers should still avoid deliberately reporting their own failures through
the same observability pipeline.

A null config passed to FoundryObservability.configure is replaced with a
disabled ObservabilityConfig.

Stack-frame source context is enabled by default. When present, the core keeps
the nearest five preceding and five following lines; source-context arrays are
omitted unless the frame has a nonempty current `context_line`. Source context
can reveal source text, so callers must review it before capture. Frame
variables are disabled by default. They may contain credentials, tokens, PII,
or game state. Acquiring, inspecting, and copying stack locals can also be
expensive and increase capture latency and memory use. Producers must check
`stack_trace_variables_enabled` before acquiring locals, and only acquire them
when it is explicitly enabled. This is type filtering and bounding, not content
redaction: supported strings are forwarded verbatim, and callers must review
both source context and variables before capture. When enabled, the core and
native bridges retain only a bounded, type-filtered copy of supported values:
booleans, finite numbers, strings, arrays, and string-keyed dictionaries, up to
eight nested containers and 256 examined items per frame. Unsupported values,
nonfinite numbers, non-string keys, and cycles are omitted.

## ObservabilityException

ObservabilityException carries script or native failure data.

Constructor:

~~~
ObservabilityException.new(
		p_type_name: String = "Error",
		p_message: String = "",
		p_stack_trace: String = "",
		p_attributes: Dictionary = {},
		p_frames: Array[ObservabilityStackFrame] = [],
)
~~~

Accessors:

~~~
func type_name() -> String
func message() -> String
func stack_trace() -> String
func attributes() -> Dictionary
func frames() -> Array[ObservabilityStackFrame]
~~~

attributes are deep-copied on construction and access. The constructor copies
the containing frame array and `frames()` returns a copy, so callers cannot
replace stored entries through either array. `stack_trace()` remains the
supported formatted-string fallback: structured frames are additive and never
synthesize or overwrite it. The core does not interpret the type or formatted
stack string; providers decide how to map them.

## ObservabilityStackFrame

ObservabilityStackFrame is a provider-neutral structured exception frame.
Callers supply frames oldest-to-newest; core/providers preserve that order.
Source-context arrays are ordered earlier-to-later.

Constructor:

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

Accessors:

~~~
func file() -> String
func function() -> String
func line() -> int
func language() -> String
func in_app() -> bool
func context_line() -> String
func pre_context() -> PackedStringArray
func post_context() -> PackedStringArray
func variables() -> Dictionary
~~~

`line` is a positive one-based source line, or `-1` when unknown. On capture,
any nonpositive line normalizes to `-1`. A useful frame has a nonempty file,
function, or language, or a positive line; null, empty, and otherwise malformed
frames are omitted. A partial identity is still useful and survives. Malformed
native frame entries never prevent use of the formatted-stack fallback.

The constructor copies `pre_context` and `post_context` and deep-copies
`variables`; their accessors return new copies. The constructor's variable
ownership copy applies the same maximum of eight nested containers and 256
examined items, and omits cycle back-edges. Scalar fields are immutable after
construction. The capture boundary applies the source-context and variables
policy described under `ObservabilityConfig` before dispatching to a provider.

## ObservabilityEvent

ObservabilityEvent is the normalized provider-neutral payload.

Timestamp sentinel:

~~~
const UNASSIGNED_TIMESTAMP: int = -1
~~~

The constructor uses this sentinel when the service should resolve wall-clock
time at capture.

Constructor:

~~~
ObservabilityEvent.new(
		p_kind: StringName = &"message",
		p_level: int = ObservabilityLevel.INFO,
		p_message: String = "",
		p_source: StringName = &"",
		p_timestamp_msec: int = UNASSIGNED_TIMESTAMP,
		p_attributes: Dictionary = {},
		p_exception: ObservabilityException? = null,
		p_engine_ticks_msec: int = -1,
)
~~~

Fields:

| Parameter | Meaning |
| --- | --- |
| kind | Event category, such as message, exception, or log |
| level | ObservabilityLevel value or another provider-defined integer |
| message | Human-readable event text |
| source | Subsystem that produced the event |
| timestamp_msec | Unix epoch timestamp in milliseconds; -1 means unresolved |
| attributes | Structured fields copied into the event |
| exception | Optional exception payload |
| engine_ticks_msec | Monotonic engine tick in milliseconds; -1 means unavailable |

Accessors:

~~~
func kind() -> StringName
func level() -> int
func message() -> String
func source() -> StringName
func timestamp_msec() -> int
func engine_ticks_msec() -> int
func attributes() -> Dictionary
func exception() -> ObservabilityException?
~~~

attributes are deep-copied on construction and access. exception is optional and
is returned as the original payload object. Every event field is final after
construction; timestamp resolution creates a new normalized event rather than
mutating the caller's event.

## ObservabilityCaptureMask

ObservabilityCaptureMask defines bit flags used by the three automatic
destination policies:

| Constant | Value | Meaning |
| --- | ---: | --- |
| NONE | 0 | No categories |
| ERROR | `1 << 0` | Engine/native errors, `push_error`, and fatals |
| WARNING | `1 << 1` | Warnings and `push_warning` |
| SCRIPT | `1 << 2` | Script runtime errors |
| SHADER | `1 << 3` | Shader errors |
| MESSAGE | `1 << 7` | Ordinary output messages |
| ALL_ERRORS | combined | ERROR, WARNING, SCRIPT, and SHADER |
| ALL | combined | Every error category plus MESSAGE |
| DEFAULT_EVENTS | combined | ERROR, SCRIPT, and SHADER |
| DEFAULT_BREADCRUMBS | combined | ALL |

Combine categories with bitwise OR. For example, opt warnings and script errors
into automatic structured logs with
`ObservabilityCaptureMask.WARNING | ObservabilityCaptureMask.SCRIPT`.

## ObservabilityBreadcrumb

ObservabilityBreadcrumb is the normalized provider-neutral trail record.

Constructor:

~~~
ObservabilityBreadcrumb.new(
		p_message: String = "",
		p_level: int = ObservabilityLevel.INFO,
		p_category: StringName = &"",
		p_timestamp_msec: int = 0,
		p_attributes: Dictionary = {},
)
~~~

Accessors:

~~~
func message() -> String
func level() -> int
func category() -> StringName
func timestamp_msec() -> int
func attributes() -> Dictionary
~~~

attributes are deep-copied on construction and access. Breadcrumb delivery is
capability-based so event-only providers remain compatible.

## ObservabilityFeedback

ObservabilityFeedback is the explicit player-submitted payload used by
capture_feedback(). It is separate from ObservabilityEvent and is never created
by automatic error, message, or log capture.

Constructor:

~~~
ObservabilityFeedback.new(
		p_message: String,
		p_name: String = "",
		p_contact_email: String = "",
		p_associated_event_id: String = "",
)
~~~

Accessors:

~~~
func message() -> String
func name() -> String
func contact_email() -> String
func associated_event_id() -> String
~~~

The message is required, must contain non-whitespace text, and is limited to
4096 Unicode characters. Optional values are omitted when empty. Non-empty
optional values must contain no control characters; a contact email must have
one non-empty local and domain portion. The associated event ID is opaque to
the core and is forwarded only when the caller supplies it.

## ObservabilityMetricType

ObservabilityMetricType defines the provider-neutral metric kinds:

| Constant | Value | Meaning |
| --- | ---: | --- |
| COUNTER | 0 | Non-negative whole-number occurrence count |
| GAUGE | 1 | Current numeric measurement that may rise or fall |
| DISTRIBUTION | 2 | Numeric sample used for aggregate statistics |

## ObservabilityMetric

ObservabilityMetric is the normalized provider-neutral metric payload.

Constructor:

~~~
ObservabilityMetric.new(
		p_type: int = ObservabilityMetricType.COUNTER,
		p_name: String = "",
		p_value: float = 0.0,
		p_unit: String = "",
		p_attributes: Dictionary = {},
)
~~~

Accessors:

~~~
func type() -> int
func name() -> String
func value() -> float
func unit() -> String
func attributes() -> Dictionary
~~~

attributes are deep-copied on construction and access. The core validates and
normalizes every metric before dispatch.

## ObservabilityProvider

ObservabilityProvider is the trait backend integrations implement:

~~~
trait_name ObservabilityProvider

abstract func provider_name() -> StringName
abstract func is_available() -> bool
abstract func configure(config: ObservabilityConfig) -> int
abstract func capture(event: ObservabilityEvent) -> String
abstract func capture_feedback(feedback: ObservabilityFeedback) -> String
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
~~~

Method contracts:

- provider_name returns a stable identifier such as memory, null, or sentry.
- is_available reports whether the backend can currently accept events. It does
  not configure or shut down the provider.
- configure applies the complete config and returns Error.OK or a failure.
  FoundryObservability configures a candidate before making it active.
- capture translates one normalized event and returns a provider event ID.
  Return an empty string when the event cannot be accepted.
- capture_feedback translates an explicit player feedback payload through the
  provider's dedicated feedback API and returns a provider ID. It must not
  convert feedback into an ordinary error event.
- flush attempts to deliver pending data within timeout_msec. It returns an
  Error value; the service stores that value in last_error().
- shutdown releases provider resources. Implementations must make repeated
  shutdown calls safe because the service owns lifecycle cleanup.

Providers must not report their own configuration, capture, or flush failures
through the FoundryLib logging sink. Doing so would create recursive reporting.

## ObservabilityMetricsProvider

ObservabilityMetricsProvider is an optional provider capability:

~~~
trait_name ObservabilityMetricsProvider

abstract func capture_metric(metric: ObservabilityMetric) -> bool
~~~

A provider implements this trait when it accepts normalized custom metrics.
Existing providers that implement only ObservabilityProvider remain compatible:
event, log, and feedback capture continue to work, while metric capture safely
returns false and stores Error.ERR_UNAVAILABLE.

## ObservabilityBreadcrumbsProvider

ObservabilityBreadcrumbsProvider is an optional provider capability:

~~~
trait_name ObservabilityBreadcrumbsProvider

abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
~~~

A provider implements this trait when it accepts normalized breadcrumbs.
Providers that implement only ObservabilityProvider remain compatible: their
event, log, feedback, and metric behavior is unchanged, while breadcrumb
capture returns false and stores Error.ERR_UNAVAILABLE.

## FoundryObservabilityApi

FoundryObservabilityApi is the provider-neutral service trait. It allows an
integration or game subsystem to depend on the service contract without
depending on the concrete autoload class:

~~~
trait_name FoundryObservabilityApi

abstract func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int
abstract func is_enabled() -> bool
abstract func is_available() -> bool
abstract func provider_name() -> StringName
abstract func last_error() -> int
abstract func capture_event(event: ObservabilityEvent) -> String
abstract func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}) -> String
abstract func capture_log(message: String, level: int = ObservabilityLevel.INFO, source: StringName = &"game", timestamp_msec: int = -1, attributes: Dictionary = {}, engine_ticks_msec: int = -1) -> String
abstract func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String
abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
abstract func capture_feedback(feedback: ObservabilityFeedback) -> String
abstract func capture_metric(metric: ObservabilityMetric) -> bool
abstract func capture_counter(metric_name: String, value: int = 1, attributes: Dictionary = {}) -> bool
abstract func capture_gauge(metric_name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
abstract func capture_distribution(metric_name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
~~~

FoundryObservability implements this trait and is the service instance normally
used by game code.

## FoundryObservability autoload

FoundryObservability is the registered service and implements
FoundryObservabilityApi.

### configure

~~~
func configure(
		provider: ObservabilityProvider,
		config: ObservabilityConfig? = null,
) -> int
~~~

Behavior:

1. A null provider returns Error.FAILED and remains inactive.
2. A null config becomes a disabled ObservabilityConfig.
3. The candidate provider is configured before replacing the active provider.
4. A failed candidate configuration leaves the existing provider and config
   active and stores the returned error.
5. Configuring the already-active provider updates its config without shutting
   it down.
6. Configuring a different provider shuts down the old provider once, then
   activates the candidate and clears last_error().
7. Successful enabled configuration installs or updates automatic capture when
   automatic_capture_enabled is true. Failed configuration does not disturb
   the current logger.

The method returns the provider configure result.

When the enabled provider is `SentryObservabilityProvider`, a missing native
bridge or a bridge without lifecycle contract version 1 returns
`Error.ERR_UNAVAILABLE`. This result leaves the existing active provider
unchanged.

### Status methods

~~~
func is_enabled() -> bool
func is_available() -> bool
func provider_name() -> StringName
func last_error() -> int
~~~

Before configuration, the service is disabled, unavailable, reports provider
name null, and has last_error() equal to Error.OK.

is_enabled reflects config.enabled. is_available delegates to the active
provider. provider_name delegates to the active provider and returns null when
no provider is active. last_error returns the latest stored configuration,
capture, or flush error. A successful provider configuration clears the error.

### capture_event

~~~
func capture_event(event: ObservabilityEvent) -> String
~~~

Returns an event ID from the active provider. It returns an empty string without
calling the provider when event is null, the service is disabled, or no
provider is active. If an enabled provider returns an empty ID, the service
stores Error.FAILED.

### capture_message

~~~
func capture_message(
		message: String,
		level: int = ObservabilityLevel.INFO,
		attributes: Dictionary = {},
) -> String
~~~

Creates an event with:

- kind message
- the requested level
- the supplied message
- source game
- the current Unix timestamp and monotonic engine tick, captured together
- the supplied attributes
- no exception payload

It then forwards that event through capture_event.

Example:

~~~
import foundry.observability

var event_id: String = FoundryObservability.capture_message(
		"player entered the arena",
		ObservabilityLevel.INFO,
		{"arena": "north"},
)
~~~

### capture_log

~~~
func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
		engine_ticks_msec: int = -1,
) -> String
~~~

Creates a first-class structured log record. It preserves the supplied source,
Unix timestamp, monotonic engine tick, level, and scalar attributes. The core
passes both per-record attributes and ObservabilityConfig global attributes to
provider integrations; providers decide how to merge or map them. A
`timestamp_msec` of -1 is derived from `engine_ticks_msec` when one was
supplied; otherwise both missing values resolve to the current capture-time
pair. Log records remain distinct from message and exception events.

Example:

~~~
import foundry.observability

FoundryObservability.capture_log(
		"match started",
		ObservabilityLevel.INFO,
		&"matchmaking",
		-1,
		{"region": "iad", "party_size": 4},
)
~~~

Providers that do not support structured logs may safely return an empty ID;
the service records the failed capture status when the provider is enabled.

### capture_breadcrumb

~~~
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
~~~

Dispatches a normalized breadcrumb when the active provider implements
ObservabilityBreadcrumbsProvider. A true result means the provider accepted
the breadcrumb into its local SDK or store; it does not guarantee remote
delivery. Null input returns false and stores Error.ERR_INVALID_PARAMETER.
Disabled capture returns false without calling the provider. A provider without
the optional capability returns false and stores Error.ERR_UNAVAILABLE; a
non-boolean or false provider result stores Error.FAILED.

Automatic capture silently skips this optional destination when the provider
does not implement it, so a successfully accepted exception event does not
leave `last_error()` in an unrelated unavailable state. Explicit
`capture_breadcrumb()` calls retain the observable `ERR_UNAVAILABLE` behavior.
A successful automatic breadcrumb also does not clear a failure from an earlier
automatic event destination in the same callback.
If a provider implements breadcrumb capture but rejects an automatic
breadcrumb, that provider failure remains observable as `Error.FAILED`, even
when an independent event destination accepted the same diagnostic.

### capture_feedback

~~~
func capture_feedback(feedback: ObservabilityFeedback) -> String
~~~

Accepts an explicitly constructed feedback payload and returns a provider ID.
Null or invalid feedback returns an empty string and stores
Error.ERR_INVALID_PARAMETER. Invalid feedback includes a missing or
oversized message, malformed email, or control characters in optional values.
When the service is disabled, the submission is ignored without collecting or
forwarding it. When an enabled provider returns an empty ID, the service stores
Error.FAILED.

Example:

~~~
import foundry.observability

var feedback := ObservabilityFeedback.new(
		p_message = "The tutorial was confusing.",
		p_name = "Player One",
		p_contact_email = "player@example.com",
		p_associated_event_id = previous_event_id,
)
var feedback_id: String = FoundryObservability.capture_feedback(feedback)
~~~

The core does not persist feedback or implement an offline retry queue. A
provider owns its transport, offline storage, and retry policy. Call
FoundryObservability.flush() when the game reaches a suitable delivery point;
the call is a best-effort provider flush within the supplied timeout.

### capture_metric and metric conveniences

~~~
func capture_metric(metric: ObservabilityMetric) -> bool
func capture_counter(
		metric_name: String,
		value: int = 1,
		attributes: Dictionary = {},
) -> bool
func capture_gauge(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool
func capture_distribution(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool
~~~

capture_metric validates, normalizes, filters, samples, and dispatches one
custom metric. The convenience methods construct the corresponding
ObservabilityMetric. A true result means the active provider accepted the
metric into its local SDK or store; it does not guarantee remote delivery.

Metric names must contain 1 through 200 characters, have no leading or trailing
whitespace, and contain no control characters. Values must be finite. Counters
must be non-negative whole numbers and cannot have a unit. Gauge and
distribution units are optional, limited to 64 characters, and cannot contain
whitespace or control characters.

Attribute keys must be String or StringName values containing 1 through 200
characters with no surrounding whitespace or control characters. Attribute
values are limited to bool, int, finite float, String, and StringName. Global
configuration attributes are merged first and per-metric attributes win on
duplicate keys. Invalid global or per-metric metric attributes reject the
metric with Error.ERR_INVALID_PARAMETER.

Examples:

~~~
import foundry.observability

FoundryObservability.capture_counter(
		"match.started",
		1,
		{"mode": "ranked"},
)
FoundryObservability.capture_gauge(
		"players.active",
		12.0,
		"player",
)
FoundryObservability.capture_distribution(
		"matchmaking.duration",
		187.5,
		"millisecond",
		{"region": "iad"},
)
~~~

When metrics or the whole service are disabled, a metric filtered out, or a
metric dropped by sampling, capture returns false without treating the drop as
a provider failure. Providers own batching, transport, offline retry, and
delivery. flush() forwards the timeout to the active provider so its SDK can
attempt delivery.

### capture_exception

~~~
func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
) -> String
~~~

A null exception returns an empty ID and stores Error.FAILED. Otherwise it
creates an event with kind exception, level ERROR, source game, the current Unix
timestamp and monotonic engine tick captured together, exception.message() as
the message, the supplied attributes, and the exception payload.

Example:

~~~
import foundry.observability

# Local values can contain secrets or player data. Enable only when intended,
# before acquiring or adding them to a frame.
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(
		provider,
		ObservabilityConfig.new(
				p_enabled = true,
				p_stack_trace_variables_enabled = true,
		),
)

var exception := ObservabilityException.new(
		p_type_name = "NetworkError",
		p_message = "Matchmaking request failed",
		# Keep a formatted fallback for providers that do not use frames.
		p_stack_trace = "at Matchmaker.join()",
		p_attributes = {"region": "iad"},
		p_frames = [
				ObservabilityStackFrame.new(
						p_file = "res://network/Matchmaker.fs",
						p_function = "Matchmaker.join",
						p_line = 42,
						p_language = "foundryscript",
						p_context_line = "\treturn _transport.send(request)",
						p_pre_context = PackedStringArray(["\tvar request := _request()"]),
						p_post_context = PackedStringArray(["\treturn Error.FAILED"]),
						# Variables are supplied only because of the explicit opt-in above.
						p_variables = {"match_id": "match-12345"},
				),
		],
)
FoundryObservability.capture_exception(exception)
~~~

### flush

~~~
func flush(timeout_msec: int = 2000) -> int
~~~

Forwards timeout_msec to the active provider and stores the returned Error
value. With no active provider, it returns Error.OK.

### shutdown

~~~
func shutdown() -> void
~~~

shutdown is idempotent. The first call flushes, shuts down the active provider,
restores the NullObservabilityProvider, restores a disabled config, and resets
last_error() to Error.OK. Later calls do nothing. The autoload also calls
shutdown from _exit_tree.

## Built-in providers

### NullObservabilityProvider

NullObservabilityProvider is active before configuration and whenever shutdown
restores the service. It reports provider name null, is_available false,
returns Error.OK from configure and flush, returns empty IDs from capture, and
has a safe no-op shutdown.

### MemoryObservabilityProvider

MemoryObservabilityProvider is a deterministic local provider for tests and
development. It reports provider name memory and is always available.

Public test controls:

| Member | Behavior |
| --- | --- |
| configure_result | Result returned by configure before state changes |
| flush_result | Result returned by flush |
| last_flush_timeout_msec | Timeout from the most recent flush |
| flush_count | Number of flush calls |
| shutdown_count | Number of effective shutdown calls |
| metric_capture_result | Whether custom metric capture accepts metrics |

Public methods:

~~~
func events() -> Array[ObservabilityEvent]
func breadcrumbs() -> Array[ObservabilityBreadcrumb]
func feedback() -> Array[ObservabilityFeedback]
func metrics() -> Array[ObservabilityMetric]
func clear() -> void
func clear_breadcrumbs() -> void
func clear_feedback() -> void
func clear_metrics() -> void
~~~

events returns a copy of the captured event list. clear removes captured events
without changing configuration. Successful event capture returns sequential
IDs in the form memory:N. Breadcrumbs are stored in their own list and return
true when accepted. Feedback is stored separately and returns IDs in the form
memory-feedback:N. Metrics are stored in another list and return true when
accepted. Capture returns an empty ID or false while disabled or after
shutdown.

Example:

~~~
import foundry.observability

var provider := MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, ObservabilityConfig.new(p_enabled = true))
FoundryObservability.capture_message("test event")
var captured: Array[ObservabilityEvent] = provider.events()
~~~

## FoundryLib integration

The core addon includes FoundryLibObservabilitySink. FoundryLib must be installed
as a project package before importing this adapter. The sink is explicit: it
does not install itself and does not register an autoload.

~~~
import foundry.logging
import foundry.observability
import foundry.observability.foundrylib

var sink := FoundryLibObservabilitySink.new(
		p_service = FoundryObservability,
		p_minimum_level = ObservabilityLevel.ERROR,
)
Log.add_sink(sink)
~~~

Constructor:

~~~
FoundryLibObservabilitySink.new(
		p_service: FoundryObservabilityApi,
		p_minimum_level: int = ObservabilityLevel.ERROR,
)
~~~

emit filters records below minimum_level or when its target service is null.
Eligible records are sent through FoundryObservability.capture_log() with:

| Field | Value |
| --- | --- |
| kind | log |
| source | foundry.logging |
| level | Explicit LogLevel mapping below |
| message | LogFormatter.render_message(record) |
| timestamp_msec | Derived Unix epoch milliseconds |
| engine_ticks_msec | record.timestamp_msec |
| attributes | Deep copy of record.fields plus logger_name |
| exception | null |

FoundryLib records use monotonic engine ticks. The service retains the original
tick and derives the wall-clock occurrence time from the wall/tick pair read
when the record is captured.

Level mapping:

| FoundryLib LogLevel | ObservabilityLevel |
| --- | --- |
| TRACE | TRACE |
| DEBUG | DEBUG |
| INFO | INFO |
| WARN | WARN |
| ERROR | ERROR |
| FATAL | FATAL |
| Unknown | INFO |

flush forwards to FoundryObservability.flush() with its default timeout.
Provider failures are stored in the service status API and are not emitted
through FoundryLib, preventing recursive error reporting.

## Mobile hang and ANR diagnostics

ObservabilityConfig exposes provider-neutral controls for native main-thread
hang diagnostics. `application_hang_detection_enabled` and
`application_hang_timeout_msec` apply to macOS and iOS.
`android_anr_detection_enabled`, `android_anr_timeout_msec`, and
`android_anr_attach_thread_dump` apply only to Android. Both detectors are
enabled by default with a 5000 ms timeout; Android thread-dump attachment is
disabled by default. Timeout values below 1000 ms normalize to 1000 ms.

The Sentry provider applies these values before native SDK startup. Apple uses
native app-hang tracking. Android versions before 11 use watchdog-based ANR
detection and honor the configured timeout. Android 11 and later use historical
process-exit information, so the operating system determines the ANR threshold
and delivery may occur on the next launch. Thread-dump attachment applies only
when the native Android implementation provides a dump.

Android 11 and later historical ANR reporting requires usable operating-system
`ApplicationExitInfo` and trace data. If the operating system provides no
actionable, readable trace, no diagnostic event may be produced. Parsed thread
data is present only when trace parsing succeeds. A raw thread-dump attachment
is present only when requested and readable and available from the native
implementation.

FoundryObservability does not synthesize these events. The native SDK therefore
retains severity, mechanism, blocked-thread state, attachments, lifecycle
handling, and platform diagnostic metadata. Native lifecycle integrations own
startup, shutdown, and suspension handling. A genuine main-thread block longer
than the applicable threshold is expected to report; intentional long work
should run off the main thread or the detector should be disabled before
provider configuration.

Providers without these capabilities may ignore the neutral settings.
Platforms without the native Sentry bridge retain the existing configuration
failure and `is_available()` behavior.

Manual release validation:

Use a test-only controlled trigger in a non-production build. Launch detached
from the debugger: Apple app-hang tracking and pre-Android-11 ANR reporting may
be disabled or ignored while a debugger is attached. Block the main thread with
a deliberate margin beyond the configured threshold, rather than only barely
exceeding it. After the controlled stall and recovery, allow capture to
finalize and transport to complete on every platform. After a recovered Apple
hang, also allow time for native app-hang capture to finalize. On Android 10 or
earlier, the configured timeout is the SDK watchdog threshold, but reporting
also requires ActivityManager to consider the process actually not responding.
On Android 11 or later, relaunch the app and wait for historical processing and
transport.

| Target | Trigger | Expected |
| --- | --- | --- |
| macOS | Block the main thread for longer than 5 seconds in a controlled test build. | Enabled: one native event after recovery; disabled: no event. Inspect severity, mechanism, blocked thread, stack, release, environment, device, and OS data. |
| iOS | Block the main thread for longer than 5 seconds on a physical test device. | Enabled: one native event after recovery; disabled: no event. Inspect severity, mechanism, blocked thread, stack, release, environment, device, and OS data. |
| Android 10 or earlier | Block the main thread beyond the configured watchdog timeout. | Enabled: one watchdog ANR event only when ActivityManager considers the process not responding; native severity is ERROR. Disabled: no event. Inspect mechanism, threads, release, environment, device, and OS data. |
| Android 11 or later | Trigger a controlled system ANR, then relaunch the app. | Enabled: when usable `ApplicationExitInfo` and readable trace data produce the native V2 ANR diagnostic, its severity is FATAL; parsed threads appear only after successful trace parsing, and a raw thread dump only when requested and readable and available. If no actionable trace is available, no event may be produced. Disabled: no event. Inspect mechanism, threads when parsed, release, environment, device, and OS data. |

## Sentry native crash lifecycle

The Sentry provider starts the native SDK when enabled configuration succeeds.
Call `FoundryObservability.configure(SentryObservabilityProvider.new(), config)`
at the earliest supported startup boundary, once the DSN and deployment
metadata are available. This is the first point at which the addon can install
native crash handlers. A fatal crash in the pre-configuration gap is outside
the addon's capture boundary and cannot be recovered later.

The native startup configuration includes `release`, `environment`, `dist`,
and scalar global attributes. Global attributes are installed under the
`foundry.global_attributes` context before capture begins. Apple enables the
Sentry crash handler. Android enables its uncaught-exception handler, NDK
integration, and native scope synchronization. Sentry persists fatal crash data
in-process and sends the previous launch report after the next launch starts
with the same deployment identity.

Sentry is process-global, while providers are ordinary FoundryScript objects.
The bridge therefore uses an owner-safe lifecycle:

- Equivalent configuration transfers ownership without restarting the SDK.
- Changed configuration performs a bounded 2-second shutdown before starting
  the replacement.
- If replacement startup fails, the prior owner and configuration are restored.
- Flush and shutdown calls from a stale owner do nothing, so an obsolete
  provider cannot stop a newer session. These idempotent no-ops return
  `Error.OK`; availability remains the observable ownership signal.

Enabled configuration returns `Error.ERR_UNAVAILABLE` if the native bridge is
missing or too old. After `Error.OK`, use
`FoundryObservability.is_available()` to verify that this provider owns the
running SDK. Keep release, environment, and distribution stable between the
crash run and its recovery launch so Sentry can classify the stored report
consistently.

The repository does not ship a callable production crash helper. Follow
[Native Crash Validation](NATIVE_CRASH_VALIDATION.md) for guarded macOS and
Android tooling plus LLDB/Xcode steps for iOS. Perform that procedure only
against a non-production Sentry project and disposable test data.

## Automatic runtime context

Enabled `SentryObservabilityProvider` configuration automatically enriches
macOS, iOS, and Android Sentry data with provider-private Godot runtime
contexts. This does not add fields or methods to the provider-neutral API and
does not modify caller-supplied event attributes or global attributes.

The addon uses six custom context names:

| Context | Field families |
| --- | --- |
| `foundry_app` | Project name and version, process start time, and architecture |
| `godot_engine` | Engine version and commit, architecture, runtime mode, and editor/debug/headless flags |
| `foundry_device` | Model and device type, architecture, processor, memory, storage, and opt-in identifying values |
| `display` | Display server, screen/touch availability, primary dimensions, DPI, refresh rate, and orientation |
| `gpu` | Adapter and vendor, API and device type, driver, and rendering method |
| `foundry_runtime` | Deployment environment, classified runtime mode, sandbox state, and user-storage persistence |

Configuration collects a stable snapshot and installs it on the native Sentry
scope before capture begins. This makes the snapshot available to native crash
handling. Ordinary event capture copies that snapshot and refreshes current
free memory, usable memory, free user-storage space, and primary orientation
for that event only. A failed reconfiguration retains the last successful
snapshot; successful disable and shutdown clear it.

Runtime mode uses deterministic precedence: headless or dedicated-server
execution is `headless`, otherwise editor execution is `editor`, otherwise a
debug build is `debug_export`, and the remaining case is `release_export`.
Empty strings, `GenericDevice`, nonpositive dimensions and counts, negative
capacities, missing platform values, unsupported objects, nonfinite numbers,
invalid keys, cycles, and empty contexts are omitted rather than guessed.

Godot's memory-information API is not called on iOS. Consequently
`memory_size`, `free_memory`, and `usable_memory` are omitted there; supported
storage, display, GPU, and runtime values continue to be collected. The addon
uses only the safe cross-platform engine APIs exposed for each target.

Identifying values are opt-in. By default, the automatic device context omits
the operating-system unique identifier, locale, and timezone. Set
`provider_options["send_default_pii"] = true` only after the project has made
the corresponding privacy disclosure and obtained any required consent. This
also enables the pinned native Sentry SDK's default-PII behavior. Invalid or
non-boolean option values do not opt in.

These custom names do not replace Sentry's native `app`, `device`, or
operating-system contexts. The existing `foundry.global_attributes` crash
context also remains separate. Native fatal reports can include the stable
configuration-time snapshot, but the addon deliberately does not attempt to
query volatile Godot values while recovering a previous-launch crash. Such a
report therefore has no capture-time refresh beyond the stable snapshot that
was installed before the crash.

## Sentry structured-log delivery

The optional `FoundryObservabilitySentry` addon maps `kind = log` records to
the native structured logging API of the pinned Sentry SDK instead of ordinary
error events. Apple uses Sentry Cocoa 9.23.0 and enables `SentrySDK.logger`;
Android uses Sentry Android 8.50.1 and enables `Sentry.logger()` through
`SentryLogParameters` and `SentryAttributes.fromMap`. Both bridges preserve
the normalized level, message, source, timestamp, global attributes, and
per-record scalar attributes. Ordinary events explicitly set Sentry's native
occurrence timestamp from `timestamp_msec`. Reserved metadata is also available
as `foundry.kind`, `foundry.source`, `foundry.timestamp_msec`, and
`foundry.engine_ticks_msec` attributes.

The native structured-log APIs do not accept an occurrence timestamp, so log
timestamps are retained in the reserved metadata instead of being assigned to a
native log timestamp field.

The Sentry SDK owns batching and delivery queues; `FoundryObservability.flush()`
forwards to the native bridge. An enabled configuration with structured logs
requires a native bridge exposing `captureLog`; configuration fails when that
method is absent, so mismatched native and FoundryScript provider versions are
detected before the provider becomes active. Ordinary messages and exceptions
continue using the regular event path.

## Sentry breadcrumb delivery

The optional Sentry provider implements ObservabilityBreadcrumbsProvider and
maps normalized breadcrumbs directly to native Sentry breadcrumbs. Apple uses
Sentry Cocoa 9.23.0 `Breadcrumb` and `SentrySDK.addBreadcrumb`; Android uses
Sentry Android 8.50.1 `Breadcrumb` and `Sentry.addBreadcrumb`. Both preserve
message, normalized level, category, global attributes, and per-breadcrumb
attributes. Per-breadcrumb values override matching global values. The native
SDK timestamp records wall-clock receipt time; the normalized engine uptime is
retained as reserved `foundry.timestamp_msec` data.

Breadcrumb support remains optional at both provider boundaries. A provider
without ObservabilityBreadcrumbsProvider, or an older Sentry native bridge
without `captureBreadcrumb`, returns false for that breadcrumb while ordinary
event capture continues.

## Sentry structured exception stacks

The optional Sentry provider forwards the neutral frame dictionaries from
`ObservabilityStackFrame`; it does not require callers to construct a
provider-specific type. On Apple, the bridge maps them to Sentry Cocoa
`Frame`/`SentryStacktrace` values. On Android, it maps them to
`SentryStackFrame`/`SentryStackTrace` values. Both preserve the normalized
frame order and supported source context and variables. The legacy formatted
string remains in the `foundry.stack_trace` extras when nonempty, including
when structured frames are unavailable or malformed, for fallback compatibility
and operator visibility.

## Sentry feedback delivery

The optional Sentry provider maps capture_feedback() to Sentry's dedicated
feedback API, not to an error event. The Apple bridge uses Sentry Cocoa's
`SentryFeedback`; Android uses Sentry Android's `Feedback`. Both preserve the
message and caller-supplied optional name, contact email, and associated event
ID. Anonymous feedback is valid, and empty optional fields are omitted.

The native `send_default_pii` option defaults to false. Set
`provider_options["send_default_pii"] = true` only when the project explicitly
accepts the provider's default PII behavior; this option does not cause the
feedback API to collect name or email unless those fields were supplied in the
feedback object. A feedback call requires a native bridge exposing
`captureFeedback`; if an older bridge lacks it, that feedback call returns an
empty ID while ordinary messages, exceptions, and logs continue to use the
bridge's supported methods.

## Sentry custom-metric delivery

The optional Sentry provider maps counters, gauges, and distributions directly
to the native metrics APIs in Sentry Cocoa 9.23.0 and Sentry Android 8.50.1.
It enables metrics from ObservabilityConfig.metrics_enabled and forwards the
normalized name, numeric value, optional unit, and merged scalar attributes.
Sentry's native SDK owns batching and transport; FoundryObservability.flush()
uses the same native flush path as events and logs.

Metric support is capability-based. When an older or unsupported native bridge
does not expose captureMetric, metric capture returns false while ordinary
events, structured logs, and feedback continue using their available paths.

## Custom provider outline

A provider implements all methods in ObservabilityProvider and may also
implement optional capabilities to translate metrics and breadcrumbs:

~~~
namespace my_game.telemetry

import foundry.observability

class_name MyProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityMetricsProvider, ObservabilityBreadcrumbsProvider

func provider_name() -> StringName:
	return &"my_provider"

func is_available() -> bool:
	return true

func configure(config: ObservabilityConfig) -> int:
	return Error.OK

func capture(event: ObservabilityEvent) -> String:
	return "my_provider:event"

func capture_feedback(feedback: ObservabilityFeedback) -> String:
	return "my_provider:feedback"

func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	# Enqueue the normalized breadcrumb in the provider SDK.
	return true

func capture_metric(metric: ObservabilityMetric) -> bool:
	# Enqueue the normalized metric in the provider SDK.
	return true

func flush(timeout_msec: int = 2000) -> int:
	return Error.OK

func shutdown() -> void:
	pass
~~~

Configure the provider through the autoload. Provider-specific credentials and
options belong in ObservabilityConfig.provider_options(); the core does not
interpret them.

## Deliberate boundary

This core API does not include Sentry, Apple/Android native bindings, crash
handlers, automatic identity collection, persistence, or retry queues,
attachments, or performance transactions. Providers own native delivery,
offline storage, retry policy, and flush behavior. The optional Sentry sibling
addon contains its native bindings, breadcrumb delivery, structured-log
delivery, feedback delivery, and custom-metric delivery. A foundry-cpp project
is not required by this API.
