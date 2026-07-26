namespace foundry.observability.sentry

import foundry.observability

## FoundryScript adapter for the optional cross-platform Sentry native bridge.
class_name SentryObservabilityProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityMetricsProvider, ObservabilityBreadcrumbsProvider, ObservabilityScopeProvider, ObservabilityAttachmentsProvider

const _NATIVE_CLASS: String = "SentryObservabilityBridge"
const _LIFECYCLE_VERSION: int = 1
const DEFAULT_MAX_ATTACHMENT_BYTES: int = 20 * 1024 * 1024

var _bridge: Object? = null
var _context_collector: SentryRuntimeContextCollector
var _attachment_collector: SentryBuiltInAttachmentCollector
var _stable_contexts: Dictionary = {}
var _scope: ObservabilityScope = ObservabilityScope.new()
var _user: ObservabilityUser? = null
var _last_config_payload: Dictionary = {}
var _has_last_config_payload: bool = false
var _enabled: bool = false
var _owner: String = ""
var _shutdown: bool = false
var _attachments: Dictionary = {}
var _attachment_sequence: int = 0
var _last_attachment_failures: Array[ObservabilityAttachmentFailure] = []
var _persistent_builtin_attachments: Array[Dictionary] = []
var _native_attachment_payloads: Array[Dictionary] = []
var _attachment_config: ObservabilityConfig


## Creates a provider with an optional bridge seam used by deterministic tests.
func _init(
		p_bridge: Object? = null,
		p_runtime_context_probe: Object? = null,
		p_attachment_runtime_probe: Object? = null,
) -> void:
	_bridge = p_bridge
	var runtime_context_probe: Object = (
			p_runtime_context_probe
			if p_runtime_context_probe != null
			else SentryRuntimeContextProbe.new()
		)
	_context_collector = SentryRuntimeContextCollector.new(runtime_context_probe)
	var attachment_runtime_probe: Object = (
			p_attachment_runtime_probe
			if p_attachment_runtime_probe != null
			else SentryAttachmentRuntimeProbe.new()
		)
	_attachment_collector = SentryBuiltInAttachmentCollector.new(
			attachment_runtime_probe,
		)
	_attachment_config = _attachment_config_from(ObservabilityConfig.new(
			p_enabled = false,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
		))
	_owner = str(get_instance_id())


## Returns the stable Sentry provider identifier.
func provider_name() -> StringName:
	return &"sentry"


## Returns whether the configured native bridge can currently accept events.
func is_available() -> bool:
	if not _enabled or _shutdown:
		return false
	var bridge: Object? = _resolve_bridge()
	if bridge == null:
		return false
	return _is_bridge_available(bridge)


## Validates and forwards the complete provider configuration.
func configure(config: ObservabilityConfig) -> int:
	var options: Dictionary = config.provider_options()
	var dsn: String = str(options.get("dsn", ""))
	if config.enabled and dsn.is_empty():
		return Error.FAILED

	var bridge: Object? = _resolve_bridge()
	if config.enabled and bridge == null:
		return Error.ERR_UNAVAILABLE
	if bridge != null and not _has_lifecycle_contract(bridge):
		if config.enabled or _enabled:
			return Error.ERR_UNAVAILABLE
		_enabled = false
		_stable_contexts = {}
		_scope = ObservabilityScope.new()
		_user = null
		_clear_attachment_state()
		_clear_last_config_payload()
		_shutdown = false
		return Error.OK
	if config.enabled and config.logs_enabled and (bridge == null or not bridge.has_method("captureLog")):
		return Error.FAILED
	if config.enabled \
			and bridge != null \
			and bridge.has_method("captureBreadcrumb") \
			and not bridge.has_method("clearBreadcrumbs"):
		return Error.FAILED
	var attachment_features_enabled: bool = (
			config.attach_game_log
			or config.attach_screenshot
			or config.attach_scene_tree
		)
	if config.enabled and attachment_features_enabled and (
			bridge == null
			or not bridge.has_method("replaceAttachments")
			or not bridge.has_method("captureWithAttachments")
	):
		return Error.FAILED

	if bridge == null:
		_enabled = false
		_stable_contexts = {}
		_scope = ObservabilityScope.new()
		_user = null
		_clear_attachment_state()
		_clear_last_config_payload()
		_shutdown = false
		return Error.OK

	var candidate_stable_contexts: Dictionary = {}
	if config.enabled:
		var send_default_pii: bool = false
		var send_default_pii_value: Variant = options.get("send_default_pii")
		if send_default_pii_value is bool:
			send_default_pii = send_default_pii_value
		candidate_stable_contexts = _context_collector.stable_contexts(
				config.environment,
				send_default_pii,
			)
	var payload: Dictionary = {
			"enabled": config.enabled,
			"dsn": dsn,
			"environment": config.environment,
			"release": config.release,
			"dist": config.dist,
			"global_attributes": config.global_attributes(),
			"provider_options": options,
			"logs_enabled": config.logs_enabled,
			"log_minimum_level": config.log_minimum_level,
			"log_rate_limit_per_second": config.log_rate_limit_per_second,
			"metrics_enabled": config.metrics_enabled,
			"application_hang_detection_enabled":
					config.application_hang_detection_enabled,
			"application_hang_timeout_msec":
					maxi(1000, config.application_hang_timeout_msec),
			"android_anr_detection_enabled": config.android_anr_detection_enabled,
			"android_anr_timeout_msec": maxi(1000, config.android_anr_timeout_msec),
			"android_anr_attach_thread_dump": config.android_anr_attach_thread_dump,
			"max_breadcrumbs": config.max_breadcrumbs,
			"max_attachment_bytes": config.max_attachment_bytes,
			"attach_game_log": config.attach_game_log,
			"attach_screenshot": config.attach_screenshot,
			"attach_scene_tree": config.attach_scene_tree,
			"lifecycle_owner": _owner,
		}
	if config.enabled:
		payload["stable_contexts"] = candidate_stable_contexts
	var candidate_config_payload: Dictionary = payload.duplicate(true)
	var retained_scope_payload: Dictionary = _scope_payload(_scope, _user)
	var retained_scope_was_enabled: bool = _enabled and not _shutdown
	var retained_native_attachments: Array = _native_attachment_payloads.duplicate(true)
	var candidate_attachment_config: ObservabilityConfig = _attachment_config_from(config)
	var candidate_persistent_builtins: Array[Dictionary] = []
	if config.enabled and bridge.has_method("replaceAttachments"):
		var built_in_result: Dictionary = _attachment_collector.collect(
				null,
				candidate_attachment_config,
			)
		for attachment: Dictionary in built_in_result["attachments"]:
			if attachment.get("persistent", false) == true:
				var persistent: Dictionary = attachment.duplicate(true)
				persistent.erase("persistent")
				candidate_persistent_builtins.append(persistent)
	var candidate_matches_committed_config: bool = (
			_has_last_config_payload
			and _config_payloads_are_equivalent(candidate_config_payload)
		)
	var prior_breadcrumb_session_was_enabled: bool = (
			retained_scope_was_enabled
			and bridge.has_method("captureBreadcrumb")
			and bridge.has_method("clearBreadcrumbs")
		)
	# Every post-configure failure may retain the prior session only when no live
	# breadcrumb trail existed or the complete candidate cannot restart that session.
	var can_preserve_prior_session_after_configuration_attempt: bool = (
			not prior_breadcrumb_session_was_enabled
			or candidate_matches_committed_config
		)
	# Local candidate state commits only after configure, scope, and breadcrumb resets;
	# otherwise the prior session must be fully restored or the provider fails closed.
	var result: Variant = bridge.call(
			"configure",
			candidate_config_payload.duplicate(true),
		)
	if not (result is int):
		# A malformed result makes native mutation unknowable; rollback is untrustworthy.
		_fail_closed(bridge)
		return Error.FAILED
	var result_code: int = result
	if result_code != Error.OK:
		if not can_preserve_prior_session_after_configuration_attempt \
				or not _restore_retained_session(
				bridge,
				retained_scope_was_enabled,
				retained_scope_payload,
				retained_native_attachments,
		):
			_fail_closed(bridge)
		return result_code
	if config.enabled and bridge.has_method("replaceAttachments"):
		if not _replace_native_snapshot(bridge, candidate_persistent_builtins):
			if not can_preserve_prior_session_after_configuration_attempt \
					or not _rollback_after_session_reset_failure(
					bridge,
					retained_scope_was_enabled,
					retained_scope_payload,
					retained_native_attachments,
			):
				_fail_closed(bridge)
			return Error.FAILED
	if config.enabled and _has_scope_contract(bridge):
		var empty_scope_payload: Dictionary = _scope_payload(
				ObservabilityScope.new(),
				null,
			)
		if not _apply_scope_payload(bridge, empty_scope_payload):
			if not can_preserve_prior_session_after_configuration_attempt \
					or not _rollback_after_session_reset_failure(
					bridge,
					retained_scope_was_enabled,
					retained_scope_payload,
					retained_native_attachments,
			):
				_fail_closed(bridge)
			return Error.FAILED
	if config.enabled and bridge.has_method("clearBreadcrumbs"):
		var clear_result: Variant = bridge.call("clearBreadcrumbs")
		if not (clear_result is bool) or clear_result != true:
			if not _can_preserve_breadcrumb_trail_after_clear_failure(
					clear_result,
					can_preserve_prior_session_after_configuration_attempt,
					retained_scope_was_enabled,
			) or not _rollback_after_session_reset_failure(
				bridge,
				retained_scope_was_enabled,
				retained_scope_payload,
				retained_native_attachments,
			):
				_fail_closed(bridge)
			return Error.FAILED
	_enabled = config.enabled
	_stable_contexts = candidate_stable_contexts
	_scope = ObservabilityScope.new()
	_user = null
	_attachments = {}
	_last_attachment_failures.clear()
	_persistent_builtin_attachments = candidate_persistent_builtins.duplicate(true)
	_native_attachment_payloads = candidate_persistent_builtins.duplicate(true)
	_attachment_config = candidate_attachment_config
	_last_config_payload = candidate_config_payload.duplicate(true)
	_has_last_config_payload = true
	_shutdown = false
	return Error.OK


## Translates one normalized event and returns the native provider ID.
func capture(event: ObservabilityEvent) -> String:
	if event == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available():
		return ""
	var event_scope: ObservabilityScope? = event.scope()
	if event_scope != null \
			and not event_scope.is_empty() \
			and not _has_scope_contract(bridge):
		return ""

	var payload: Dictionary = {
			"kind": String(event.kind()),
			"level": event.level(),
			"message": event.message(),
			"source": String(event.source()),
			"timestamp_msec": event.timestamp_msec(),
			"engine_ticks_msec": event.engine_ticks_msec(),
			"attributes": event.attributes(),
		}
	var contexts: Dictionary = _context_collector.contexts_for_capture(_stable_contexts)
	if not contexts.is_empty():
		payload["contexts"] = contexts
	if event_scope != null and not event_scope.is_empty():
		payload["scope"] = _scope_payload(event_scope, null)
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		var exception_payload: Dictionary = {
				"type_name": exception.type_name(),
				"message": exception.message(),
				"stack_trace": exception.stack_trace(),
				"attributes": exception.attributes(),
			}
		var frames: Array = []
		for frame: ObservabilityStackFrame in exception.frames():
			if frame == null:
				continue
			frames.append(_stack_frame_payload(frame))
		if not frames.is_empty():
			exception_payload["frames"] = frames
		payload["exception"] = exception_payload
	var method: String = "capture"
	if event.kind() == &"log":
		method = "captureLog"
		if not bridge.has_method(method):
			return ""
	else:
		_last_attachment_failures.clear()
		var capture_attachments: Array = _capture_local_attachments(event)
		if not capture_attachments.is_empty():
			payload["attachments"] = capture_attachments
		if bridge.has_method("replaceAttachments") \
				and bridge.has_method("captureWithAttachments"):
			method = "captureWithAttachments"
	return str(bridge.call(method, payload))


## Atomically adds one persistent user attachment to the native snapshot.
func add_attachment(attachment: ObservabilityAttachment) -> String:
	if not _enabled or _shutdown or attachment == null or not attachment.is_valid():
		return ""
	var bridge: Object? = _resolve_bridge()
	if bridge == null \
			or not bridge.has_method("replaceAttachments") \
			or not bridge.has_method("captureWithAttachments"):
		return ""
	_attachment_sequence += 1
	var handle: String = "sentry-attachment:%s" % _attachment_sequence
	var candidate: Dictionary = _attachments.duplicate(true)
	candidate[handle] = attachment.duplicate()
	var native_candidate: Array[Dictionary] = _native_payloads_for(candidate)
	if not _replace_native_snapshot(bridge, native_candidate):
		_set_provider_rejected_failure(handle, attachment)
		return ""
	_attachments = candidate
	_native_attachment_payloads = native_candidate.duplicate(true)
	_last_attachment_failures.clear()
	return handle


## Atomically removes one persistent user attachment.
func remove_attachment(handle: String) -> int:
	if not _enabled or _shutdown:
		return Error.FAILED
	if not _attachments.has(handle):
		return Error.ERR_DOES_NOT_EXIST
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not bridge.has_method("replaceAttachments"):
		return Error.FAILED
	var candidate: Dictionary = _attachments.duplicate(true)
	var attachment: ObservabilityAttachment = candidate[handle]
	candidate.erase(handle)
	var native_candidate: Array[Dictionary] = _native_payloads_for(candidate)
	if not _replace_native_snapshot(bridge, native_candidate):
		_set_provider_rejected_failure(handle, attachment)
		return Error.FAILED
	_attachments = candidate
	_native_attachment_payloads = native_candidate.duplicate(true)
	_last_attachment_failures.clear()
	return Error.OK


## Atomically clears user attachments while retaining configured built-ins.
func clear_attachments() -> bool:
	if not _enabled or _shutdown:
		return false
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not bridge.has_method("replaceAttachments"):
		return false
	var native_candidate: Array[Dictionary] = _persistent_builtin_attachments.duplicate(true)
	if not _replace_native_snapshot(bridge, native_candidate):
		_last_attachment_failures.clear()
		_append_attachment_failure(
				"",
				"",
				ObservabilityAttachmentFailure.PROVIDER_REJECTED,
				Error.FAILED,
			)
		return false
	_attachments = {}
	_native_attachment_payloads = native_candidate.duplicate(true)
	_last_attachment_failures.clear()
	return true


## Returns defensive typed failures from the latest applicable attachment operation.
func last_attachment_failures() -> Array:
	var failures: Array = []
	for failure: ObservabilityAttachmentFailure in _last_attachment_failures:
		failures.append(failure.duplicate())
	return failures


## Sets a global tag only after the native bridge accepts the complete candidate scope.
func set_tag(key: String, value: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.set_tag(key, value) or not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Removes a global tag only after the native bridge accepts the complete candidate scope.
func remove_tag(key: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.remove_tag(key) or not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Clears global tags only after the native bridge accepts the complete candidate scope.
func clear_tags() -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	candidate.clear_tags()
	if not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Sets a global context only after the native bridge accepts the complete candidate scope.
func set_context(context_name: String, value: Dictionary) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.set_context(context_name, value):
		return false
	if not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Removes a global context only after the native bridge accepts the complete candidate scope.
func remove_context(context_name: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.remove_context(context_name):
		return false
	if not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Clears global contexts only after the native bridge accepts the complete candidate scope.
func clear_contexts() -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	candidate.clear_contexts()
	if not _apply_scope_candidate(candidate, _user):
		return false
	_scope = candidate
	return true


## Replaces the global user only after the native bridge accepts the complete candidate scope.
func set_user(user: ObservabilityUser) -> bool:
	if not _enabled or _shutdown or user == null or not user.is_valid():
		return false
	var candidate_user: ObservabilityUser = ObservabilityUser.new(
			user.application_user_id(),
			user.display_name(),
			user.contact_email(),
		)
	if not _apply_scope_candidate(_scope, candidate_user):
		return false
	_user = candidate_user
	return true


## Removes the global user only after the native bridge accepts the complete candidate scope.
func remove_user() -> bool:
	if not _enabled or _shutdown or not _apply_scope_candidate(_scope, null):
		return false
	_user = null
	return true


## Translates one normalized breadcrumb to the optional native breadcrumb API.
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	if breadcrumb == null or not _enabled or _shutdown:
		return false

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureBreadcrumb"):
		return false

	var result: Variant = bridge.call("captureBreadcrumb", {
			"message": breadcrumb.message(),
			"level": breadcrumb.level(),
			"category": String(breadcrumb.category()),
			"timestamp_msec": breadcrumb.timestamp_msec(),
			"attributes": breadcrumb.attributes(),
			"type": String(breadcrumb.type()),
		})
	if not (result is bool):
		return false
	return result


## Clears native breadcrumbs through the optional bridge operation.
func clear_breadcrumbs() -> bool:
	if not _enabled or _shutdown:
		return false

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("clearBreadcrumbs"):
		return false

	var result: Variant = bridge.call("clearBreadcrumbs")
	return result is bool and result == true


## Translates explicit feedback to the native dedicated feedback API.
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if feedback == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureFeedback"):
		return ""

	var payload: Dictionary = {"message": feedback.message()}
	if not feedback.name().is_empty():
		payload["name"] = feedback.name()
	if not feedback.contact_email().is_empty():
		payload["contact_email"] = feedback.contact_email()
	if not feedback.associated_event_id().is_empty():
		payload["associated_event_id"] = feedback.associated_event_id()
	return str(bridge.call("captureFeedback", payload))


## Translates one normalized custom metric to the optional native metrics API.
func capture_metric(metric: ObservabilityMetric) -> bool:
	if metric == null or not _enabled or _shutdown:
		return false

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureMetric"):
		return false

	var result: Variant = bridge.call("captureMetric", {
			"type": metric.type(),
			"name": metric.name(),
			"value": metric.value(),
			"unit": metric.unit(),
			"attributes": metric.attributes(),
		})
	if not (result is bool):
		return false
	return result


## Flushes native Sentry work within the requested timeout.
func flush(timeout_msec: int = 2000) -> int:
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not _enabled or _shutdown or not is_available():
		return Error.OK
	var result: Variant = bridge.call("flush", _owner, timeout_msec)
	if not (result is int):
		return Error.FAILED
	return result


## Shuts down the native bridge once.
func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	_enabled = false
	_stable_contexts = {}
	_scope = ObservabilityScope.new()
	_user = null
	_clear_attachment_state()
	_clear_last_config_payload()
	var bridge: Object? = _resolve_bridge()
	if bridge != null and _has_lifecycle_contract(bridge):
		bridge.call("shutdown", _owner)


func _resolve_bridge() -> Object?:
	if _bridge != null:
		return _bridge
	if Engine.has_singleton(_NATIVE_CLASS):
		_bridge = Engine.get_singleton(_NATIVE_CLASS)
		return _bridge
	if not ClassDB.class_exists(_NATIVE_CLASS) or not ClassDB.can_instantiate(_NATIVE_CLASS):
		return null
	_bridge = ClassDB.instantiate(_NATIVE_CLASS)
	return _bridge


func _has_lifecycle_contract(bridge: Object) -> bool:
	for method: String in [
		"lifecycleVersion",
		"configure",
		"isAvailable",
		"capture",
		"flush",
		"shutdown",
	]:
		if not bridge.has_method(method):
			return false
	var version_result: Variant = bridge.call("lifecycleVersion")
	return version_result is int and version_result >= _LIFECYCLE_VERSION


func _has_scope_contract(bridge: Object) -> bool:
	return bridge.has_method("applyScope")


func _is_bridge_available(bridge: Object) -> bool:
	if not _has_lifecycle_contract(bridge):
		return false
	var result: Variant = bridge.call("isAvailable", _owner)
	return result is bool and result == true


func _native_payloads_for(candidate: Dictionary) -> Array[Dictionary]:
	var payloads: Array[Dictionary] = _persistent_builtin_attachments.duplicate(true)
	for handle: String in candidate:
		var attachment: ObservabilityAttachment = candidate[handle]
		if attachment.is_path() and attachment.path().begins_with("res://"):
			continue
		var payload: Dictionary = {
			"filename": attachment.effective_filename(),
			"content_type": attachment.content_type(),
			"category": String(attachment.category()),
		}
		if attachment.is_path():
			var path: String = attachment.path()
			if path.begins_with("user://"):
				path = ProjectSettings.globalize_path(path)
			payload["path"] = path
		else:
			payload["bytes"] = attachment.bytes()
		payloads.append(payload)
	return payloads


func _replace_native_snapshot(bridge: Object, payloads: Array) -> bool:
	if not bridge.has_method("replaceAttachments"):
		return false
	var result: Variant = bridge.call(
			"replaceAttachments",
			payloads.duplicate(true),
		)
	return result is bool and result == true


func _capture_local_attachments(event: ObservabilityEvent) -> Array:
	var local: Array[Dictionary] = []
	for handle: String in _attachments:
		var attachment: ObservabilityAttachment = _attachments[handle]
		if attachment.is_bytes():
			if attachment.bytes().size() > _attachment_config.max_attachment_bytes:
				_append_attachment_failure(
						handle,
						attachment.effective_filename(),
						ObservabilityAttachmentFailure.OVERSIZED,
						Error.FAILED,
					)
			continue
		var materialized: Dictionary = _preflight_path_attachment(handle, attachment)
		if attachment.path().begins_with("res://") \
				and materialized.get("accepted", false) == true:
			local.append({
				"bytes": materialized["bytes"],
				"filename": attachment.effective_filename(),
				"content_type": attachment.content_type(),
				"category": String(attachment.category()),
			})
	var built_ins: Dictionary = _attachment_collector.collect(
			event,
			_attachment_config,
		)
	for failure: ObservabilityAttachmentFailure in built_ins["failures"]:
		_last_attachment_failures.append(failure.duplicate())
	for payload: Dictionary in built_ins["attachments"]:
		if payload.get("persistent", false) == true:
			continue
		var capture_payload: Dictionary = payload.duplicate(true)
		capture_payload.erase("persistent")
		local.append(capture_payload)
	return local


func _preflight_path_attachment(
		handle: String,
		attachment: ObservabilityAttachment,
) -> Dictionary:
	var path: String = attachment.path()
	var readable_path: String = path
	if readable_path.begins_with("user://"):
		readable_path = ProjectSettings.globalize_path(readable_path)
	if not FileAccess.file_exists(readable_path):
		_append_attachment_failure(
				handle,
				attachment.effective_filename(),
				ObservabilityAttachmentFailure.MISSING_FILE,
				Error.ERR_FILE_NOT_FOUND,
			)
		return {"accepted": false}
	var file: FileAccess = FileAccess.open(readable_path, FileAccess.READ)
	if file == null:
		_append_attachment_failure(
				handle,
				attachment.effective_filename(),
				ObservabilityAttachmentFailure.UNREADABLE_FILE,
				Error.ERR_FILE_CANT_OPEN,
			)
		return {"accepted": false}
	var length: int = file.get_length()
	if length > _attachment_config.max_attachment_bytes:
		file.close()
		_append_attachment_failure(
				handle,
				attachment.effective_filename(),
				ObservabilityAttachmentFailure.OVERSIZED,
				Error.FAILED,
			)
		return {"accepted": false}
	if not path.begins_with("res://"):
		file.close()
		return {"accepted": true}
	var bytes: PackedByteArray = file.get_buffer(length)
	file.close()
	if bytes.size() != length:
		_append_attachment_failure(
				handle,
				attachment.effective_filename(),
				ObservabilityAttachmentFailure.UNREADABLE_FILE,
				Error.ERR_FILE_CANT_READ,
			)
		return {"accepted": false}
	return {
		"accepted": true,
		"bytes": bytes,
	}


func _set_provider_rejected_failure(
		handle: String,
		attachment: ObservabilityAttachment,
) -> void:
	_last_attachment_failures.clear()
	_append_attachment_failure(
			handle,
			attachment.effective_filename(),
			ObservabilityAttachmentFailure.PROVIDER_REJECTED,
			Error.FAILED,
		)


func _append_attachment_failure(
		handle: String,
		filename: String,
		reason: StringName,
		error: int,
) -> void:
	_last_attachment_failures.append(ObservabilityAttachmentFailure.new(
			handle,
			filename,
			reason,
			error,
		))


func _attachment_config_from(config: ObservabilityConfig) -> ObservabilityConfig:
	return ObservabilityConfig.new(
			p_enabled = config.enabled,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_max_attachment_bytes = config.max_attachment_bytes,
			p_attach_game_log = config.attach_game_log,
			p_attach_screenshot = config.attach_screenshot,
			p_attach_scene_tree = config.attach_scene_tree,
		)


func _apply_scope_candidate(
		candidate_scope: ObservabilityScope,
		candidate_user: ObservabilityUser?,
) -> bool:
	var bridge: Object? = _resolve_bridge()
	if bridge == null:
		return false
	return _apply_scope_payload(
			bridge,
			_scope_payload(candidate_scope, candidate_user),
		)


func _apply_scope_payload(bridge: Object, payload: Dictionary) -> bool:
	if not _has_scope_contract(bridge) or not _is_bridge_available(bridge):
		return false
	var result: Variant = bridge.call(
			"applyScope",
			payload,
		)
	return result is bool and result == true


func _restore_retained_session(
		bridge: Object,
		retained_scope_was_enabled: bool,
		retained_scope_payload: Dictionary,
		retained_native_attachments: Array,
) -> bool:
	if not retained_scope_was_enabled:
		return true
	if _has_scope_contract(bridge) \
			and not _apply_scope_payload(bridge, retained_scope_payload):
		return false
	if bridge.has_method("replaceAttachments") \
			and not _replace_native_snapshot(bridge, retained_native_attachments):
		return false
	return true


func _rollback_after_session_reset_failure(
		bridge: Object,
		retained_scope_was_enabled: bool,
		retained_scope_payload: Dictionary,
		retained_native_attachments: Array,
) -> bool:
	if not _has_last_config_payload:
		return false
	var rollback_result: Variant = bridge.call(
			"configure",
			_last_config_payload.duplicate(true),
		)
	if not (rollback_result is int) or rollback_result != Error.OK:
		return false
	return _restore_retained_session(
			bridge,
			retained_scope_was_enabled,
			retained_scope_payload,
			retained_native_attachments,
		)


func _can_preserve_breadcrumb_trail_after_clear_failure(
		clear_result: Variant,
		can_preserve_prior_session_after_configuration_attempt: bool,
		retained_scope_was_enabled: bool,
) -> bool:
	if not (clear_result is bool) or clear_result != false:
		return false
	if not can_preserve_prior_session_after_configuration_attempt:
		return false
	if not retained_scope_was_enabled or not _has_last_config_payload:
		return false
	var committed_enabled: Variant = _last_config_payload.get("enabled")
	if not (committed_enabled is bool) or committed_enabled != true:
		return false
	return true


func _config_payloads_are_equivalent(candidate_config_payload: Dictionary) -> bool:
	# Dictionary equality is deep; defensive complete snapshots include every
	# nested configuration field and the lifecycle owner.
	var candidate_snapshot: Dictionary = candidate_config_payload.duplicate(true)
	var committed_snapshot: Dictionary = _last_config_payload.duplicate(true)
	return candidate_snapshot == committed_snapshot


func _fail_closed(bridge: Object) -> void:
	bridge.call("shutdown", _owner)
	_enabled = false
	_stable_contexts = {}
	_scope = ObservabilityScope.new()
	_user = null
	_clear_attachment_state()
	_clear_last_config_payload()
	_shutdown = true


func _clear_attachment_state() -> void:
	_attachments = {}
	_last_attachment_failures.clear()
	_persistent_builtin_attachments = []
	_native_attachment_payloads = []
	_attachment_config = _attachment_config_from(ObservabilityConfig.new(
			p_enabled = false,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_max_attachment_bytes = DEFAULT_MAX_ATTACHMENT_BYTES,
		))


func _clear_last_config_payload() -> void:
	_last_config_payload = {}
	_has_last_config_payload = false


func _scope_payload(
		scope: ObservabilityScope,
		user: ObservabilityUser?,
) -> Dictionary:
	var payload: Dictionary = {
			"tags": scope.tags(),
			"contexts": scope.contexts(),
		}
	if user != null:
		payload["user"] = {
				"id": user.application_user_id(),
				"display_name": user.display_name(),
				"contact_email": user.contact_email(),
			}
	return payload


func _stack_frame_payload(frame: ObservabilityStackFrame) -> Dictionary:
	var payload: Dictionary = {
			"file": frame.file(),
			"function": frame.function(),
			"line": frame.line(),
			"language": frame.language(),
			"in_app": frame.in_app(),
		}
	var context_line: String = frame.context_line()
	if not context_line.is_empty():
		payload["context_line"] = context_line
	var pre_context: PackedStringArray = frame.pre_context()
	if not pre_context.is_empty():
		payload["pre_context"] = Array(pre_context)
	var post_context: PackedStringArray = frame.post_context()
	if not post_context.is_empty():
		payload["post_context"] = Array(post_context)
	var variables: Dictionary = frame.variables()
	if not variables.is_empty():
		payload["variables"] = variables
	return payload
