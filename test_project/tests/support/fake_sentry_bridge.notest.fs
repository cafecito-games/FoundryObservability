namespace foundry.observability.sentry.tests

class_name FakeSentryBridge
extends RefCounted

var available: bool = true
var configure_result: int = Error.OK
var flush_result: int = Error.OK
var configured_payload: Dictionary = {}
var captured_payloads: Array[Dictionary] = []
var flush_timeouts: Array[int] = []
var shutdown_count: int = 0
var next_event_id: int = 1


func configure(payload: Dictionary) -> int:
	configured_payload = payload.duplicate(true)
	return configure_result


func isAvailable() -> bool:
	return available


func capture(payload: Dictionary) -> String:
	captured_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry:%s" % next_event_id
	next_event_id += 1
	return event_id


func flush(timeout_msec: int) -> int:
	flush_timeouts.append(timeout_msec)
	return flush_result


func shutdown() -> void:
	shutdown_count += 1
