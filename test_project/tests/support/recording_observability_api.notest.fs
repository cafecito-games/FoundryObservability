namespace foundry.observability.tests

import foundry.observability

class_name RecordingObservabilityApi
extends RefCounted
uses FoundryObservabilityApi

var captured_events: Array[ObservabilityEvent] = []
var captured_logs: Array[Dictionary] = []
var captured_breadcrumbs: Array[ObservabilityBreadcrumb] = []
var captured_feedback: Array[ObservabilityFeedback] = []


func configure(_provider: ObservabilityProvider, _config: ObservabilityConfig? = null) -> int:
	return Error.OK


func initialize_from_project_settings() -> int:
	return Error.OK


func startup_status() -> StringName:
	return ObservabilityStartupStatus.INITIALIZED


func startup_message() -> String:
	return "recording startup initialized"


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
		_scope: ObservabilityScope? = null,
) -> String:
	return "message:1"


func capture_exception(
		_exception: ObservabilityException,
		_attributes: Dictionary = {},
		_scope: ObservabilityScope? = null,
) -> String:
	return "exception:1"


func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
		engine_ticks_msec: int = -1,
		_scope: ObservabilityScope? = null,
) -> String:
	captured_logs.append({
			"message": message,
			"level": level,
			"source": source,
			"timestamp_msec": timestamp_msec,
			"attributes": attributes.duplicate(true),
			"engine_ticks_msec": engine_ticks_msec,
		})
	return "log:1"


func set_tag(_key: String, _value: String) -> bool:
	return true


func remove_tag(_key: String) -> bool:
	return true


func clear_tags() -> bool:
	return true


func set_context(_name: String, _value: Dictionary) -> bool:
	return true


func remove_context(_name: String) -> bool:
	return true


func clear_contexts() -> bool:
	return true


func set_user(_user: ObservabilityUser) -> bool:
	return true


func remove_user() -> bool:
	return true


func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	captured_breadcrumbs.append(breadcrumb)
	return true


func clear_breadcrumbs() -> bool:
	captured_breadcrumbs.clear()
	return true


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
