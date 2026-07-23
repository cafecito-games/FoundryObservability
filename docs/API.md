# FoundryObservability API

The core API lives in the `games.cafecito.foundryobservability` namespace. It
does not depend on FoundryLib, Sentry, or native SDKs.

## Setup

Copy or install `addons/FoundryObservability`, enable its editor plugin, and
use the registered `FoundryObservability` autoload:

```foundryscript
import games.cafecito.foundryobservability

var config := ObservabilityConfig.new(true, "production", "1.0.0")
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, config)
FoundryObservability.capture_message("game started")
```

The null provider is active before configuration. `MemoryObservabilityProvider`
is deterministic and intended for tests or local integration work.

## Value types

### `ObservabilityLevel`

Severity constants are ordered from least to most severe:

`TRACE = 10`, `DEBUG = 20`, `INFO = 30`, `WARN = 40`, `ERROR = 50`, and
`FATAL = 60`.

`ObservabilityLevel.name(level: int) -> String` returns the uppercase level
name, or `LEVEL(value)` for an unknown value.

### `ObservabilityConfig`

Constructor:

```foundryscript
ObservabilityConfig.new(
		enabled = true,
		environment = "",
		release = "",
		dist = "",
		global_attributes = {},
		provider_options = {},
)
```

The `enabled`, `environment`, `release`, and `dist` fields are public.
`global_attributes()` and `provider_options()` return deep copies. The core
does not interpret provider options.

### `ObservabilityException`

Constructor arguments are `type_name`, `message`, `stack_trace`, and
`attributes`, with defaults `"Error"`, empty strings, and an empty dictionary.

Accessors:

- `type_name() -> String`
- `message() -> String`
- `stack_trace() -> String`
- `attributes() -> Dictionary`

The attributes dictionary is deep-copied on construction and access.

### `ObservabilityEvent`

Constructor arguments are `kind`, `level`, `message`, `source`,
`timestamp_msec`, `attributes`, and optional `exception`. Defaults are
`&"message"`, `INFO`, empty strings/names, `0`, an empty dictionary, and null.

Accessors:

`kind()`, `level()`, `message()`, `source()`, `timestamp_msec()`,
`attributes()`, and `exception()` return the corresponding values. Event
attributes are deep-copied on construction and access.

## Provider contract

`ObservabilityProvider` is a trait implemented by backend adapters:

```foundryscript
trait_name ObservabilityProvider

abstract func provider_name() -> StringName
abstract func is_available() -> bool
abstract func configure(config: ObservabilityConfig) -> int
abstract func capture(event: ObservabilityEvent) -> String
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
```

`capture()` returns a provider event ID or an empty string on failure.
`configure()` and `flush()` return Foundry `Error` values.

`NullObservabilityProvider` reports the name `&"null"`, is unavailable, and
performs safe no-op operations.

`MemoryObservabilityProvider` reports the name `&"memory"`, stores captured
events, and exposes test controls: `configure_result`, `flush_result`,
`last_flush_timeout_msec`, `flush_count`, and `shutdown_count`. Its `events()`
and `clear()` methods support deterministic tests.

## Autoload service

`FoundryObservabilityApi` exposes the public service methods implemented by the
`FoundryObservability` autoload:

- `configure(provider, config = null) -> int`
- `is_enabled() -> bool`
- `is_available() -> bool`
- `provider_name() -> StringName`
- `last_error() -> int`
- `capture_event(event) -> String`
- `capture_message(message, level = INFO, attributes = {}) -> String`
- `capture_exception(exception, attributes = {}) -> String`
- `flush(timeout_msec = 2000) -> int`
- `shutdown() -> void`

If `config` is null, the service uses a disabled configuration. A candidate
provider is configured before it replaces the active provider. A failed
configuration leaves the existing provider and configuration active, and
stores the returned error in `last_error()`.

Reconfiguring the already-active provider updates its configuration without
shutting it down. Replacing a different provider shuts the old provider down
once, then clears the error state and activates the candidate.

Message events use kind `&"message"`, source `&"game"`, and the current engine
timestamp. Exception events use kind `&"exception"`, `ERROR`, source `&"game"`,
the exception message, and the exception payload.

Capture methods are non-throwing. They return an empty ID while disabled or
when the provider cannot capture. An enabled provider that returns an empty ID
sets `last_error()` to `Error.FAILED`. `flush()` forwards its timeout and stores
the returned error. `shutdown()` flushes and shuts down once, restores the
disabled null-provider state, and is also called from `_exit_tree()`.

Provider failures are stored in the status API and are not emitted through
FoundryLib logging, preventing recursive error reporting.

## Optional FoundryLib integration

Install `addons/FoundryObservabilityFoundryLib` alongside the core addon when
using FoundryLib's structured logging. The adapter is explicit; it does not
install itself:

```foundryscript
import foundry.logging
import games.cafecito.foundryobservability
import games.cafecito.foundryobservability.foundrylib

var sink := FoundryLibObservabilitySink.new(
		FoundryObservability, ObservabilityLevel.ERROR)
Log.add_sink(sink)
```

`FoundryLibObservabilitySink` implements `foundry.logging.LogSink`:

- Records below the configured minimum level are ignored.
- `LogLevel` values are explicitly mapped to `ObservabilityLevel` values;
  unknown values become `INFO`.
- The event kind is `&"log"`, source is `&"foundry.logging"`, message text is
  rendered with `LogFormatter.render_message(record)`, and the original log
  timestamp is preserved.
- Structured fields are deep-copied and augmented with `logger_name`.
- `flush()` forwards to `FoundryObservability`.

The default minimum level is `ERROR`, which avoids turning routine logs into
telemetry volume. Use a lower threshold when the project explicitly needs
debug or info telemetry.

## Deliberate boundary

This release does not include Sentry, Apple/Android native bindings, crash
handlers, persistence, retry policy, user identity, breadcrumbs, attachments,
or performance transactions. The stable provider/event contract is the
foundation for those future integrations. A `foundry-cpp` project is not
required by this core API.
