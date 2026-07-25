namespace foundry.observability.sentry.tests

class_name EventOnlySentryBridge
extends RefCounted

var _active_owner: String = ""


func lifecycleVersion() -> int:
	return 1


func configure(payload: Dictionary) -> int:
	_active_owner = str(payload.get("lifecycle_owner", ""))
	return Error.OK


func isAvailable(owner: String) -> bool:
	return owner == _active_owner


func capture(_payload: Dictionary) -> String:
	return "sentry:1"


func captureFeedback(_payload: Dictionary) -> String:
	return "sentry-feedback:1"


func flush(_owner: String, _timeout_msec: int) -> int:
	return Error.OK


func shutdown(owner: String) -> void:
	if owner == _active_owner:
		_active_owner = ""
