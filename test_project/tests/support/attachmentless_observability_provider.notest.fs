namespace foundry.observability.tests

import foundry.observability

## Event-capable provider fixture that intentionally omits diagnostic attachments.
class_name AttachmentlessObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var capture_count: int = 0
var _enabled: bool = false
var _shutdown: bool = false


func provider_name() -> StringName:
	return &"attachmentless"


func is_available() -> bool:
	return _enabled and not _shutdown


func configure(config: ObservabilityConfig) -> int:
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	if not is_available():
		return ""
	capture_count += 1
	return "attachmentless:%s" % capture_count


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	_shutdown = true
	_enabled = false
