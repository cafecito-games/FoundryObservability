namespace foundry.observability.tests

import foundry.observability

## Event-capable provider fixture that intentionally implements only seven scope methods.
class_name PartialScopeObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var capture_count: int = 0
var scope_call_count: int = 0
var _enabled: bool = false
var _shutdown: bool = false


func provider_name() -> StringName:
	return &"partial-scope"


func is_available() -> bool:
	return _enabled and not _shutdown


func configure(config: ObservabilityConfig) -> int:
	_enabled = config.enabled()
	_shutdown = false
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	if not is_available():
		return ""
	capture_count += 1
	return "partial-scope:%s" % capture_count


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


func set_tag(_key: String, _value: String) -> bool:
	scope_call_count += 1
	return true


func remove_tag(_key: String) -> bool:
	scope_call_count += 1
	return true


func clear_tags() -> bool:
	scope_call_count += 1
	return true


func set_context(_name: String, _value: Dictionary) -> bool:
	scope_call_count += 1
	return true


func remove_context(_name: String) -> bool:
	scope_call_count += 1
	return true


func clear_contexts() -> bool:
	scope_call_count += 1
	return true


func set_user(_user: ObservabilityUser) -> bool:
	scope_call_count += 1
	return true


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	_shutdown = true
	_enabled = false
