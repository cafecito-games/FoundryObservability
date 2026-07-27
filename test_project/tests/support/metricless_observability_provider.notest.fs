namespace foundry.observability.tests

import foundry.observability

## Event-capable provider fixture that intentionally omits custom metrics.
class_name MetriclessObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var _enabled: bool = false
var _shutdown: bool = false
var _event_sequence: int = 0


func provider_name() -> StringName:
	return &"metricless"


func is_available() -> bool:
	return _enabled and not _shutdown


func configure(config: ObservabilityConfig) -> int:
	_enabled = config.enabled()
	_shutdown = false
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	if not is_available():
		return ""
	_event_sequence += 1
	return "metricless:%s" % _event_sequence


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	_shutdown = true
	_enabled = false
