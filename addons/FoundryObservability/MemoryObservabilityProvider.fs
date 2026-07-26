namespace foundry.observability

## Deterministic provider for tests and local integration work.
class_name MemoryObservabilityProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityMetricsProvider, ObservabilityBreadcrumbsProvider, ObservabilityScopeProvider, ObservabilityAttachmentsProvider

const DEFAULT_MAX_ATTACHMENT_BYTES: int = 20 * 1024 * 1024

## Result returned by the next configure call.
var configure_result: int = Error.OK
## Result returned by flush calls.
var flush_result: int = Error.OK
## Timeout passed to the most recent flush call.
var last_flush_timeout_msec: int = 0
## Number of flush calls received by this provider.
var flush_count: int = 0
## Number of effective shutdown calls received by this provider.
var shutdown_count: int = 0
## Result returned by custom metric capture calls.
var metric_capture_result: bool = true

var _events: Array[ObservabilityEvent] = []
var _captured_scopes: Array[Dictionary] = []
var _attachments: Dictionary = {}
var _captured_attachments: Array[Array] = []
var _last_attachment_failures: Array[ObservabilityAttachmentFailure] = []
var _breadcrumbs: Array[ObservabilityBreadcrumb] = []
var _feedback: Array[ObservabilityFeedback] = []
var _metrics: Array[ObservabilityMetric] = []
var _scope: ObservabilityScope = ObservabilityScope.new()
var _user: ObservabilityUser? = null
var _max_breadcrumbs: int = 100
var _event_sequence: int = 0
var _attachment_sequence: int = 0
var _feedback_sequence: int = 0
var _max_attachment_bytes: int = DEFAULT_MAX_ATTACHMENT_BYTES
var _enabled: bool = false
var _shutdown: bool = false


## Returns the memory provider identifier.
func provider_name() -> StringName:
	return &"memory"


## Always returns true because this provider is local and deterministic.
func is_available() -> bool:
	return true


## Applies the configured test result and enables capture when config.enabled is true.
func configure(config: ObservabilityConfig) -> int:
	if configure_result != Error.OK:
		return configure_result
	_scope = ObservabilityScope.new()
	_user = null
	_breadcrumbs.clear()
	_attachments.clear()
	_last_attachment_failures.clear()
	_max_breadcrumbs = maxi(0, config.max_breadcrumbs)
	_max_attachment_bytes = maxi(0, config.max_attachment_bytes)
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


## Stores an event and returns a sequential memory event ID when enabled.
func capture(event: ObservabilityEvent) -> String:
	if event == null or not _enabled or _shutdown:
		return ""
	var attachment_snapshot: Array = []
	if event.kind() != &"log":
		_last_attachment_failures.clear()
		attachment_snapshot = _materialize_attachments()
	var effective_tags: Dictionary = _scope.tags()
	var effective_contexts: Dictionary = _scope.contexts()
	var event_scope: ObservabilityScope? = event.scope()
	if event_scope != null:
		var local_tags: Dictionary = event_scope.tags()
		var local_contexts: Dictionary = event_scope.contexts()
		for key: String in local_tags:
			effective_tags[key] = local_tags[key]
		for context_name: String in local_contexts:
			effective_contexts[context_name] = local_contexts[context_name]
	var user_snapshot: Variant = null
	if _user != null:
		user_snapshot = {
			"id": _user.application_user_id(),
			"display_name": _user.display_name(),
			"contact_email": _user.contact_email(),
		}
	var captured_scope: Dictionary = {
		"tags": effective_tags,
		"contexts": effective_contexts,
		"user": user_snapshot,
	}
	_events.append(event)
	_captured_scopes.append(captured_scope)
	_captured_attachments.append(attachment_snapshot)
	_event_sequence += 1
	return "memory:%s" % _event_sequence


## Retains a defensive attachment value for subsequent event captures.
func add_attachment(attachment: ObservabilityAttachment) -> String:
	if not _enabled or _shutdown or attachment == null or not attachment.is_valid():
		return ""
	_attachment_sequence += 1
	var handle: String = "memory-attachment:%s" % _attachment_sequence
	_attachments[handle] = attachment.duplicate()
	return handle


## Removes a retained attachment, distinguishing inactive and unknown handles.
func remove_attachment(handle: String) -> int:
	if not _enabled or _shutdown:
		return Error.FAILED
	if not _attachments.has(handle):
		return Error.ERR_DOES_NOT_EXIST
	_attachments.erase(handle)
	return Error.OK


## Clears all retained attachments while the provider is active.
func clear_attachments() -> bool:
	if not _enabled or _shutdown:
		return false
	_attachments.clear()
	return true


## Returns isolated attachment failures from the latest captured event.
func last_attachment_failures() -> Array:
	var failures: Array = []
	for failure: ObservabilityAttachmentFailure in _last_attachment_failures:
		failures.append(failure.duplicate())
	return failures


func _materialize_attachments() -> Array:
	var snapshot: Array = []
	if _max_attachment_bytes == 0:
		for handle: String in _attachments:
			var disabled_attachment: ObservabilityAttachment = _attachments[handle]
			_append_attachment_failure(
					handle,
					disabled_attachment,
					ObservabilityAttachmentFailure.OVERSIZED,
					Error.FAILED,
				)
		return snapshot
	for handle: String in _attachments:
		var attachment: ObservabilityAttachment = _attachments[handle]
		var bytes: PackedByteArray = attachment.bytes()
		if attachment.is_path():
			var materialized: Dictionary = _read_attachment_path(handle, attachment)
			if not materialized["accepted"]:
				continue
			bytes = materialized["bytes"]
		elif bytes.size() > _max_attachment_bytes:
			_append_attachment_failure(
					handle,
					attachment,
					ObservabilityAttachmentFailure.OVERSIZED,
					Error.FAILED,
			)
			continue
		snapshot.append({
			"bytes": bytes.duplicate(),
			"filename": attachment.effective_filename(),
			"content_type": attachment.content_type(),
			"category": attachment.category(),
			"path": attachment.path(),
		})
	return snapshot


func _read_attachment_path(
		handle: String,
		attachment: ObservabilityAttachment,
) -> Dictionary:
	var original_path: String = attachment.path()
	var readable_path: String = original_path
	if readable_path.begins_with("user://"):
		readable_path = ProjectSettings.globalize_path(readable_path)
	if not FileAccess.file_exists(readable_path):
		_append_attachment_failure(
				handle,
				attachment,
				ObservabilityAttachmentFailure.MISSING_FILE,
				Error.ERR_FILE_NOT_FOUND,
		)
		return {"accepted": false}
	var file: FileAccess = FileAccess.open(readable_path, FileAccess.READ)
	if file == null:
		_append_attachment_failure(
				handle,
				attachment,
				ObservabilityAttachmentFailure.UNREADABLE_FILE,
				Error.ERR_FILE_CANT_OPEN,
		)
		return {"accepted": false}
	var length: int = file.get_length()
	if length > _max_attachment_bytes:
		file.close()
		_append_attachment_failure(
				handle,
				attachment,
				ObservabilityAttachmentFailure.OVERSIZED,
				Error.FAILED,
		)
		return {"accepted": false}
	var bytes: PackedByteArray = file.get_buffer(length)
	file.close()
	if bytes.size() != length:
		_append_attachment_failure(
				handle,
				attachment,
				ObservabilityAttachmentFailure.UNREADABLE_FILE,
				Error.ERR_FILE_CANT_READ,
		)
		return {"accepted": false}
	return {
		"accepted": true,
		"bytes": bytes,
	}


func _append_attachment_failure(
		handle: String,
		attachment: ObservabilityAttachment,
		reason: StringName,
		error: int,
) -> void:
	_last_attachment_failures.append(ObservabilityAttachmentFailure.new(
			handle,
			attachment.effective_filename(),
			reason,
			error,
	))


## Stores a breadcrumb within the configured bound when enabled.
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	if not _enabled or _shutdown or _max_breadcrumbs == 0:
		return false
	_breadcrumbs.append(breadcrumb)
	while _breadcrumbs.size() > _max_breadcrumbs:
		_breadcrumbs.remove_at(0)
	return true


## Sets a provider-owned global tag atomically.
func set_tag(key: String, value: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.set_tag(key, value):
		return false
	_scope = candidate
	return true


## Removes a provider-owned global tag atomically.
func remove_tag(key: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.remove_tag(key):
		return false
	_scope = candidate
	return true


## Clears all provider-owned global tags.
func clear_tags() -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	candidate.clear_tags()
	_scope = candidate
	return true


## Sets a provider-owned global context atomically.
func set_context(context_name: String, value: Dictionary) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.set_context(context_name, value):
		return false
	_scope = candidate
	return true


## Removes a provider-owned global context atomically.
func remove_context(context_name: String) -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	if not candidate.remove_context(context_name):
		return false
	_scope = candidate
	return true


## Clears all provider-owned global contexts.
func clear_contexts() -> bool:
	if not _enabled or _shutdown:
		return false
	var candidate: ObservabilityScope = _scope.duplicate()
	candidate.clear_contexts()
	_scope = candidate
	return true


## Replaces the explicit provider-owned application user.
func set_user(user: ObservabilityUser) -> bool:
	if not _enabled or _shutdown or user == null or not user.is_valid():
		return false
	_user = ObservabilityUser.new(
			user.application_user_id(),
			user.display_name(),
			user.contact_email(),
		)
	return true


## Removes the explicit provider-owned application user.
func remove_user() -> bool:
	if not _enabled or _shutdown:
		return false
	_user = null
	return true


## Stores explicit feedback and returns a sequential memory feedback ID when enabled.
func capture_feedback(p_feedback: ObservabilityFeedback) -> String:
	if not _enabled or _shutdown:
		return ""
	_feedback.append(p_feedback)
	_feedback_sequence += 1
	return "memory-feedback:%s" % _feedback_sequence


## Stores a normalized custom metric when enabled.
func capture_metric(metric: ObservabilityMetric) -> bool:
	if not _enabled or _shutdown or not metric_capture_result:
		return false
	_metrics.append(metric)
	return true


## Records the timeout and returns flush_result.
func flush(timeout_msec: int = 2000) -> int:
	last_flush_timeout_msec = timeout_msec
	flush_count += 1
	return flush_result


## Marks the provider shut down and increments shutdown_count once.
func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	_enabled = false
	_scope = ObservabilityScope.new()
	_user = null
	_breadcrumbs.clear()
	_attachments.clear()
	_last_attachment_failures.clear()
	_max_breadcrumbs = 100
	_max_attachment_bytes = DEFAULT_MAX_ATTACHMENT_BYTES
	shutdown_count += 1


## Returns a shallow copy of the captured event list.
func events() -> Array[ObservabilityEvent]:
	return _events.duplicate()


## Returns defensive effective scope snapshots aligned with captured events.
func captured_scopes() -> Array[Dictionary]:
	return _captured_scopes.duplicate(true)


## Returns defensive attachment snapshots aligned with captured events.
func captured_attachments() -> Array[Array]:
	return _captured_attachments.duplicate(true)


## Returns a shallow copy of captured breadcrumbs.
func breadcrumbs() -> Array[ObservabilityBreadcrumb]:
	return _breadcrumbs.duplicate()


## Returns a shallow copy of explicitly captured feedback.
func feedback() -> Array[ObservabilityFeedback]:
	return _feedback.duplicate()


## Returns a shallow copy of captured normalized metrics.
func metrics() -> Array[ObservabilityMetric]:
	return _metrics.duplicate()


## Removes captured events without changing provider configuration.
func clear() -> void:
	_events.clear()
	_captured_scopes.clear()
	_captured_attachments.clear()


## Removes captured breadcrumbs without changing provider configuration.
func clear_breadcrumbs() -> bool:
	if not _enabled or _shutdown:
		return false
	_breadcrumbs.clear()
	return true


## Removes captured feedback without changing provider configuration.
func clear_feedback() -> void:
	_feedback.clear()


## Removes captured metrics without changing provider configuration.
func clear_metrics() -> void:
	_metrics.clear()
