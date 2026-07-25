# Automatic Godot Error and Log Capture Design

## Goal

Add provider-neutral automatic capture of Foundry/Godot runtime errors and
messages. Once `FoundryObservability` is successfully configured, projects
receive useful error events and breadcrumbs without calling the observability
API at every source location. The integration remains absent and
side-effect-free before configuration or while observability is disabled.

The implementation targets macOS, iOS, and Android and follows the useful
behavior of `getsentry/sentry-godot` while keeping engine capture in the core
addon rather than coupling it to Sentry.

## Architecture

The core addon gains an `AutomaticObservabilityLogger` implemented in
FoundryScript and derived from Foundry's script-exposed `Logger` class.
`FoundryObservability` registers this logger with `OS.add_logger()` only after
a provider has been configured successfully with both observability and
automatic capture enabled. It removes the logger before disabling the service,
replacing the active provider, or shutting down.

Foundry's logger callback supplies the originating function, file, line, code,
rationale, error type, and available `ScriptBacktrace` values. Because the
hook is exposed by Foundry on every target platform, the core implementation
does not need separate Apple and Android capture logic. Native provider
bridges remain responsible only for delivering normalized provider-neutral
records.

Automatic capture is enabled by default after successful configuration. The
initial null-provider state, a configuration with `enabled = false`, or a
configuration with automatic capture disabled does not register a logger.

## Public types and configuration

### Capture masks

A provider-neutral `ObservabilityCaptureMask` class defines bit flags for the
categories exposed by Foundry's logger:

- `ERROR` covers engine, native-extension, `push_error()`, and fatal errors;
- `WARNING` covers engine and `push_warning()` warnings;
- `SCRIPT` covers FoundryScript and other script-language errors;
- `SHADER` covers shader errors;
- `MESSAGE` covers ordinary print and configured log output.

The bit values and safe defaults mirror the established `sentry-godot`
behavior:

- event mask: `ERROR | SCRIPT | SHADER`;
- breadcrumb mask:
  `ERROR | WARNING | SCRIPT | SHADER | MESSAGE`;
- structured-log mask: empty by default.

The automatic logger is therefore useful immediately after configuration
without turning every warning or print statement into a backend event.
Applications opt into automatic structured logs by setting their log mask;
the existing `logs_enabled` and `log_minimum_level` settings still gate all
structured logs.

### Configuration fields

`ObservabilityConfig` gains:

```text
automatic_capture_enabled: bool = true
automatic_event_mask: int = ERROR | SCRIPT | SHADER
automatic_breadcrumb_mask: int = ERROR | WARNING | SCRIPT | SHADER | MESSAGE
automatic_log_mask: int = 0
automatic_events_per_frame: int = 5
automatic_repeated_error_window_msec: int = 1000
automatic_event_throttle_count: int = 20
automatic_event_throttle_window_msec: int = 10000
automatic_message_filter_prefixes: PackedStringArray = ["FoundryObservability: "]
```

Negative limit values are normalized to zero. A zero repeated-error window
disables duplicate suppression. A zero per-frame event limit disables the
per-frame limit. A zero throttle count or window disables sliding-window
event throttling. Prefixes are copied defensively.

Events, breadcrumbs, and structured logs can be enabled or disabled
independently by setting the corresponding mask to zero or to any combination
of categories. Setting `automatic_capture_enabled = false` disables the whole
engine logger without changing explicit API capture.

### Breadcrumb value and provider capability

The core gains an `ObservabilityBreadcrumb` value with:

```text
message: String
level: int
category: StringName
timestamp_msec: int
attributes: Dictionary
```

Constructor data is copied defensively, matching `ObservabilityEvent`.
`FoundryObservabilityApi` gains a typed `capture_breadcrumb` method returning
whether a provider accepted the breadcrumb.

Breadcrumb delivery is an optional provider capability expressed by a new
`ObservabilityBreadcrumbsProvider` trait. This follows the existing metrics
capability pattern and avoids changing the required `ObservabilityProvider`
contract for third-party providers. The memory and Sentry providers implement
the capability. When an active provider does not implement it,
`capture_breadcrumb` returns `false`, stores `Error.ERR_UNAVAILABLE`, and
leaves event, log, feedback, and metric capture operational.

## Error normalization

For an error callback, `AutomaticObservabilityLogger` derives the human-readable
message from `rationale` when present and otherwise from `code`. It maps the
Foundry logger error type to an `ObservabilityCaptureMask` category, normalized
severity, and stable type name.

The normalized source attributes are:

```text
error.function
error.file
error.line
error.code
error.rationale
error.type
error.editor_notify
error.script_backtraces
observability.origin = "auto.log.foundry"
```

Empty optional strings remain represented where doing so preserves the engine
callback faithfully. `error.script_backtraces` is an array of dictionaries.
Each dictionary includes the language name and an ordered `frames` array. Each
available frame contains its function, file, and line. The same information is
also rendered deterministically into `ObservabilityException.stack_trace` so
providers that only understand the existing string field still retain the
backtrace.

When the event mask accepts the category, the logger creates an exception event
whose source is `foundry.engine`. When the breadcrumb mask accepts it, the
logger creates an `error` breadcrumb with the same source metadata. When the
structured-log mask accepts it, the logger calls `capture_log` with the same
severity and attributes.

Provider event IDs are not injected into the parallel breadcrumb or structured
log. This keeps the three destinations independent and avoids making automatic
logs depend on event delivery success.

## Message normalization

`Logger._log_message(message, error)` receives ordinary engine output. The
logger strips control characters and ANSI escape sequences before processing
it. Empty output and messages beginning with a configured filter prefix are
ignored.

Messages never create exception events. When `MESSAGE` is present in the
breadcrumb mask, the logger creates a `log` breadcrumb. When it is present in
the structured-log mask, the logger creates a structured log at `ERROR` for
error-stream output and `INFO` otherwise. Both records carry:

```text
observability.origin = "auto.log.foundry"
log.error_stream
```

Existing explicit messages, exceptions, FoundryLib logs, feedback, and metrics
are unchanged.

## Filtering and throttling

Throttling uses injected clock and frame suppliers internally so its behavior
is deterministic in tests. Production defaults use `Time.get_ticks_msec()` and
`Engine.get_process_frames()`.

An error identity consists of its normalized message, file, line, and error
type. If the same identity was accepted for any automatic destination within
`automatic_repeated_error_window_msec`, all automatic destinations suppress
the duplicate. Identities are retained in a bounded table; the logger clears
the table when it exceeds 100 entries.

The event destination additionally observes:

- `automatic_events_per_frame`, keyed by the current engine process frame;
- `automatic_event_throttle_count` within the configured sliding time window.

Per-frame and sliding-window limits affect events only. If an event is
throttled, an independently enabled breadcrumb or structured log may still be
captured. The duplicate timestamp is recorded only when at least one
destination accepts the error, so fully masked records do not consume the
repeated-error window.

Configuration and provider replacement reset all automatic-capture throttle
state.

## Recursion and lifecycle safety

All provider calls are wrapped by a non-blocking service-level pipeline guard.
The automatic logger checks that guard before normalizing or delivering a
record. If provider configuration, capture, flush, shutdown, or bridge code
emits another engine error synchronously, the nested logger callback returns
without reporting it.

The guard is deliberately process-wide for the service. A concurrent automatic
diagnostic is dropped while the provider pipeline is active rather than
risking a cross-thread feedback loop. Logger throttling state is protected by
a mutex.

Provider failures continue to update `last_error()` through existing service
contracts. The automatic logger does not call `push_error`, `push_warning`,
FoundryLib logging, or any other path that would re-enter itself.

Failed candidate configuration leaves the current provider, configuration,
and automatic logger active. Successful reconfiguration updates the logger
registration and resets its state without shutting down a provider that is
being reconfigured in place. Shutdown removes the logger before flushing and
closing the provider.

## Provider delivery

### Memory provider

`MemoryObservabilityProvider` implements
`ObservabilityBreadcrumbsProvider`, stores accepted breadcrumbs separately
from events, and exposes copy-returning `breadcrumbs()` and
`clear_breadcrumbs()` helpers for tests.

### Sentry FoundryScript provider

`SentryObservabilityProvider` implements the optional breadcrumb trait and
forwards normalized breadcrumb dictionaries to a `captureBreadcrumb` bridge
method when available. A bridge without that method reports unsupported
breadcrumb delivery without preventing ordinary events or structured logs.

Automatic capture policy remains entirely in the core. The Sentry provider
does not duplicate masks or throttling.

### Apple bridge

The Swift bridge maps normalized breadcrumb level, message, category,
timestamp, global attributes, and breadcrumb attributes to Sentry Cocoa's
breadcrumb API. Per-record attributes override globals, while reserved Foundry
metadata is written last. The bridge returns `true` once the breadcrumb is
accepted by the SDK.

### Android bridge

The Java bridge maps the same payload to Sentry Android's `Breadcrumb`, using
the same attribute precedence and normalized levels. It returns `true` after
adding the breadcrumb to the active Sentry scope.

The existing event, structured-log, feedback, metric, flush, and shutdown
bridge behavior remains unchanged.

## Testing

Core tests cover:

- defensive configuration and breadcrumb-value copying;
- logger registration only after enabled successful configuration;
- removal on disable, replacement, and shutdown;
- error category and severity mapping;
- preservation of function, file, line, code, rationale, type, and available
  backtrace frames;
- independent event, breadcrumb, and structured-log masks;
- ordinary message filtering and routing;
- deterministic duplicate, per-frame, and sliding-window throttling;
- throttle reset during reconfiguration;
- recursion suppression when a provider emits an error;
- safe behavior for providers without breadcrumb support;
- memory-provider breadcrumb storage and clearing;
- an integration-level `push_error()` path through the registered Foundry
  logger.

Tests instantiate the logger with fake clock and frame suppliers rather than
sleeping. A recording or deliberately failing provider supplies deterministic
recursion cases.

Sentry provider tests cover bridge routing and missing breadcrumb support.
Swift and Android unit tests cover level mapping, attribute precedence,
timestamps, and successful delivery. Build-contract tests require the new
bridge method, and packaging/UID tests cover all newly tracked addon files.

The full `task test` gate must pass. Public documentation and the changelog
describe configuration defaults, masks, throttling, source metadata,
provider-capability behavior, and macOS/iOS/Android support.

## Out of scope

This change does not:

- load script source files to attach surrounding source-code context;
- collect local, member, or global variable values;
- add crash or signal handling;
- make warnings or print messages into exception events by default;
- add Sentry-specific automatic-capture configuration to the core API;
- change explicit capture semantics or FoundryLib's existing logging adapter.

The engine-provided backtrace and source metadata satisfy this issue's
preservation requirement without introducing source loading or variable
collection overhead.
