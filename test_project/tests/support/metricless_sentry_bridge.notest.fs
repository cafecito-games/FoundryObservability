namespace foundry.observability.sentry.tests

## Native bridge fixture that supports events and logs but not metrics.
class_name MetriclessSentryBridge
extends RefCounted

var available: bool = true
var _event_sequence: int = 0


func configure(_payload: Dictionary) -> int:
	return Error.OK


func isAvailable() -> bool:
	return available


func capture(_payload: Dictionary) -> String:
	_event_sequence += 1
	return "sentry:%s" % _event_sequence


func captureLog(_payload: Dictionary) -> String:
	return "sentry-log:1"


func captureFeedback(_payload: Dictionary) -> String:
	return "sentry-feedback:1"


func flush(_timeout_msec: int) -> int:
	return Error.OK


func shutdown() -> void:
	available = false
