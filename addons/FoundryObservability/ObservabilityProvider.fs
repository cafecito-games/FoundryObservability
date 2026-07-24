namespace foundry.observability

## Provider contract for translating core events into a backend SDK.
trait_name ObservabilityProvider

abstract func provider_name() -> StringName
abstract func is_available() -> bool
abstract func configure(config: ObservabilityConfig) -> int
abstract func capture(event: ObservabilityEvent) -> String
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
