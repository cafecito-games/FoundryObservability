namespace foundry.observability

import foundry.observability.processing
import foundry.observability.runtime

## Coordinates provider-neutral processing before provider delivery.
class_name ObservabilityProcessingPipeline
extends RefCounted

## These values intentionally mirror FoundryObservability's metric normalization contract.
const _MAX_METRIC_NAME_LENGTH: int = 200
const _MAX_METRIC_UNIT_LENGTH: int = 64
const _MAX_METRIC_ATTRIBUTE_KEY_LENGTH: int = 200
## Caps payload-free provider completion tokens retained between processing and delivery.
const MAX_PENDING_PROVIDER_RESULTS: int = 1024

var _runtime: ObservabilityRuntime
var _redactor: ObservabilityRedactor = ObservabilityRedactor.new()
var _event_processors: Array[Callable] = []
var _log_processors: Array[Callable] = []
var _metric_processors: Array[Callable] = []
var _metric_filter: Callable = Callable()
var _event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _event_limiter_mutex: Mutex = Mutex.new()
var _log_limiter_mutex: Mutex = Mutex.new()
var _metric_limiter_mutex: Mutex = Mutex.new()
var _config_generation: int = 0
var _operation_sequence: int = 0
var _active_operations: Dictionary = {}
var _pending_provider_results: Dictionary = {}
var _processing_depth: int = 0
var _recursive_drops: int = 0
var _diagnostic_sequence: int = 0
var _last_diagnostic: ObservabilityProcessingDiagnostic?
var _state_mutex: Mutex = Mutex.new()


## Creates a coordinator with a shared observability runtime.
func _init(runtime: ObservabilityRuntime) -> void:
	assert(runtime != null, "ObservabilityProcessingPipeline requires a runtime.")
	_runtime = runtime


## Atomically replaces all processing state after candidate validation succeeds.
func configure(config: ObservabilityConfig? = null) -> int:
	if config == null:
		return Error.ERR_INVALID_PARAMETER
	if not _valid_sample_rate(config.processing().event_sample_rate()) \
			or not _valid_sample_rate(config.processing().log_sample_rate()) \
			or not _valid_sample_rate(config.processing().metric_sample_rate()):
		return Error.ERR_INVALID_PARAMETER

	var event_processors: Array[Callable] = config.processing().event_processors()
	var log_processors: Array[Callable] = config.processing().log_processors()
	var metric_processors: Array[Callable] = config.processing().metric_processors()
	if not _valid_processors(event_processors) or not _valid_processors(log_processors) \
			or not _valid_processors(metric_processors):
		return Error.ERR_INVALID_DATA
	var metric_filter: Variant = config.processing().metric_filter()
	var candidate_metric_filter: Callable = Callable()
	if metric_filter != null:
		if not (metric_filter is Callable):
			return Error.ERR_INVALID_DATA
		@warning_ignore("unsafe_cast")
		candidate_metric_filter = metric_filter as Callable
		if not candidate_metric_filter.is_valid():
			return Error.ERR_INVALID_DATA
	var policy: ObservabilityRedactionPolicy = config.processing().redaction_policy()
	var event_limits: ObservabilitySignalLimits = config.processing().event_limits()
	var log_limits: ObservabilitySignalLimits = config.processing().log_limits()
	var metric_limits: ObservabilitySignalLimits = config.processing().metric_limits()
	if policy == null or not policy.is_valid() or not _valid_limits(event_limits) \
			or not _valid_limits(log_limits) or not _valid_limits(metric_limits):
		return Error.ERR_INVALID_DATA

	var candidate_redactor: ObservabilityRedactor = ObservabilityRedactor.new(policy)
	if not candidate_redactor.is_valid():
		return Error.ERR_INVALID_DATA
	var candidate_event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.processing().event_sample_rate(), event_limits)
	var candidate_log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.processing().log_sample_rate(), log_limits, config.processing().log_rate_limit_per_second())
	var candidate_metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			config.processing().metric_sample_rate(), metric_limits)
	var candidate_event_limiter_mutex: Mutex = Mutex.new()
	var candidate_log_limiter_mutex: Mutex = Mutex.new()
	var candidate_metric_limiter_mutex: Mutex = Mutex.new()

	_state_mutex.lock()
	_config_generation += 1
	_redactor = candidate_redactor
	_event_processors = _copy_processors(event_processors)
	_log_processors = _copy_processors(log_processors)
	_metric_processors = _copy_processors(metric_processors)
	_metric_filter = candidate_metric_filter
	_event_limiter = candidate_event_limiter
	_log_limiter = candidate_log_limiter
	_metric_limiter = candidate_metric_limiter
	_event_limiter_mutex = candidate_event_limiter_mutex
	_log_limiter_mutex = candidate_log_limiter_mutex
	_metric_limiter_mutex = candidate_metric_limiter_mutex
	_recursive_drops = 0
	_diagnostic_sequence = 0
	_last_diagnostic = null
	_pending_provider_results.clear()
	_state_mutex.unlock()
	return Error.OK


## Classifies structured log events separately from ordinary events.
func process_event(event: ObservabilityEvent) -> Dictionary:
	var p_signal: StringName = &"log" if event != null and event.kind() == &"log" else &"event"
	return _process_event_signal(event, p_signal)


## Processes one custom metric independently from event/log state.
func process_metric(metric: ObservabilityMetric) -> Dictionary:
	return _process_metric_signal(metric)


## Rebuilds provider-owned contexts through the committed redactor.
func redact_contexts(contexts: Dictionary) -> Dictionary:
	return _redact_state_value(&"contexts", contexts)


## Rebuilds a provider-owned user through the committed redactor.
func redact_user(user: ObservabilityUser) -> Dictionary:
	return _redact_state_value(&"user", user)


## Rebuilds a provider-owned breadcrumb through the committed redactor.
func redact_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> Dictionary:
	return _redact_state_value(&"breadcrumb", breadcrumb)


## Rebuilds a provider-owned attachment through the committed redactor.
func redact_attachment(attachment: ObservabilityAttachment) -> Dictionary:
	return _redact_state_value(&"attachment", attachment)


## Records a provider outcome only for a matching current pending processing result.
func record_provider_result(
		p_signal: StringName,
		accepted: bool,
		error: int,
		operation_token: Variant = null,
) -> void:
	if not _valid_signal(p_signal):
		return
	var owner_id: int = -1
	if operation_token != null and not (operation_token is int):
		return
	if operation_token == null:
		owner_id = _owner_id()

	_state_mutex.lock()
	var resolved_token: int = -1
	if operation_token is int:
		resolved_token = operation_token
	else:
		resolved_token = _unambiguous_pending_token_locked(owner_id, p_signal)
	if resolved_token < 0 or not _pending_provider_results.has(resolved_token):
		_state_mutex.unlock()
		return
	var pending: Dictionary = _pending_provider_results[resolved_token]
	if _int_value(pending, "generation", -1) != _config_generation \
			or StringName(str(pending.get("signal", &""))) != p_signal:
		_state_mutex.unlock()
		return
	_pending_provider_results.erase(resolved_token)
	if accepted:
		_publish_locked(
				p_signal, ObservabilityProcessingDiagnostic.ACCEPTED,
				&"", -1, -1, &"", Error.OK)
		_state_mutex.unlock()
		return
	var effective_error: int = Error.FAILED if error == Error.OK else error
	_publish_locked(
			p_signal,
			ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.PROVIDER_REJECTED,
			-1,
			-1,
			&"",
			effective_error,
	)
	_state_mutex.unlock()


## Returns an isolated payload-free diagnostic snapshot.
func last_diagnostic() -> ObservabilityProcessingDiagnostic?:
	_state_mutex.lock()
	var snapshot: ObservabilityProcessingDiagnostic? = (
			_last_diagnostic.duplicate() if _last_diagnostic != null else null)
	_state_mutex.unlock()
	return snapshot


## Returns the stable total of entries rejected for recursive processing.
func recursive_drop_count() -> int:
	_state_mutex.lock()
	var count: int = _recursive_drops
	_state_mutex.unlock()
	return count


func _process_event_signal(event: ObservabilityEvent, p_signal: StringName) -> Dictionary:
	var owner_id: int = _owner_id()
	var snapshot: Dictionary = _reserve(p_signal, owner_id)
	if snapshot.is_empty():
		return _rejected(p_signal)
	if event == null:
		return _finish_invalid_payload(snapshot, p_signal)

	@warning_ignore("unsafe_cast")
	var redactor: ObservabilityRedactor = snapshot["redactor"] as ObservabilityRedactor
	var signal_type: ObservabilitySignal = (
			ObservabilitySignal.LOG
			if p_signal == ObservabilityProcessingDiagnostic.LOG
			else ObservabilitySignal.EVENT
		)
	var redacted: ObservabilityRedactionResult[ObservabilityEvent] = (
			redactor.redact_event(event, signal_type)
		)
	if not redacted.valid():
		return _finish_redaction_failure(
				snapshot,
				p_signal,
				redacted.failed_rule_index(),
			)
	var current: ObservabilityEvent = redacted.value()
	if not _valid_event(current, p_signal):
		return _finish_invalid_payload(snapshot, p_signal)

	var processors: Array[Callable] = snapshot["processors"]
	for index: int in range(processors.size()):
		if not processors[index].is_valid():
			return _finish_invalid_processor(snapshot, p_signal, index)
		var result: Variant = processors[index].call(current)
		if result == null:
			return _finish_drop(
					snapshot, p_signal, ObservabilityProcessingDiagnostic.PROCESSOR,
					index, -1, &"", Error.OK)
		if not (result is ObservabilityEvent):
			return _finish_invalid_processor(snapshot, p_signal, index)
		@warning_ignore("unsafe_cast")
		current = result as ObservabilityEvent
		if not _valid_event(current, p_signal):
			return _finish_invalid_processor(snapshot, p_signal, index)

	redacted = redactor.redact_event(current, signal_type)
	if not redacted.valid():
		return _finish_redaction_failure(
				snapshot,
				p_signal,
				redacted.failed_rule_index(),
			)
	current = redacted.value()
	if not _valid_event(current, p_signal):
		return _finish_invalid_payload(snapshot, p_signal)

	var admission: Dictionary = _admit(
			snapshot, p_signal, _event_identity(current, p_signal))
	if not admission["accepted"]:
		return admission
	return _finish_success(snapshot, p_signal, current)


func _process_metric_signal(metric: ObservabilityMetric) -> Dictionary:
	var p_signal: StringName = ObservabilityProcessingDiagnostic.METRIC
	var owner_id: int = _owner_id()
	var snapshot: Dictionary = _reserve(p_signal, owner_id)
	if snapshot.is_empty():
		return _rejected(p_signal)
	if not _valid_metric(metric):
		return _finish_invalid_payload(snapshot, p_signal)

	@warning_ignore("unsafe_cast")
	var redactor: ObservabilityRedactor = snapshot["redactor"] as ObservabilityRedactor
	var redacted: ObservabilityRedactionResult[ObservabilityMetric] = (
			redactor.redact_metric(metric)
		)
	if not redacted.valid():
		return _finish_redaction_failure(
				snapshot,
				p_signal,
				redacted.failed_rule_index(),
			)
	var current: ObservabilityMetric = redacted.value()
	if not _valid_metric(current):
		return _finish_invalid_payload(snapshot, p_signal)

	var metric_filter: Callable = snapshot["metric_filter"]
	if metric_filter != Callable() and not metric_filter.is_valid():
		return _finish_invalid_processor(snapshot, p_signal, -1)
	if metric_filter.is_valid():
		var filter_result: Variant = metric_filter.call(current)
		if not (filter_result is bool):
			return _finish_invalid_processor(snapshot, p_signal, -1)
		if not filter_result:
			return _finish_drop(
					snapshot, p_signal, ObservabilityProcessingDiagnostic.PROCESSOR,
					-1, -1, &"", Error.OK)

	var processors: Array[Callable] = snapshot["processors"]
	for index: int in range(processors.size()):
		if not processors[index].is_valid():
			return _finish_invalid_processor(snapshot, p_signal, index)
		var result: Variant = processors[index].call(current)
		if result == null:
			return _finish_drop(
					snapshot, p_signal, ObservabilityProcessingDiagnostic.PROCESSOR,
					index, -1, &"", Error.OK)
		if not (result is ObservabilityMetric):
			return _finish_invalid_processor(snapshot, p_signal, index)
		@warning_ignore("unsafe_cast")
		current = result as ObservabilityMetric
		if not _valid_metric(current):
			return _finish_invalid_processor(snapshot, p_signal, index)

	redacted = redactor.redact_metric(current)
	if not redacted.valid():
		return _finish_redaction_failure(
				snapshot,
				p_signal,
				redacted.failed_rule_index(),
			)
	current = redacted.value()
	if not _valid_metric(current):
		return _finish_invalid_payload(snapshot, p_signal)

	var admission: Dictionary = _admit(snapshot, p_signal, _metric_identity(current))
	if not admission["accepted"]:
		return admission
	return _finish_success(snapshot, p_signal, current)


func _redact_state_value(kind: StringName, value: Variant) -> Dictionary:
	var p_signal: StringName = ObservabilityProcessingDiagnostic.STATE
	var snapshot: Dictionary = _reserve(p_signal, _owner_id())
	if snapshot.is_empty():
		return _state_rejected(
				ObservabilityProcessingDiagnostic.RECURSIVE,
				Error.ERR_BUSY,
			)
	@warning_ignore("unsafe_cast")
	var redactor: ObservabilityRedactor = snapshot["redactor"] as ObservabilityRedactor
	match kind:
		&"contexts":
			if not (value is Dictionary):
				return _finish_state_invalid(snapshot)
			@warning_ignore("unsafe_call_argument")
			return _finish_state_redaction[Dictionary](
					snapshot,
					redactor.redact_contexts(value),
				)
		&"user":
			if not (value is ObservabilityUser):
				return _finish_state_invalid(snapshot)
			@warning_ignore("unsafe_call_argument")
			return _finish_state_redaction[ObservabilityUser](
					snapshot,
					redactor.redact_user(value),
				)
		&"breadcrumb":
			if not (value is ObservabilityBreadcrumb):
				return _finish_state_invalid(snapshot)
			@warning_ignore("unsafe_call_argument")
			return _finish_state_redaction[ObservabilityBreadcrumb](
					snapshot,
					redactor.redact_breadcrumb(value),
				)
		&"attachment":
			if not (value is ObservabilityAttachment):
				return _finish_state_invalid(snapshot)
			@warning_ignore("unsafe_call_argument")
			return _finish_state_redaction[ObservabilityAttachment](
					snapshot,
					redactor.redact_attachment(value),
				)
		_:
			return _finish_state_invalid(snapshot)


func _finish_state_redaction[T](
		snapshot: Dictionary,
		redacted: ObservabilityRedactionResult[T],
) -> Dictionary:
	var p_signal: StringName = ObservabilityProcessingDiagnostic.STATE
	if not redacted.valid():
		_finish_redaction_failure(
				snapshot,
				p_signal,
				redacted.failed_rule_index(),
			)
		return _state_rejected(
				ObservabilityProcessingDiagnostic.REDACTION_FAILED,
				Error.ERR_INVALID_DATA,
			)
	return _finish_state_success(snapshot, redacted.value())


func _reserve(p_signal: StringName, owner_id: int) -> Dictionary:
	_state_mutex.lock()
	if _active_operations.has(owner_id):
		_recursive_drops += 1
		var recursive_error: int = (
			Error.ERR_BUSY
			if p_signal == ObservabilityProcessingDiagnostic.STATE
			else Error.OK
		)
		_publish_locked(p_signal, ObservabilityProcessingDiagnostic.DROPPED,
				ObservabilityProcessingDiagnostic.RECURSIVE, -1, -1, &"",
				recursive_error)
		_state_mutex.unlock()
		return {}
	_operation_sequence += 1
	var operation_token: int = _operation_sequence
	var generation: int = _config_generation
	_active_operations[owner_id] = {
		"token": operation_token,
		"generation": generation,
	}
	_processing_depth += 1
	var processors: Array[Callable] = _event_processors if p_signal == &"event" else _log_processors
	var limiter: ObservabilitySignalLimiter = _event_limiter if p_signal == &"event" else _log_limiter
	var limiter_mutex: Mutex = (
			_event_limiter_mutex if p_signal == &"event" else _log_limiter_mutex)
	if p_signal == &"metric":
		processors = _metric_processors
		limiter = _metric_limiter
		limiter_mutex = _metric_limiter_mutex
	var snapshot: Dictionary = {
		"generation": generation,
		"operation_token": operation_token,
		"owner": owner_id,
		"redactor": _redactor,
		"processors": _copy_processors(processors),
		"metric_filter": _metric_filter,
		"limiter": limiter,
		"limiter_mutex": limiter_mutex,
	}
	_state_mutex.unlock()
	return snapshot


func _finish_state_success(snapshot: Dictionary, value: Variant) -> Dictionary:
	_state_mutex.lock()
	var released: bool = _release_locked(snapshot)
	var current: bool = (
			released
			and _int_value(snapshot, "generation", -1) == _config_generation
		)
	_state_mutex.unlock()
	if not current:
		return _state_rejected(&"", Error.ERR_BUSY)
	return {
		"accepted": true,
		"valid": true,
		"value": value,
		"signal": ObservabilityProcessingDiagnostic.STATE,
		"reason": &"",
		"error": Error.OK,
	}


func _finish_state_invalid(snapshot: Dictionary) -> Dictionary:
	_finish_redaction_failure(
			snapshot,
			ObservabilityProcessingDiagnostic.STATE,
			-1,
		)
	return _state_rejected(
			ObservabilityProcessingDiagnostic.REDACTION_FAILED,
			Error.ERR_INVALID_DATA,
		)


func _state_rejected(reason: StringName, error: int) -> Dictionary:
	return {
		"accepted": false,
		"valid": false,
		"value": null,
		"signal": ObservabilityProcessingDiagnostic.STATE,
		"reason": reason,
		"error": error,
	}


func _release_locked(snapshot: Dictionary) -> bool:
	var owner_id: int = _int_value(snapshot, "owner", -1)
	var operation_token: int = _int_value(snapshot, "operation_token", -1)
	var generation: int = _int_value(snapshot, "generation", -1)
	if not _active_operations.has(owner_id):
		return false
	var active: Dictionary = _active_operations[owner_id]
	if _int_value(active, "token", -1) != operation_token \
			or _int_value(active, "generation", -1) != generation:
		return false
	_active_operations.erase(owner_id)
	_processing_depth = maxi(0, _processing_depth - 1)
	return true


func _finish_success(
		snapshot: Dictionary,
		p_signal: StringName,
		value: Variant,
) -> Dictionary:
	_state_mutex.lock()
	var released: bool = _release_locked(snapshot)
	var generation: int = _int_value(snapshot, "generation", -1)
	if not released or generation != _config_generation:
		_state_mutex.unlock()
		return _rejected(p_signal)
	var operation_token: int = _int_value(snapshot, "operation_token", -1)
	_register_pending_result_locked(
			operation_token, generation, p_signal, _int_value(snapshot, "owner", -1))
	_state_mutex.unlock()
	return {
		"accepted": true,
		"value": value,
		"signal": p_signal,
		"operation_token": operation_token,
	}


func _finish_invalid_payload(snapshot: Dictionary, p_signal: StringName) -> Dictionary:
	return _finish_drop(
			snapshot, p_signal, ObservabilityProcessingDiagnostic.INVALID_PAYLOAD,
			-1, -1, &"", Error.ERR_INVALID_DATA)


func _finish_redaction_failure(
		snapshot: Dictionary,
		p_signal: StringName,
		rule_index: int,
) -> Dictionary:
	return _finish_drop(
			snapshot, p_signal, ObservabilityProcessingDiagnostic.REDACTION_FAILED,
			-1, rule_index, &"", Error.ERR_INVALID_DATA)


func _finish_invalid_processor(
		snapshot: Dictionary,
		p_signal: StringName,
		processor_index: int,
) -> Dictionary:
	return _finish_drop(
			snapshot, p_signal, ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT,
			processor_index, -1, &"", Error.ERR_INVALID_DATA)


func _finish_drop(
		snapshot: Dictionary,
		p_signal: StringName,
		reason: StringName,
		processor_index: int,
		rule_index: int,
		limit_kind: StringName,
		error: int,
) -> Dictionary:
	_state_mutex.lock()
	var released: bool = _release_locked(snapshot)
	if released and _int_value(snapshot, "generation", -1) == _config_generation:
		_publish_locked(
				p_signal, ObservabilityProcessingDiagnostic.DROPPED,
				reason, processor_index, rule_index, limit_kind, error)
	_state_mutex.unlock()
	return _rejected(p_signal)


func _admit(
		snapshot: Dictionary,
		p_signal: StringName,
		identity: String,
) -> Dictionary:
	@warning_ignore("unsafe_cast")
	var limiter: ObservabilitySignalLimiter = snapshot["limiter"] as ObservabilitySignalLimiter
	@warning_ignore("unsafe_cast")
	var limiter_mutex: Mutex = snapshot["limiter_mutex"] as Mutex
	var now_msec: int = _now_msec()
	var frame_index: int = _frame_index()
	limiter_mutex.lock()
	var admission: ObservabilityAdmissionDecision = limiter.admit(
			identity,
			now_msec,
			frame_index,
		)
	limiter_mutex.unlock()
	if not admission.accepted():
		return _finish_drop(
				snapshot,
				p_signal,
				_diagnostic_reason(admission.reason()),
				-1,
				-1,
				_diagnostic_limit_kind(admission.limit_kind()),
				Error.OK,
			)
	return {"accepted": true}


func _diagnostic_reason(reason: ObservabilityProcessingReason) -> StringName:
	match reason:
		ObservabilityProcessingReason.SAMPLED:
			return &"sampled"
		ObservabilityProcessingReason.RATE_LIMITED:
			return &"rate_limited"
		_:
			push_error("Unexpected dropped admission reason.")
			assert(false, "Unexpected dropped admission reason.")
			return &"invalid_admission_reason"


func _diagnostic_limit_kind(limit_kind: ObservabilityLimitKind) -> StringName:
	match limit_kind:
		ObservabilityLimitKind.NONE:
			return &""
		ObservabilityLimitKind.PER_FRAME:
			return &"per_frame"
		ObservabilityLimitKind.REPEATED:
			return &"repeated"
		ObservabilityLimitKind.WINDOW:
			return &"window"
		ObservabilityLimitKind.LEGACY_LOG_WINDOW:
			return &"legacy_log_window"
	push_error("Unexpected admission limit kind.")
	assert(false, "Unexpected admission limit kind.")
	return &"invalid_admission_limit"


func _register_pending_result_locked(
		operation_token: int,
		generation: int,
		p_signal: StringName,
		owner_id: int,
) -> void:
	var oldest_token: int = operation_token
	for key: Variant in _pending_provider_results.keys():
		if not (key is int):
			continue
		var token: int = key
		var pending: Dictionary = _pending_provider_results[key]
		if _int_value(pending, "owner", -1) == owner_id \
				and StringName(str(pending.get("signal", &""))) == p_signal:
			_pending_provider_results.erase(key)
			continue
		oldest_token = mini(oldest_token, token)
	_pending_provider_results[operation_token] = {
		"generation": generation,
		"signal": p_signal,
		"owner": owner_id,
	}
	if _pending_provider_results.size() > MAX_PENDING_PROVIDER_RESULTS:
		_pending_provider_results.erase(oldest_token)


func _unambiguous_pending_token_locked(owner_id: int, p_signal: StringName) -> int:
	var matched_token: int = -1
	for key: Variant in _pending_provider_results.keys():
		if not (key is int):
			continue
		var pending: Dictionary = _pending_provider_results[key]
		if _int_value(pending, "generation", -1) != _config_generation \
				or _int_value(pending, "owner", -1) != owner_id \
				or StringName(str(pending.get("signal", &""))) != p_signal:
			continue
		if matched_token >= 0:
			return -1
		matched_token = key
	return matched_token


func _int_value(values: Dictionary, key: String, fallback: int) -> int:
	var value: Variant = values.get(key, fallback)
	return value if value is int else fallback


func _publish_locked(
		p_signal: StringName,
		outcome: StringName,
		reason: StringName,
		processor_index: int,
		rule_index: int,
		limit_kind: StringName,
		error: int,
) -> void:
	_diagnostic_sequence += 1
	_last_diagnostic = ObservabilityProcessingDiagnostic.new(
			_diagnostic_sequence, p_signal, outcome, reason, processor_index,
			rule_index, limit_kind, error)


func _rejected(p_signal: StringName) -> Dictionary:
	return {"accepted": false, "signal": p_signal}


func _valid_sample_rate(value: float) -> bool:
	return is_finite(value) and value >= 0.0 and value <= 1.0


func _valid_processors(processors: Array[Callable]) -> bool:
	for processor: Callable in processors:
		if not processor.is_valid():
			return false
	return true


func _valid_limits(limits: ObservabilitySignalLimits) -> bool:
	return limits != null and limits.per_frame() >= 0 and limits.repeated_window_msec() >= 0 \
			and limits.window_count() >= 0 and limits.window_msec() >= 0


func _valid_signal(p_signal: StringName) -> bool:
	return p_signal == &"event" or p_signal == &"log" or p_signal == &"metric"


func _valid_event(event: ObservabilityEvent, p_signal: StringName) -> bool:
	return event != null and event.kind() != &"" \
			and ((p_signal == &"log" and event.kind() == &"log") \
			or (p_signal == &"event" and event.kind() != &"log"))


func _valid_metric(metric: ObservabilityMetric) -> bool:
	if metric == null or not _is_valid_metric_name(metric.name()) \
			or not is_finite(metric.value()) \
			or not _is_valid_metric_attributes(metric.attributes()):
		return false
	if metric.type() < ObservabilityMetricType.COUNTER \
			or metric.type() > ObservabilityMetricType.DISTRIBUTION:
		return false
	if metric.type() == ObservabilityMetricType.COUNTER:
		return metric.value() >= 0.0 and metric.value() == floorf(metric.value()) \
			and metric.unit().is_empty()
	return _is_valid_metric_unit(metric.unit())


## Mirrors FoundryObservability metric acceptance for pre-redacted and processor result values.
func _is_valid_metric_name(value: String) -> bool:
	return not value.is_empty() \
			and value.length() <= _MAX_METRIC_NAME_LENGTH \
			and value.strip_edges() == value \
			and not _has_control_character(value)


func _is_valid_metric_unit(value: String) -> bool:
	return value.length() <= _MAX_METRIC_UNIT_LENGTH \
			and not _has_control_character(value) \
			and not _has_whitespace(value)


func _is_valid_metric_attributes(attributes: Dictionary) -> bool:
	for key: Variant in attributes.keys():
		if not (key is String) and not (key is StringName):
			return false
		var key_string: String = str(key)
		if key_string.is_empty() \
				or key_string.length() > _MAX_METRIC_ATTRIBUTE_KEY_LENGTH \
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


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return true
	return false


func _event_identity(event: ObservabilityEvent, p_signal: StringName) -> String:
	if p_signal == &"log":
		return JSON.stringify([String(event.source()), event.level(), event.message()])
	var exception_identity: Variant = null
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		exception_identity = [exception.type_name(), exception.message(), exception.stack_trace()]
	return JSON.stringify([
		String(event.kind()), String(event.source()), event.level(), event.message(), exception_identity,
	])


func _metric_identity(metric: ObservabilityMetric) -> String:
	return JSON.stringify([metric.type(), metric.name(), metric.unit()])


func _rule_index(redacted: Dictionary) -> int:
	var value: Variant = redacted.get("rule_index", -1)
	if value is int:
		return value
	return -1


func _now_msec() -> int:
	return _runtime.monotonic_time_msec()


func _frame_index() -> int:
	return _runtime.process_frame()


func _owner_id() -> int:
	return _runtime.caller_id()


func _copy_processors(source: Array[Callable]) -> Array[Callable]:
	var copied: Array[Callable] = []
	for processor: Callable in source:
		copied.append(processor)
	return copied
