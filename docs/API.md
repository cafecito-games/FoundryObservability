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

The smallest manual setup is:

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

## Project-settings startup

The preferred Sentry setup is automatic initialization from `project.foundry`.
The editor plugin registers these settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `foundry_observability/startup/auto_init` | `true` | Run project-settings initialization during autoload construction. |
| `foundry_observability/startup/enabled` | `true` | Enable the resulting observability configuration. |
| `foundry_observability/startup/skip_editor_play` | `false` | Skip automatic startup when a game is run with the editor feature. |
| `foundry_observability/startup/skip_debug_exports` | `false` | Skip automatic startup in a debug export. |
| `foundry_observability/options/dsn` | `""` | Sentry DSN; a nonempty project value overrides `SENTRY_DSN`. |
| `foundry_observability/options/environment` | `""` | Deployment environment; a nonempty project value overrides `SENTRY_ENVIRONMENT`. |
| `foundry_observability/options/release` | `""` | Release value or template; a nonempty project value overrides `SENTRY_RELEASE`. |
| `foundry_observability/options/dist` | `""` | Optional distribution value. |
| `foundry_observability/options/debug_diagnostics` | `2` (`Auto`) | Sentry diagnostic output: `0` is Off, `1` is On, and `2` follows debug-build state. |
| `foundry_observability/options/provider_options` | `{}` | Additional data-only options copied into the provider configuration. |

A complete explicit configuration uses normal `project.foundry` section/key
syntax:

```ini
[foundry_observability]

startup/auto_init=true
startup/enabled=true
startup/skip_editor_play=false
startup/skip_debug_exports=false
options/dsn="NON_PRODUCTION_SENTRY_DSN"
options/environment="production"
options/release="{app_name}@{app_version}"
options/dist="store-macos"
options/debug_diagnostics=2
options/provider_options={
"send_default_pii": false
}
```

### Deployment value resolution

The DSN, release, and environment each resolve in this order: a nonempty
project setting, the corresponding `SENTRY_DSN`, `SENTRY_RELEASE`, or
`SENTRY_ENVIRONMENT` process environment variable, then a deterministic
default. A DSN has no usable final default, so an unresolved DSN produces the
`missing_dsn` status. `dist` comes only from the project setting.
Startup only tests whether the trimmed DSN is nonempty; the provider may reject its syntax during configuration.

The default release is `{app_name}@{app_version}`. The release setting and
`SENTRY_RELEASE` may contain `{app_name}` and `{app_version}` tokens. Expansion
is a single pass, so token text inside an application name or version is not
expanded again. Missing or blank application metadata uses
`Unknown Foundry project` and `noversion`, producing
`Unknown Foundry project@noversion` when neither release metadata nor project
identity is available.

The detected environment is the first matching runtime value:

| Runtime | Environment |
| --- | --- |
| Dedicated server | `dedicated_server` |
| Editor process | `editor_dev` |
| Game running with the editor feature | `editor_dev_run` |
| Debug export | `export_debug` |
| Other export | `export_release` |

### Provider-option validation

The top-level `provider_options` value must be a `Dictionary`. Nested values may be null, booleans, integers, finite floats, strings, `StringName` values, arrays, or dictionaries.
Dictionary keys must be strings or `StringName` values. Nested arrays and
dictionaries may contain at most eight nested containers. Validation examines
at most 256 dictionary entries and array elements across the complete value,
rejects cycles and unsupported types, and stores a deep copy.

The typed DSN and diagnostic settings are authoritative. After validation,
startup overwrites any `provider_options["dsn"]` and
`provider_options["debug"]` values with the resolved DSN and boolean diagnostic
value. `Auto` enables diagnostics for debug builds and disables them otherwise.

### Skip decisions and results

Startup applies skip outcomes before acting on a missing DSN, option-validation
errors, or provider availability, in this order:

1. `auto_init=false` or `enabled=false` produces `disabled`.
2. Running in the editor process produces `skipped_editor`.
3. An editor-feature game with `skip_editor_play=true` produces
   `skipped_editor_play`.
4. A debug export with `skip_debug_exports=true` produces `skipped_debug`.

During initial autoload construction, an intentional skip leaves the null
provider active. These intentional skips return `Error.OK` and take precedence
over a missing DSN or invalid provider-option values.

When `enabled=false` is reread after successful startup, observability flushes
and shuts down the active provider, removes automatic engine logging, restores
the disabled null provider, and records `disabled`, its stable message, and
`Error.OK`. A flush failure does not change that final disabled result.
Repeating the same disabled settings is idempotent. A later `enabled=true`
startup reuses the cached startup provider and resumes capture.

By contrast, a later `auto_init=false` reread records `disabled`, its stable
message, and `Error.OK`, but preserves an already active provider and
configuration. Editor, editor-play, and debug-export skip outcomes have the
same preservation behavior. `ObservabilityStartupSettings.capture_enabled()`
provides the provider-neutral distinction between capture disablement and
these startup-only skips.

The startup result can be inspected through the provider-neutral public methods:

```foundryscript
func initialize_from_project_settings() -> int
func startup_status() -> StringName
func startup_message() -> String
```

`initialize_from_project_settings()` rereads all registered settings and
reruns the startup path. `startup_status()` returns one of the stable values
below. `startup_message()` returns its concise diagnostic. The error column
describes the method result and `last_error()` immediately after that startup
attempt.

| Status | Error result / `last_error()` | Stable message |
| --- | --- | --- |
| `not_started` | `Error.OK` | `Startup has not run.` |
| `initialized` | `Error.OK` | `Startup provider initialized.` |
| `disabled` | `Error.OK` | `Automatic startup is disabled.` |
| `skipped_editor` | `Error.OK` | `Automatic startup is skipped in the editor.` |
| `skipped_editor_play` | `Error.OK` | `Automatic startup is skipped for editor play.` |
| `skipped_debug` | `Error.OK` | `Automatic startup is skipped for debug exports.` |
| `missing_dsn` | `Error.ERR_UNCONFIGURED` | `Startup is disabled because no DSN is configured.` |
| `provider_unavailable` | `Error.ERR_UNAVAILABLE` | `The optional Sentry startup provider is unavailable.` when it cannot be loaded; `Startup provider configuration failed with Error N.` when configuration returns `Error.ERR_UNAVAILABLE`. |
| `configuration_failed` | `Error.ERR_INVALID_PARAMETER` or the provider's error | `Startup configuration is invalid.`, `Startup configuration contains invalid values.`, or `Startup provider configuration failed with Error N.` |

Automatic startup begins synchronously in the `FoundryObservability` autoload
constructor. It therefore completes before the main scene and before any autoload ordered after `FoundryObservability`;
those later hooks may capture immediately. Autoloads ordered earlier and engine work before autoload construction
are outside this ordering guarantee, as are native failures before successful
provider configuration.

Repeated initialization reuses and reconfigures the startup provider instead
of creating duplicate active providers. A failed reconfiguration updates the
startup status, message, and error but preserves the previously working
provider and configuration. Manual `configure()` remains authoritative: game
code may reconfigure or replace the active provider, and automatic startup does
not lock it. Calling `initialize_from_project_settings()` later explicitly
reapplies the project-settings configuration.

Calling `shutdown()` closes admission immediately through the shutdown-request latch.
If provider or configuration work is active, the void call returns before flush, provider shutdown, and the disabled null-state commit; teardown completes after that work drains.
With no active work, the same call completes teardown directly. Repeated calls
are safe. Shutdown does not erase the latest startup status. A later successful
`initialize_from_project_settings()` intentionally reuses the cached startup
provider and starts reporting again.

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
- ObservabilityUser
- ObservabilityScope
- ObservabilityBreadcrumb
- ObservabilityCaptureMask
- ObservabilityFeedback
- ObservabilityMetricType
- ObservabilityMetric
- ObservabilityAttachment
- ObservabilityAttachmentFailure
- ObservabilitySignalLimits
- ObservabilityRedactionRule
- ObservabilityRedactionPolicy
- ObservabilityProcessingDiagnostic
- ObservabilityStartupStatus: stable project-settings startup result constants; see the [startup status table](#skip-decisions-and-results).

Optional provider capabilities:

- ObservabilityMetricsProvider
- ObservabilityBreadcrumbsProvider
- ObservabilityScopeProvider
- ObservabilityAttachmentsProvider

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
Global scope methods also return bool: invalid names, contexts, or users store
Error.ERR_INVALID_PARAMETER; a provider missing any part of the complete scope
capability stores Error.ERR_UNAVAILABLE; and false or non-boolean provider
results store Error.FAILED. Disabled no-ops return false without introducing a
new provider error.

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
		p_max_breadcrumbs: int = 100,
		p_max_attachment_bytes: int = 20 * 1024 * 1024,
		p_attach_game_log: bool = false,
		p_attach_screenshot: bool = false,
		p_attach_scene_tree: bool = false,
		p_event_sample_rate: float = 1.0,
		p_log_sample_rate: float = 1.0,
		p_event_processors: Array[Callable] = [],
		p_log_processors: Array[Callable] = [],
		p_metric_processors: Array[Callable] = [],
		p_event_limits: ObservabilitySignalLimits? = null,
		p_log_limits: ObservabilitySignalLimits? = null,
		p_metric_limits: ObservabilitySignalLimits? = null,
		p_redaction_policy: ObservabilityRedactionPolicy? = null,
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
| log_rate_limit_per_second | int | Compatibility maximum accepted logs per monotonic engine-tick second, evaluated after the log signal limits; zero disables this limit |
| metrics_enabled | bool | Whether custom metrics are accepted; enabled by default |
| metric_sample_rate | float | Deterministic accepted fraction from 0.0 through 1.0 |
| metric_filter | Callable | Optional predicate receiving each normalized, pre-redacted metric before metric processors |
| automatic_capture_enabled | bool | Whether successful enabled configuration installs the automatic engine logger |
| automatic_event_mask | int | Categories routed to exception events |
| automatic_breadcrumb_mask | int | Categories routed to breadcrumbs |
| automatic_log_mask | int | Categories routed to structured logs |
| automatic_events_per_frame | int | Compatibility per-frame event limit used when `p_event_limits` is absent; zero disables it |
| automatic_repeated_error_window_msec | int | Compatibility repeated-identity event window used when `p_event_limits` is absent; zero disables it |
| automatic_event_throttle_count | int | Compatibility event sliding-window count used when `p_event_limits` is absent; zero disables it |
| automatic_event_throttle_window_msec | int | Compatibility event sliding-window duration used when `p_event_limits` is absent; zero disables it |
| stack_trace_source_context_enabled | bool | Retain bounded stack-frame source context; enabled by default |
| stack_trace_variables_enabled | bool | Retain a bounded, type-filtered copy of stack-frame variables; disabled by default and requires explicit opt-in |
| application_hang_detection_enabled | bool | Enable native main-thread hang detection on macOS and iOS; enabled by default |
| application_hang_timeout_msec | int | Apple hang threshold in milliseconds; defaults to 5000 and normalizes to at least 1000 |
| android_anr_detection_enabled | bool | Enable native Android ANR detection; enabled by default |
| android_anr_timeout_msec | int | Pre-Android-11 watchdog threshold in milliseconds; defaults to 5000 and normalizes to at least 1000 |
| android_anr_attach_thread_dump | bool | Request an Android 11+ ANR thread dump when available; disabled by default |
| max_breadcrumbs | int | Maximum retained breadcrumbs; defaults to 100, negative values normalize to zero, and zero disables storage |
| max_attachment_bytes | int | Maximum bytes accepted per attachment; defaults to 20 MiB, negative values normalize to zero, and zero disables attachment delivery |
| attach_game_log | bool | Include the current game log when supported; disabled by default |
| attach_screenshot | bool | Include a capture-time screenshot when supported; disabled by default |
| attach_scene_tree | bool | Include a bounded capture-time scene-tree snapshot when supported; disabled by default |
| event_sample_rate | float | Deterministic retained fraction for ordinary and automatic events; defaults to 1.0 |
| log_sample_rate | float | Deterministic retained fraction for structured logs; defaults to 1.0 |

Accessors:

~~~
func global_attributes() -> Dictionary
func provider_options() -> Dictionary
func automatic_message_filter_prefixes() -> PackedStringArray
func event_processors() -> Array[Callable]
func log_processors() -> Array[Callable]
func metric_processors() -> Array[Callable]
func event_limits() -> ObservabilitySignalLimits
func log_limits() -> ObservabilitySignalLimits
func metric_limits() -> ObservabilitySignalLimits
func redaction_policy() -> ObservabilityRedactionPolicy
~~~

Dictionary accessors return deep copies. global_attributes are shared metadata
for a provider integration. The Sentry provider redacts its candidate copy
before native configuration, using the canonical
`contexts/global_attributes/...` path described below; the caller's config is
not modified. provider_options are opaque to the core and are passed to
provider implementations through the config object.
automatic_message_filter_prefixes returns a copied list of ordinary output
prefixes excluded from automatic capture.

Structured logs are enabled by default independently of messages and
exceptions. The core applies `log_minimum_level` before entering the log
pipeline. `log_rate_limit_per_second` remains a compatibility limit evaluated
after the log signal's per-frame, repeated, and sliding limits. It uses the
pipeline's monotonic clock in a fixed one-second window; zero disables it. A
disabled log configuration or a record below the configured level returns an
empty ID without calling the provider or replacing the latest processing
diagnostic.

Metrics are independently enabled by default. metric_sample_rate must be finite
and between 0.0 and 1.0 inclusive; invalid configuration returns
Error.ERR_INVALID_PARAMETER without replacing the active provider. A valid
metric_filter must return bool. It receives the normalized, pre-redacted
metric before the ordered metric processor array. Returning false produces the
same normal `processor` drop as a processor returning null; returning another
type produces `invalid_processor_result` and stores Error.ERR_INVALID_DATA.

### Provider-neutral signal processing

Events, structured logs, and metrics enter one provider-neutral policy boundary
before provider delivery. The core processing and admission order is:

~~~
Core admission: normalize → pre-redact → processors → post-redact → validate → sample → signal limit
Event/log dispatch after admission: accepted replacement → final timestamp and exception normalization → provider
~~~

The service initially resolves the input event/log timestamp and applies the
active exception-frame policy before pipeline entry. The normalized candidate
is structurally checked before callbacks run. The validation shown after
post-redaction checks the final reconstructed processor replacement. Sampling
and signal limits then admit that replacement and commit capacity.

`ObservabilityProcessingPipeline` ends at admission. The service takes the
accepted event/log replacement and normalizes it once more before provider
dispatch. Final event/log normalization resolves a replacement timestamp and reapplies the active exception frame, source-context, and variable policy.
An explicit replacement timestamp is preserved; an unassigned timestamp is
resolved from the capture's wall-clock/monotonic pair. Final exception
normalization drops null or unusable frames, normalizes nonpositive line
numbers, enforces the source-context opt-in and five-line bounds, and enforces
the variable opt-in and bounded supported-value copy. Metrics do not have this
extra service stage: the validated admitted metric replacement is dispatched
directly. Provider acceptance is recorded only after dispatch. An explicit
processor drop, sampling drop, or limit drop never calls the provider.

Each processor receives an immutable signal DTO and returns either an immutable replacement of the exact same signal type or `null` to drop it.
Processors run in array order, and each accepted replacement becomes the next
processor's input. Event processors receive non-log `ObservabilityEvent`
instances. Log processors receive `ObservabilityEvent` instances whose kind is
`log`; changing between log and non-log kinds is invalid. Metric processors
receive `ObservabilityMetric`. Returning the original immutable object is
valid. A wrong type, invalid DTO, or wrong event kind fails closed as
`invalid_processor_result`. FoundryScript callable failures cannot be caught as
language exceptions, so a failure that yields `null` has the same closed
delivery result as an intentional drop. The pipeline does not print callable
errors or payloads.

The legacy `metric_filter` runs on the pre-redacted normalized metric before
`metric_processors`. Ordinary event processors never receive logs, and all
Foundry-originated automatic and explicit events use the same event pipeline.
Provider-owned state redaction uses the same policy but does not run signal
processors, sampling, or signal limits.

#### ObservabilitySignalLimits

One immutable limit group has this constructor and copied accessors:

~~~
ObservabilitySignalLimits.new(
		p_per_frame: int = 0,
		p_repeated_window_msec: int = 0,
		p_window_count: int = 0,
		p_window_msec: int = 0,
)

func per_frame() -> int
func repeated_window_msec() -> int
func window_count() -> int
func window_msec() -> int
func duplicate() -> ObservabilitySignalLimits
~~~

Negative values normalize to zero. Zero disables the corresponding limit. The
sliding window is enabled only when both `window_count` and `window_msec` are
positive.

Events, structured logs, and metrics have independent sampling accumulators and independent `ObservabilitySignalLimits` groups.
The default event group is 5 accepted events per processed frame, one accepted
matching event per 1,000 milliseconds, and 20 accepted events per 10,000
milliseconds. It applies to manual and automatic events. These defaults come
from the four legacy `automatic_*` fields when `p_event_limits` is absent; an
explicit `p_event_limits` wins. Log and metric limit groups are disabled by
default. `log_rate_limit_per_second` remains an additional disabled-by-default
legacy log-only limit, so no signal can consume another signal's capacity.

Limits are evaluated in this order:

1. `per_frame`: accepts at most the configured number in one processed frame.
2. `repeated`: suppresses a matching identity whose last accepted time is
   younger than `repeated_window_msec`.
3. `window`: accepts at most `window_count` values during `window_msec`.
4. `legacy_log_window`: accepts at most `log_rate_limit_per_second` logs in the
   monotonic one-second bucket.

The limiter commits capacity only when every enabled limit accepts. Event
identity uses kind, source, level, message, and exception identity; log
identity uses source, level, and message; metric identity uses type, name, and
unit. Attributes are excluded. Repeated identity state stores SHA-256 digests,
not payload text, and is bounded to 1,024 entries per signal. Provider
rejection does not roll back already committed limit capacity.

Every sample rate must be finite and within `0.0` through `1.0`. Each otherwise
eligible signal adds its configured rate to a deterministic accumulator. A
value is sampled out while the accumulator is below one; reaching one accepts
the value and subtracts one. Thus `0.25` accepts every fourth otherwise
eligible value, `0.0` accepts none, and `1.0` accepts all. Sampling runs after
processors and redaction but before limits, and each signal has its own
accumulator.

#### Redaction rules and policy

`ObservabilityRedactionPolicy` is an immutable ordered set of defensively copied
rules:

~~~
ObservabilityRedactionPolicy.new(
		p_rules: Array[ObservabilityRedactionRule] = [],
)

func rules() -> Array[ObservabilityRedactionRule]
func duplicate() -> ObservabilityRedactionPolicy
func is_valid() -> bool
~~~

`ObservabilityRedactionRule` supports these factories and accessors:

~~~
static func remove_field(p_path: PackedStringArray) -> ObservabilityRedactionRule
static func replace_value(p_path: PackedStringArray, p_replacement: Variant) -> ObservabilityRedactionRule
static func replace_text(p_path: PackedStringArray, p_pattern: String = "", p_replacement: String = "[REDACTED]") -> ObservabilityRedactionRule
static func sensitive_key(key: String, p_replacement: String = "[REDACTED]") -> ObservabilityRedactionRule

func path() -> PackedStringArray
func action() -> int
func pattern() -> String
func replacement() -> Variant
func duplicate() -> ObservabilityRedactionRule
func is_valid() -> bool
~~~

Actions are `REMOVE_FIELD`, `REPLACE_VALUE`, and `REPLACE_TEXT`. Paths are
nonempty and match case-insensitive exact segments. `*` matches exactly one
segment; `**` matches zero or more. `sensitive_key("password")` is equivalent
to a whole-string replacement at `["**", "password"]`. An empty
`REPLACE_TEXT` pattern replaces the complete matched string; a nonempty pattern
is compiled as `RegEx` and replaces every match. `REPLACE_VALUE` requires the
replacement to have the same runtime type as the matched value.
`REMOVE_FIELD` removes dictionary entries and optional reconstructed fields;
removing a required typed field makes that payload invalid.

Rules run in declaration order across the complete rebuilt tree. Each rule sees
the prior rule's complete result: an earlier child rule does not run again
inside a later parent replacement, while a later child rule does inspect an
earlier parent replacement. Removal ends traversal for that dictionary field
for its rule and leaves it absent to later rules unless a later ancestor
replacement recreates it. Each rule pass independently enforces the
64-container-depth and 10,000-value traversal bounds. Invalid paths, actions,
regular expressions, cyclic replacements, or policies reject configuration.
At runtime, incompatible wildcard matches, cyclic source containers, excessive
depth, excessive traversal, or a result that cannot reconstruct the typed DTO
fail closed as `redaction_failed`.

Redaction runs once before processors so callbacks cannot inspect configured
sensitive fields, and again afterward so a replacement cannot reintroduce
them. Canonical roots and their typed fields are:

| Root | Foundry-owned fields |
| --- | --- |
| `event` | kind, level, message, source, timestamp_msec, attributes, exception, engine_ticks_msec, and local scope |
| `log` | the same event shape, for structured logs |
| `metric` | type, name, value, unit, and attributes |
| `contexts` | provider-owned named context dictionaries; Sentry configuration global attributes are nested at `contexts/global_attributes/...` |
| `user` | application_user_id, display_name, and contact_email |
| `breadcrumbs` | message, level, category, timestamp_msec, attributes, and type |
| `attachments` | outbound filename, content_type, and category |

Event exception fields, stack-frame source context and variables, and
event-local scope tags and contexts are nested beneath `event` or `log`.
Dictionary and array children use their key or numeric index as the next path
segment.

An attachment's path is a private local source used to load bytes; it is not an
outbound metadata field and is not a redaction path. Redaction rebuilds only
the outbound filename, content type, and category while preserving that private
source internally. If user attachment metadata becomes invalid, the state
operation fails closed. If Sentry-created built-in attachment metadata becomes
invalid after the core boundary, that attachment is omitted and reported with
the `redacted` attachment-failure reason; an otherwise accepted event may
continue.

#### Processing diagnostics

The service exposes:

~~~
func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?
~~~

It returns an isolated snapshot of the latest published processing outcome, or
`null` when the active pipeline has not published one. Accessors are:

~~~
func sequence() -> int
func processing_signal() -> StringName
func outcome() -> StringName
func reason() -> StringName
func processor_index() -> int
func rule_index() -> int
func limit_kind() -> StringName
func error() -> int
func duplicate() -> ObservabilityProcessingDiagnostic
~~~

`processing_signal()` is named this way because `signal` is a reserved
FoundryScript keyword. Signals are `event`, `log`, `metric`, and `state`;
outcomes are `accepted` and `dropped`. Successful provider delivery publishes
`accepted`, an empty reason, and `Error.OK`. State is published for redaction
failures or recursive provider-state mutation; successful state changes do not
replace the diagnostic.

Drop reasons map to errors as follows:

| Reason | Diagnostic error | `last_error()` behavior |
| --- | --- | --- |
| `processor` | `Error.OK` | Expected policy drop; remains `Error.OK` |
| `sampled` | `Error.OK` | Expected policy drop; remains `Error.OK` |
| `rate_limited` | `Error.OK` | Expected policy drop; remains `Error.OK` |
| `recursive` | `Error.OK` for event/log/metric; `Error.ERR_BUSY` for state | Signal recursion is an expected drop; recursive state mutation reports busy |
| `invalid_processor_result` | `Error.ERR_INVALID_DATA` | Stores `Error.ERR_INVALID_DATA` |
| `redaction_failed` | `Error.ERR_INVALID_DATA` | Stores `Error.ERR_INVALID_DATA` |
| `invalid_payload` | `Error.ERR_INVALID_DATA` | Stores `Error.ERR_INVALID_DATA` |
| `provider_rejected` | Provider/effective non-OK error, or `Error.FAILED` when none was supplied | Stores the effective provider error |

For `rate_limited`, `limit_kind()` is `per_frame`, `repeated`, `window`, or
`legacy_log_window`. Other drops use an empty limit kind.
`processor_index()` and `rule_index()` identify only the configured array
position; they are `-1` when not applicable. Sequence numbers are local,
monotonic within one configured pipeline, and reset after successful
configuration.

Diagnostics never retain payload objects, messages, attribute keys or values, context names, user fields, attachment paths or filenames, callable error strings, processor identities, or redaction patterns.
Disabled calls and severity/capability gates that never enter a signal pipeline
preserve the prior diagnostic, matching the existing disabled no-op behavior
of `last_error()`.

#### Recursion, lifecycle, and privacy boundaries

The pipeline rejects same-owner recursive entry before another redactor,
processor, sampler, limiter, or provider call. Production ownership is the
current caller thread. Independent owners may process concurrently, while each
signal's mutable admission state is serialized. The provider-call reservation
separately prevents provider diagnostics from feeding the automatic engine
logger back into capture. Automatic destinations remain independent: an event
drop does not suppress its breadcrumb or log, and a later successful
destination does not erase an earlier provider failure from the callback's
final `last_error()`.

Configuration validates a complete candidate pipeline before committing it.
Invalid sample rates return `Error.ERR_INVALID_PARAMETER`; invalid processors,
metric predicates, limits, or redaction policies return
`Error.ERR_INVALID_DATA`. A failed candidate preserves the active core pipeline,
its sampling and limit state, and its latest diagnostic. A successful
configuration atomically installs a new generation and resets processors,
recursion tracking, sampling accumulators, all limit state, and diagnostics.
Calls already processed by an older generation cannot cross into the new
provider generation. Configuration attempted during an active provider call
returns `Error.ERR_BUSY`. Calling `shutdown()` closes admission immediately through the shutdown-request latch.
If provider or configuration work is active, the void call returns before flush, provider shutdown, and the disabled null-state commit; teardown completes after that work drains.
Completion clears processing state and callables and installs a disabled
pipeline. A later intentional successful `configure()` can start a new
generation.

The service applies the policy to Foundry-owned global contexts, explicit user
fields, breadcrumbs, attachment metadata, event-local scope, events, logs, and
metrics. `SentryObservabilityProvider` also commits the shared redactor for
Foundry-owned data it creates after the core boundary: stable and volatile
Foundry runtime contexts and built-in game-log, screenshot, or scene-tree
attachment metadata. Stable runtime-context redaction failure rejects Sentry
configuration; volatile context failure rejects that provider capture.

This policy is not a replacement for native SDK privacy controls. It does not
process SDK-owned native crash, hang, ANR, device, operating-system, network,
request, or integration data that did not originate in FoundryObservability.
Configure those fields with the native provider's privacy options, including
`send_default_pii` where applicable.

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
Exception events and structured logs pass the diagnostic's monotonic engine
tick as `engine_ticks_msec`, allowing the core capture boundary to derive the
Unix occurrence timestamp. Breadcrumbs retain the engine tick in their
provider-neutral `timestamp_msec`; native Sentry breadcrumbs use wall-clock
receipt time.

Automatic exception events use the same event sampler and event limits as
manual events. The default repeated identity includes the final event kind,
source, level, message, and exception type, message, and stack trace. The
default per-frame and sliding settings therefore limit all events, not only
automatic ones. Breadcrumbs remain a separate provider-state destination.
Automatic structured logs use the independent log processor, sampler, and log
limits, so an event drop does not suppress either of those destinations.
Successful reconfiguration resets every accumulated processing state.

Provider calls are guarded so an error emitted by provider configuration,
capture, flush, or shutdown cannot recursively enter automatic capture.
Providers should still avoid deliberately reporting their own failures through
the same observability pipeline.

`max_breadcrumbs` is a provider-neutral capacity. The memory provider and
native Apple/Android Sentry integrations retain at most that many breadcrumbs,
evicting the oldest first. Zero disables storage; the memory provider rejects
capture at zero, while native Sentry retains no breadcrumb. Negative constructor
input normalizes to zero. Memory configure/reconfigure starts a fresh trail,
including when the new bound is smaller. `clear_breadcrumbs()` explicitly
clears the live trail without changing the configured bound.

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
when it is explicitly enabled. Without an explicit redaction policy this is
type filtering and bounding, not content redaction: supported strings are
forwarded verbatim, and callers must review both source context and variables
before capture. A policy may target source or variable values under the
`event.exception.frames` and `log.exception.frames` paths. When variables are
enabled, the core and native bridges retain only a bounded, type-filtered copy
of supported values:
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
func duplicate() -> ObservabilityException
~~~

Every exception field is final after construction. attributes are deep-copied
on construction and access. The constructor copies the containing frame array,
`frames()` returns another array copy, and `duplicate()` returns a distinct
exception with its own attribute and frame containers. The immutable
ObservabilityStackFrame values may be shared between those isolated arrays.
`stack_trace()` remains the supported formatted-string fallback: structured
frames are additive and never synthesize or overwrite it. The core does not
interpret the type or formatted
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
		p_scope: ObservabilityScope? = null,
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
| scope | Optional event-local tags and contexts |

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
func scope() -> ObservabilityScope?
~~~

attributes are deep-copied on construction and access. exception is optional;
the event snapshots it on construction, and `exception()` returns a fresh
duplicate, so later mutation of either the caller's payload or an accessor
result cannot alter a queued, processed, or captured event. The optional scope
parameter was appended to preserve every legacy positional constructor call.
It is duplicated on construction, and `scope()` returns another duplicate, so
later caller mutation cannot alter a queued or captured event. Every event
field is final after construction; timestamp resolution creates a new
normalized event rather than mutating the caller's event.

## ObservabilityUser

ObservabilityUser is the explicit provider-neutral application identity DTO.
It never discovers a device identity, IP address, account, or other PII.

Constructor:

~~~
ObservabilityUser.new(
		p_application_user_id: String = "",
		p_display_name: String = "",
		p_contact_email: String = "",
)
~~~

Accessors and validation:

~~~
func application_user_id() -> String
func display_name() -> String
func contact_email() -> String
func is_valid() -> bool
~~~

The three private backing fields are `final`; consumers get read-only scalar
accessors and cannot mutate identity after construction. `is_valid()` requires
at least one nonempty field. Each optional field may be empty, but a nonempty
field must have no leading or trailing whitespace and no control characters.
The core treats `contact_email` as an explicit opaque contact string; unlike
`ObservabilityFeedback`, this DTO does not parse or infer email syntax.

Only these caller-supplied fields are represented:

| Foundry field | Sentry user field | Privacy behavior |
| --- | --- | --- |
| `application_user_id` | `id` | Sent only when explicitly supplied |
| `display_name` | `username` | Sent only when explicitly supplied |
| `contact_email` | `email` | Sent only when explicitly supplied |

Empty optional fields remain empty in the provider-neutral payload and are not
invented. Neither bridge sets Sentry `ip_address`, and this identity path does
not infer default PII or enable `send_default_pii`; that separate native option
remains opt-in. `FoundryObservability.remove_user()` removes the live explicit
user.

## ObservabilityScope

ObservabilityScope carries event-local tags and named structured contexts. It
starts empty; its final backing dictionary references are private and cannot be
reassigned.

Constructor and bounds:

~~~
ObservabilityScope.new()
const MAX_CONTAINER_DEPTH: int = 8
const MAX_CONTAINER_ITEMS: int = 256
~~~

Methods:

~~~
func tags() -> Dictionary
func contexts() -> Dictionary
func set_tag(key: String, value: String) -> bool
func remove_tag(key: String) -> bool
func clear_tags() -> void
func set_context(name: String, value: Dictionary) -> bool
func remove_context(name: String) -> bool
func clear_contexts() -> void
func is_empty() -> bool
func duplicate() -> ObservabilityScope
~~~

`tags()` and `contexts()` return deep defensive copies. `duplicate()` creates
an independent scope and deep-copies every context; tags are scalar strings.
`is_empty()` is true only when both collections are empty. `remove_tag()` and
`remove_context()` return false for an invalid or absent name. Clear operations
are idempotent.

Top-level tag keys and context names must be nonempty String values without
leading or trailing whitespace or control characters. Nested dictionary keys
may be String or StringName; both are normalized to String. A context value is
a Dictionary whose recursive values may be:

- null
- bool
- int
- finite float
- String or StringName (StringName normalizes to String)
- Dictionary with String or StringName keys
- Array containing any supported value

Unsupported objects, nonfinite floats, non-string dictionary keys, and
container cycles reject the whole context. A context may contain at most eight
nested container levels and 256 examined dictionary entries/array elements in
total. Reusing the same child container in separate non-cyclic branches is
valid. Validation and normalization finish before assignment, so a failed
`set_context()` is atomic and preserves the previous value. Successful
assignment owns a deep normalized copy; later mutations of the input or of an
accessor result do not leak back.

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
		p_type: StringName = &"default",
)
~~~

Accessors:

~~~
func message() -> String
func level() -> int
func category() -> StringName
func timestamp_msec() -> int
func attributes() -> Dictionary
func type() -> StringName
~~~

attributes are deep-copied on construction and access. Breadcrumb delivery is
capability-based so event-only providers remain compatible. `type()` is the
provider-neutral breadcrumb type, appended to the constructor for legacy
positional compatibility; it defaults to `default` and maps to Sentry's native
breadcrumb type.

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
abstract func clear_breadcrumbs() -> bool
~~~

A provider implements this trait when it accepts normalized breadcrumbs.
Providers that implement only ObservabilityProvider remain compatible: their
event, log, feedback, and metric behavior is unchanged, while breadcrumb
capture returns false and stores Error.ERR_UNAVAILABLE.

## ObservabilityScopeProvider

ObservabilityScopeProvider is an all-or-nothing optional provider capability
for live global session scope:

~~~
trait_name ObservabilityScopeProvider

abstract func set_tag(key: String, value: String) -> bool
abstract func remove_tag(key: String) -> bool
abstract func clear_tags() -> bool
abstract func set_context(name: String, value: Dictionary) -> bool
abstract func remove_context(name: String) -> bool
abstract func clear_contexts() -> bool
abstract func set_user(user: ObservabilityUser) -> bool
abstract func remove_user() -> bool
~~~

A provider must implement every method before the service exposes any scope
mutation. A scopeless or partial provider remains fully compatible with
ordinary unscoped events, logs, feedback, metrics, and breadcrumbs. Global
scope calls return false and store `Error.ERR_UNAVAILABLE`; a nonempty
event-local scope likewise rejects that event before provider capture. An empty
local scope is equivalent to no local scope.

## FoundryObservabilityApi

FoundryObservabilityApi is the provider-neutral service trait. It allows an
integration or game subsystem to depend on the service contract without
depending on the concrete autoload class:

~~~
trait_name FoundryObservabilityApi

abstract func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int
abstract func initialize_from_project_settings() -> int
abstract func startup_status() -> StringName
abstract func startup_message() -> String
abstract func is_enabled() -> bool
abstract func is_available() -> bool
abstract func provider_name() -> StringName
abstract func last_error() -> int
abstract func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?
abstract func add_attachment(attachment: ObservabilityAttachment) -> String
abstract func remove_attachment(handle: String) -> bool
abstract func clear_attachments() -> bool
abstract func last_attachment_failures() -> Array
abstract func capture_event(event: ObservabilityEvent) -> String
abstract func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}, scope: ObservabilityScope? = null) -> String
abstract func capture_log(message: String, level: int = ObservabilityLevel.INFO, source: StringName = &"game", timestamp_msec: int = -1, attributes: Dictionary = {}, engine_ticks_msec: int = -1, scope: ObservabilityScope? = null) -> String
abstract func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}, scope: ObservabilityScope? = null) -> String
abstract func set_tag(key: String, value: String) -> bool
abstract func remove_tag(key: String) -> bool
abstract func clear_tags() -> bool
abstract func set_context(name: String, value: Dictionary) -> bool
abstract func remove_context(name: String) -> bool
abstract func clear_contexts() -> bool
abstract func set_user(user: ObservabilityUser) -> bool
abstract func remove_user() -> bool
abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
abstract func clear_breadcrumbs() -> bool
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
4. A failed memory-provider configuration leaves the existing provider,
   config, global scope, explicit user, and breadcrumb trail active and stores
   the returned error. Sentry failure recovery is narrower and may fail closed,
   as described below.
5. Successfully configuring the already-active provider updates its config
   without shutting it down and starts a fresh provider-owned global scope,
   user, and breadcrumb trail.
6. Successfully configuring a different provider shuts down the old provider
   once, activates the candidate with a fresh provider-owned global scope,
   user, and breadcrumb trail, and clears last_error().
7. Successful enabled configuration installs or updates automatic capture when
   automatic_capture_enabled is true. Failed configuration does not disturb
   the current logger.

The method returns the provider configure result.

Every successful enabled configure or reconfigure begins with an empty
provider-owned global scope, explicit user, and breadcrumb trail. The memory
provider resets all three directly. Sentry resets its Foundry-owned scope and
user, then clears breadcrumbs through the native adapter before committing the
session. This also applies to a deeply equivalent configuration that keeps the
native SDK running.

A failed memory-provider configure preserves its prior live state. For Sentry,
a malformed or non-integer native configure result always fails closed. An
exact non-OK integer result preserves the prior session only when no live
breadcrumb-capable session existed or the candidate is deeply equivalent, and
retained-scope restoration succeeds; the original integer error is returned.
After native configure succeeds, an empty-scope reset failure uses the same
preservation-safety rule and also requires successful rollback of the committed
configuration and scope. Breadcrumb-clear recovery is narrower: the result
must be the exact boolean `false`, the prior committed session must be enabled,
configuration/scope rollback must succeed, and the preservation-safety rule
must hold: either no live fully breadcrumb-capable prior session existed or the
candidate is deeply equivalent. A malformed clear result, an unsafe materially
changed breadcrumb-capable session, or any unsuccessful or unprovable
restoration fails closed. A later valid configure can establish a fresh
session.

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
func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?
~~~

Before configuration, the service is disabled, unavailable, reports provider
name null, and has last_error() equal to Error.OK.

is_enabled reflects config.enabled. is_available delegates to the active
provider. provider_name delegates to the active provider and returns null when
no provider is active. last_error returns the latest stored configuration,
capture, or flush error. A successful provider configuration clears the error.
`last_processing_diagnostic()` returns the isolated processing snapshot
described above and does not change `last_error()`.

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
		scope: ObservabilityScope? = null,
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
- an isolated copy of the optional event-local scope

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
		scope: ObservabilityScope? = null,
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

### Global and event-local scope

~~~
func set_tag(key: String, value: String) -> bool
func remove_tag(key: String) -> bool
func clear_tags() -> bool
func set_context(name: String, value: Dictionary) -> bool
func remove_context(name: String) -> bool
func clear_contexts() -> bool
func set_user(user: ObservabilityUser) -> bool
func remove_user() -> bool
~~~

These methods mutate the active provider's global session scope. Input is
validated with the same rules as ObservabilityScope and ObservabilityUser before
calling the provider. Invalid input returns false and stores
`Error.ERR_INVALID_PARAMETER`. A disabled service returns false without a
provider call. A provider must expose the complete ObservabilityScopeProvider
contract: a scopeless or partial provider returns false and stores
`Error.ERR_UNAVAILABLE`. A false or non-boolean provider result stores
`Error.FAILED`; true stores `Error.OK`. Removal of an absent tag or context is a
provider rejection, while clear operations are idempotent when the provider
accepts them.

`capture_message()`, `capture_exception()`, and `capture_log()` append an
optional ObservabilityScope parameter, and ObservabilityEvent accepts the same
appended constructor parameter. Existing calls remain source-compatible.
Effective event scope is resolved as follows:

- Global tags are the base; same-named local tags override them.
- Global contexts are the base; a same-named local context wholly replaces the
  global context, including an explicitly empty local dictionary. Contexts are
  not field-merged.
- The global ObservabilityUser remains attached because local scope carries no
  user.
- Other global entries remain present.

The event snapshots local scope at construction, and providers construct a
fresh effective scope for each capture. Mutating the original local scope later
cannot change that event, and local overrides never mutate or leak into global
scope or later events.

Ordinary events without a local scope continue to work with providers that do
not implement scope. A nonempty local scope requires the complete scope
capability and otherwise returns an empty ID with `Error.ERR_UNAVAILABLE`.

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

`clear_breadcrumbs()` uses the optional provider operation:

~~~
func clear_breadcrumbs() -> bool
~~~

It returns false without a call while disabled. A missing operation returns
false with `Error.ERR_UNAVAILABLE`; a false or non-boolean result returns false
with `Error.FAILED`; true clears the live trail and stores `Error.OK`.

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

`capture_metric()` follows the shared processing section: pre-redaction, the legacy filter, ordered metric processors, post-redaction, validation, deterministic metric sampling, metric signal limits, and provider dispatch.
Before that shared path, the service validates and normalizes the input metric
and merges valid global and per-metric attributes. The convenience methods
construct the corresponding `ObservabilityMetric`. A true result means the
active provider accepted the processed replacement into its local SDK or
store; it does not guarantee remote delivery.

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

Invalid input returns false with `Error.ERR_INVALID_PARAMETER` before entering
the processing pipeline. A valid metric submitted while the service or metrics
are disabled returns false without changing the prior processing diagnostic or
error. A missing metric provider capability returns false with
`Error.ERR_UNAVAILABLE`.

Once processing begins, a false legacy filter result or processor `null`
publishes `processor`; deterministic sampling publishes `sampled`; and a metric
limit publishes `rate_limited`. A same-owner recursive metric capture publishes
`recursive`. Those expected false results leave `last_error()` at `Error.OK`.
Invalid processor results, redaction failures, or invalid final payloads return
false and store `Error.ERR_INVALID_DATA`. A false or malformed provider result
publishes `provider_rejected` and stores `Error.FAILED`. See
[Provider-neutral signal processing](#provider-neutral-signal-processing) for
the complete diagnostic fields and reason table. Providers own batching,
transport, offline retry, and delivery. `flush()` forwards the timeout to the
active provider so its SDK can attempt delivery.

### capture_exception

~~~
func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String
~~~

A null exception returns an empty ID and stores Error.FAILED. Otherwise it
creates an event with kind exception, level ERROR, source game, the current Unix
timestamp and monotonic engine tick captured together, exception.message() as
the message, the supplied attributes, the exception payload, and an isolated
copy of the optional event-local scope.

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

Calling `shutdown()` closes admission immediately through the shutdown-request latch.
If provider or configuration work is active, the void call returns before flush, provider shutdown, and the disabled null-state commit; teardown completes after that work drains.
When no provider or configuration work is active, the call performs the
bounded flush and provider shutdown before it returns, then installs
`NullObservabilityProvider`, a disabled config, a fresh disabled processing
pipeline, and `Error.OK`. Provider shutdown clears live global scope, explicit
user, breadcrumbs, and attachment handles. Repeated calls are safe after
teardown. The autoload also calls `shutdown()` from `_exit_tree`. A later
intentional successful `configure()` or project-settings initialization can
start a fresh session.

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
func captured_scopes() -> Array[Dictionary]
func captured_attachments() -> Array[Array]
func breadcrumbs() -> Array[ObservabilityBreadcrumb]
func feedback() -> Array[ObservabilityFeedback]
func metrics() -> Array[ObservabilityMetric]
func clear() -> void
func clear_breadcrumbs() -> bool
func clear_feedback() -> void
func clear_metrics() -> void
~~~

events returns a copy of the captured event list.
`captured_scopes()` returns deep defensive snapshots of the effective tags,
contexts, and user for each event. `captured_attachments()` returns deep
defensive snapshots aligned one-to-one with captured events. `clear()` clears
captured events, scopes, and attachment history but retains the live session
attachment set. It does not change provider configuration.

Successful event capture returns sequential IDs in the form memory:N.
Breadcrumbs are stored in their own bounded FIFO list and return true when
accepted. Feedback is stored separately and returns IDs in the form
memory-feedback:N. Metrics are stored in another list and return true when
accepted. Capture returns an empty ID or false while disabled or after shutdown.

Successful configure/reconfigure, provider replacement, and shutdown clear the
memory provider's live global scope, explicit user, and breadcrumb trail.
Failed configure preserves them. Captured event, effective-scope, and attachment
history survives those lifecycle changes and remains available until `clear()`;
feedback and metric histories use their own explicit clear methods.

Example:

~~~
import foundry.observability

var provider := MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, ObservabilityConfig.new(p_enabled = true))
FoundryObservability.capture_message("test event")
var captured: Array[ObservabilityEvent] = provider.events()
~~~

## Diagnostic attachments

Diagnostic attachments are provider-neutral files or immutable byte snapshots
that accompany captured events. The core and memory provider expose the
lifecycle contract on every platform; the included Sentry provider delivers
attachments natively on Apple and Android.

### ObservabilityAttachment

Create an attachment with one of the validated factories:

~~~
static func from_path(path: String, filename: String = "", content_type: String = "", category: StringName = DEFAULT_CATEGORY) -> ObservabilityAttachment?
static func from_bytes(bytes: PackedByteArray, filename: String, content_type: String = "", category: StringName = DEFAULT_CATEGORY) -> ObservabilityAttachment?
~~~

Both factories return null for invalid input. A path must be absolute or begin
with `user://` or `res://`; it must be nonempty and have a usable filename,
either supplied explicitly or derived from the final path component. A byte
attachment requires a nonempty, explicit filename. Paths and metadata reject
leading or trailing whitespace and control characters. A content type may be
omitted, in which case `application/octet-stream` is used.

Only `event.attachment` (the default) and `event.view_hierarchy` are supported
categories. A value contains exactly one source: path-backed attachments have
no byte payload, while byte-backed attachments have no path. Validation checks
the descriptor, not whether a path is currently readable.

Accessors:

~~~
func path() -> String
func bytes() -> PackedByteArray
func filename() -> String
func effective_filename() -> String
func content_type() -> String
func category() -> StringName
func is_path() -> bool
func is_bytes() -> bool
func duplicate() -> ObservabilityAttachment
func is_valid() -> bool
~~~

Byte attachments are copied on construction and access. `duplicate()` also
returns an isolated value, so later caller mutation cannot change pending
delivery.

### ObservabilityAttachmentFailure

An attachment failure describes one attachment that could not accompany an
otherwise accepted event:

~~~
func handle() -> String
func filename() -> String
func reason() -> StringName
func error() -> int
func duplicate() -> ObservabilityAttachmentFailure
~~~

The stable reasons are:

| Reason | Meaning |
| --- | --- |
| `missing_file` | The path no longer exists at capture time |
| `unreadable_file` | The path exists but cannot be read |
| `oversized` | The attachment exceeds the per-attachment limit, or delivery is disabled by a zero limit |
| `platform_unavailable` | The active platform cannot collect or deliver the requested attachment |
| `provider_rejected` | The provider or native SDK rejected attachment preparation |
| `redacted` | Configured redaction made provider-created attachment metadata unsafe or invalid, so that attachment was omitted |

Returned failure DTOs are defensive copies.

### Service and provider contracts

The public service API is:

~~~
func add_attachment(attachment: ObservabilityAttachment) -> String
func remove_attachment(handle: String) -> bool
func clear_attachments() -> bool
func last_attachment_failures() -> Array
~~~

`add_attachment()` returns a provider-local, opaque handle. Invalid input
returns an empty string and stores `Error.ERR_INVALID_PARAMETER`; a provider
without the complete optional capability stores `Error.ERR_UNAVAILABLE`; and
an empty or malformed provider result stores `Error.FAILED`. Success stores
`Error.OK`.

`remove_attachment()` validates the handle and maps the provider's integer
result directly to `last_error()`. In particular, an unknown handle returns
false with `Error.ERR_DOES_NOT_EXIST`. `clear_attachments()` stores
`Error.ERR_UNAVAILABLE` when the capability is absent, `Error.FAILED` when the
provider rejects or returns a malformed result, and `Error.OK` on success.
While the service is disabled, these mutating calls are no-ops and leave the
previous error unchanged.

Providers opt in by implementing the complete trait:

~~~
trait_name ObservabilityAttachmentsProvider

abstract func add_attachment(attachment: ObservabilityAttachment) -> String
abstract func remove_attachment(handle: String) -> int
abstract func clear_attachments() -> bool
abstract func last_attachment_failures() -> Array
~~~

The integer provider removal result lets the service preserve
`Error.ERR_DOES_NOT_EXIST` rather than collapsing every rejection into
`Error.FAILED`.

Attachments persist for the configured provider session until removed or cleared.
Successful reconfiguration, provider replacement, and shutdown invalidate every prior handle.
Failed reconfiguration preserves attachments and their handles. A successful
reconfiguration starts a fresh attachment session even when the same provider
object remains active.

### Limits, materialization, and failures

| Configuration key | Default |
| --- | --- |
| max_attachment_bytes | `20 * 1024 * 1024` (20 MiB) |
| attach_game_log | `false` |
| attach_screenshot | `false` |
| attach_scene_tree | `false` |

Negative values normalize to zero. Zero disables all attachment delivery, while attachment management may continue.
The limit applies independently to each attachment.

Absolute paths and `user://` paths stay lazy until capture. For the native
Sentry global scope, `user://` is converted to its absolute globalized path.
This lets native crashes and Android ANRs include a file's latest contents,
subject to the native SDK's timing. Packaged `res://` paths are materialized only for the current Foundry-originated event.
They are not installed as native global-scope paths because packaged resources
may not exist as ordinary files.

Foundry preflight runs independently for each event. It validates path
availability and the per-attachment size without deleting persistent
attachments. Partial attachment failures never reject an accepted event.
`last_error()` remains `Error.OK` after an accepted event with attachment failures.
Only the latest event envelope replaces `last_attachment_failures()`.
Non-event APIs and automatic structured logs do not replace the latest attachment failures.
Calling the failure accessor itself also leaves `last_error()` unchanged.

### Built-in attachments and native delivery

`attach_game_log`, `attach_screenshot`, and `attach_scene_tree` are independent,
false-by-default opt-ins. The game log remains a lazy path attachment.
Screenshot capture runs on the main thread, is unavailable headlessly, and may
reuse the current frame.
Scene-tree output is bounded and may contain game-authored names or text.
Game logs and screenshots may contain sensitive data. Review every built-in
payload for privacy-sensitive information before enabling it. Screenshot and
scene-tree collection can affect frame time.
Scene-tree collection requires the main thread and an initialized scene tree.

Apple and Android are the supported native attachment targets.
User-supplied byte attachments, absolute and globalized `user://` paths, and the
persistent game log are mirrored to the Apple and Android Sentry global scope.
They may accompany native crashes, Apple app hangs, and Android ANRs.
Only materialized `res://` bytes and capture-time screenshot or scene-tree
snapshots remain capture-local. Capture-time screenshots and scene-tree
snapshots do not accompany recovered native crashes.
The Sentry provider owns its SDK attachment collection while configured;
applications should not expect direct native SDK attachments to coexist with
that collection.

The native SDK owns diagnostic timing and the final attachment race. A lazy
file can change, disappear, become unreadable, or exceed the native limit after
Foundry preflight. Native crash and ANR delivery is consequently best effort;
the latest `last_attachment_failures()` reports Foundry event-envelope
preflight, not failures that occur later inside native diagnostic handling.

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
Prefer the project-settings startup path, which configures the provider during
`FoundryObservability` autoload construction. Manual
`FoundryObservability.configure(SentryObservabilityProvider.new(), config)`
remains available for targeted setup at the earliest supported startup
boundary. Successful provider configuration is the first point at which the
addon can install native crash handlers. A fatal crash in the
pre-configuration gap is outside the addon's capture boundary and cannot be
recovered later.

The native startup configuration includes `release`, `environment`, `dist`,
and scalar global attributes. Before native configuration, the Sentry provider
applies the committed redaction policy to a candidate context shaped as
`contexts/global_attributes/...`; an invalid result rejects configuration
without committing it. Only the redacted snapshot is installed under the
`foundry.global_attributes` context before capture begins and reused by native
events, structured logs, breadcrumbs, and crash handling. Apple enables the
Sentry crash handler. Android enables its uncaught-exception handler, NDK
integration, and native scope synchronization. Sentry persists fatal crash data
in-process and sends the previous launch report after the next launch starts
with the same deployment identity.

Sentry is process-global, while providers are ordinary FoundryScript objects.
The bridge therefore uses an owner-safe lifecycle:

- Equivalent configuration transfers ownership without restarting the SDK,
  but a successful configure still clears Foundry-owned scope, user, and
  breadcrumbs for the new session.
- Changed configuration performs a bounded 2-second shutdown before starting
  the replacement.
- A malformed or non-integer native configure result fails closed. An exact
  non-OK integer configure error preserves the prior session when no live
  breadcrumb-capable session existed or the candidate is deeply equivalent,
  provided retained-scope restoration succeeds; the original error remains.
- Flush and shutdown calls from a stale owner do nothing, so an obsolete
  provider cannot stop a newer session. These idempotent no-ops return
  `Error.OK`; availability remains the observable ownership signal.

The FoundryScript Sentry provider commits candidate configuration only after
native startup, the required empty-scope reset, and adapter breadcrumb clearing
all succeed. Scope-reset failure uses the same preservation-safety rule as an
integer configure error and additionally requires successful rollback of the
committed configuration and scope. Breadcrumb-clear rollback requires an exact
boolean `false`, a prior enabled committed session, successful
configuration/scope rollback, and the preservation-safety rule: either no live
fully breadcrumb-capable prior session existed or the candidate is deeply
equivalent. A malformed clear, an unsafe materially changed breadcrumb-capable
session, or any unsuccessful or unprovable restoration shuts down the owner
and fails closed: availability, capture, scope mutation, and breadcrumb
operations remain disabled until a later successful configure.

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
macOS, iOS, and Android Sentry data with provider-private runtime
contexts. This does not add fields or methods to the provider-neutral API and
does not modify caller-supplied event attributes or global-attribute
containers; the provider transmits only its redacted global-attribute snapshot.

The addon uses six custom context names:

| Context | Field families |
| --- | --- |
| `foundry_app` | Project name and version, process start time, and architecture |
| `foundry_engine` | Engine version and commit, architecture, runtime mode, and editor/debug/headless flags |
| `foundry_device` | Model and device type, architecture, processor, memory, storage, and opt-in identifying values |
| `display` | Display server, screen/touch availability, primary dimensions, DPI, refresh rate, and orientation |
| `gpu` | Adapter and vendor, API and device type, driver, and rendering method |
| `foundry_runtime` | Deployment environment, classified runtime mode, sandbox state, and user-storage persistence |

Configuration collects a stable snapshot and installs it on the native Sentry
scope before capture begins. This makes the snapshot available to native crash
handling. Ordinary event capture copies that snapshot and refreshes current
free memory, usable memory, free user-storage space, and primary orientation
for that event only. A recoverable deeply equivalent failed reconfiguration
retains the last successful snapshot. A materially changed or unprovable
failure may fail closed until a later valid configure collects a fresh
snapshot. Successful disable and shutdown clear it.

Runtime mode uses deterministic precedence: headless or dedicated-server
execution is `headless`, otherwise editor execution is `editor`, otherwise a
debug build is `debug_export`, and the remaining case is `release_export`.
Empty strings, `GenericDevice`, nonpositive dimensions and counts, negative
capacities, missing platform values, unsupported objects, nonfinite numbers,
invalid keys, cycles, and empty contexts are omitted rather than guessed.

The runtime memory-information API is not called on iOS. Consequently
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
query volatile runtime values while recovering a previous-launch crash. Such a
report therefore has no capture-time refresh beyond the stable snapshot that
was installed before the crash.

## Sentry scope and identity delivery

The optional Sentry provider implements ObservabilityScopeProvider on Apple and
Android. Global tags map to native Sentry tags, structured context dictionaries
map to named native contexts without flattening nested dictionaries or arrays,
and ObservabilityUser maps only the three explicit fields in the privacy table
above. `remove_user()` clears the native Sentry user.

Every global mutation sends the complete candidate scope to the native bridge.
Apple and Android remove keys that existed in the previous Foundry-owned scope,
then install the candidate atomically with owner validation. A rejected bridge
operation leaves the prior FoundryScript and native state in place.

For ordinary message and exception events, both bridges apply the
ObservabilityScope payload only to the native capture scope. Same-named local
tags override global tags; a same-named local context replaces the entire
global context, including an empty dictionary; the global user remains. That
one-shot scope does not mutate the process-global Sentry scope and cannot leak
to later captures.

Scope remains an optional provider capability. A Sentry native bridge without
`applyScope` continues accepting unscoped events. Global scope changes and
nonempty event-local scope are rejected rather than silently dropped; at the
service boundary the former is an active-provider rejection
(`Error.FAILED`), while a genuinely scopeless or partial provider is reported
as `Error.ERR_UNAVAILABLE`.

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

Native Sentry Logs cannot faithfully apply the event-local scope used by
ordinary events. Apple and Android therefore reject a structured log with
semantically nonempty event-local scope before invoking the native logger. The
capture returns an empty provider ID and records an active-provider failure.
Missing or semantically empty event-local scope remains accepted. Global
Sentry scope remains independent of this event-local limitation.

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

A provider implements every method in ObservabilityProvider and may implement
optional capability traits. ObservabilityScopeProvider is optional and
all-or-nothing: implement its complete trait or omit it. The `MyProvider`
example intentionally omits that trait and is therefore scopeless. It does
implement metrics and breadcrumbs; the breadcrumb capability includes both
capture and clear operations:

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

func clear_breadcrumbs() -> bool:
	# Clear the current breadcrumb trail in the provider SDK.
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
or performance transactions. Providers own native delivery,
offline storage, retry policy, and flush behavior. The optional Sentry sibling
addon contains its native bindings, breadcrumb delivery, structured-log
delivery, feedback delivery, custom-metric delivery, and attachment delivery.
A foundry-cpp project is not required by this API.
