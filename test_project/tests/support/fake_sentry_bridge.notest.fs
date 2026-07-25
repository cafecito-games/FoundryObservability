namespace foundry.observability.sentry.tests

class_name FakeSentryBridge
extends RefCounted

var available: bool = true
var configure_result: int = Error.OK
var flush_result: int = Error.OK
var configured_payload: Dictionary = {}
var configured_payloads: Array[Dictionary] = []
var captured_payloads: Array[Dictionary] = []
var captured_log_payloads: Array[Dictionary] = []
var captured_breadcrumb_payloads: Array[Dictionary] = []
var captured_feedback_payloads: Array[Dictionary] = []
var captured_metric_payloads: Array[Dictionary] = []
var active_owner: String = ""
var flush_owners: Array[String] = []
var flush_timeouts: Array[int] = []
var shutdown_owners: Array[String] = []
var shutdown_count: int = 0
var next_event_id: int = 1
var next_log_id: int = 1
var next_feedback_id: int = 1


func lifecycleVersion() -> int:
	return 1


func configure(payload: Dictionary) -> int:
	configured_payload = payload.duplicate(true)
	configured_payloads.append(configured_payload)
	if configure_result == Error.OK:
		var owner: String = str(payload.get("lifecycle_owner", ""))
		if payload.get("enabled", false):
			active_owner = owner
		elif owner == active_owner:
			active_owner = ""
	return configure_result


func isAvailable(owner: String) -> bool:
	return available and not owner.is_empty() and owner == active_owner


func capture(payload: Dictionary) -> String:
	captured_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry:%s" % next_event_id
	next_event_id += 1
	return event_id


func captureLog(payload: Dictionary) -> String:
	captured_log_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry-log:%s" % next_log_id
	next_log_id += 1
	return event_id


func captureBreadcrumb(payload: Dictionary) -> bool:
	captured_breadcrumb_payloads.append(payload.duplicate(true))
	return true


func captureFeedback(payload: Dictionary) -> String:
	captured_feedback_payloads.append(payload.duplicate(true))
	var feedback_id: String = "sentry-feedback:%s" % next_feedback_id
	next_feedback_id += 1
	return feedback_id


func captureMetric(payload: Dictionary) -> bool:
	captured_metric_payloads.append(payload.duplicate(true))
	return true


func flush(owner: String, timeout_msec: int) -> int:
	flush_owners.append(owner)
	flush_timeouts.append(timeout_msec)
	return flush_result


func shutdown(owner: String) -> void:
	shutdown_owners.append(owner)
	if owner == active_owner:
		active_owner = ""
		shutdown_count += 1
