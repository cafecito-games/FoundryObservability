namespace foundry.observability

## Provider contract for translating core events into a backend SDK.
trait_name ObservabilityProvider

## Returns the stable provider identifier used by status and diagnostics.
abstract func provider_name() -> StringName
## Reports whether the backend can currently accept events.
abstract func is_available() -> bool
## Applies configuration and returns Error.OK or a provider failure code.
abstract func configure(config: ObservabilityConfig) -> int
## Captures one normalized event and returns a backend ID or an empty string.
abstract func capture(event: ObservabilityEvent) -> String
## Flushes pending events within the requested timeout in milliseconds.
abstract func flush(timeout_msec: int = 2000) -> int
## Releases provider resources; repeated calls must be safe.
abstract func shutdown() -> void
