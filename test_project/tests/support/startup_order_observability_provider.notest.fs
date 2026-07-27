namespace foundry.observability.tests

import foundry.observability

## Provider fixture that records startup lifecycle call ordering.
class_name StartupOrderObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var call_order: Array[StringName] = []
var _enabled: bool = false


func provider_name() -> StringName:
	call_order.append(&"provider_name")
	return &"startup_order"


func is_available() -> bool:
	call_order.append(&"is_available")
	return _enabled


func configure(config: ObservabilityConfig) -> int:
	call_order.append(&"configure")
	_enabled = config.enabled()
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	call_order.append(&"capture")
	return ""


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	call_order.append(&"capture_feedback")
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	call_order.append(&"flush")
	return Error.OK


func shutdown() -> void:
	call_order.append(&"shutdown")
	_enabled = false
