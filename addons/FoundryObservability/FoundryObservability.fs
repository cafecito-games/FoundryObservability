@autoload
namespace foundry.observability

## Autoload entry point for the provider-neutral game observability API.
class_name FoundryObservability extends Node
uses FoundryObservabilityApi

var _provider: ObservabilityProvider
var _config: ObservabilityConfig
var _last_error: int = Error.OK
var _shutdown: bool = false


func _init() -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)


## Configures a provider and activates it only after successful setup.
## A failed candidate configuration leaves the current provider unchanged.
func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
	if provider == null:
		_last_error = Error.FAILED
		return Error.FAILED

	var candidate_config: ObservabilityConfig = config
	if candidate_config == null:
		candidate_config = ObservabilityConfig.new(p_enabled = false)

	var result: int = provider.configure(candidate_config)
	if result != Error.OK:
		_last_error = result
		return result

	if provider == _provider:
		_config = candidate_config
		_last_error = Error.OK
		_shutdown = false
		return Error.OK

	if _provider != null:
		_provider.shutdown()

	_provider = provider
	_config = candidate_config
	_last_error = Error.OK
	_shutdown = false
	return Error.OK


## Returns whether the active configuration permits event capture.
func is_enabled() -> bool:
	return _config.enabled


## Returns whether the active provider can currently accept events.
func is_available() -> bool:
	return _provider != null and _provider.is_available()


## Returns the active provider identifier, including null before configuration.
func provider_name() -> StringName:
	if _provider == null:
		return &"null"
	return _provider.provider_name()


## Returns the most recent provider, capture, configuration, or flush error.
func last_error() -> int:
	return _last_error


## Captures an event and returns its provider ID, or an empty string on no-op or failure.
func capture_event(event: ObservabilityEvent) -> String:
	if event == null or not is_enabled() or _provider == null:
		return ""

	var event_id: String = _provider.capture(event)
	if event_id.is_empty():
		_last_error = Error.FAILED
	return event_id


## Creates a game-sourced message event using the current engine timestamp.
func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}) -> String:
	return capture_event(
		ObservabilityEvent.new(
			p_kind = &"message",
			p_level = level,
			p_message = message,
			p_source = &"game",
			p_timestamp_msec = Time.get_ticks_msec(),
			p_attributes = attributes,
		),
	)


## Creates a game-sourced ERROR event containing the supplied exception payload.
func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String:
	if exception == null:
		_last_error = Error.FAILED
		return ""
	return capture_event(
		ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = exception.message(),
			p_source = &"game",
			p_timestamp_msec = Time.get_ticks_msec(),
			p_attributes = attributes,
			p_exception = exception,
		),
	)


## Flushes pending provider work within timeout_msec and stores the returned error.
func flush(timeout_msec: int = 2000) -> int:
	if _provider == null:
		return Error.OK

	var result: int = _provider.flush(timeout_msec)
	_last_error = result
	return result


## Flushes and shuts down once, then restores the disabled null-provider state.
func shutdown() -> void:
	if _shutdown:
		return

	_shutdown = true
	flush()
	if _provider != null:
		_provider.shutdown()
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_last_error = Error.OK


## Shuts down the service when its autoload leaves the scene tree.
func _exit_tree() -> void:
	shutdown()
