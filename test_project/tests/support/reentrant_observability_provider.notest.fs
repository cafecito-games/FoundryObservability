namespace foundry.observability.tests

import foundry.observability

## Provider fixture that emits an engine error while capturing an event.
class_name ReentrantObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var capture_count: int = 0
var _enabled: bool = false
var _shutdown: bool = false


func provider_name() -> StringName:
	return &"reentrant"


func is_available() -> bool:
	return true


func configure(config: ObservabilityConfig) -> int:
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	if not _enabled or _shutdown:
		return ""
	capture_count += 1
	push_error("provider recursion")
	return "reentrant:%s" % capture_count


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	_shutdown = true
