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
var _metric_sample_accumulator: float = 0.0
var _pipeline_mutex: Mutex = Mutex.new()
var _provider_call_count: int = 0
var _automatic_logger: AutomaticObservabilityLogger

const MAX_FEEDBACK_MESSAGE_LENGTH: int = 4096
const MAX_METRIC_NAME_LENGTH: int = 200
const MAX_METRIC_UNIT_LENGTH: int = 64
const MAX_METRIC_ATTRIBUTE_KEY_LENGTH: int = 200


func _init() -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_reset_log_rate_limit()
	_reset_metric_sampling()


## Configures a provider and activates it only after successful setup.
## A failed candidate configuration leaves the current provider unchanged.
func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
	if provider == null:
		_last_error = Error.FAILED
		return Error.FAILED

	var candidate_config: ObservabilityConfig = config
	if candidate_config == null:
		candidate_config = ObservabilityConfig.new(p_enabled = false)
	if not is_finite(candidate_config.metric_sample_rate) \
			or candidate_config.metric_sample_rate < 0.0 \
			or candidate_config.metric_sample_rate > 1.0:
		_last_error = Error.ERR_INVALID_PARAMETER
		return Error.ERR_INVALID_PARAMETER

	_begin_provider_call()
	var result: int = provider.configure(candidate_config)
	_end_provider_call()
	if result != Error.OK:
		_last_error = result
		return result

	if provider == _provider:
		_config = candidate_config
		_last_error = Error.OK
		_shutdown = false
		_reset_log_rate_limit()
		_reset_metric_sampling()
		_refresh_automatic_logger()
		return Error.OK

	_remove_automatic_logger()
	if _provider != null:
		_begin_provider_call()
		_provider.shutdown()
		_end_provider_call()

	_provider = provider
	_config = candidate_config
	_last_error = Error.OK
	_shutdown = false
	_reset_log_rate_limit()
	_reset_metric_sampling()
	_refresh_automatic_logger()
	return Error.OK


## Returns whether the active configuration permits event capture.
func is_enabled() -> bool:
	return _config.enabled


## Returns whether the active provider can currently accept events.
func is_available() -> bool:
	if _provider == null:
		return false
	_begin_provider_call()
	var available: bool = _provider.is_available()
	_end_provider_call()
	return available


## Returns the active provider identifier, including null before configuration.
func provider_name() -> StringName:
	if _provider == null:
		return &"null"
	_begin_provider_call()
	var provider_id: StringName = _provider.provider_name()
	_end_provider_call()
	return provider_id


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


## Captures a breadcrumb when the active provider supports the optional capability.
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	if breadcrumb == null:
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	if not is_enabled() or _provider == null:
		return false
	if not _provider.has_method("capture_breadcrumb"):
		_last_error = Error.ERR_UNAVAILABLE
		return false

	_begin_provider_call()
	var capture_result: Variant = _provider.call("capture_breadcrumb", breadcrumb)
	_end_provider_call()
	if not (capture_result is bool) or not capture_result:
		_last_error = Error.FAILED
		return false
	_last_error = Error.OK
	return true


## Captures explicitly submitted player feedback without creating an error event.
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if not _is_valid_feedback(feedback):
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	if not is_enabled() or _provider == null:
		return ""
	return _capture_feedback(feedback)


## Validates, normalizes, filters, samples, and dispatches one custom metric.
func capture_metric(metric: ObservabilityMetric) -> bool:
	var normalized: ObservabilityMetric? = _normalized_metric(metric)
	if normalized == null:
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	if not is_enabled() or not _config.metrics_enabled or _provider == null:
		return false

	if _config.metric_filter.is_valid():
		var filter_result: Variant = _config.metric_filter.call(normalized)
		if not (filter_result is bool):
			_last_error = Error.ERR_INVALID_PARAMETER
			return false
		if not filter_result:
			return false

	if not _accept_metric_sample():
		return false
	if not _provider.has_method("capture_metric"):
		_last_error = Error.ERR_UNAVAILABLE
		return false

	_begin_provider_call()
	var capture_result: Variant = _provider.call("capture_metric", normalized)
	_end_provider_call()
	if not (capture_result is bool) or not capture_result:
		_last_error = Error.FAILED
		return false
	_last_error = Error.OK
	return true


## Creates and captures a counter metric.
func capture_counter(metric_name: String, value: int = 1, attributes: Dictionary = {}) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.COUNTER,
			p_name = metric_name,
			p_value = float(value),
			p_attributes = attributes,
		))


## Creates and captures a gauge metric.
func capture_gauge(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE,
			p_name = metric_name,
			p_value = value,
			p_unit = unit,
			p_attributes = attributes,
		))


## Creates and captures a distribution metric.
func capture_distribution(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.DISTRIBUTION,
			p_name = metric_name,
			p_value = value,
			p_unit = unit,
			p_attributes = attributes,
		))


func _normalized_metric(metric: ObservabilityMetric) -> ObservabilityMetric?:
	if metric == null or not _is_valid_metric_name(metric.name()):
		return null
	if metric.type() < ObservabilityMetricType.COUNTER \
			or metric.type() > ObservabilityMetricType.DISTRIBUTION:
		return null
	if not is_finite(metric.value()):
		return null
	if metric.type() == ObservabilityMetricType.COUNTER:
		if metric.value() < 0.0 or metric.value() != floorf(metric.value()):
			return null
		if not metric.unit().is_empty():
			return null
	elif not _is_valid_metric_unit(metric.unit()):
		return null

	var attributes: Dictionary = {}
	var global_attributes: Dictionary = _config.global_attributes()
	if not _is_valid_metric_attributes(global_attributes):
		return null
	for key: Variant in global_attributes.keys():
		attributes[str(key)] = global_attributes[key]
	var metric_attributes: Dictionary = metric.attributes()
	if not _is_valid_metric_attributes(metric_attributes):
		return null
	for key: Variant in metric_attributes.keys():
		attributes[str(key)] = metric_attributes[key]
	return ObservabilityMetric.new(
			p_type = metric.type(),
			p_name = metric.name(),
			p_value = metric.value(),
			p_unit = metric.unit(),
			p_attributes = attributes,
		)


func _is_valid_metric_name(value: String) -> bool:
	return not value.is_empty() \
			and value.length() <= MAX_METRIC_NAME_LENGTH \
			and value.strip_edges() == value \
			and not _has_control_character(value)


func _is_valid_metric_unit(value: String) -> bool:
	return value.length() <= MAX_METRIC_UNIT_LENGTH \
			and not _has_control_character(value) \
			and not _has_whitespace(value)


func _is_valid_metric_attributes(attributes: Dictionary) -> bool:
	for key: Variant in attributes.keys():
		if not (key is String) and not (key is StringName):
			return false
		var key_string: String = str(key)
		if key_string.is_empty() \
				or key_string.length() > MAX_METRIC_ATTRIBUTE_KEY_LENGTH \
				or key_string.strip_edges() != key_string \
				or _has_control_character(key_string):
			return false
		if not _is_valid_metric_attribute_value(attributes[key]):
			return false
	return true


func _is_valid_metric_attribute_value(value: Variant) -> bool:
	if value is bool or value is int or value is String or value is StringName:
		return true
	if value is float:
		return is_finite(value)
	return false


func _has_whitespace(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint == 32 or codepoint == 160 \
				or (codepoint >= 8192 and codepoint <= 8202) \
				or codepoint == 8232 or codepoint == 8233 \
				or codepoint == 8239 or codepoint == 8287 or codepoint == 12288:
			return true
	return false


func _accept_metric_sample() -> bool:
	_metric_sample_accumulator += _config.metric_sample_rate
	if _metric_sample_accumulator < 1.0:
		return false
	_metric_sample_accumulator -= 1.0
	return true


func _reset_metric_sampling() -> void:
	_metric_sample_accumulator = 0.0


func _capture_event(event: ObservabilityEvent) -> String:
	if not is_enabled() or _provider == null:
		return ""

	_begin_provider_call()
	var event_id: String = _provider.capture(event)
	_end_provider_call()
	if event_id.is_empty():
		_last_error = Error.FAILED
	return event_id


func _capture_feedback(feedback: ObservabilityFeedback) -> String:
	if not is_enabled() or _provider == null:
		return ""

	_begin_provider_call()
	var feedback_id: String = _provider.capture_feedback(feedback)
	_end_provider_call()
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
	if email.find(" ") >= 0:
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


func _begin_provider_call() -> void:
	_pipeline_mutex.lock()
	_provider_call_count += 1
	_pipeline_mutex.unlock()


func _end_provider_call() -> void:
	_pipeline_mutex.lock()
	_provider_call_count = maxi(0, _provider_call_count - 1)
	_pipeline_mutex.unlock()


## Reports whether an automatic logger callback would re-enter provider work.
func automatic_capture_blocked() -> bool:
	if not _pipeline_mutex.try_lock():
		return true
	var blocked: bool = _provider_call_count > 0
	_pipeline_mutex.unlock()
	return blocked


func _refresh_automatic_logger() -> void:
	var should_install: bool = _config.enabled and _config.automatic_capture_enabled
	if not should_install:
		_remove_automatic_logger()
		return
	if _automatic_logger != null:
		_automatic_logger.reconfigure(_config)
		return
	_automatic_logger = AutomaticObservabilityLogger.new(self, _config)
	OS.add_logger(_automatic_logger)


func _remove_automatic_logger() -> void:
	if _automatic_logger == null:
		return
	OS.remove_logger(_automatic_logger)
	_automatic_logger.reset()
	_automatic_logger = null


## Flushes pending provider work within timeout_msec and stores the returned error.
func flush(timeout_msec: int = 2000) -> int:
	if _provider == null:
		return Error.OK

	_begin_provider_call()
	var result: int = _provider.flush(timeout_msec)
	_end_provider_call()
	_last_error = result
	return result


## Flushes and shuts down once, then restores the disabled null-provider state.
func shutdown() -> void:
	if _shutdown:
		return

	_shutdown = true
	_remove_automatic_logger()
	flush()
	if _provider != null:
		_begin_provider_call()
		_provider.shutdown()
		_end_provider_call()
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_last_error = Error.OK
	_reset_log_rate_limit()
	_reset_metric_sampling()


## Shuts down the service when its autoload leaves the scene tree.
func _exit_tree() -> void:
	shutdown()
