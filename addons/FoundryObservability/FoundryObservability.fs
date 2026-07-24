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
	_config = ObservabilityConfig.new(false)


func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
	if provider == null:
		_last_error = Error.FAILED
		return Error.FAILED

	var candidate_config: ObservabilityConfig = config
	if candidate_config == null:
		candidate_config = ObservabilityConfig.new(false)

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


func is_enabled() -> bool:
	return _config.enabled


func is_available() -> bool:
	return _provider != null and _provider.is_available()


func provider_name() -> StringName:
	if _provider == null:
		return &"null"
	return _provider.provider_name()


func last_error() -> int:
	return _last_error


func capture_event(event: ObservabilityEvent) -> String:
	if event == null or not is_enabled() or _provider == null:
		return ""

	var event_id: String = _provider.capture(event)
	if event_id.is_empty():
		_last_error = Error.FAILED
	return event_id


func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}) -> String:
	return capture_event(
		ObservabilityEvent.new(
			&"message",
			level,
			message,
			&"game",
			Time.get_ticks_msec(),
			attributes,
		),
	)


func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String:
	if exception == null:
		_last_error = Error.FAILED
		return ""
	return capture_event(
		ObservabilityEvent.new(
			&"exception",
			ObservabilityLevel.ERROR,
			exception.message(),
			&"game",
			Time.get_ticks_msec(),
			attributes,
			exception,
		),
	)


func flush(timeout_msec: int = 2000) -> int:
	if _provider == null:
		return Error.OK

	var result: int = _provider.flush(timeout_msec)
	_last_error = result
	return result


func shutdown() -> void:
	if _shutdown:
		return

	_shutdown = true
	flush()
	if _provider != null:
		_provider.shutdown()
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(false)
	_last_error = Error.OK


func _exit_tree() -> void:
	shutdown()
