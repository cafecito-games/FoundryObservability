# FoundryObservability Core API

**Date:** 2026-07-23

**Status:** Approved for implementation planning

## Goal

Replace the behavior-free `FoundryObservability` bootstrap with a small,
provider-neutral observability core. The core will give Foundry game code one
stable API for emitting messages and exceptions, while allowing provider
implementations such as Sentry to be added later without changing consumers.

The first implementation will run entirely in FoundryScript. It will not add
Sentry, native bindings, crash detection, network transport, or a
`foundry-cpp` project.

## Scope

The first implementation includes:

- Typed event, exception, configuration, and provider contracts.
- A `FoundryObservability` autoload that owns provider lifecycle and dispatch.
- A safe null provider used before configuration and when no provider is
  available.
- An in-memory provider for deterministic tests and local integration work.
- An optional FoundryLib integration addon containing a typed `LogSink` adapter.
- Consumer-project tests covering the public contract, lifecycle, event
  immutability, disabled behavior, and structured log mapping.

The first implementation explicitly defers user identity, breadcrumbs, scope
mutation, performance transactions, attachments, crash handlers, persistence,
retry policy, and provider-specific configuration fields beyond an opaque
options dictionary.

## Public API

All core types live in the `games.cafecito.foundryobservability` namespace.

`ObservabilityLevel` defines `TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`, and
`FATAL` in ascending severity order.

`ObservabilityException` is a typed value object containing an exception type,
message, stack trace, and additional attributes. It does not depend on a
language-specific exception class, so native and script failures can use the
same representation.

`ObservabilityEvent` is a typed value object containing an event kind, level,
message, exception data when present, source name, timestamp, and attributes.
Its dictionaries are copied on construction and when exposed to prevent a
provider from mutating the caller's data.

`ObservabilityConfig` contains the provider-neutral enabled flag, environment,
release, distribution, global attributes, and an opaque provider-options
dictionary. The core never interprets provider options.

`ObservabilityProvider` is a trait with these responsibilities:

- Report a stable provider name and availability.
- Configure from `ObservabilityConfig`.
- Capture an `ObservabilityEvent`, returning a provider event ID or an empty
  string.
- Flush pending work with a timeout and return a Foundry `Error` code.
- Shut down cleanly.

`FoundryObservability` implements `FoundryObservabilityApi` and exposes:

- `configure(provider, config)` to replace the active provider after successful
  configuration.
- `is_enabled`, `is_available`, `provider_name`, and `last_error` status
  accessors.
- `capture_event`, `capture_message`, and `capture_exception` convenience
  methods.
- `flush(timeout_msec)` and `shutdown()` lifecycle methods.

Capture methods are non-throwing and return an empty event ID when disabled or
when the active provider cannot capture. Configuration and flush return Foundry
`Error` values. Provider failures are stored in `last_error` and are not
written through FoundryLib logging, preventing logging recursion.

The null provider is the default. A failed configuration leaves the previous
provider active, so a game never loses an already working provider because a
replacement failed to start.

## FoundryLib integration

The core addon will not import FoundryLib. The optional
`FoundryObservabilityFoundryLib` integration addon will depend on both addons
and provide `FoundryLibObservabilitySink`, implementing `foundry.logging.LogSink`.

The sink will be explicitly installed by game code. It will map a
`LogRecord` into an `ObservabilityEvent` by preserving the logger name,
rendering the message template, copying structured fields, and translating
the shared severity values. It will have a configurable minimum level,
defaulting to `ERROR` to avoid turning routine logs into telemetry volume.
Sink/provider failures will be silent from the sink's perspective and exposed
through the observability status API.

Keeping this adapter in a separate addon means projects can use the core API
without installing FoundryLib, while FoundryLib users get a first-party typed
integration rather than a dynamic or string-based bridge.

## Data flow

```text
Game code ----------------------> FoundryObservability -----> active provider
FoundryLib LogSink adapter -----/             |
                                              v
                                         Null provider
```

The core owns event construction, configuration, provider replacement, and
lifecycle. Providers only translate the stable event model into their native
SDK calls.

## Error handling and lifecycle

The autoload starts with the null provider and remains safe on desktop,
headless test runs, and platforms without native support. `configure` first
configures a candidate provider and swaps it in only after success. Replacing
or shutting down a provider calls its shutdown method exactly once.

`flush` forwards the timeout to the provider and is safe before configuration.
`shutdown` flushes and shuts down the active provider, then restores the null
provider state. The autoload also invokes shutdown during tree exit, but game
code may call `flush` earlier at lifecycle boundaries that matter to it.

The core does not attempt to observe engine crashes or script diagnostics yet.
Those features need platform-specific hooks and will be added behind provider
capabilities after the event contract is stable.

## Testing

FoundryLib's test runner will exercise the core with an in-memory provider and
will never require network access or native libraries. Tests will verify:

- Typed construction and defensive copying of config, exception, and event
  data.
- Null-provider behavior before configuration and while disabled.
- Successful provider configuration and failed replacement behavior.
- Capture routing, event IDs, flush propagation, and shutdown ordering.
- Provider availability and `last_error` reporting.
- FoundryLib sink severity translation, rendered messages, copied fields,
  minimum-level filtering, and non-recursive failure behavior.

The existing shell contract checks will be extended to validate the new core
resources, the optional integration addon, and all tracked FoundryScript UID
companions.

## Future native boundary

Sentry will implement `ObservabilityProvider` behind Apple SwiftGodot and
Android AAR bridges, following the existing MobileKit architecture. A
`foundry-cpp` binding remains a separate future project, justified only if a
shared C++ provider or Windows-native crash integration becomes more valuable
than platform-specific bridges. It is not a dependency of this core API.
