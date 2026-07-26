namespace foundry.observability

## Coordinates provider-neutral processing before provider delivery.
class_name ObservabilityProcessingPipeline
extends RefCounted

## These values intentionally mirror FoundryObservability's metric normalization contract.
const _MAX_METRIC_NAME_LENGTH: int = 200
const _MAX_METRIC_UNIT_LENGTH: int = 64
const _MAX_METRIC_ATTRIBUTE_KEY_LENGTH: int = 200

var _clock: Callable
var _frame: Callable
var _redactor: ObservabilityRedactor = ObservabilityRedactor.new()
var _event_processors: Array[Callable] = []
var _log_processors: Array[Callable] = []
var _metric_processors: Array[Callable] = []
var _metric_filter: Callable = Callable()
var _event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _processing_depth: int = 0
var _recursive_drops: int = 0
var _diagnostic_sequence: int = 0
var _last_diagnostic: ObservabilityProcessingDiagnostic?
var _state_mutex: Mutex = Mutex.new()


## Creates a coordinator with optional deterministic admission suppliers.
func _init(clock: Callable = Callable(), frame: Callable = Callable()) -> void:
	_clock = clock if clock.is_valid() else func() -> int: return Time.get_ticks_msec()
	_frame = frame if frame.is_valid() else func() -> int: return Engine.get_process_frames()


## Atomically replaces all processing state after candidate validation succeeds.
func configure(config: ObservabilityConfig? = null) -> int:
	if config == null:
		return Error.ERR_INVALID_PARAMETER
	if not _valid_sample_rate(config.event_sample_rate) \
			or not _valid_sample_rate(config.log_sample_rate) \
			or not _valid_sample_rate(config.metric_sample_rate):
		return Error.ERR_INVALID_PARAMETER

	var event_processors: Array[Callable] = config.event_processors()
	var log_processors: Array[Callable] = config.log_processors()
	var metric_processors: Array[Callable] = config.metric_processors()
	if not _valid_processors(event_processors) or not _valid_processors(log_processors) \
			or not _valid_processors(metric_processors):
		return Error.ERR_INVALID_DATA
	if config.metric_filter.is_valid() == false and config.metric_filter != Callable():
		return Error.ERR_INVALID_DATA

	var policy: ObservabilityRedactionPolicy = config.redaction_policy()
	var event_limits: ObservabilitySignalLimits = config.event_limits()
	var log_limits: ObservabilitySignalLimits = config.log_limits()
	var metric_limits: ObservabilitySignalLimits = config.metric_limits()
	if policy == null or not policy.is_valid() or not _valid_limits(event_limits) \
			or not _valid_limits(log_limits) or not _valid_limits(metric_limits):
		return Error.ERR_INVALID_DATA

	var candidate_redactor: ObservabilityRedactor = ObservabilityRedactor.new(policy)
	if not _redactor_accepts_all_signals(candidate_redactor):
		return Error.ERR_INVALID_DATA
	var candidate_event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.event_sample_rate, event_limits)
	var candidate_log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.log_sample_rate, log_limits, config.log_rate_limit_per_second)
	var candidate_metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.metric_sample_rate, metric_limits)

	_state_mutex.lock()
	_redactor = candidate_redactor
	_event_processors = _copy_processors(event_processors)
	_log_processors = _copy_processors(log_processors)
	_metric_processors = _copy_processors(metric_processors)
	_metric_filter = config.metric_filter
	_event_limiter = candidate_event_limiter
	_log_limiter = candidate_log_limiter
	_metric_limiter = candidate_metric_limiter
	_processing_depth = 0
	_recursive_drops = 0
	_diagnostic_sequence = 0
	_last_diagnostic = null
	_state_mutex.unlock()
	return Error.OK


## Classifies structured log events separately from ordinary events.
func process_event(event: ObservabilityEvent) -> Dictionary:
	var p_signal: StringName = &"log" if event != null and event.kind() == &"log" else &"event"
	return _process_event_signal(event, p_signal)


## Processes one custom metric independently from event/log state.
func process_metric(metric: ObservabilityMetric) -> Dictionary:
	return _process_metric_signal(metric)


## Records the provider outcome after a successful pre-provider processing result.
func record_provider_result(p_signal: StringName, accepted: bool, error: int) -> void:
	if not _valid_signal(p_signal):
		return
	if accepted:
		_publish(p_signal, ObservabilityProcessingDiagnostic.ACCEPTED, &"", -1, -1, &"", Error.OK)
		return
	var effective_error: int = Error.FAILED if error == Error.OK else error
	_publish(
			p_signal,
			ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.PROVIDER_REJECTED,
			-1,
			-1,
			&"",
			effective_error,
	)


## Returns an isolated payload-free diagnostic snapshot.
func last_diagnostic() -> ObservabilityProcessingDiagnostic?:
	_state_mutex.lock()
	var snapshot: ObservabilityProcessingDiagnostic? = (
			_last_diagnostic.duplicate() if _last_diagnostic != null else null)
	_state_mutex.unlock()
	return snapshot


## Returns the stable total of entries rejected for recursive processing.
func recursive_drop_count() -> int:
	_state_mutex.lock()
	var count: int = _recursive_drops
	_state_mutex.unlock()
	return count


func _process_event_signal(event: ObservabilityEvent, p_signal: StringName) -> Dictionary:
	var snapshot: Dictionary = _reserve(p_signal)
	if snapshot.is_empty():
		return _rejected(p_signal)
	if event == null:
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	@warning_ignore("unsafe_cast")
	var redactor: ObservabilityRedactor = snapshot["redactor"] as ObservabilityRedactor
	var redacted: Dictionary = redactor.redact_event(event, p_signal)
	if not redacted["valid"]:
		_release()
		_publish_redaction_failure(p_signal, _rule_index(redacted))
		return _rejected(p_signal)
	if not (redacted["value"] is ObservabilityEvent):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)
	@warning_ignore("unsafe_cast")
	var current: ObservabilityEvent = redacted["value"] as ObservabilityEvent
	if not _valid_event(current, p_signal):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	var processors: Array[Callable] = snapshot["processors"]
	for index: int in range(processors.size()):
		var result: Variant = processors[index].call(current)
		if result == null:
			_release()
			_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
					ObservabilityProcessingDiagnostic.PROCESSOR, index, -1, &"", Error.OK)
			return _rejected(p_signal)
		if not (result is ObservabilityEvent):
			_release()
			_publish_invalid_processor(p_signal, index)
			return _rejected(p_signal)
		@warning_ignore("unsafe_cast")
		current = result as ObservabilityEvent
		if not _valid_event(current, p_signal):
			_release()
			_publish_invalid_processor(p_signal, index)
			return _rejected(p_signal)

	redacted = redactor.redact_event(current, p_signal)
	if not redacted["valid"]:
		_release()
		_publish_redaction_failure(p_signal, _rule_index(redacted))
		return _rejected(p_signal)
	if not (redacted["value"] is ObservabilityEvent):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)
	@warning_ignore("unsafe_cast")
	current = redacted["value"] as ObservabilityEvent
	if not _valid_event(current, p_signal):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	@warning_ignore("unsafe_cast")
	var limiter: ObservabilitySignalLimiter = snapshot["limiter"] as ObservabilitySignalLimiter
	@warning_ignore("unsafe_cast")
	var clock: Callable = snapshot["clock"] as Callable
	@warning_ignore("unsafe_cast")
	var frame: Callable = snapshot["frame"] as Callable
	var admission: Dictionary = limiter.admit(
			_event_identity(current, p_signal), _now_msec(clock), _frame_index(frame))
	_release()
	if not admission["accepted"]:
		_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
				StringName(str(admission["reason"])), -1, -1,
				StringName(str(admission["limit_kind"])), Error.OK)
		return _rejected(p_signal)
	return {"accepted": true, "value": current, "signal": p_signal}


func _process_metric_signal(metric: ObservabilityMetric) -> Dictionary:
	var p_signal: StringName = ObservabilityProcessingDiagnostic.METRIC
	var snapshot: Dictionary = _reserve(p_signal)
	if snapshot.is_empty():
		return _rejected(p_signal)
	if not _valid_metric(metric):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	@warning_ignore("unsafe_cast")
	var redactor: ObservabilityRedactor = snapshot["redactor"] as ObservabilityRedactor
	var redacted: Dictionary = redactor.redact_metric(metric)
	if not redacted["valid"]:
		_release()
		_publish_redaction_failure(p_signal, _rule_index(redacted))
		return _rejected(p_signal)
	if not (redacted["value"] is ObservabilityMetric):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)
	@warning_ignore("unsafe_cast")
	var current: ObservabilityMetric = redacted["value"] as ObservabilityMetric
	if not _valid_metric(current):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	var metric_filter: Callable = snapshot["metric_filter"]
	if metric_filter.is_valid():
		var filter_result: Variant = metric_filter.call(current)
		if not (filter_result is bool):
			_release()
			_publish_invalid_processor(p_signal, -1)
			return _rejected(p_signal)
		if not filter_result:
			_release()
			_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
					ObservabilityProcessingDiagnostic.PROCESSOR, -1, -1, &"", Error.OK)
			return _rejected(p_signal)

	var processors: Array[Callable] = snapshot["processors"]
	for index: int in range(processors.size()):
		var result: Variant = processors[index].call(current)
		if result == null:
			_release()
			_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
					ObservabilityProcessingDiagnostic.PROCESSOR, index, -1, &"", Error.OK)
			return _rejected(p_signal)
		if not (result is ObservabilityMetric):
			_release()
			_publish_invalid_processor(p_signal, index)
			return _rejected(p_signal)
		@warning_ignore("unsafe_cast")
		current = result as ObservabilityMetric
		if not _valid_metric(current):
			_release()
			_publish_invalid_processor(p_signal, index)
			return _rejected(p_signal)

	redacted = redactor.redact_metric(current)
	if not redacted["valid"]:
		_release()
		_publish_redaction_failure(p_signal, _rule_index(redacted))
		return _rejected(p_signal)
	if not (redacted["value"] is ObservabilityMetric):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)
	@warning_ignore("unsafe_cast")
	current = redacted["value"] as ObservabilityMetric
	if not _valid_metric(current):
		_release()
		_publish_invalid_payload(p_signal)
		return _rejected(p_signal)

	@warning_ignore("unsafe_cast")
	var limiter: ObservabilitySignalLimiter = snapshot["limiter"] as ObservabilitySignalLimiter
	@warning_ignore("unsafe_cast")
	var clock: Callable = snapshot["clock"] as Callable
	@warning_ignore("unsafe_cast")
	var frame: Callable = snapshot["frame"] as Callable
	var admission: Dictionary = limiter.admit(
			_metric_identity(current), _now_msec(clock), _frame_index(frame))
	_release()
	if not admission["accepted"]:
		_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
				StringName(str(admission["reason"])), -1, -1,
				StringName(str(admission["limit_kind"])), Error.OK)
		return _rejected(p_signal)
	return {"accepted": true, "value": current, "signal": p_signal}


func _reserve(p_signal: StringName) -> Dictionary:
	_state_mutex.lock()
	if _processing_depth > 0:
		_recursive_drops += 1
		_publish_locked(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
				ObservabilityProcessingDiagnostic.RECURSIVE, -1, -1, &"", Error.OK)
		_state_mutex.unlock()
		return {}
	_processing_depth += 1
	var processors: Array[Callable] = _event_processors if p_signal == &"event" else _log_processors
	var limiter: ObservabilitySignalLimiter = _event_limiter if p_signal == &"event" else _log_limiter
	if p_signal == &"metric":
		processors = _metric_processors
		limiter = _metric_limiter
	var snapshot: Dictionary = {
		"redactor": _redactor,
		"processors": _copy_processors(processors),
		"metric_filter": _metric_filter,
		"limiter": limiter,
		"clock": _clock,
		"frame": _frame,
	}
	_state_mutex.unlock()
	return snapshot


func _release() -> void:
	_state_mutex.lock()
	_processing_depth = maxi(0, _processing_depth - 1)
	_state_mutex.unlock()


func _publish_invalid_payload(p_signal: StringName) -> void:
	_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.INVALID_PAYLOAD, -1, -1, &"", Error.ERR_INVALID_DATA)


func _publish_redaction_failure(p_signal: StringName, rule_index: int) -> void:
	_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.REDACTION_FAILED, -1, rule_index, &"", Error.ERR_INVALID_DATA)


func _publish_invalid_processor(p_signal: StringName, processor_index: int) -> void:
	_publish(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT,
			processor_index, -1, &"", Error.ERR_INVALID_DATA)


func _publish(
		p_signal: StringName,
		outcome: StringName,
		reason: StringName,
		processor_index: int,
		rule_index: int,
		limit_kind: StringName,
		error: int,
) -> void:
	_state_mutex.lock()
	_publish_locked(p_signal, outcome, reason, processor_index, rule_index, limit_kind, error)
	_state_mutex.unlock()


func _publish_locked(
		p_signal: StringName,
		outcome: StringName,
		reason: StringName,
		processor_index: int,
		rule_index: int,
		limit_kind: StringName,
		error: int,
) -> void:
	_diagnostic_sequence += 1
	_last_diagnostic = ObservabilityProcessingDiagnostic.new(
			_diagnostic_sequence, p_signal, outcome, reason, processor_index,
			rule_index, limit_kind, error)


func _rejected(p_signal: StringName) -> Dictionary:
	return {"accepted": false, "signal": p_signal}


func _valid_sample_rate(value: float) -> bool:
	return is_finite(value) and value >= 0.0 and value <= 1.0


func _valid_processors(processors: Array[Callable]) -> bool:
	for processor: Callable in processors:
		if not processor.is_valid():
			return false
	return true


func _valid_limits(limits: ObservabilitySignalLimits) -> bool:
	return limits != null and limits.per_frame() >= 0 and limits.repeated_window_msec() >= 0 \
			and limits.window_count() >= 0 and limits.window_msec() >= 0


func _valid_signal(p_signal: StringName) -> bool:
	return p_signal == &"event" or p_signal == &"log" or p_signal == &"metric"


func _valid_event(event: ObservabilityEvent, p_signal: StringName) -> bool:
	return event != null and event.kind() != &"" \
			and ((p_signal == &"log" and event.kind() == &"log") \
			or (p_signal == &"event" and event.kind() != &"log"))


func _valid_metric(metric: ObservabilityMetric) -> bool:
	if metric == null or not _is_valid_metric_name(metric.name()) \
			or not is_finite(metric.value()) \
			or not _is_valid_metric_attributes(metric.attributes()):
		return false
	if metric.type() < ObservabilityMetricType.COUNTER \
			or metric.type() > ObservabilityMetricType.DISTRIBUTION:
		return false
	if metric.type() == ObservabilityMetricType.COUNTER:
		return metric.value() >= 0.0 and metric.value() == floorf(metric.value()) \
			and metric.unit().is_empty()
	return _is_valid_metric_unit(metric.unit())


## Confirms a candidate policy can redact representative payloads for every signal.
func _redactor_accepts_all_signals(redactor: ObservabilityRedactor) -> bool:
	var exception: ObservabilityException = ObservabilityException.new(
			p_type_name = "Error", p_message = "message", p_stack_trace = "stack",
			p_attributes = {"attribute": "value"},
	)
	var event: ObservabilityEvent = ObservabilityEvent.new(
			p_kind = &"message", p_message = "message", p_source = &"game",
			p_attributes = {"attribute": "value"}, p_exception = exception,
	)
	var log_event: ObservabilityEvent = ObservabilityEvent.new(
			p_kind = &"log", p_message = "message", p_source = &"game",
			p_attributes = {"attribute": "value"},
	)
	var metric: ObservabilityMetric = ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0, p_unit = "unit",
			p_attributes = {"attribute": "value"},
	)
	return redactor.redact_event(event, &"event").get("valid", false) == true \
			and redactor.redact_event(log_event, &"log").get("valid", false) == true \
			and redactor.redact_metric(metric).get("valid", false) == true


## Mirrors FoundryObservability metric acceptance for pre-redacted and processor result values.
func _is_valid_metric_name(value: String) -> bool:
	return not value.is_empty() \
			and value.length() <= _MAX_METRIC_NAME_LENGTH \
			and value.strip_edges() == value \
			and not _has_control_character(value)


func _is_valid_metric_unit(value: String) -> bool:
	return value.length() <= _MAX_METRIC_UNIT_LENGTH \
			and not _has_control_character(value) \
			and not _has_whitespace(value)


func _is_valid_metric_attributes(attributes: Dictionary) -> bool:
	for key: Variant in attributes.keys():
		if not (key is String) and not (key is StringName):
			return false
		var key_string: String = str(key)
		if key_string.is_empty() \
				or key_string.length() > _MAX_METRIC_ATTRIBUTE_KEY_LENGTH \
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


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false


func _event_identity(event: ObservabilityEvent, p_signal: StringName) -> String:
	if p_signal == &"log":
		return JSON.stringify([String(event.source()), event.level(), event.message()])
	var exception_identity: Variant = null
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		exception_identity = [exception.type_name(), exception.message(), exception.stack_trace()]
	return JSON.stringify([
		String(event.kind()), String(event.source()), event.level(), event.message(), exception_identity,
	])


func _metric_identity(metric: ObservabilityMetric) -> String:
	return JSON.stringify([metric.type(), metric.name(), metric.unit()])


func _rule_index(redacted: Dictionary) -> int:
	var value: Variant = redacted.get("rule_index", -1)
	if value is int:
		return value
	return -1


func _now_msec(clock: Callable) -> int:
	var value: Variant = clock.call()
	return value if value is int else 0


func _frame_index(frame: Callable) -> int:
	var value: Variant = frame.call()
	return value if value is int else 0


func _copy_processors(source: Array[Callable]) -> Array[Callable]:
	var copied: Array[Callable] = []
	for processor: Callable in source:
		copied.append(processor)
	return copied
