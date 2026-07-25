namespace foundry.observability.sentry.tests

class_name BreadcrumblessSentryBridge
extends RefCounted


func configure(_payload: Dictionary) -> int:
	return Error.OK


func isAvailable() -> bool:
	return true


func capture(_payload: Dictionary) -> String:
	return "sentry:1"


func captureLog(_payload: Dictionary) -> String:
	return "sentry-log:1"


func captureFeedback(_payload: Dictionary) -> String:
	return "sentry-feedback:1"


func flush(_timeout_msec: int) -> int:
	return Error.OK


func shutdown() -> void:
	pass
