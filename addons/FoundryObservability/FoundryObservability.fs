@autoload
namespace foundry.observability

## Autoload entry point for the provider-neutral game observability API.
class_name FoundryObservability extends Node
uses FoundryObservabilityApi

var _provider: ObservabilityProvider
var _config: ObservabilityConfig
var _last_error: int = Error.OK
var _shutdown: bool = false
var _log_window_second: int = -1
var _log_window_count: int = 0

const MAX_FEEDBACK_MESSAGE_LENGTH: int = 4096


func _init() -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_reset_log_rate_limit()


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
		_reset_log_rate_limit()
		return Error.OK

	if _provider != null:
		_provider.shutdown()

	_provider = provider
	_config = candidate_config
	_last_error = Error.OK
	_shutdown = false
	_reset_log_rate_limit()
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
	if event == null:
		return ""
	if not is_enabled() or _provider == null:
		return ""
	if event.kind() == &"log":
		if not _config.logs_enabled or event.level() < _config.log_minimum_level:
			return ""
		if not _accept_log(event.timestamp_msec()):
			return ""
	return _capture_event(event)


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


## Creates a structured log event using the supplied or current engine timestamp.
func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
) -> String:
	if not is_enabled() or _provider == null:
		return ""
	if not _config.logs_enabled or level < _config.log_minimum_level:
		return ""
	var event_timestamp: int = timestamp_msec
	if event_timestamp < 0:
		event_timestamp = Time.get_ticks_msec()
	if not _accept_log(event_timestamp):
		return ""
	return _capture_event(ObservabilityEvent.new(
			p_kind = &"log",
			p_level = level,
			p_message = message,
			p_source = source,
			p_timestamp_msec = event_timestamp,
			p_attributes = attributes,
	))


## Captures explicitly submitted player feedback without creating an error event.
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if not _is_valid_feedback(feedback):
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	if not is_enabled() or _provider == null:
		return ""
	return _capture_feedback(feedback)


func _capture_event(event: ObservabilityEvent) -> String:
	if not is_enabled() or _provider == null:
		return ""

	var event_id: String = _provider.capture(event)
	if event_id.is_empty():
		_last_error = Error.FAILED
	return event_id


func _capture_feedback(feedback: ObservabilityFeedback) -> String:
	if not is_enabled() or _provider == null:
		return ""

	var feedback_id: String = _provider.capture_feedback(feedback)
	if feedback_id.is_empty():
		_last_error = Error.FAILED
	return feedback_id


func _is_valid_feedback(feedback: ObservabilityFeedback) -> bool:
	if feedback == null:
		return false
	var message: String = feedback.message()
	if message.strip_edges().is_empty() or message.length() > MAX_FEEDBACK_MESSAGE_LENGTH:
		return false
	if not _is_valid_optional_text(feedback.name()):
		return false
	if not _is_valid_email(feedback.contact_email()):
		return false
	return _is_valid_optional_text(feedback.associated_event_id())


func _is_valid_optional_text(value: String) -> bool:
	if value.is_empty():
		return true
	if value.strip_edges().is_empty():
		return false
	return not _has_control_character(value)


func _is_valid_email(email: String) -> bool:
	if email.is_empty():
		return true
	if not _is_valid_optional_text(email):
		return false
	var at_index: int = email.find("@")
	return at_index > 0 and at_index < email.length() - 1 and at_index == email.rfind("@")


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false


func _accept_log(timestamp_msec: int) -> bool:
	var window_second: int = floori(float(timestamp_msec) / 1000.0)
	if window_second != _log_window_second:
		_log_window_second = window_second
		_log_window_count = 0
	if _config.log_rate_limit_per_second <= 0:
		return true
	if _log_window_count >= _config.log_rate_limit_per_second:
		return false
	_log_window_count += 1
	return true


func _reset_log_rate_limit() -> void:
	_log_window_second = -1
	_log_window_count = 0


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
	_reset_log_rate_limit()


## Shuts down the service when its autoload leaves the scene tree.
func _exit_tree() -> void:
	shutdown()
