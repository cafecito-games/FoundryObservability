namespace foundry.observability.sentry.tests

import foundry.observability.sentry

class_name FakeSentryNativeBridge
extends RefCounted
uses SentryNativeBridge

var available: bool = true
var configure_result: int = Error.OK
var configure_results: Array[int] = []
var flush_result: int = Error.OK
var apply_scope_result: bool = true
var apply_scope_results: Array[bool] = []
var clear_breadcrumbs_result: bool = true
var replace_attachments_result: bool = true
var replace_attachments_results: Array[bool] = []
var scope_supported: bool = true
var breadcrumbs_supported: bool = true
var feedback_supported: bool = true
var metrics_supported: bool = true
var attachments_supported: bool = true
var logs_supported: bool = true
var core_supported: bool = true
var clear_breadcrumbs_count: int = 0
var configured_payload: Dictionary = {}
var configured_payloads: Array[Dictionary] = []
var applied_scope_payloads: Array[Dictionary] = []
var current_scope_payload: Dictionary = {
		"tags": {},
		"contexts": {},
	}
var captured_payloads: Array[Dictionary] = []
var captured_native_attachment_payloads: Array[Array] = []
var captured_log_payloads: Array[Dictionary] = []
var captured_breadcrumb_payloads: Array[Dictionary] = []
var current_breadcrumb_payloads: Array[Dictionary] = []
var replaced_attachment_payloads: Array[Array] = []
var current_attachment_payloads: Array[Dictionary] = []
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
var _active_configuration: Dictionary = {}


func contract_valid() -> bool:
	return true


func supports_core() -> bool:
	return core_supported


func lifecycle_version() -> int:
	return 1


func configure(payload: Dictionary) -> int:
	configured_payload = payload.duplicate(true)
	configured_payloads.append(configured_payload.duplicate(true))
	var result: int = configure_result
	if not configure_results.is_empty():
		result = configure_results.pop_front()
	var changed_configuration: bool = (
			not active_owner.is_empty()
			and configured_payload != _active_configuration
		)
	if result == Error.OK:
		var owner: String = str(payload.get("lifecycle_owner", ""))
		if payload.get("enabled", false):
			if active_owner.is_empty() or changed_configuration:
				current_scope_payload = {
					"tags": {},
					"contexts": {},
				}
				current_breadcrumb_payloads = []
			active_owner = owner
			_active_configuration = configured_payload.duplicate(true)
		elif owner == active_owner:
			active_owner = ""
			_active_configuration = {}
			current_scope_payload = {
				"tags": {},
				"contexts": {},
			}
			current_breadcrumb_payloads = []
	elif not active_owner.is_empty():
		current_scope_payload = {
			"tags": {},
			"contexts": {},
		}
		if changed_configuration:
			current_breadcrumb_payloads = []
	return result


func active_configuration() -> Dictionary:
	return _active_configuration.duplicate(true)


func is_available(owner: String) -> bool:
	if not available or owner.is_empty() or owner != active_owner:
		return false
	return true


func capture(payload: Dictionary) -> String:
	captured_payloads.append(payload.duplicate(true))
	captured_native_attachment_payloads.append(
			current_attachment_payloads.duplicate(true),
		)
	var event_id: String = "sentry:%s" % next_event_id
	next_event_id += 1
	return event_id


func capture_with_attachments(payload: Dictionary) -> String:
	if not attachments_supported:
		return ""
	return capture(payload)


func supports_logs() -> bool:
	return logs_supported


func capture_log(payload: Dictionary) -> String:
	if not logs_supported:
		return ""
	captured_log_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry-log:%s" % next_log_id
	next_log_id += 1
	return event_id


func supports_scope() -> bool:
	return scope_supported


func apply_scope(payload: Dictionary) -> bool:
	if not scope_supported:
		return false
	applied_scope_payloads.append(payload.duplicate(true))
	var result: bool = apply_scope_result
	if not apply_scope_results.is_empty():
		result = apply_scope_results.pop_front()
	if result:
		current_scope_payload = payload.duplicate(true)
	return result


func supports_breadcrumbs() -> bool:
	return breadcrumbs_supported


func capture_breadcrumb(payload: Dictionary) -> bool:
	if not breadcrumbs_supported:
		return false
	captured_breadcrumb_payloads.append(payload.duplicate(true))
	current_breadcrumb_payloads.append(payload.duplicate(true))
	return true


func clear_breadcrumbs() -> bool:
	if not breadcrumbs_supported:
		return false
	clear_breadcrumbs_count += 1
	if clear_breadcrumbs_result:
		current_breadcrumb_payloads = []
	return clear_breadcrumbs_result


func supports_feedback() -> bool:
	return feedback_supported


func capture_feedback(payload: Dictionary) -> String:
	if not feedback_supported:
		return ""
	captured_feedback_payloads.append(payload.duplicate(true))
	var feedback_id: String = "sentry-feedback:%s" % next_feedback_id
	next_feedback_id += 1
	return feedback_id


func supports_metrics() -> bool:
	return metrics_supported


func capture_metric(payload: Dictionary) -> bool:
	if not metrics_supported:
		return false
	captured_metric_payloads.append(payload.duplicate(true))
	return true


func supports_attachments() -> bool:
	return attachments_supported


func replace_attachments(payloads: Array[Dictionary]) -> bool:
	if not attachments_supported:
		return false
	var snapshot: Array[Dictionary] = payloads.duplicate(true)
	replaced_attachment_payloads.append(snapshot)
	var result: bool = replace_attachments_result
	if not replace_attachments_results.is_empty():
		result = replace_attachments_results.pop_front()
	if result:
		current_attachment_payloads = snapshot.duplicate(true)
	return result


func flush(owner: String, timeout_msec: int) -> int:
	flush_owners.append(owner)
	flush_timeouts.append(timeout_msec)
	return flush_result


func shutdown(owner: String) -> void:
	shutdown_owners.append(owner)
	if owner == active_owner:
		active_owner = ""
		_active_configuration = {}
		current_scope_payload = {
			"tags": {},
			"contexts": {},
		}
		current_breadcrumb_payloads = []
		current_attachment_payloads = []
		shutdown_count += 1
