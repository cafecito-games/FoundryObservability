# FoundryObservability API

FoundryObservability is the provider-neutral observability boundary for Foundry
games. It normalizes messages, exceptions, and structured log records before
dispatching them to a backend provider.

The public namespace is **foundry.observability**. The FoundryLib adapter lives
in **foundry.observability.foundrylib**. Global class and trait names are
unchanged by the namespace migration.

## Installation and setup

Install FoundryLib as a project package, then copy or install the
addons/FoundryObservability directory. Enable the FoundryObservability editor
plugin. The plugin registers the FoundryObservability autoload.

The addon currently requires FoundryLib because its core package includes the
FoundryLib LogSink adapter. The first production backend, Sentry, is not part
of this release.

The smallest setup is:

~~~
import foundry.observability

var config := ObservabilityConfig.new(
		true,
		"production",
		"1.0.0",
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
- ObservabilityEvent

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

### Timestamps

Event timestamps are integer engine milliseconds. The convenience capture
methods use the current engine tick count. Providers should preserve the event
timestamp when translating it to a backend format.

### Defensive copies

Configuration and event dictionaries are copied deeply when stored and when
returned through accessors. This prevents callers from mutating a payload after
it has been handed to the observability service. Memory provider events() copies
the containing array; the event objects themselves are the captured objects.

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
		enabled: bool = true,
		environment: String = "",
		release: String = "",
		dist: String = "",
		global_attributes: Dictionary = {},
		provider_options: Dictionary = {},
)
~~~

Public fields:

| Field | Type | Meaning |
| --- | --- | --- |
| enabled | bool | Whether the service may capture events after configuration |
| environment | String | Deployment environment such as production or staging |
| release | String | Game release identifier |
| dist | String | Optional distribution variant |

Accessors:

~~~
func global_attributes() -> Dictionary
func provider_options() -> Dictionary
~~~

Both accessors return deep copies. global_attributes are shared metadata for a
provider integration. provider_options are opaque to the core and are passed
to provider implementations through the config object.

A null config passed to FoundryObservability.configure is replaced with a
disabled ObservabilityConfig.

## ObservabilityException

ObservabilityException carries script or native failure data.

Constructor:

~~~
ObservabilityException.new(
		type_name: String = "Error",
		message: String = "",
		stack_trace: String = "",
		attributes: Dictionary = {},
)
~~~

Accessors:

~~~
func type_name() -> String
func message() -> String
func stack_trace() -> String
func attributes() -> Dictionary
~~~

attributes are deep-copied on construction and access. The core does not
interpret the type or stack string; providers decide how to map them.

## ObservabilityEvent

ObservabilityEvent is the normalized provider-neutral payload.

Constructor:

~~~
ObservabilityEvent.new(
		kind: StringName = &"message",
		level: int = ObservabilityLevel.INFO,
		message: String = "",
		source: StringName = &"",
		timestamp_msec: int = 0,
		attributes: Dictionary = {},
		exception: ObservabilityException? = null,
)
~~~

Fields:

| Parameter | Meaning |
| --- | --- |
| kind | Event category, such as message, exception, or log |
| level | ObservabilityLevel value or another provider-defined integer |
| message | Human-readable event text |
| source | Subsystem that produced the event |
| timestamp_msec | Engine timestamp in milliseconds |
| attributes | Structured fields copied into the event |
| exception | Optional exception payload |

Accessors:

~~~
func kind() -> StringName
func level() -> int
func message() -> String
func source() -> StringName
func timestamp_msec() -> int
func attributes() -> Dictionary
func exception() -> ObservabilityException?
~~~

attributes are deep-copied on construction and access. exception is optional and
is returned as the original payload object.

## ObservabilityProvider

ObservabilityProvider is the trait backend integrations implement:

~~~
trait_name ObservabilityProvider

abstract func provider_name() -> StringName
abstract func is_available() -> bool
abstract func configure(config: ObservabilityConfig) -> int
abstract func capture(event: ObservabilityEvent) -> String
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
- flush attempts to deliver pending data within timeout_msec. It returns an
  Error value; the service stores that value in last_error().
- shutdown releases provider resources. Implementations must make repeated
  shutdown calls safe because the service owns lifecycle cleanup.

Providers must not report their own configuration, capture, or flush failures
through the FoundryLib logging sink. Doing so would create recursive reporting.

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
abstract func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String
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

The method returns the provider configure result.

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
- the current engine timestamp
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

### capture_exception

~~~
func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
) -> String
~~~

A null exception returns an empty ID and stores Error.FAILED. Otherwise it
creates an event with kind exception, level ERROR, source game, current engine
timestamp, exception.message() as the message, the supplied attributes, and the
exception payload.

Example:

~~~
import foundry.observability

var exception := ObservabilityException.new(
		"NetworkError",
		"Matchmaking request failed",
		stack_trace,
		{"region": "iad"},
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

Public methods:

~~~
func events() -> Array[ObservabilityEvent]
func clear() -> void
~~~

events returns a copy of the captured event list. clear removes captured events
without changing configuration. Successful capture returns sequential IDs in
the form memory:N. Capture returns an empty ID while disabled or after shutdown.

Example:

~~~
import foundry.observability

var provider := MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, ObservabilityConfig.new(true))
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
		FoundryObservability,
		ObservabilityLevel.ERROR,
)
Log.add_sink(sink)
~~~

Constructor:

~~~
FoundryLibObservabilitySink.new(
		service: FoundryObservabilityApi,
		minimum_level: int = ObservabilityLevel.ERROR,
)
~~~

emit filters records below minimum_level or when its target service is null.
Eligible records become log events with:

| Field | Value |
| --- | --- |
| kind | log |
| source | foundry.logging |
| level | Explicit LogLevel mapping below |
| message | LogFormatter.render_message(record) |
| timestamp | record.timestamp_msec |
| attributes | Deep copy of record.fields plus logger_name |
| exception | null |

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

## Custom provider outline

A provider implements all methods in ObservabilityProvider and can translate
ObservabilityEvent into any backend SDK:

~~~
namespace my_game.telemetry

import foundry.observability

class_name MyProvider
extends RefCounted
uses ObservabilityProvider

func provider_name() -> StringName:
	return &"my_provider"

func is_available() -> bool:
	return true

func configure(config: ObservabilityConfig) -> int:
	return Error.OK

func capture(event: ObservabilityEvent) -> String:
	return "my_provider:event"

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
handlers, persistence, retries, user identity, breadcrumbs, attachments, or
performance transactions. A foundry-cpp project is not required by this API.
