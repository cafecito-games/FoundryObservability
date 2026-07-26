namespace foundry.observability.sentry.tests

class_name FakeSentryBridge
extends RefCounted

var available: bool = true
var availability_result: Variant = true
var configure_result: Variant = Error.OK
var configure_results: Array[Variant] = []
var malformed_configure_mutates_session: bool = false
var flush_result: int = Error.OK
var apply_scope_result: bool = true
var apply_scope_results: Array[bool] = []
var clear_breadcrumbs_result: Variant = true
var replace_attachments_result: Variant = true
var replace_attachments_results: Array[Variant] = []
var malformed_clear_mutates_trail: bool = false
var clear_breadcrumbs_count: int = 0
var configured_payload: Dictionary = {}
var configured_payloads: Array[Dictionary] = []
var applied_scope_payloads: Array[Dictionary] = []
var current_scope_payload: Dictionary = {
		"tags": {},
		"contexts": {},
	}
var captured_payloads: Array[Dictionary] = []
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


func lifecycleVersion() -> int:
	return 1


func configure(payload: Dictionary) -> Variant:
	configured_payload = payload.duplicate(true)
	configured_payloads.append(configured_payload.duplicate(true))
	var result: Variant = configure_result
	if not configure_results.is_empty():
		result = configure_results.pop_front()
	var changed_configuration: bool = (
			not active_owner.is_empty()
			and configured_payload != _active_configuration
		)
	if not (result is int) and malformed_configure_mutates_session:
		var malformed_owner: String = str(payload.get("lifecycle_owner", ""))
		if payload.get("enabled", false):
			active_owner = malformed_owner
			_active_configuration = configured_payload.duplicate(true)
		elif malformed_owner == active_owner:
			active_owner = ""
			_active_configuration = {}
		current_scope_payload = {
			"tags": {},
			"contexts": {},
		}
		current_breadcrumb_payloads = []
	elif result is int and result == Error.OK:
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


func isAvailable(owner: String) -> Variant:
	if not available or owner.is_empty() or owner != active_owner:
		return false
	return availability_result


func capture(payload: Dictionary) -> String:
	captured_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry:%s" % next_event_id
	next_event_id += 1
	return event_id


func captureWithAttachments(payload: Dictionary) -> String:
	return capture(payload)


func captureLog(payload: Dictionary) -> String:
	captured_log_payloads.append(payload.duplicate(true))
	var event_id: String = "sentry-log:%s" % next_log_id
	next_log_id += 1
	return event_id


func captureBreadcrumb(payload: Dictionary) -> bool:
	captured_breadcrumb_payloads.append(payload.duplicate(true))
	current_breadcrumb_payloads.append(payload.duplicate(true))
	return true


func applyScope(payload: Dictionary) -> bool:
	applied_scope_payloads.append(payload.duplicate(true))
	var result: bool = apply_scope_result
	if not apply_scope_results.is_empty():
		result = apply_scope_results.pop_front()
	if result:
		current_scope_payload = payload.duplicate(true)
	return result


func clearBreadcrumbs() -> Variant:
	clear_breadcrumbs_count += 1
	if (clear_breadcrumbs_result is bool and clear_breadcrumbs_result == true) \
			or (not (clear_breadcrumbs_result is bool) and malformed_clear_mutates_trail):
		current_breadcrumb_payloads = []
	return clear_breadcrumbs_result


func replaceAttachments(payloads: Array) -> Variant:
	var snapshot: Array = payloads.duplicate(true)
	replaced_attachment_payloads.append(snapshot)
	var result: Variant = replace_attachments_result
	if not replace_attachments_results.is_empty():
		result = replace_attachments_results.pop_front()
	if result is bool and result == true:
		current_attachment_payloads = snapshot.duplicate(true)
	return result


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
		_active_configuration = {}
		current_scope_payload = {
			"tags": {},
			"contexts": {},
		}
		current_breadcrumb_payloads = []
		current_attachment_payloads = []
		shutdown_count += 1
