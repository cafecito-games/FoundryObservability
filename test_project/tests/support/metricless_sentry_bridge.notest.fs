namespace foundry.observability.sentry.tests

## Native bridge fixture that supports events and logs but not metrics.
class_name MetriclessSentryBridge
extends RefCounted

var available: bool = true
var _active_owner: String = ""
var _event_sequence: int = 0


func lifecycleVersion() -> int:
	return 1


func configure(payload: Dictionary) -> int:
	_active_owner = str(payload.get("lifecycle_owner", ""))
	return Error.OK


func isAvailable(owner: String) -> bool:
	return available and owner == _active_owner


func capture(_payload: Dictionary) -> String:
	_event_sequence += 1
	return "sentry:%s" % _event_sequence


func captureLog(_payload: Dictionary) -> String:
	return "sentry-log:1"


func captureFeedback(_payload: Dictionary) -> String:
	return "sentry-feedback:1"


func flush(_owner: String, _timeout_msec: int) -> int:
	return Error.OK


func shutdown(owner: String) -> void:
	if owner == _active_owner:
		_active_owner = ""
		available = false
