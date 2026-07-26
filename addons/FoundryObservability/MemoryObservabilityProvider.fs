namespace foundry.observability

## Deterministic provider for tests and local integration work.
class_name MemoryObservabilityProvider
extends RefCounted
uses ObservabilityProvider, ObservabilityMetricsProvider, ObservabilityBreadcrumbsProvider, ObservabilityScopeProvider

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
var _breadcrumbs: Array[ObservabilityBreadcrumb] = []
var _feedback: Array[ObservabilityFeedback] = []
var _metrics: Array[ObservabilityMetric] = []
var _scope: ObservabilityScope = ObservabilityScope.new()
var _user: ObservabilityUser? = null
var _max_breadcrumbs: int = 100
var _event_sequence: int = 0
var _feedback_sequence: int = 0
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
	_max_breadcrumbs = maxi(0, config.max_breadcrumbs)
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


## Stores an event and returns a sequential memory event ID when enabled.
func capture(event: ObservabilityEvent) -> String:
	if not _enabled or _shutdown:
		return ""
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
	_event_sequence += 1
	return "memory:%s" % _event_sequence


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
	_max_breadcrumbs = 100
	shutdown_count += 1


## Returns a shallow copy of the captured event list.
func events() -> Array[ObservabilityEvent]:
	return _events.duplicate()


## Returns defensive effective scope snapshots aligned with captured events.
func captured_scopes() -> Array[Dictionary]:
	return _captured_scopes.duplicate(true)


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
