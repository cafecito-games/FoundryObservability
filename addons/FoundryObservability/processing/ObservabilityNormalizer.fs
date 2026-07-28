namespace foundry.observability.processing

import foundry.observability
import foundry.observability.runtime

## Stateless validator and canonical value builder for public observability inputs.
final class_name ObservabilityNormalizer extends RefCounted

const MAX_FEEDBACK_MESSAGE_LENGTH: int = 4096
const MAX_METRIC_NAME_LENGTH: int = 200
const MAX_METRIC_UNIT_LENGTH: int = 64
const MAX_METRIC_ATTRIBUTE_KEY_LENGTH: int = 200

final var _runtime: ObservabilityRuntime


func _init(runtime: ObservabilityRuntime) -> void:
	assert(runtime != null, "ObservabilityNormalizer requires a runtime.")
	_runtime = runtime


func normalize_event(
		event: ObservabilityEvent,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityEvent]:
	if event == null or config == null:
		return ObservabilityNormalizationResult[ObservabilityEvent].failure(
				Error.ERR_INVALID_PARAMETER,
			)
	var capture_engine_ticks_msec: int = _runtime.monotonic_time_msec()
	var capture_unix_msec: int = _runtime.unix_time_msec()
	var resolved: ObservabilityEvent = _resolved_event_timestamp(
			event,
			capture_unix_msec,
			capture_engine_ticks_msec,
		)
	return ObservabilityNormalizationResult[ObservabilityEvent].success(
			_normalized_exception_event(resolved, config),
		)


func normalize_metric(
		metric: ObservabilityMetric,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]:
	var normalized: ObservabilityMetric? = _normalized_metric(metric, config)
	if normalized == null:
		return ObservabilityNormalizationResult[ObservabilityMetric].failure(
				Error.ERR_INVALID_PARAMETER,
			)
	return ObservabilityNormalizationResult[ObservabilityMetric].success(normalized)


func normalize_feedback(
		feedback: ObservabilityFeedback,
) -> ObservabilityNormalizationResult[ObservabilityFeedback]:
	if not _is_valid_feedback(feedback):
		return ObservabilityNormalizationResult[ObservabilityFeedback].failure(
				Error.ERR_INVALID_PARAMETER,
			)
	return ObservabilityNormalizationResult[ObservabilityFeedback].success(feedback)


func counter(
		metric_name: String,
		value: int,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]:
	return normalize_metric(
			ObservabilityMetric.new(
					p_type = ObservabilityMetricType.COUNTER,
					p_name = metric_name,
					p_value = float(value),
					p_attributes = attributes,
				),
			config,
		)


func gauge(
		metric_name: String,
		value: float,
		unit: String,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]:
	return normalize_metric(
			ObservabilityMetric.new(
					p_type = ObservabilityMetricType.GAUGE,
					p_name = metric_name,
					p_value = value,
					p_unit = unit,
					p_attributes = attributes,
				),
			config,
		)


func distribution(
		metric_name: String,
		value: float,
		unit: String,
		attributes: Dictionary,
		config: ObservabilityConfig,
) -> ObservabilityNormalizationResult[ObservabilityMetric]:
	return normalize_metric(
			ObservabilityMetric.new(
					p_type = ObservabilityMetricType.DISTRIBUTION,
					p_name = metric_name,
					p_value = value,
					p_unit = unit,
					p_attributes = attributes,
				),
			config,
		)


static func _unix_msec_from_engine_ticks(
		event_engine_ticks_msec: int,
		capture_engine_ticks_msec: int,
		capture_unix_msec: int,
) -> int:
	return capture_unix_msec + event_engine_ticks_msec - capture_engine_ticks_msec


func _resolved_event_timestamp(
		event: ObservabilityEvent,
		capture_unix_msec: int,
		capture_engine_ticks_msec: int,
) -> ObservabilityEvent:
	if event.timestamp_msec() >= 0:
		return event
	var resolved_unix_msec: int = capture_unix_msec
	var resolved_engine_ticks_msec: int = event.engine_ticks_msec()
	if resolved_engine_ticks_msec >= 0:
		resolved_unix_msec = _unix_msec_from_engine_ticks(
				resolved_engine_ticks_msec,
				capture_engine_ticks_msec,
				capture_unix_msec,
			)
	else:
		resolved_engine_ticks_msec = capture_engine_ticks_msec
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = resolved_unix_msec,
			p_attributes = event.attributes(),
			p_exception = event.exception(),
			p_engine_ticks_msec = resolved_engine_ticks_msec,
			p_scope = event.scope(),
		)


func _normalized_metric(
		metric: ObservabilityMetric,
		config: ObservabilityConfig,
) -> ObservabilityMetric?:
	if metric == null or config == null or not _is_valid_metric_name(metric.name()):
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
	var global_attributes: Dictionary = config.global_attributes()
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


func _normalized_exception_event(
		event: ObservabilityEvent,
		config: ObservabilityConfig,
) -> ObservabilityEvent:
	var exception: ObservabilityException? = event.exception()
	if exception == null:
		return event
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = event.timestamp_msec(),
			p_attributes = event.attributes(),
			p_exception = _normalized_exception(exception, config),
			p_engine_ticks_msec = event.engine_ticks_msec(),
			p_scope = event.scope(),
		)


func _normalized_exception(
		exception: ObservabilityException,
		config: ObservabilityConfig,
) -> ObservabilityException:
	var normalized_frames: Array[ObservabilityStackFrame] = []
	for frame: ObservabilityStackFrame in exception.frames():
		if frame == null or not _is_useful_stack_frame(frame):
			continue
		normalized_frames.append(_normalized_stack_frame(frame, config))
	return ObservabilityException.new(
			p_type_name = exception.type_name(),
			p_message = exception.message(),
			p_stack_trace = exception.stack_trace(),
			p_attributes = exception.attributes(),
			p_frames = normalized_frames,
		)


func _is_useful_stack_frame(frame: ObservabilityStackFrame) -> bool:
	return not frame.file().is_empty() \
			or not frame.function().is_empty() \
			or not frame.language().is_empty() \
			or frame.line() >= 1


func _normalized_stack_frame(
		frame: ObservabilityStackFrame,
		config: ObservabilityConfig,
) -> ObservabilityStackFrame:
	var line: int = frame.line()
	if line < 1:
		line = -1

	var context_line: String = ""
	var pre_context: PackedStringArray = PackedStringArray()
	var post_context: PackedStringArray = PackedStringArray()
	if config.stack_traces().source_context_enabled():
		context_line = frame.context_line()
		if not context_line.is_empty():
			var source_pre_context: PackedStringArray = frame.pre_context()
			for index: int in range(maxi(0, source_pre_context.size() - 5), source_pre_context.size()):
				pre_context.append(source_pre_context[index])
			var source_post_context: PackedStringArray = frame.post_context()
			for index: int in range(mini(5, source_post_context.size())):
				post_context.append(source_post_context[index])

	var variables: Dictionary = {}
	if config.stack_traces().variables_enabled():
		variables = frame._bounded_sanitized_variables(
				ObservabilityStackFrame.MAX_VARIABLE_CONTAINER_DEPTH,
				ObservabilityStackFrame.MAX_VARIABLE_ITEMS,
			)
	return ObservabilityStackFrame.new(
			p_file = frame.file(),
			p_function = frame.function(),
			p_line = line,
			p_language = frame.language(),
			p_in_app = frame.in_app(),
			p_context_line = context_line,
			p_pre_context = pre_context,
			p_post_context = post_context,
			p_variables = variables,
		)


func _is_valid_feedback(feedback: ObservabilityFeedback) -> bool:
	if feedback == null:
		return false
	var message: String = feedback.message()
	if message.strip_edges().is_empty() \
			or message.length() > MAX_FEEDBACK_MESSAGE_LENGTH \
			or _has_control_character(message):
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
	if not _is_valid_optional_text(email) or email.find(" ") >= 0:
		return false
	var at_index: int = email.find("@")
	return at_index > 0 and at_index < email.length() - 1 and at_index == email.rfind("@")


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false
