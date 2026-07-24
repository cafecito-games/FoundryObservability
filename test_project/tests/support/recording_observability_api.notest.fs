namespace foundry.observability.tests

import foundry.observability

class_name RecordingObservabilityApi
extends RefCounted
uses FoundryObservabilityApi

var captured_events: Array[ObservabilityEvent] = []
var captured_logs: Array[Dictionary] = []
var captured_feedback: Array[ObservabilityFeedback] = []


func configure(_provider: ObservabilityProvider, _config: ObservabilityConfig? = null) -> int:
	return Error.OK


func is_enabled() -> bool:
	return true


func is_available() -> bool:
	return true


func provider_name() -> StringName:
	return &"recording"


func last_error() -> int:
	return Error.OK


func capture_event(event: ObservabilityEvent) -> String:
	captured_events.append(event)
	return "event:1"


func capture_message(
		_message: String,
		_level: int = ObservabilityLevel.INFO,
		_attributes: Dictionary = {},
) -> String:
	return "message:1"


func capture_exception(
		_exception: ObservabilityException,
		_attributes: Dictionary = {},
) -> String:
	return "exception:1"


func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
) -> String:
	captured_logs.append({
			"message": message,
			"level": level,
			"source": source,
			"timestamp_msec": timestamp_msec,
			"attributes": attributes.duplicate(true),
		})
	return "log:1"


func capture_feedback(feedback: ObservabilityFeedback) -> String:
	captured_feedback.append(feedback)
	return "feedback:1"


func capture_metric(_metric: ObservabilityMetric) -> bool:
	return true


func capture_counter(
		_metric_name: String,
		_value: int = 1,
		_attributes: Dictionary = {},
) -> bool:
	return true


func capture_gauge(
		_metric_name: String,
		_value: float,
		_unit: String = "",
		_attributes: Dictionary = {},
) -> bool:
	return true


func capture_distribution(
		_metric_name: String,
		_value: float,
		_unit: String = "",
		_attributes: Dictionary = {},
) -> bool:
	return true


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	pass
