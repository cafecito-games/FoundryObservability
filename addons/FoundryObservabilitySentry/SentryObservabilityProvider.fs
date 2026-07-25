namespace foundry.observability.sentry

import foundry.observability

## FoundryScript adapter for the optional cross-platform Sentry native bridge.
class_name SentryObservabilityProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityMetricsProvider, ObservabilityBreadcrumbsProvider

const _NATIVE_CLASS: String = "SentryObservabilityBridge"
const _LIFECYCLE_VERSION: int = 1

var _bridge: Object? = null
var _context_collector: SentryRuntimeContextCollector
var _stable_contexts: Dictionary = {}
var _enabled: bool = false
var _owner: String = ""
var _shutdown: bool = false


## Creates a provider with an optional bridge seam used by deterministic tests.
func _init(
		p_bridge: Object? = null,
		p_runtime_context_probe: Object? = null,
) -> void:
	_bridge = p_bridge
	var runtime_context_probe: Object = (
			p_runtime_context_probe
			if p_runtime_context_probe != null
			else SentryRuntimeContextProbe.new()
		)
	_context_collector = SentryRuntimeContextCollector.new(runtime_context_probe)
	_owner = str(get_instance_id())


## Returns the stable Sentry provider identifier.
func provider_name() -> StringName:
	return &"sentry"


## Returns whether the configured native bridge can currently accept events.
func is_available() -> bool:
	if not _enabled or _shutdown:
		return false
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not _has_lifecycle_contract(bridge):
		return false
	return bridge.call("isAvailable", _owner) == true


## Validates and forwards the complete provider configuration.
func configure(config: ObservabilityConfig) -> int:
	var options: Dictionary = config.provider_options()
	var dsn: String = str(options.get("dsn", ""))
	if config.enabled and dsn.is_empty():
		return Error.FAILED

	var bridge: Object? = _resolve_bridge()
	if config.enabled and bridge == null:
		return Error.ERR_UNAVAILABLE
	if bridge != null and not _has_lifecycle_contract(bridge):
		if config.enabled or _enabled:
			return Error.ERR_UNAVAILABLE
		_enabled = false
		_stable_contexts = {}
		_shutdown = false
		return Error.OK
	if config.enabled and config.logs_enabled and (bridge == null or not bridge.has_method("captureLog")):
		return Error.FAILED

	_shutdown = false
	if bridge == null:
		_enabled = false
		_stable_contexts = {}
		return Error.OK

	var candidate_stable_contexts: Dictionary = {}
	if config.enabled:
		var send_default_pii: bool = false
		var send_default_pii_value: Variant = options.get("send_default_pii")
		if send_default_pii_value is bool:
			send_default_pii = send_default_pii_value
		candidate_stable_contexts = _context_collector.stable_contexts(
				config.environment,
				send_default_pii,
			)
	var payload: Dictionary = {
			"enabled": config.enabled,
			"dsn": dsn,
			"environment": config.environment,
			"release": config.release,
			"dist": config.dist,
			"global_attributes": config.global_attributes(),
			"provider_options": options,
			"logs_enabled": config.logs_enabled,
			"log_minimum_level": config.log_minimum_level,
			"log_rate_limit_per_second": config.log_rate_limit_per_second,
			"metrics_enabled": config.metrics_enabled,
			"application_hang_detection_enabled":
					config.application_hang_detection_enabled,
			"application_hang_timeout_msec":
					maxi(1000, config.application_hang_timeout_msec),
			"android_anr_detection_enabled": config.android_anr_detection_enabled,
			"android_anr_timeout_msec": maxi(1000, config.android_anr_timeout_msec),
			"android_anr_attach_thread_dump": config.android_anr_attach_thread_dump,
			"lifecycle_owner": _owner,
		}
	if config.enabled:
		payload["stable_contexts"] = candidate_stable_contexts
	var result: Variant = bridge.call("configure", payload)
	if not (result is int):
		return Error.FAILED
	var result_code: int = result
	if result_code == Error.OK:
		_enabled = config.enabled
		_stable_contexts = candidate_stable_contexts
	return result_code


## Translates one normalized event and returns the native provider ID.
func capture(event: ObservabilityEvent) -> String:
	if event == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available():
		return ""

	var payload: Dictionary = {
			"kind": String(event.kind()),
			"level": event.level(),
			"message": event.message(),
			"source": String(event.source()),
			"timestamp_msec": event.timestamp_msec(),
			"engine_ticks_msec": event.engine_ticks_msec(),
			"attributes": event.attributes(),
		}
	var contexts: Dictionary = _context_collector.contexts_for_capture(_stable_contexts)
	if not contexts.is_empty():
		payload["contexts"] = contexts
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		var exception_payload: Dictionary = {
				"type_name": exception.type_name(),
				"message": exception.message(),
				"stack_trace": exception.stack_trace(),
				"attributes": exception.attributes(),
			}
		var frames: Array = []
		for frame: ObservabilityStackFrame in exception.frames():
			if frame == null:
				continue
			frames.append(_stack_frame_payload(frame))
		if not frames.is_empty():
			exception_payload["frames"] = frames
		payload["exception"] = exception_payload
	var method: String = "capture"
	if event.kind() == &"log":
		method = "captureLog"
		if not bridge.has_method(method):
			return ""
	return str(bridge.call(method, payload))


## Translates one normalized breadcrumb to the optional native breadcrumb API.
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	if breadcrumb == null or not _enabled or _shutdown:
		return false

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureBreadcrumb"):
		return false

	var result: Variant = bridge.call("captureBreadcrumb", {
			"message": breadcrumb.message(),
			"level": breadcrumb.level(),
			"category": String(breadcrumb.category()),
			"timestamp_msec": breadcrumb.timestamp_msec(),
			"attributes": breadcrumb.attributes(),
		})
	if not (result is bool):
		return false
	return result


## Translates explicit feedback to the native dedicated feedback API.
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if feedback == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureFeedback"):
		return ""

	var payload: Dictionary = {"message": feedback.message()}
	if not feedback.name().is_empty():
		payload["name"] = feedback.name()
	if not feedback.contact_email().is_empty():
		payload["contact_email"] = feedback.contact_email()
	if not feedback.associated_event_id().is_empty():
		payload["associated_event_id"] = feedback.associated_event_id()
	return str(bridge.call("captureFeedback", payload))


## Translates one normalized custom metric to the optional native metrics API.
func capture_metric(metric: ObservabilityMetric) -> bool:
	if metric == null or not _enabled or _shutdown:
		return false

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureMetric"):
		return false

	var result: Variant = bridge.call("captureMetric", {
			"type": metric.type(),
			"name": metric.name(),
			"value": metric.value(),
			"unit": metric.unit(),
			"attributes": metric.attributes(),
		})
	if not (result is bool):
		return false
	return result


## Flushes native Sentry work within the requested timeout.
func flush(timeout_msec: int = 2000) -> int:
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not _enabled or _shutdown or not is_available():
		return Error.OK
	var result: Variant = bridge.call("flush", _owner, timeout_msec)
	if not (result is int):
		return Error.FAILED
	return result


## Shuts down the native bridge once.
func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	_enabled = false
	_stable_contexts = {}
	var bridge: Object? = _resolve_bridge()
	if bridge != null and _has_lifecycle_contract(bridge):
		bridge.call("shutdown", _owner)


func _resolve_bridge() -> Object?:
	if _bridge != null:
		return _bridge
	if Engine.has_singleton(_NATIVE_CLASS):
		_bridge = Engine.get_singleton(_NATIVE_CLASS)
		return _bridge
	if not ClassDB.class_exists(_NATIVE_CLASS) or not ClassDB.can_instantiate(_NATIVE_CLASS):
		return null
	_bridge = ClassDB.instantiate(_NATIVE_CLASS)
	return _bridge


func _has_lifecycle_contract(bridge: Object) -> bool:
	for method: String in [
		"lifecycleVersion",
		"configure",
		"isAvailable",
		"capture",
		"flush",
		"shutdown",
	]:
		if not bridge.has_method(method):
			return false
	var version_result: Variant = bridge.call("lifecycleVersion")
	return version_result is int and version_result >= _LIFECYCLE_VERSION


func _stack_frame_payload(frame: ObservabilityStackFrame) -> Dictionary:
	var payload: Dictionary = {
			"file": frame.file(),
			"function": frame.function(),
			"line": frame.line(),
			"language": frame.language(),
			"in_app": frame.in_app(),
		}
	var context_line: String = frame.context_line()
	if not context_line.is_empty():
		payload["context_line"] = context_line
	var pre_context: PackedStringArray = frame.pre_context()
	if not pre_context.is_empty():
		payload["pre_context"] = Array(pre_context)
	var post_context: PackedStringArray = frame.post_context()
	if not post_context.is_empty():
		payload["post_context"] = Array(post_context)
	var variables: Dictionary = frame.variables()
	if not variables.is_empty():
		payload["variables"] = variables
	return payload
