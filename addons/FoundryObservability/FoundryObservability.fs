@autoload
namespace foundry.observability

import foundry.observability.runtime

## Autoload entry point for the provider-neutral game observability API.
class_name FoundryObservability extends Node
uses FoundryObservabilityApi

const _SENTRY_PROVIDER_PATH: String = (
	"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs"
)
const _OBSERVABILITY_PROVIDER_TRAIT_PATH: String = (
	"res://addons/FoundryObservability/ObservabilityProvider.fs"
)

var _provider: ObservabilityProvider
var _config: ObservabilityConfig
var _last_error: int = Error.OK
var _shutdown: bool = false
var _shutdown_requested: bool = false
var _runtime: ObservabilityRuntime
var _pipeline: ObservabilityProcessingPipeline
var _pipeline_mutex: Mutex = Mutex.new()
var _provider_call_count: int = 0
var _configuration_generation: int = 0
var _configuration_in_progress: bool = false
var _automatic_capture_owner: int = -1
var _automatic_capture_failure: int = Error.OK
var _processing_failures: Dictionary = {}
var _processing_failure_depths: Dictionary = {}
var _automatic_logger: AutomaticObservabilityLogger
var _startup_provider: ObservabilityProvider
var _startup_provider_path: String = _SENTRY_PROVIDER_PATH
var _startup_status: StringName = ObservabilityStartupStatus.NOT_STARTED
var _startup_message: String = "Startup has not run."

const MAX_FEEDBACK_MESSAGE_LENGTH: int = 4096
const MAX_METRIC_NAME_LENGTH: int = 200
const MAX_METRIC_UNIT_LENGTH: int = 64
const MAX_METRIC_ATTRIBUTE_KEY_LENGTH: int = 200


func _init(
	startup_settings: ObservabilityStartupSettings? = null,
	startup_provider_path: String = _SENTRY_PROVIDER_PATH,
	runtime: ObservabilityRuntime? = null,
) -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_runtime = runtime if runtime != null else SystemObservabilityRuntime.new()
	_pipeline = _new_disabled_pipeline()
	_startup_provider_path = startup_provider_path
	if startup_settings == null:
		initialize_from_project_settings()
	else:
		_initialize_startup(startup_settings)


func _new_disabled_pipeline() -> ObservabilityProcessingPipeline:
	var pipeline: ObservabilityProcessingPipeline = ObservabilityProcessingPipeline.new(_runtime)
	pipeline.configure(ObservabilityConfig.new(p_enabled = false))
	return pipeline


func _configure_candidate_pipeline(
		pipeline: ObservabilityProcessingPipeline,
		config: ObservabilityConfig,
) -> int:
	return pipeline.configure(config)


## Rereads project settings and runs the supported startup path.
func initialize_from_project_settings() -> int:
	return _initialize_startup(ObservabilityStartupSettings.from_project_settings())


## Returns the latest startup-settings result.
func startup_status() -> StringName:
	return _startup_status


## Returns a concise explanation of the latest startup-settings result.
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

	var startup_provider: ObservabilityProvider? = _load_startup_provider()
	if startup_provider == null:
		return _record_startup(
				ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
				"The optional Sentry startup provider is unavailable.",
				Error.ERR_UNAVAILABLE,
				settings.debug_enabled(),
			)

	var result: int = configure(startup_provider, settings.observability_config())
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
	if _startup_provider_path == _OBSERVABILITY_PROVIDER_TRAIT_PATH:
		return null
	if not ResourceLoader.exists(_startup_provider_path):
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
	_last_error = result
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
		_:
			return "Automatic startup was skipped."


## Configures a provider and activates it only after successful setup.
## A failed candidate configuration leaves the current provider unchanged.
func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
	if provider == null:
		_last_error = Error.FAILED
		return Error.FAILED
	_pipeline_mutex.lock()
	if _shutdown_requested or _configuration_in_progress or _provider_call_count > 0:
		_last_error = Error.ERR_BUSY
		_pipeline_mutex.unlock()
		return Error.ERR_BUSY
	_configuration_in_progress = true
	var previous_provider: ObservabilityProvider = _provider
	_pipeline_mutex.unlock()

	var candidate_config: ObservabilityConfig = config
	if candidate_config == null:
		candidate_config = ObservabilityConfig.new(p_enabled = false)
	var candidate_pipeline: ObservabilityProcessingPipeline = ObservabilityProcessingPipeline.new(_runtime)
	var pipeline_result: int = _configure_candidate_pipeline(
			candidate_pipeline,
			candidate_config,
		)
	if pipeline_result != Error.OK:
		_pipeline_mutex.lock()
		_last_error = pipeline_result
		_configuration_in_progress = false
		_pipeline_mutex.unlock()
		_complete_requested_shutdown()
		return pipeline_result

	_pipeline_mutex.lock()
	if _shutdown_requested:
		_last_error = Error.ERR_BUSY
		_configuration_in_progress = false
		_pipeline_mutex.unlock()
		_complete_requested_shutdown()
		return Error.ERR_BUSY
	_pipeline_mutex.unlock()

	var result: int = provider.configure(candidate_config)
	if result != Error.OK:
		if provider != previous_provider:
			provider.shutdown()
		_pipeline_mutex.lock()
		_last_error = result
		_configuration_in_progress = false
		_pipeline_mutex.unlock()
		_complete_requested_shutdown()
		return result

	if provider != previous_provider:
		_remove_automatic_logger()
		if previous_provider != null:
			previous_provider.shutdown()

	_pipeline_mutex.lock()
	_provider = provider
	_config = candidate_config
	_pipeline = candidate_pipeline
	_configuration_generation += 1
	_processing_failures.clear()
	_processing_failure_depths.clear()
	_last_error = Error.OK
	_shutdown = false
	_configuration_in_progress = false
	var should_shutdown: bool = _shutdown_requested
	_pipeline_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()
	else:
		_refresh_automatic_logger()
	return Error.OK


## Returns whether the active configuration permits event capture.
func is_enabled() -> bool:
	_pipeline_mutex.lock()
	var enabled: bool = not _shutdown \
			and not _shutdown_requested \
			and not _configuration_in_progress \
			and _config.enabled
	_pipeline_mutex.unlock()
	return enabled


## Returns whether the active provider can currently accept events.
func is_available() -> bool:
	var state: Dictionary = _capture_state()
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	var available: bool = provider.is_available()
	_end_provider_call()
	return available


## Returns the active provider identifier, including null before configuration.
func provider_name() -> StringName:
	var state: Dictionary = _capture_state()
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return &"null"
	var provider_id: StringName = provider.provider_name()
	_end_provider_call()
	return provider_id


## Returns the most recent provider, capture, configuration, or flush error.
func last_error() -> int:
	return _last_error


## Returns a payload-free snapshot of the latest signal processing outcome without changing last_error.
func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?:
	_pipeline_mutex.lock()
	var pipeline: ObservabilityProcessingPipeline = _pipeline
	_pipeline_mutex.unlock()
	return pipeline.last_diagnostic() if pipeline != null else null


func _capture_state() -> Dictionary:
	_pipeline_mutex.lock()
	if _shutdown or _shutdown_requested \
			or _configuration_in_progress \
			or _provider == null or _pipeline == null:
		_pipeline_mutex.unlock()
		return {"valid": false}
	var result: Dictionary = {
		"valid": true,
		"provider": _provider,
		"config": _config,
		"pipeline": _pipeline,
		"generation": _configuration_generation,
	}
	_pipeline_mutex.unlock()
	return result


func _state_config(state: Dictionary) -> ObservabilityConfig:
	@warning_ignore("unsafe_cast")
	return state["config"] as ObservabilityConfig


func _begin_state_provider_call(state: Dictionary) -> ObservabilityProvider?:
	if not state.get("valid", false):
		return null
	@warning_ignore("unsafe_cast")
	var provider: ObservabilityProvider = state["provider"] as ObservabilityProvider
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
	)
	var generation: int = state["generation"]
	if not _try_begin_pinned_provider_call(provider, pipeline, generation):
		return null
	return provider


func _record_processing_rejection(
		pipeline: ObservabilityProcessingPipeline,
		provider: ObservabilityProvider,
		generation: int,
) -> void:
	var diagnostic: ObservabilityProcessingDiagnostic? = pipeline.last_diagnostic()
	if diagnostic == null:
		return
	_pipeline_mutex.lock()
	if not _configuration_in_progress \
			and generation == _configuration_generation \
			and pipeline == _pipeline \
			and provider == _provider:
		_record_capture_result_locked(diagnostic.error())
	_pipeline_mutex.unlock()


func _record_state_processing_rejection(
		state: Dictionary,
		result: Dictionary,
) -> void:
	if not state.get("valid", false):
		return
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
		)
	@warning_ignore("unsafe_cast")
	var provider: ObservabilityProvider = state["provider"] as ObservabilityProvider
	var generation_value: Variant = state.get("generation")
	if not (generation_value is int):
		return
	var generation: int = generation_value
	var error_value: Variant = result.get("error")
	var error: int = (
		error_value
		if error_value is int and error_value != Error.OK
		else Error.ERR_INVALID_DATA
	)
	var reason: StringName = StringName(str(result.get("reason", &"")))
	var owner_id: int = _runtime.caller_id()
	_pipeline_mutex.lock()
	if not _configuration_in_progress \
			and generation == _configuration_generation \
			and pipeline == _pipeline \
			and provider == _provider:
		_record_capture_result_locked(error)
		if reason == ObservabilityProcessingDiagnostic.RECURSIVE:
			var depth: int = _processing_failure_depth(owner_id)
			if depth > 0:
				_processing_failures[_processing_failure_key(owner_id, depth)] = error
	_pipeline_mutex.unlock()


func _begin_processing_failure_scope() -> void:
	var owner_id: int = _runtime.caller_id()
	_pipeline_mutex.lock()
	var depth: int = _processing_failure_depth(owner_id) + 1
	_processing_failure_depths[owner_id] = depth
	_processing_failures.erase(_processing_failure_key(owner_id, depth))
	_pipeline_mutex.unlock()


func _take_processing_failure() -> int:
	var owner_id: int = _runtime.caller_id()
	_pipeline_mutex.lock()
	var depth: int = _processing_failure_depth(owner_id)
	var key: String = _processing_failure_key(owner_id, depth)
	var error_value: Variant = _processing_failures.get(key, Error.OK)
	_processing_failures.erase(key)
	if depth <= 1:
		_processing_failure_depths.erase(owner_id)
	else:
		_processing_failure_depths[owner_id] = depth - 1
	_pipeline_mutex.unlock()
	return error_value if error_value is int else Error.ERR_INVALID_DATA


func _processing_failure_key(owner_id: int, depth: int) -> String:
	return "%s:%s" % [owner_id, depth]


func _processing_failure_depth(owner_id: int) -> int:
	var value: Variant = _processing_failure_depths.get(owner_id, 0)
	return value if value is int else 0


## Adds one persistent diagnostic attachment through an optional provider capability.
func add_attachment(attachment: ObservabilityAttachment) -> String:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return ""
	if attachment == null or not attachment.is_valid():
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
		)
	var redaction: Dictionary = pipeline.redact_attachment(attachment)
	if not redaction.get("valid", false) \
			or not (redaction.get("value") is ObservabilityAttachment):
		_record_state_processing_rejection(state, redaction)
		return ""
	@warning_ignore("unsafe_cast")
	var redacted_attachment: ObservabilityAttachment = (
			redaction["value"] as ObservabilityAttachment
		)
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return ""
	var attachments_provider: ObservabilityProvider? = _attachments_provider(provider)
	if attachments_provider == null:
		_finish_provider_call(Error.ERR_UNAVAILABLE)
		return ""

	var result: Variant = attachments_provider.call(
			"add_attachment",
			redacted_attachment,
	)
	if not (result is String):
		_finish_provider_call(Error.FAILED)
		return ""
	var handle: String = str(result)
	if handle.is_empty():
		_finish_provider_call(Error.FAILED)
		return ""
	_finish_provider_call(Error.OK)
	return handle


## Removes one persistent diagnostic attachment through an optional provider capability.
func remove_attachment(handle: String) -> bool:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	if handle.is_empty() or handle.strip_edges() != handle or _has_control_character(handle):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	var attachments_provider: ObservabilityProvider? = _attachments_provider(provider)
	if attachments_provider == null:
		_finish_provider_call(Error.ERR_UNAVAILABLE)
		return false

	var result: Variant = attachments_provider.call("remove_attachment", handle)
	if not (result is int):
		_finish_provider_call(Error.FAILED)
		return false
	var error: int = result
	_finish_provider_call(error)
	return error == Error.OK


## Clears all persistent diagnostic attachments through an optional provider capability.
func clear_attachments() -> bool:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	var attachments_provider: ObservabilityProvider? = _attachments_provider(provider)
	if attachments_provider == null:
		_finish_provider_call(Error.ERR_UNAVAILABLE)
		return false

	var result: Variant = attachments_provider.call("clear_attachments")
	if not (result is bool) or not result:
		_finish_provider_call(Error.FAILED)
		return false
	_finish_provider_call(Error.OK)
	return true


## Returns isolated failures from the latest attachment-bearing provider event.
## This diagnostic accessor intentionally leaves last_error unchanged.
func last_attachment_failures() -> Array:
	var state: Dictionary = _capture_state()
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return []
	var attachments_provider: ObservabilityProvider? = _attachments_provider(provider)
	if attachments_provider == null:
		_end_provider_call()
		return []
	var result: Variant = attachments_provider.call("last_attachment_failures")
	_end_provider_call()
	if not (result is Array):
		return []
	var failures: Array = []
	for item: Variant in result:
		if item is ObservabilityAttachmentFailure:
			@warning_ignore("unsafe_cast")
			var failure: ObservabilityAttachmentFailure = item as ObservabilityAttachmentFailure
			failures.append(failure.duplicate())
	return failures


func _attachments_provider(provider: ObservabilityProvider) -> ObservabilityProvider?:
	if provider == null:
		return null
	if not provider.has_method("add_attachment") \
			or not provider.has_method("remove_attachment") \
			or not provider.has_method("clear_attachments") \
			or not provider.has_method("last_attachment_failures"):
		return null
	return provider


## Captures an event and returns its provider ID, or an empty string on no-op or failure.
func capture_event(event: ObservabilityEvent) -> String:
	if event == null:
		return ""
	var state: Dictionary = _capture_state()
	if not state["valid"]:
		return ""
	@warning_ignore("unsafe_cast")
	var provider: ObservabilityProvider = state["provider"] as ObservabilityProvider
	@warning_ignore("unsafe_cast")
	var config: ObservabilityConfig = state["config"] as ObservabilityConfig
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
	)
	var generation: int = state["generation"]
	if not config.enabled:
		return ""
	var capture_engine_ticks_msec: int = _runtime.monotonic_time_msec()
	var capture_unix_msec: int = _runtime.unix_time_msec()
	var normalized: ObservabilityEvent = _resolved_event_timestamp(
			event,
			capture_unix_msec,
			capture_engine_ticks_msec,
		)
	if normalized.kind() == &"log":
		if not config.logs_enabled or normalized.level() < config.log_minimum_level:
			return ""
	var processable: ObservabilityEvent = _normalized_exception_event(normalized, config)
	_begin_processing_failure_scope()
	var result: Dictionary = pipeline.process_event(processable)
	var processing_error: int = _take_processing_failure()
	if not result.get("accepted", false):
		if processing_error != Error.OK:
			_pipeline_mutex.lock()
			if not _configuration_in_progress \
					and generation == _configuration_generation \
					and pipeline == _pipeline \
					and provider == _provider:
				_record_capture_result_locked(processing_error)
			_pipeline_mutex.unlock()
		else:
			_record_processing_rejection(pipeline, provider, generation)
		return ""
	@warning_ignore("unsafe_cast")
	var processed: ObservabilityEvent = result["value"] as ObservabilityEvent
	var final_event: ObservabilityEvent = _normalized_exception_event(
			_resolved_event_timestamp(
					processed,
					capture_unix_msec,
					capture_engine_ticks_msec,
				),
			config,
		)
	if not _try_begin_pinned_provider_call(provider, pipeline, generation):
		pipeline.record_provider_result(
				StringName(str(result["signal"])),
				false,
				Error.ERR_BUSY,
				result["operation_token"],
			)
		return ""
	var event_id: String = ""
	var effective_error: int = Error.OK
	var event_scope: ObservabilityScope? = final_event.scope()
	if event_scope != null \
			and not event_scope.is_empty() \
			and not _has_scope_capability(provider):
		effective_error = Error.ERR_UNAVAILABLE
	else:
		event_id = provider.capture(final_event)
		if event_id.is_empty():
			effective_error = Error.FAILED
	var accepted: bool = not event_id.is_empty()
	pipeline.record_provider_result(
			StringName(str(result["signal"])),
			accepted,
			effective_error,
			result["operation_token"],
		)
	_finish_provider_call(
			processing_error if processing_error != Error.OK else effective_error,
		)
	return event_id


## Creates a game-sourced message event using the current wall-clock and engine times.
func capture_message(
		message: String,
		level: int = ObservabilityLevel.INFO,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String:
	return capture_event(
		ObservabilityEvent.new(
			p_kind = &"message",
			p_level = level,
			p_message = message,
			p_source = &"game",
			p_attributes = attributes,
			p_scope = scope,
		),
	)


## Creates a game-sourced ERROR event containing the supplied exception payload.
func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String:
	if exception == null:
		_last_error = Error.FAILED
		return ""
	return capture_event(
		ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = exception.message(),
			p_source = &"game",
			p_attributes = attributes,
			p_exception = exception,
			p_scope = scope,
		),
	)


## Creates a structured log using an optional Unix timestamp and original engine tick.
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


## Sets a provider-owned global session tag.
func set_tag(key: String, value: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_tag(key, value):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	return _call_scope_operation(&"set_tag", [key, value])


## Removes a provider-owned global session tag.
func remove_tag(key: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_tag(key, ""):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	return _call_scope_operation(&"remove_tag", [key])


## Clears all provider-owned global session tags.
func clear_tags() -> bool:
	return _call_scope_operation(&"clear_tags", [])


## Sets a provider-owned global structured context.
func set_context(context_name: String, value: Dictionary) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_context(context_name, value):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
		)
	var redaction: Dictionary = pipeline.redact_contexts({context_name: value})
	if not redaction.get("valid", false) \
			or not (redaction.get("value") is Dictionary):
		_record_state_processing_rejection(state, redaction)
		return false
	@warning_ignore("unsafe_cast")
	var contexts: Dictionary = redaction["value"] as Dictionary
	var redacted_context: Dictionary = {}
	if contexts.has(context_name) and contexts[context_name] is Dictionary:
		@warning_ignore("unsafe_cast")
		redacted_context = contexts[context_name] as Dictionary
	return _call_scope_operation_in_state(
			state,
			&"set_context",
			[context_name, redacted_context],
		)


## Removes a provider-owned global structured context.
func remove_context(context_name: String) -> bool:
	var validation: ObservabilityScope = ObservabilityScope.new()
	if not validation.set_context(context_name, {}):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	return _call_scope_operation(&"remove_context", [context_name])


## Clears all provider-owned global structured contexts.
func clear_contexts() -> bool:
	return _call_scope_operation(&"clear_contexts", [])


## Sets the explicit provider-owned application user.
func set_user(user: ObservabilityUser) -> bool:
	if user == null or not user.is_valid():
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
		)
	var redaction: Dictionary = pipeline.redact_user(user)
	if not redaction.get("valid", false) \
			or not (redaction.get("value") is ObservabilityUser):
		_record_state_processing_rejection(state, redaction)
		return false
	@warning_ignore("unsafe_cast")
	var redacted_user: ObservabilityUser = redaction["value"] as ObservabilityUser
	return _call_scope_operation_in_state(
			state,
			&"set_user",
			[redacted_user],
		)


## Removes the explicit provider-owned application user.
func remove_user() -> bool:
	return _call_scope_operation(&"remove_user", [])


func _call_scope_operation(method_name: StringName, arguments: Array) -> bool:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	return _call_scope_operation_in_state(state, method_name, arguments)


func _call_scope_operation_in_state(
		state: Dictionary,
		method_name: StringName,
		arguments: Array,
) -> bool:
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	if not _has_scope_capability(provider):
		_finish_provider_call(Error.ERR_UNAVAILABLE)
		return false
	return _call_reserved_provider_bool(provider, method_name, arguments)


func _has_scope_capability(provider: ObservabilityProvider) -> bool:
	return provider.has_method("set_tag") \
			and provider.has_method("remove_tag") \
			and provider.has_method("clear_tags") \
			and provider.has_method("set_context") \
			and provider.has_method("remove_context") \
			and provider.has_method("clear_contexts") \
			and provider.has_method("set_user") \
			and provider.has_method("remove_user")


func _call_reserved_provider_bool(
		provider: ObservabilityProvider,
		method_name: StringName,
		arguments: Array,
) -> bool:
	var result: Variant = provider.callv(method_name, arguments)
	if not (result is bool) or not result:
		_finish_provider_call(Error.FAILED)
		return false
	_finish_provider_call(Error.OK)
	return true


## Captures a breadcrumb when the active provider supports the optional capability.
func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	return _capture_breadcrumb(breadcrumb, true, true)


## Clears breadcrumbs when the active provider supports the explicit optional operation.
func clear_breadcrumbs() -> bool:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	if not provider.has_method("clear_breadcrumbs"):
		_finish_provider_call(Error.ERR_UNAVAILABLE)
		return false
	return _call_reserved_provider_bool(provider, &"clear_breadcrumbs", [])


## Captures an automatic breadcrumb without treating an absent optional capability as an error.
func _capture_automatic_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool:
	return _capture_breadcrumb(breadcrumb, false, false)


func _capture_breadcrumb(
		breadcrumb: ObservabilityBreadcrumb,
		report_unsupported: bool,
		report_success: bool,
) -> bool:
	if breadcrumb == null:
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return false
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
		)
	var redaction: Dictionary = pipeline.redact_breadcrumb(breadcrumb)
	if not redaction.get("valid", false) \
			or not (redaction.get("value") is ObservabilityBreadcrumb):
		_record_state_processing_rejection(state, redaction)
		return false
	@warning_ignore("unsafe_cast")
	var redacted_breadcrumb: ObservabilityBreadcrumb = (
			redaction["value"] as ObservabilityBreadcrumb
		)
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return false
	if not provider.has_method("capture_breadcrumb"):
		if report_unsupported:
			_finish_provider_call(Error.ERR_UNAVAILABLE)
		else:
			_end_provider_call()
		return false

	var capture_result: Variant = provider.call(
			"capture_breadcrumb",
			redacted_breadcrumb,
		)
	if not (capture_result is bool) or not capture_result:
		_finish_provider_call(Error.FAILED)
		return false
	if report_success:
		_finish_provider_call(Error.OK)
	else:
		_end_provider_call()
	return true


## Captures explicitly submitted player feedback without creating an error event.
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if not _is_valid_feedback(feedback):
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	return _capture_feedback(feedback)


## Validates, normalizes, filters, samples, and dispatches one custom metric.
func capture_metric(metric: ObservabilityMetric) -> bool:
	var state: Dictionary = _capture_state()
	if not state["valid"]:
		return false
	@warning_ignore("unsafe_cast")
	var provider: ObservabilityProvider = state["provider"] as ObservabilityProvider
	@warning_ignore("unsafe_cast")
	var config: ObservabilityConfig = state["config"] as ObservabilityConfig
	@warning_ignore("unsafe_cast")
	var pipeline: ObservabilityProcessingPipeline = (
			state["pipeline"] as ObservabilityProcessingPipeline
	)
	var generation: int = state["generation"]
	var normalized: ObservabilityMetric? = _normalized_metric(metric, config)
	if normalized == null:
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	if not config.enabled or not config.metrics_enabled:
		return false
	if not provider.has_method("capture_metric"):
		_last_error = Error.ERR_UNAVAILABLE
		return false
	_begin_processing_failure_scope()
	var result: Dictionary = pipeline.process_metric(normalized)
	var processing_error: int = _take_processing_failure()
	if not result.get("accepted", false):
		if processing_error != Error.OK:
			_pipeline_mutex.lock()
			if not _configuration_in_progress \
					and generation == _configuration_generation \
					and pipeline == _pipeline \
					and provider == _provider:
				_record_capture_result_locked(processing_error)
			_pipeline_mutex.unlock()
		else:
			_record_processing_rejection(pipeline, provider, generation)
		return false
	@warning_ignore("unsafe_cast")
	var processed: ObservabilityMetric = result["value"] as ObservabilityMetric

	if not _try_begin_pinned_provider_call(provider, pipeline, generation):
		pipeline.record_provider_result(
				StringName(str(result["signal"])),
				false,
				Error.ERR_BUSY,
				result["operation_token"],
			)
		return false
	var capture_result: Variant = provider.call("capture_metric", processed)
	var accepted: bool = capture_result is bool and capture_result
	var effective_error: int = Error.OK if accepted else Error.FAILED
	pipeline.record_provider_result(
			StringName(str(result["signal"])),
			accepted,
			effective_error,
			result["operation_token"],
		)
	_finish_provider_call(
			processing_error if processing_error != Error.OK else effective_error,
		)
	return accepted


## Creates and captures a counter metric.
func capture_counter(metric_name: String, value: int = 1, attributes: Dictionary = {}) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.COUNTER,
			p_name = metric_name,
			p_value = float(value),
			p_attributes = attributes,
		))


## Creates and captures a gauge metric.
func capture_gauge(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE,
			p_name = metric_name,
			p_value = value,
			p_unit = unit,
			p_attributes = attributes,
		))


## Creates and captures a distribution metric.
func capture_distribution(
		metric_name: String,
		value: float,
		unit: String = "",
		attributes: Dictionary = {},
) -> bool:
	return capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.DISTRIBUTION,
			p_name = metric_name,
			p_value = value,
			p_unit = unit,
			p_attributes = attributes,
	))


static func _unix_msec_from_engine_ticks(
		event_engine_ticks_msec: int,
		capture_engine_ticks_msec: int,
		capture_unix_msec: int,
) -> int:
	return capture_unix_msec + event_engine_ticks_msec - capture_engine_ticks_msec


func _resolved_event_timestamp(
		event: ObservabilityEvent,
		capture_unix_msec: int,
		capture_engine_ticks_msec: int,
) -> ObservabilityEvent:
	if event.timestamp_msec() >= 0:
		return event
	var resolved_unix_msec: int = capture_unix_msec
	var resolved_engine_ticks_msec: int = event.engine_ticks_msec()
	if resolved_engine_ticks_msec >= 0:
		resolved_unix_msec = _unix_msec_from_engine_ticks(
				resolved_engine_ticks_msec,
				capture_engine_ticks_msec,
				capture_unix_msec,
			)
	else:
		resolved_engine_ticks_msec = capture_engine_ticks_msec
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = resolved_unix_msec,
			p_attributes = event.attributes(),
			p_exception = event.exception(),
			p_engine_ticks_msec = resolved_engine_ticks_msec,
			p_scope = event.scope(),
		)


func _normalized_metric(
		metric: ObservabilityMetric,
		config: ObservabilityConfig,
) -> ObservabilityMetric?:
	if metric == null or not _is_valid_metric_name(metric.name()):
		return null
	if metric.type() < ObservabilityMetricType.COUNTER \
			or metric.type() > ObservabilityMetricType.DISTRIBUTION:
		return null
	if not is_finite(metric.value()):
		return null
	if metric.type() == ObservabilityMetricType.COUNTER:
		if metric.value() < 0.0 or metric.value() != floorf(metric.value()):
			return null
		if not metric.unit().is_empty():
			return null
	elif not _is_valid_metric_unit(metric.unit()):
		return null

	var attributes: Dictionary = {}
	var global_attributes: Dictionary = config.global_attributes()
	if not _is_valid_metric_attributes(global_attributes):
		return null
	for key: Variant in global_attributes.keys():
		attributes[str(key)] = global_attributes[key]
	var metric_attributes: Dictionary = metric.attributes()
	if not _is_valid_metric_attributes(metric_attributes):
		return null
	for key: Variant in metric_attributes.keys():
		attributes[str(key)] = metric_attributes[key]
	return ObservabilityMetric.new(
			p_type = metric.type(),
			p_name = metric.name(),
			p_value = metric.value(),
			p_unit = metric.unit(),
			p_attributes = attributes,
		)


func _is_valid_metric_name(value: String) -> bool:
	return not value.is_empty() \
			and value.length() <= MAX_METRIC_NAME_LENGTH \
			and value.strip_edges() == value \
			and not _has_control_character(value)


func _is_valid_metric_unit(value: String) -> bool:
	return value.length() <= MAX_METRIC_UNIT_LENGTH \
			and not _has_control_character(value) \
			and not _has_whitespace(value)


func _is_valid_metric_attributes(attributes: Dictionary) -> bool:
	for key: Variant in attributes.keys():
		if not (key is String) and not (key is StringName):
			return false
		var key_string: String = str(key)
		if key_string.is_empty() \
				or key_string.length() > MAX_METRIC_ATTRIBUTE_KEY_LENGTH \
				or key_string.strip_edges() != key_string \
				or _has_control_character(key_string):
			return false
		if not _is_valid_metric_attribute_value(attributes[key]):
			return false
	return true


func _is_valid_metric_attribute_value(value: Variant) -> bool:
	if value is bool or value is int or value is String or value is StringName:
		return true
	if value is float:
		return is_finite(value)
	return false


func _has_whitespace(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint == 32 or codepoint == 160 \
				or (codepoint >= 8192 and codepoint <= 8202) \
				or codepoint == 8232 or codepoint == 8233 \
				or codepoint == 8239 or codepoint == 8287 or codepoint == 12288:
			return true
	return false


func _normalized_exception_event(
		event: ObservabilityEvent,
		config: ObservabilityConfig,
) -> ObservabilityEvent:
	var exception: ObservabilityException? = event.exception()
	if exception == null:
		return event
	return ObservabilityEvent.new(
			p_kind = event.kind(),
			p_level = event.level(),
			p_message = event.message(),
			p_source = event.source(),
			p_timestamp_msec = event.timestamp_msec(),
			p_attributes = event.attributes(),
			p_exception = _normalized_exception(exception, config),
			p_engine_ticks_msec = event.engine_ticks_msec(),
			p_scope = event.scope(),
		)


func _normalized_exception(
		exception: ObservabilityException,
		config: ObservabilityConfig,
) -> ObservabilityException:
	var normalized_frames: Array[ObservabilityStackFrame] = []
	for frame: ObservabilityStackFrame in exception.frames():
		if frame == null:
			continue
		if not _is_useful_stack_frame(frame):
			continue
		normalized_frames.append(_normalized_stack_frame(frame, config))
	return ObservabilityException.new(
			p_type_name = exception.type_name(),
			p_message = exception.message(),
			p_stack_trace = exception.stack_trace(),
			p_attributes = exception.attributes(),
			p_frames = normalized_frames,
	)


func _is_useful_stack_frame(frame: ObservabilityStackFrame) -> bool:
	return not frame.file().is_empty() \
			or not frame.function().is_empty() \
			or not frame.language().is_empty() \
			or frame.line() >= 1


func _normalized_stack_frame(
		frame: ObservabilityStackFrame,
		config: ObservabilityConfig,
) -> ObservabilityStackFrame:
	var line: int = frame.line()
	if line < 1:
		line = -1

	var context_line: String = ""
	var pre_context: PackedStringArray = PackedStringArray()
	var post_context: PackedStringArray = PackedStringArray()
	if config.stack_trace_source_context_enabled:
		context_line = frame.context_line()
		if not context_line.is_empty():
			var source_pre_context: PackedStringArray = frame.pre_context()
			for index: int in range(maxi(0, source_pre_context.size() - 5), source_pre_context.size()):
				pre_context.append(source_pre_context[index])
			var source_post_context: PackedStringArray = frame.post_context()
			for index: int in range(mini(5, source_post_context.size())):
				post_context.append(source_post_context[index])

	var variables: Dictionary = {}
	if config.stack_trace_variables_enabled:
		variables = frame._bounded_sanitized_variables(
				ObservabilityStackFrame.MAX_VARIABLE_CONTAINER_DEPTH,
				ObservabilityStackFrame.MAX_VARIABLE_ITEMS,
		)
	return ObservabilityStackFrame.new(
			p_file = frame.file(),
			p_function = frame.function(),
			p_line = line,
			p_language = frame.language(),
			p_in_app = frame.in_app(),
			p_context_line = context_line,
			p_pre_context = pre_context,
			p_post_context = post_context,
			p_variables = variables,
	)


func _capture_feedback(feedback: ObservabilityFeedback) -> String:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false) or not _state_config(state).enabled:
		return ""
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return ""

	var feedback_id: String = provider.capture_feedback(feedback)
	if feedback_id.is_empty():
		_finish_provider_call(Error.FAILED)
	else:
		_end_provider_call()
	return feedback_id


func _is_valid_feedback(feedback: ObservabilityFeedback) -> bool:
	if feedback == null:
		return false
	var message: String = feedback.message()
	if message.strip_edges().is_empty() or message.length() > MAX_FEEDBACK_MESSAGE_LENGTH:
		return false
	if not _is_valid_optional_text(feedback.name()):
		return false
	if not _is_valid_email(feedback.contact_email()):
		return false
	return _is_valid_optional_text(feedback.associated_event_id())


func _is_valid_optional_text(value: String) -> bool:
	if value.is_empty():
		return true
	if value.strip_edges().is_empty():
		return false
	return not _has_control_character(value)


func _is_valid_email(email: String) -> bool:
	if email.is_empty():
		return true
	if not _is_valid_optional_text(email):
		return false
	if email.find(" ") >= 0:
		return false
	var at_index: int = email.find("@")
	return at_index > 0 and at_index < email.length() - 1 and at_index == email.rfind("@")


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false


func _try_begin_pinned_provider_call(
		provider: ObservabilityProvider,
		pipeline: ObservabilityProcessingPipeline,
		generation: int,
) -> bool:
	_pipeline_mutex.lock()
	var valid: bool = not _shutdown \
			and not _shutdown_requested \
			and not _configuration_in_progress \
			and provider == _provider \
			and pipeline == _pipeline \
			and generation == _configuration_generation
	if valid:
		_provider_call_count += 1
	_pipeline_mutex.unlock()
	return valid


func _end_provider_call() -> void:
	_pipeline_mutex.lock()
	_provider_call_count = maxi(0, _provider_call_count - 1)
	var should_shutdown: bool = _shutdown_requested \
			and not _shutdown \
			and not _configuration_in_progress \
			and _provider_call_count == 0
	_pipeline_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func _finish_provider_call(error: int) -> void:
	_pipeline_mutex.lock()
	_record_capture_result_locked(error)
	_provider_call_count = maxi(0, _provider_call_count - 1)
	var should_shutdown: bool = _shutdown_requested \
			and not _shutdown \
			and not _configuration_in_progress \
			and _provider_call_count == 0
	_pipeline_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func _record_capture_result_locked(error: int) -> void:
	var automatic_owner: bool = _automatic_capture_owner == _runtime.caller_id()
	if automatic_owner and error != Error.OK and _automatic_capture_failure == Error.OK:
		_automatic_capture_failure = error
	if automatic_owner and _automatic_capture_failure != Error.OK:
		_last_error = _automatic_capture_failure
	else:
		_last_error = error


## Atomically reserves the provider pipeline for one automatic logger callback.
func try_begin_automatic_capture() -> bool:
	if not _pipeline_mutex.try_lock():
		return false
	if _shutdown or _shutdown_requested \
			or _configuration_in_progress or _provider_call_count > 0:
		_pipeline_mutex.unlock()
		return false
	_provider_call_count += 1
	_automatic_capture_owner = _runtime.caller_id()
	_automatic_capture_failure = Error.OK
	_pipeline_mutex.unlock()
	return true


## Releases a successful automatic logger callback reservation.
func end_automatic_capture() -> void:
	_pipeline_mutex.lock()
	var failure: int = _automatic_capture_failure
	_automatic_capture_owner = -1
	_automatic_capture_failure = Error.OK
	_provider_call_count = maxi(0, _provider_call_count - 1)
	if failure != Error.OK:
		_last_error = failure
	var should_shutdown: bool = _shutdown_requested \
			and not _shutdown \
			and not _configuration_in_progress \
			and _provider_call_count == 0
	_pipeline_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func _refresh_automatic_logger() -> void:
	var should_install: bool = _config.enabled and _config.automatic_capture_enabled
	if not should_install:
		_remove_automatic_logger()
		return
	if _automatic_logger != null:
		_automatic_logger.reconfigure(_config)
		return
	_automatic_logger = AutomaticObservabilityLogger.new(self, _config, _runtime)
	OS.add_logger(_automatic_logger)


func _remove_automatic_logger() -> void:
	if _automatic_logger == null:
		return
	OS.remove_logger(_automatic_logger)
	_automatic_logger.reset()
	_automatic_logger = null


## Flushes pending provider work within timeout_msec and stores the returned error.
func flush(timeout_msec: int = 2000) -> int:
	var state: Dictionary = _capture_state()
	if not state.get("valid", false):
		_pipeline_mutex.lock()
		var inactive_result: int = Error.OK if _shutdown else Error.ERR_BUSY
		_pipeline_mutex.unlock()
		return inactive_result
	var provider: ObservabilityProvider? = _begin_state_provider_call(state)
	if provider == null:
		return Error.ERR_BUSY
	var result: int = provider.flush(timeout_msec)
	_finish_provider_call(result)
	return result


## Flushes and shuts down once, then restores the disabled null-provider state.
func shutdown() -> void:
	_pipeline_mutex.lock()
	if _shutdown and not _configuration_in_progress:
		_pipeline_mutex.unlock()
		return
	_shutdown_requested = true
	var should_shutdown: bool = not _configuration_in_progress \
			and _provider_call_count == 0
	_pipeline_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func _complete_requested_shutdown() -> void:
	_pipeline_mutex.lock()
	if not _shutdown_requested \
			or _configuration_in_progress \
			or _provider_call_count > 0:
		_pipeline_mutex.unlock()
		return
	if _shutdown:
		_shutdown_requested = false
		_last_error = Error.OK
		_pipeline_mutex.unlock()
		return
	_configuration_in_progress = true
	var previous_provider: ObservabilityProvider = _provider
	_pipeline_mutex.unlock()

	_remove_automatic_logger()
	if previous_provider != null:
		previous_provider.flush(2000)
		previous_provider.shutdown()

	var disabled_config: ObservabilityConfig = ObservabilityConfig.new(p_enabled = false)
	var disabled_pipeline: ObservabilityProcessingPipeline = _new_disabled_pipeline()
	_pipeline_mutex.lock()
	_provider = NullObservabilityProvider.new()
	_config = disabled_config
	_pipeline = disabled_pipeline
	_configuration_generation += 1
	_shutdown = true
	_last_error = Error.OK
	_configuration_in_progress = false
	_shutdown_requested = false
	_automatic_capture_owner = -1
	_automatic_capture_failure = Error.OK
	_processing_failures.clear()
	_processing_failure_depths.clear()
	_pipeline_mutex.unlock()


## Shuts down the service when its autoload leaves the scene tree.
func _exit_tree() -> void:
	shutdown()
