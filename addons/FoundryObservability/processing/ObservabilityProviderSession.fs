namespace foundry.observability.processing

import foundry.observability
import foundry.observability.runtime

## Owns provider replacement, generation pinning, in-flight calls, flush, and shutdown.
final class_name ObservabilityProviderSession extends RefCounted

final var _runtime: ObservabilityRuntime
final var _mutex: Mutex = Mutex.new()
final var _claim_token: ObservabilityPipelineClaimToken = ObservabilityPipelineClaimToken.new()
var _snapshot: ObservabilityProviderSnapshot
var _in_flight_calls: int = 0
final var _active_calls: Dictionary[ObservabilityProviderCall, bool] = {}
var _configuration_in_progress: bool = false
var _shutdown_requested_state: bool = false
var _shutdown_complete: bool = false


func _init(runtime: ObservabilityRuntime) -> void:
	assert(runtime != null, "ObservabilityProviderSession requires a runtime.")
	_runtime = runtime
	_snapshot = _disabled_snapshot(0)


func snapshot() -> ObservabilityProviderSnapshot:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		push_error("ObservabilityProviderSession lost current pipeline ownership.")
	var current: ObservabilityProviderSnapshot = _snapshot
	_mutex.unlock()
	return current


func replace(
		provider: ObservabilityProvider,
		config: ObservabilityConfig,
		pipeline: ObservabilityProcessingPipeline,
) -> int:
	if provider == null:
		return Error.FAILED
	if config == null or pipeline == null:
		return Error.ERR_INVALID_PARAMETER
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		_mutex.unlock()
		return Error.ERR_ALREADY_IN_USE
	if _shutdown_requested_state or _configuration_in_progress or _in_flight_calls > 0:
		_mutex.unlock()
		return Error.ERR_BUSY
	var previous: ObservabilityProviderSnapshot = _snapshot
	if is_same(provider, previous.provider()):
		var is_exact_no_op: bool = is_same(config, previous.config()) \
				and is_same(pipeline, previous.pipeline())
		_mutex.unlock()
		## Active providers are never configured in place: only the exact committed
		## provider/config/pipeline identity triple is an idempotent replacement.
		return Error.OK if is_exact_no_op else Error.ERR_ALREADY_IN_USE
	_configuration_in_progress = true
	_mutex.unlock()

	var reuses_previous_pipeline: bool = is_same(pipeline, previous.pipeline())
	var claim_result: int = pipeline.claim(config, _claim_token)
	if claim_result != Error.OK:
		_finish_configuration_transition()
		return claim_result

	var result: int = provider.configure(config)
	if result != Error.OK:
		provider.shutdown()
		if not reuses_previous_pipeline:
			_release_unpublished_pipeline(pipeline)
		_finish_configuration_transition()
		return result

	_mutex.lock()
	if not _owns_current_pipeline_locked() or not pipeline.is_claimed_by(_claim_token):
		_mutex.unlock()
		provider.shutdown()
		if not reuses_previous_pipeline:
			_release_unpublished_pipeline(pipeline)
		_finish_configuration_transition()
		return Error.ERR_ALREADY_IN_USE
	var freeze_result: int = pipeline.freeze(_claim_token)
	if freeze_result != Error.OK:
		_mutex.unlock()
		provider.shutdown()
		if not reuses_previous_pipeline:
			_release_unpublished_pipeline(pipeline)
		_finish_configuration_transition()
		return freeze_result
	_snapshot = ObservabilityProviderSnapshot.new(
			provider,
			config,
			pipeline,
			previous.generation() + 1,
		)
	_shutdown_complete = false
	_mutex.unlock()

	## The new generation is authoritative before detached-provider callbacks run.
	## Admission remains closed until detached-provider shutdown finishes. Every
	## published pipeline remains permanently frozen for retained snapshots/calls.
	previous.provider().shutdown()
	_finish_configuration_transition()
	return Error.OK


@warning_ignore("shadowed_variable_base_class")
func begin_call() -> ObservabilityProviderCall:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		_mutex.unlock()
		return ObservabilityProviderCall.rejected(Error.ERR_ALREADY_IN_USE)
	if _shutdown_requested_state or _configuration_in_progress or _shutdown_complete:
		_mutex.unlock()
		return ObservabilityProviderCall.rejected(Error.ERR_BUSY)
	if not _snapshot.enabled():
		_mutex.unlock()
		return ObservabilityProviderCall.rejected(Error.ERR_UNCONFIGURED)
	var provider_call: ObservabilityProviderCall = ObservabilityProviderCall.begin(_snapshot)
	_active_calls[provider_call] = true
	_in_flight_calls += 1
	_mutex.unlock()
	return provider_call


@warning_ignore("shadowed_variable_base_class")
func end_call(call: ObservabilityProviderCall) -> void:
	if call == null or not call.accepted():
		return
	_mutex.lock()
	if not _owns_current_pipeline_locked() or not _active_calls.has(call):
		_mutex.unlock()
		return
	_active_calls.erase(call)
	_in_flight_calls = maxi(0, _in_flight_calls - 1)
	var should_shutdown: bool = _shutdown_requested_state \
			and not _shutdown_complete \
			and not _configuration_in_progress \
			and _in_flight_calls == 0
	_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


@warning_ignore("unused_parameter")
@warning_ignore("shadowed_variable_base_class")
func finish_call(call: ObservabilityProviderCall, error: int) -> void:
	## The facade maps the provider error into public last_error; the session owns release.
	end_call(call)


@warning_ignore("shadowed_variable_base_class")
func flush(timeout_msec: int = 2000) -> int:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		_mutex.unlock()
		return Error.ERR_ALREADY_IN_USE
	if _shutdown_complete:
		_mutex.unlock()
		return Error.OK
	if _shutdown_requested_state or _configuration_in_progress:
		_mutex.unlock()
		return Error.ERR_BUSY
	var provider_call: ObservabilityProviderCall = ObservabilityProviderCall.begin(_snapshot)
	_active_calls[provider_call] = true
	_in_flight_calls += 1
	_mutex.unlock()

	var result: int = provider_call.provider().flush(timeout_msec)
	end_call(provider_call)
	return result


func shutdown() -> void:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		push_error("ObservabilityProviderSession lost current pipeline ownership.")
		_mutex.unlock()
		return
	if _shutdown_complete and not _configuration_in_progress:
		_mutex.unlock()
		return
	_shutdown_requested_state = true
	var should_shutdown: bool = not _configuration_in_progress \
			and _in_flight_calls == 0
	_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func shutdown_requested() -> bool:
	_mutex.lock()
	var requested: bool = _shutdown_requested_state
	_mutex.unlock()
	return requested


func in_flight_call_count() -> int:
	_mutex.lock()
	var count: int = _in_flight_calls
	_mutex.unlock()
	return count


func _complete_requested_shutdown() -> void:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		_mutex.unlock()
		push_error("ObservabilityProviderSession lost current pipeline ownership.")
		return
	if not _shutdown_requested_state \
			or _configuration_in_progress \
			or _in_flight_calls > 0:
		_mutex.unlock()
		return
	if _shutdown_complete:
		_shutdown_requested_state = false
		_mutex.unlock()
		return
	_configuration_in_progress = true
	var previous: ObservabilityProviderSnapshot = _snapshot
	_mutex.unlock()

	previous.provider().flush(2000)
	previous.provider().shutdown()
	var disabled: ObservabilityProviderSnapshot = _disabled_snapshot(
			previous.generation() + 1,
		)

	_mutex.lock()
	_snapshot = disabled
	_shutdown_complete = true
	_shutdown_requested_state = false
	_active_calls.clear()
	_in_flight_calls = 0
	_mutex.unlock()

	_mutex.lock()
	_shutdown_requested_state = false
	_configuration_in_progress = false
	_mutex.unlock()


func _finish_configuration_transition() -> void:
	_mutex.lock()
	if not _owns_current_pipeline_locked():
		_configuration_in_progress = false
		_mutex.unlock()
		push_error("ObservabilityProviderSession lost current pipeline ownership.")
		return
	_configuration_in_progress = false
	var should_shutdown: bool = _shutdown_requested_state \
			and not _shutdown_complete \
			and _in_flight_calls == 0
	_mutex.unlock()
	if should_shutdown:
		_complete_requested_shutdown()


func _release_unpublished_pipeline(pipeline: ObservabilityProcessingPipeline) -> void:
	var result: int = pipeline.release(_claim_token)
	if result != Error.OK:
		push_error("ObservabilityProviderSession could not release an unpublished candidate.")


func _owns_current_pipeline_locked() -> bool:
	return _snapshot.pipeline().is_claimed_by(_claim_token)


func _disabled_snapshot(generation: int) -> ObservabilityProviderSnapshot:
	var config: ObservabilityConfig = ObservabilityConfig.new(p_enabled = false)
	var pipeline: ObservabilityProcessingPipeline = ObservabilityProcessingPipeline.new(_runtime)
	var result: int = pipeline.configure(config)
	assert(result == Error.OK, "Disabled observability pipeline must be valid.")
	result = pipeline.claim(config, _claim_token)
	assert(result == Error.OK, "Disabled observability pipeline must be session-owned.")
	result = pipeline.freeze(_claim_token)
	assert(result == Error.OK, "Disabled observability pipeline must be permanently frozen.")
	return ObservabilityProviderSnapshot.new(
			NullObservabilityProvider.new(),
			config,
			pipeline,
			generation,
		)
