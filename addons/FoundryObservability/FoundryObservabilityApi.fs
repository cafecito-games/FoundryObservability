namespace foundry.observability

## Public contract implemented by the FoundryObservability autoload.
trait_name FoundryObservabilityApi

## Configures a provider and returns Error.OK or the provider's failure code.
abstract func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int
## Returns whether the active configuration permits capture.
abstract func is_enabled() -> bool
## Returns whether the active provider is currently available.
abstract func is_available() -> bool
## Returns the active provider's stable identifier.
abstract func provider_name() -> StringName
## Returns the most recent stored provider or capture error.
abstract func last_error() -> int
## Captures an event and returns a provider ID, or an empty string on no-op/failure.
abstract func capture_event(event: ObservabilityEvent) -> String
## Creates and captures a game-sourced message event.
abstract func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}) -> String
## Creates and captures a game-sourced ERROR exception event.
abstract func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String
## Creates and captures a structured log record.
abstract func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
) -> String
## Flushes pending work within timeout_msec and returns an Error value.
abstract func flush(timeout_msec: int = 2000) -> int
## Flushes and shuts down the service; repeated calls are safe.
abstract func shutdown() -> void
