namespace foundry.observability

## Public contract implemented by the FoundryObservability autoload.
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
