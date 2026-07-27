@autoload
namespace foundry.observability

import foundry.observability.processing
import foundry.observability.runtime

## Autoload entry point that orchestrates typed observability collaborators.
class_name FoundryObservability extends Node
uses FoundryObservabilityApi

const _SENTRY_PROVIDER_PATH: String = (
	"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs"
)
const _OBSERVABILITY_PROVIDER_TRAIT_PATH: String = (
	"res://addons/FoundryObservability/ObservabilityProvider.fs"
)

var _runtime: ObservabilityRuntime
var _normalizer: ObservabilityNormalizer
var _session: ObservabilityProviderSession
var _automatic_logger: AutomaticObservabilityLogger?
var _last_error: int = Error.OK
var _startup_provider: ObservabilityProvider?
var _startup_provider_path: String = _SENTRY_PROVIDER_PATH
var _startup_status: StringName = ObservabilityStartupStatus.NOT_STARTED
var _startup_message: String = "Startup has not run."

func _init(
		startup_settings: ObservabilityStartupSettings? = null,
		startup_provider_path: String = _SENTRY_PROVIDER_PATH,
		runtime: ObservabilityRuntime? = null,
) -> void:
	_runtime = runtime if runtime != null else SystemObservabilityRuntime.new()
	_normalizer = ObservabilityNormalizer.new(_runtime)
	_session = ObservabilityProviderSession.new(_runtime)
	_startup_provider_path = startup_provider_path
	if startup_settings == null:
		initialize_from_project_settings()
	else:
		_initialize_startup(startup_settings)

func initialize_from_project_settings() -> int:
	return _initialize_startup(ObservabilityStartupSettings.from_project_settings())

func startup_status() -> StringName:
	return _startup_status

func startup_message() -> String:
	return _startup_message

func _initialize_startup(settings: ObservabilityStartupSettings?) -> int:
	if settings == null:
		return _record_startup(
				ObservabilityStartupStatus.CONFIGURATION_FAILED,
				"Startup configuration is invalid.",
				Error.ERR_INVALID_PARAMETER,
				false,
			)
	var skip_status: StringName = settings.skip_status()
	if skip_status != ObservabilityStartupStatus.NOT_STARTED:
		if skip_status == ObservabilityStartupStatus.DISABLED \
				and not settings.capture_enabled():
			shutdown()
		return _record_startup(
				skip_status,
				_startup_skip_message(skip_status),
				Error.OK,
				settings.debug_enabled(),
			)
	if settings.validation_error() != Error.OK:
		return _record_startup(
				ObservabilityStartupStatus.CONFIGURATION_FAILED,
				"Startup configuration contains invalid values.",
				settings.validation_error(),
				settings.debug_enabled(),
			)
	if not settings.has_dsn():
		return _record_startup(
				ObservabilityStartupStatus.MISSING_DSN,
				"Startup is disabled because no DSN is configured.",
				Error.ERR_UNCONFIGURED,
				settings.debug_enabled(),
			)
	var requested_config: ObservabilityConfig = settings.observability_config()
	var current: ObservabilityProviderSnapshot = _session.snapshot()
	if _startup_provider != null \
			and is_same(_startup_provider, current.provider()) \
			and not is_same(requested_config, current.config()):
		## Provider-session ownership forbids in-place reconfiguration. A materially
		## new startup configuration therefore receives a fresh provider instance.
		_startup_provider = null
	var startup_provider: ObservabilityProvider? = _load_startup_provider()
	if startup_provider == null:
		return _record_startup(
				ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
				"The optional Sentry startup provider is unavailable.",
				Error.ERR_UNAVAILABLE,
				settings.debug_enabled(),
			)
	var result: int = configure(startup_provider, requested_config)
	if result == Error.OK:
		return _record_startup(
				ObservabilityStartupStatus.INITIALIZED,
				"Startup provider initialized.",
				Error.OK,
				settings.debug_enabled(),
			)
	var failed_status: StringName = ObservabilityStartupStatus.CONFIGURATION_FAILED
	if result == Error.ERR_UNAVAILABLE:
		failed_status = ObservabilityStartupStatus.PROVIDER_UNAVAILABLE
	return _record_startup(
			failed_status,
			"Startup provider configuration failed with Error %s." % result,
			result,
			settings.debug_enabled(),
		)

func _load_startup_provider() -> ObservabilityProvider?:
	if _startup_provider != null:
		return _startup_provider
	if _startup_provider_path == _OBSERVABILITY_PROVIDER_TRAIT_PATH \
			or not ResourceLoader.exists(_startup_provider_path):
		return null
	var provider_script: Script = ResourceLoader.load(_startup_provider_path) as Script
	if provider_script == null or not provider_script.can_instantiate():
		return null
	@warning_ignore("unsafe_method_access")
	var candidate: Variant = provider_script.new()
	if not (candidate is ObservabilityProvider):
		return null
	@warning_ignore("unsafe_cast")
	_startup_provider = candidate as ObservabilityProvider
	return _startup_provider

func _record_startup(
		status: StringName,
		message: String,
		result: int,
		print_diagnostics: bool,
) -> int:
	_startup_status = status
	_startup_message = message
	_set_error(result)
	if print_diagnostics:
		print("FoundryObservability: " + message)
	return result

func _startup_skip_message(status: StringName) -> String:
	match status:
		ObservabilityStartupStatus.DISABLED:
			return "Automatic startup is disabled."
		ObservabilityStartupStatus.SKIPPED_EDITOR:
			return "Automatic startup is skipped in the editor."
		ObservabilityStartupStatus.SKIPPED_EDITOR_PLAY:
			return "Automatic startup is skipped for editor play."
		ObservabilityStartupStatus.SKIPPED_DEBUG:
			return "Automatic startup is skipped for debug exports."
	return "Automatic startup was skipped."

## Atomically replaces the provider through a pristine typed pipeline candidate.
## An active provider may only be repeated with the exact committed config object.
## Material reconfiguration requires a fresh provider instance.
func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
	if provider == null:
		_set_error(Error.FAILED)
		return Error.FAILED
	var candidate_config: ObservabilityConfig = (
			config if config != null else ObservabilityConfig.new(p_enabled = false)
		)
	var intent: ObservabilityProviderSnapshot = _session.snapshot()
	if is_same(provider, intent.provider()):
		var repeated_result: int = Error.ERR_ALREADY_IN_USE
		if is_same(candidate_config, intent.config()):
			repeated_result = _session.replace_if_generation(
					provider, candidate_config, intent.pipeline(), intent.generation(),
				)
		_set_error(repeated_result)
		return repeated_result
	var candidate_pipeline: ObservabilityProcessingPipeline = (
			ObservabilityProcessingPipeline.new(_runtime)
		)
	var pipeline_result: int = _prepare_candidate_pipeline(
			candidate_config,
			candidate_pipeline,
		)
	if pipeline_result != Error.OK:
		_set_error(pipeline_result)
		return pipeline_result
	var result: int = _session.replace_if_generation(
			provider,
			candidate_config,
			candidate_pipeline,
			intent.generation(),
		)
	_set_error(result)
	if result == Error.OK:
		_refresh_automatic_logger(_session.snapshot())
	elif _session.snapshot().generation() != intent.generation():
		_refresh_automatic_logger(_session.snapshot())
	return result

func _prepare_candidate_pipeline(
		config: ObservabilityConfig,
		pipeline: ObservabilityProcessingPipeline,
) -> int:
	return pipeline.configure(config)

func is_enabled() -> bool:
	return _session.snapshot().enabled()

func is_available() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_status_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call, false)
		return false
	var available: bool = provider_call.provider().is_available()
	_session.end_call(provider_call)
	return available

func provider_name() -> StringName:
	var provider_call: ObservabilityProviderCall = _session.begin_status_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call, false)
		return &"null"
	var identifier: StringName = provider_call.provider().provider_name()
	_session.end_call(provider_call)
	return &"null" if identifier.is_empty() else identifier

func last_error() -> int:
	return _last_error

func _set_error(error: int, propagate_to_current: bool = true) -> void:
	_last_error = error
	if propagate_to_current and error != Error.OK:
		_session.record_current_call_error(error)

func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?:
	return _session.snapshot().pipeline().last_diagnostic()

func add_attachment(attachment: ObservabilityAttachment) -> String:
	if attachment == null or not attachment.is_valid():
		_set_error(Error.ERR_INVALID_PARAMETER)
		return ""
	var provider_call: ObservabilityProviderCall = _begin_provider_call(
			_session.snapshot(),
		)
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return ""
	var redaction: ObservabilityProcessingResult[Dictionary] = (
			provider_call.pipeline().redact_attachment(attachment)
		)
	var payload: Dictionary? = redaction.value()
	if not redaction.is_accepted() or payload == null \
			or not (payload.get("attachment") is ObservabilityAttachment):
		_finish_processing_rejection(provider_call, redaction)
		return ""
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityAttachmentsProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return ""
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityAttachmentsProvider = (
			provider_value as ObservabilityAttachmentsProvider
		)
	@warning_ignore("unsafe_cast")
	var redacted: ObservabilityAttachment = payload.get("attachment") as ObservabilityAttachment
	var handle: String = capability.add_attachment(redacted)
	_finish_call(provider_call, Error.OK if not handle.is_empty() else Error.FAILED)
	return handle

func remove_attachment(handle: String) -> bool:
	if handle.is_empty() or handle.strip_edges() != handle \
			or _contains_control_character(handle):
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityAttachmentsProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return false
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityAttachmentsProvider = (
			provider_value as ObservabilityAttachmentsProvider
		)
	var result: int = capability.remove_attachment(handle)
	_finish_call(provider_call, result)
	return result == Error.OK

func clear_attachments() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityAttachmentsProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return false
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityAttachmentsProvider = (
			provider_value as ObservabilityAttachmentsProvider
		)
	var accepted: bool = capability.clear_attachments()
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func last_attachment_failures() -> Array:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return []
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityAttachmentsProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return []
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityAttachmentsProvider = (
			provider_value as ObservabilityAttachmentsProvider
		)
	var source: Array = capability.last_attachment_failures()
	var failures: Array = []
	for item: Variant in source:
		if item is ObservabilityAttachmentFailure:
			@warning_ignore("unsafe_cast")
			var failure: ObservabilityAttachmentFailure = item as ObservabilityAttachmentFailure
			failures.append(failure.duplicate())
	_finish_call(provider_call, Error.OK)
	return failures

func capture_event(event: ObservabilityEvent) -> String:
	if event == null:
		return ""
	var snapshot: ObservabilityProviderSnapshot = _session.snapshot()
	var config: ObservabilityConfig = snapshot.config()
	var normalization: ObservabilityNormalizationResult[ObservabilityEvent] = (
			_normalizer.normalize_event(event, config)
		)
	var normalized: ObservabilityEvent? = normalization.value()
	if not normalization.valid() or normalized == null:
		_set_error(normalization.error())
		return ""
	if not config.enabled():
		return ""
	if normalized.kind() == &"log" \
			and (
				not config.processing().logs_enabled()
				or normalized.level() < config.processing().log_minimum_level()
			):
		return ""
	var provider_call: ObservabilityProviderCall = _begin_provider_call(snapshot)
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return ""
	_set_error(Error.OK)
	var pipeline: ObservabilityProcessingPipeline = provider_call.pipeline()
	var processing: ObservabilityProcessingResult[ObservabilityEvent] = (
			pipeline.process_event(normalized)
		)
	var nested_error: int = _session.nested_error(provider_call)
	var processed: ObservabilityEvent? = processing.value()
	if not processing.is_accepted() or processed == null:
		_finish_processing_rejection(provider_call, processing)
		return ""
	var final_event: ObservabilityEvent = processed
	if processed.timestamp_msec() < 0 or processed.engine_ticks_msec() < 0:
		var final_normalization: ObservabilityNormalizationResult[ObservabilityEvent] = (
				_normalizer.normalize_event(processed, provider_call.config())
			)
		var final_value: ObservabilityEvent? = final_normalization.value()
		if not final_normalization.valid() or final_value == null:
			pipeline.record_provider_result(
					processing.processing_signal(),
					false,
					final_normalization.error(),
					processing.operation_token(),
				)
			_finish_call(provider_call, final_normalization.error())
			return ""
		final_event = final_value
	var event_scope: ObservabilityScope? = final_event.scope()
	var event_id: String = ""
	var provider_error: int = Error.OK
	var provider_value: Variant = provider_call.provider()
	if event_scope != null and not event_scope.is_empty() \
			and not (provider_value is ObservabilityScopeProvider):
		provider_error = Error.ERR_UNAVAILABLE
	else:
		event_id = provider_call.provider().capture(final_event)
		if event_id.is_empty():
			provider_error = Error.FAILED
	pipeline.record_provider_result(
			processing.processing_signal(),
			not event_id.is_empty(),
			provider_error,
			processing.operation_token(),
		)
	_finish_call(
			provider_call,
			nested_error if nested_error != Error.OK else provider_error,
		)
	return event_id

func capture_message(
		message: String,
		level: int = ObservabilityLevel.INFO,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String:
	return capture_event(ObservabilityEvent.new(
			p_kind = &"message",
			p_level = level,
			p_message = message,
			p_source = &"game",
			p_attributes = attributes,
			p_scope = scope,
		))

func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String:
	if exception == null:
		_set_error(Error.FAILED)
		return ""
	return capture_event(ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = exception.message(),
			p_source = &"game",
			p_attributes = attributes,
			p_exception = exception,
			p_scope = scope,
		))

func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
		engine_ticks_msec: int = -1,
		scope: ObservabilityScope? = null,
) -> String:
	return capture_event(ObservabilityEvent.new(
			p_kind = &"log",
			p_level = level,
			p_message = message,
			p_source = source,
			p_timestamp_msec = timestamp_msec,
			p_attributes = attributes,
			p_engine_ticks_msec = engine_ticks_msec,
			p_scope = scope,
		))

func set_tag(key: String, value: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_tag(key, value):
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _begin_provider_call(
			_session.snapshot(),
		)
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.set_tag(key, value)
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func remove_tag(key: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_tag(key, ""):
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.remove_tag(key)
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func clear_tags() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.clear_tags()
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

@warning_ignore("shadowed_variable_base_class")
func set_context(name: String, value: Dictionary) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_context(name, value):
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var processing: ObservabilityProcessingResult[Dictionary] = (
			provider_call.pipeline().redact_contexts({name: value})
		)
	var contexts: Dictionary? = processing.value()
	if not processing.is_accepted() or contexts == null:
		_finish_processing_rejection(provider_call, processing)
		return false
	var redacted: Dictionary = {}
	if contexts.get(name) is Dictionary:
		@warning_ignore("unsafe_cast")
		redacted = contexts.get(name) as Dictionary
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.set_context(name, redacted)
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

@warning_ignore("shadowed_variable_base_class")
func remove_context(name: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_context(name, {}):
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.remove_context(name)
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func clear_contexts() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.clear_contexts()
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func set_user(user: ObservabilityUser) -> bool:
	if user == null or not user.is_valid():
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var processing: ObservabilityProcessingResult[Dictionary] = (
			provider_call.pipeline().redact_user(user)
		)
	var payload: Dictionary? = processing.value()
	if not processing.is_accepted() or payload == null \
			or not (payload.get("user") is ObservabilityUser):
		_finish_processing_rejection(provider_call, processing)
		return false
	@warning_ignore("unsafe_cast")
	var redacted: ObservabilityUser = payload.get("user") as ObservabilityUser
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.set_user(redacted)
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func remove_user() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	var capability: ObservabilityScopeProvider? = _scope_capability(provider_call)
	if capability == null:
		return false
	var accepted: bool = capability.remove_user()
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted

func _scope_capability(
		provider_call: ObservabilityProviderCall,
) -> ObservabilityScopeProvider?:
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return null
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityScopeProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return null
	@warning_ignore("unsafe_cast")
	return provider_value as ObservabilityScopeProvider

func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	return _capture_breadcrumb(breadcrumb, true, true)


func clear_breadcrumbs() -> bool:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityBreadcrumbsProvider):
		_finish_call(provider_call, Error.ERR_UNAVAILABLE)
		return false
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityBreadcrumbsProvider = (
			provider_value as ObservabilityBreadcrumbsProvider
		)
	var accepted: bool = capability.clear_breadcrumbs()
	_finish_call(provider_call, Error.OK if accepted else Error.FAILED)
	return accepted


## Captures an automatic breadcrumb without reporting an absent optional capability.
func _capture_automatic_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	return _capture_breadcrumb(breadcrumb, false, false)

func _capture_breadcrumb(
		breadcrumb: ObservabilityBreadcrumb,
		report_unsupported: bool,
		report_success: bool,
) -> bool:
	if breadcrumb == null:
		_set_error(Error.ERR_INVALID_PARAMETER)
		return false
	var provider_call: ObservabilityProviderCall = _begin_provider_call(
			_session.snapshot(),
		)
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	var processing: ObservabilityProcessingResult[Dictionary] = (
			provider_call.pipeline().redact_breadcrumb(breadcrumb)
		)
	var payload: Dictionary? = processing.value()
	if not processing.is_accepted() or payload == null \
			or not (payload.get("breadcrumb") is ObservabilityBreadcrumb):
		_finish_processing_rejection(provider_call, processing)
		return false
	var provider_value: Variant = provider_call.provider()
	if not (provider_value is ObservabilityBreadcrumbsProvider):
		_finish_call(
				provider_call,
				Error.ERR_UNAVAILABLE if report_unsupported else _last_error,
			)
		return false
	@warning_ignore("unsafe_cast")
	var capability: ObservabilityBreadcrumbsProvider = (
			provider_value as ObservabilityBreadcrumbsProvider
		)
	@warning_ignore("unsafe_cast")
	var redacted: ObservabilityBreadcrumb = (
			payload.get("breadcrumb") as ObservabilityBreadcrumb
		)
	var accepted: bool = capability.capture_breadcrumb(redacted)
	var result: int = Error.OK if accepted else Error.FAILED
	if accepted and not report_success:
		result = _last_error
	_finish_call(provider_call, result)
	return accepted

func capture_feedback(feedback: ObservabilityFeedback) -> String:
	var normalization: ObservabilityNormalizationResult[ObservabilityFeedback] = (
			_normalizer.normalize_feedback(feedback)
		)
	var normalized: ObservabilityFeedback? = normalization.value()
	if not normalization.valid() or normalized == null:
		_set_error(normalization.error())
		return ""
	var provider_call: ObservabilityProviderCall = _begin_provider_call(
			_session.snapshot(),
		)
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return ""
	var event_id: String = provider_call.provider().capture_feedback(normalized)
	_finish_call(provider_call, Error.OK if not event_id.is_empty() else Error.FAILED)
	return event_id

func capture_metric(metric: ObservabilityMetric) -> bool:
	var snapshot: ObservabilityProviderSnapshot = _session.snapshot()
	var normalization: ObservabilityNormalizationResult[ObservabilityMetric] = (
			_normalizer.normalize_metric(metric, snapshot.config())
	)
	return _capture_normalized_metric(normalization, snapshot)

func _capture_normalized_metric(
		normalization: ObservabilityNormalizationResult[ObservabilityMetric],
		expected: ObservabilityProviderSnapshot,
) -> bool:
	var normalized: ObservabilityMetric? = normalization.value()
	if not normalization.valid() or normalized == null:
		_set_error(normalization.error())
		return false
	var config: ObservabilityConfig = expected.config()
	if not config.enabled() or not config.processing().metrics_enabled():
		return false
	var provider_call: ObservabilityProviderCall = _begin_provider_call(expected)
	if not provider_call.accepted():
		_record_rejected_call(provider_call)
		return false
	_set_error(Error.OK)
	var pipeline: ObservabilityProcessingPipeline = provider_call.pipeline()
	var processing: ObservabilityProcessingResult[ObservabilityMetric] = (
			pipeline.process_metric(normalized)
		)
	var nested_error: int = _session.nested_error(provider_call)
	var processed: ObservabilityMetric? = processing.value()
	if not processing.is_accepted() or processed == null:
		_finish_processing_rejection(provider_call, processing)
		return false
	var accepted: bool = false
	var provider_error: int = Error.ERR_UNAVAILABLE
	## Foundry Script currently requires this narrow Variant bridge when testing an
	## independent provider capability trait implemented beside ObservabilityProvider.
	var provider_value: Variant = provider_call.provider()
	if provider_value is ObservabilityMetricsProvider:
		@warning_ignore("unsafe_cast")
		var capability: ObservabilityMetricsProvider = (
				provider_value as ObservabilityMetricsProvider
			)
		accepted = capability.capture_metric(processed)
		provider_error = Error.OK if accepted else Error.FAILED
	pipeline.record_provider_result(
			processing.processing_signal(),
			accepted,
			provider_error,
			processing.operation_token(),
		)
	_finish_call(
			provider_call,
			nested_error if nested_error != Error.OK else provider_error,
		)
	return accepted

func capture_counter(
		metric_name: String,
		value: int = 1,
		attributes: Dictionary = {},
) -> bool:
	var snapshot: ObservabilityProviderSnapshot = _session.snapshot()
	var normalized: ObservabilityNormalizationResult[ObservabilityMetric] = (
			_normalizer.counter(metric_name, value, attributes, snapshot.config())
	)
	return _capture_normalized_metric(normalized, snapshot)

func capture_gauge(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	var snapshot: ObservabilityProviderSnapshot = _session.snapshot()
	var normalized: ObservabilityNormalizationResult[ObservabilityMetric] = (
			_normalizer.gauge(metric_name, value, unit, attributes, snapshot.config())
	)
	return _capture_normalized_metric(normalized, snapshot)

func capture_distribution(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	var snapshot: ObservabilityProviderSnapshot = _session.snapshot()
	var normalized: ObservabilityNormalizationResult[ObservabilityMetric] = (
			_normalizer.distribution(
					metric_name, value, unit, attributes, snapshot.config(),
				)
		)
	return _capture_normalized_metric(normalized, snapshot)

func _finish_processing_rejection[T](
		provider_call: ObservabilityProviderCall,
		result: ObservabilityProcessingResult[T],
) -> void:
	var error: int = result.error()
	if error == Error.OK:
		var diagnostic: ObservabilityProcessingDiagnostic? = (
				provider_call.pipeline().last_diagnostic()
			)
		if diagnostic != null:
			error = diagnostic.error()
	_finish_call(provider_call, error)

func _record_rejected_call(
		provider_call: ObservabilityProviderCall,
		update_public: bool = true,
) -> void:
	var error: int = provider_call.error()
	if update_public and error != Error.ERR_UNCONFIGURED:
		_set_error(error)
	elif error != Error.OK:
		_session.record_current_call_error(error)

func _begin_provider_call(
		expected: ObservabilityProviderSnapshot,
) -> ObservabilityProviderCall:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if provider_call.accepted() \
			and (
				provider_call.generation() != expected.generation()
				or not is_same(provider_call.provider(), expected.provider())
				or not is_same(provider_call.config(), expected.config())
				or not is_same(provider_call.pipeline(), expected.pipeline())
			):
		_session.end_call(provider_call)
		return ObservabilityProviderCall.rejected(Error.ERR_UNCONFIGURED)
	return provider_call

func _finish_call(provider_call: ObservabilityProviderCall, error: int) -> void:
	_set_error(error, false)
	_session.finish_call(provider_call, error)

func _record_automatic_capture_error(error: int) -> void:
	if error != Error.OK:
		_set_error(error, false)

func _begin_automatic_capture() -> ObservabilityProviderCall:
	var provider_call: ObservabilityProviderCall = _session.begin_call()
	if not provider_call.accepted():
		return provider_call
	if _session.in_flight_call_count() != 1:
		_session.end_call(provider_call)
		return ObservabilityProviderCall.rejected(Error.ERR_BUSY)
	return provider_call

func _end_automatic_capture(provider_call: ObservabilityProviderCall) -> void:
	var error: int = _session.nested_error(provider_call)
	## Preserve the existing public contract: automatic failures become visible,
	## while a successful finalization does not overwrite another owner's error.
	_record_automatic_capture_error(error)
	_session.finish_call(provider_call, error)

func _contains_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false

func _refresh_automatic_logger(snapshot: ObservabilityProviderSnapshot) -> void:
	var automatic: ObservabilityAutomaticCaptureConfig = (
			snapshot.config().automatic_capture()
		)
	if not snapshot.enabled() or not automatic.enabled():
		_remove_automatic_logger()
		return
	if _automatic_logger != null:
		_automatic_logger.reconfigure(automatic)
		return
	_automatic_logger = AutomaticObservabilityLogger.new(self, automatic, _runtime)
	_automatic_logger.install()

func _remove_automatic_logger() -> void:
	if _automatic_logger == null:
		return
	_automatic_logger.remove()
	_automatic_logger = null

func flush(timeout_msec: int = 2000) -> int:
	var result: int = _session.flush(timeout_msec)
	_set_error(result, false)
	return result

func shutdown() -> void:
	_remove_automatic_logger()
	_session.shutdown()
	if _session.in_flight_call_count() == 0:
		_set_error(Error.OK, false)

func _exit_tree() -> void:
	shutdown()
