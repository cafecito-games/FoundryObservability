# FoundryObservability Structured Log Delivery Design

## Goal

Add a first-class structured logging path to FoundryObservability so routine
diagnostic records are delivered as backend log records instead of ordinary
error events. The public API remains provider-neutral and supports macOS, iOS,
and Android through the existing Sentry provider.

## Scope

This change includes:

- a `capture_log` method on the provider-neutral service API;
- configuration for log enablement and minimum severity filtering;
- optional provider-neutral per-second log rate limiting, disabled by default;
- reuse of the existing `ObservabilityEvent` envelope with `kind = "log"`;
- FoundryLib forwarding through the existing `FoundryLibObservabilitySink`;
- native structured-log delivery in the Apple and Android Sentry bridges;
- safe behavior when a provider does not support structured logs;
- deterministic core, FoundryLib, provider, Swift, and Android tests;
- public API and platform-behavior documentation.

The existing message and exception paths remain ordinary observability events.
No new public Sentry-specific API is introduced.

## Public API and configuration

`FoundryObservabilityApi` gains:

```text
capture_log(
    message: String,
    level: int = ObservabilityLevel.INFO,
    source: StringName = &"game",
    timestamp_msec: int = -1,
    attributes: Dictionary = {}
) -> String
```

An omitted timestamp uses the current engine tick count. Callers such as the
FoundryLib sink pass the source record timestamp unchanged.

`ObservabilityConfig` gains:

```text
logs_enabled: bool = true
log_minimum_level: int = ObservabilityLevel.TRACE
log_rate_limit_per_second: int = 0
```

The existing `enabled` flag still gates the entire provider. `logs_enabled`
only gates structured logs, so messages and exceptions can remain enabled when
logs are disabled. Records below `log_minimum_level` are silently filtered.
When `log_rate_limit_per_second` is greater than zero, the service accepts at
most that many logs in the current one-second engine-tick window. A value of
zero delegates high-volume rate limiting to the provider SDK.

Filtered and intentionally disabled records return an empty ID without
changing `last_error()`. A provider that is available and accepts a log must
return a non-empty ID. An empty ID from an accepted log stores
`Error.FAILED`, matching the existing capture contract.

## Event flow

The service creates an `ObservabilityEvent` with:

- `kind = "log"`;
- the requested level and message;
- the requested source;
- the supplied or current timestamp;
- a deep copy of the supplied attributes;
- no exception payload.

`capture_event` continues to accept these events for custom providers. The
Sentry provider recognizes the log kind and calls a separate native bridge
method, while message and exception kinds continue through the existing event
bridge method. This keeps the existing event model and custom providers
compatible while making the delivery path distinct at the backend boundary.

The existing FoundryLib sink remains the only adapter. It keeps its local
minimum-level filter, renders the template, copies record fields, adds
`logger_name`, and calls `capture_log` with source `foundry.logging` and the
record timestamp. No second manual adapter is required.

## Provider behavior

### Sentry FoundryScript provider

The provider forwards `logs_enabled` and the log configuration in the normal
configuration payload. When structured logs are enabled, configuration fails
if the bridge does not expose `captureLog`. For a `log` event it calls that
method directly. Existing `capture`, `flush`, and shutdown behavior is
unchanged; no legacy event fallback is retained.

### Apple bridge

The bridge enables the Sentry Logs option during configuration and maps each
normalized level to the matching `SentrySDK.logger` method: trace, debug, info,
warn, error, or fatal. It merges global and per-record attributes, then writes
reserved metadata last:

```text
foundry.kind
foundry.source
foundry.timestamp_msec
```

The FoundryLib logger name remains the `logger_name` attribute. The native
structured-log API does not return an event ID, so the bridge returns a
synthetic accepted-log identifier after enqueueing the record.

### Android bridge

The bridge enables Sentry Logs during configuration and uses
`Sentry.logger().log` with the matching `SentryLogLevel` and
`SentryAttributes.fromMap` for structured scalar attributes. It uses the same
attribute precedence and reserved metadata as Apple and returns a synthetic
accepted-log identifier after enqueueing the record.

Both native SDKs provide their own batching and transport rate limiting. The
existing provider `flush(timeout_msec)` forwards to the native SDK flush call,
so queued logs are included in normal service shutdown and explicit flushes.

## Attribute and error handling

Global attributes are copied first, then per-record attributes override them.
Reserved Foundry metadata is written last so callers cannot overwrite the
delivery metadata. Structured scalar values remain attributes and are never
concatenated into the message. Unsupported values are omitted by the native
bridge conversion path rather than raising during capture.

The Null provider remains a safe no-op. Disabled logging, unsupported bridges,
missing DSNs, malformed native return values, and repeated shutdown calls
remain observable through the existing provider availability and error
contracts without recursively emitting logs through FoundryLib.

## Testing

Core tests cover default-enabled logging, event shape, global and per-record
attributes, disabled logging, minimum-level filtering, rate limiting, and
provider failure handling. FoundryLib tests verify rendered messages, logger
name, timestamp, level, and field preservation through `capture_log`.

Sentry FoundryScript tests cover configuration forwarding, native log routing,
missing `captureLog` support, disabled behavior, and shutdown. Swift tests
cover level mapping and attribute precedence for structured logs. Android
tests cover level mapping, scalar attribute conversion, bridge configuration,
log routing, and synthetic accepted-log IDs. Public documentation describes
the configuration contract and Apple/Android backend behavior.
