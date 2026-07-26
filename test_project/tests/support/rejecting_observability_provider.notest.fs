namespace foundry.observability.tests

import foundry.observability

## Provider fixture that observes automatic delivery attempts but rejects them.
class_name RejectingObservabilityProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityBreadcrumbsProvider

var capture_count: int = 0
var breadcrumb_count: int = 0
var event_capture_result: bool = false
var breadcrumb_capture_result: bool = false
var _enabled: bool = false
var _shutdown: bool = false


func provider_name() -> StringName:
	return &"rejecting"


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
	if event_capture_result:
		return "rejecting:%s" % capture_count
	return ""


func capture_breadcrumb(_breadcrumb: ObservabilityBreadcrumb) -> bool:
	if not is_available():
		return false
	breadcrumb_count += 1
	return breadcrumb_capture_result


func clear_breadcrumbs() -> bool:
	return is_available()


func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	_shutdown = true
	_enabled = false
