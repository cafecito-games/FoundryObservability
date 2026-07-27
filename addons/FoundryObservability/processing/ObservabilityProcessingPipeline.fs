namespace foundry.observability.processing

import foundry.observability
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


final class ActiveOperation extends RefCounted:
	final var _operation_token: int
	final var _generation: int

	func _init(p_operation_token: int, p_generation: int) -> void:
		_operation_token = p_operation_token
		_generation = p_generation

	func operation_token() -> int:
		return _operation_token

	func generation() -> int:
		return _generation


final class PendingProviderResult extends RefCounted:
	final var _generation: int
	final var _signal: ObservabilitySignal
	final var _owner_id: int

	func _init(
			p_generation: int,
			p_signal: ObservabilitySignal,
			p_owner_id: int,
	) -> void:
		_generation = p_generation
		_signal = p_signal
		_owner_id = p_owner_id

	func generation() -> int:
		return _generation

	func processing_signal() -> ObservabilitySignal:
		return _signal

	func owner_id() -> int:
		return _owner_id


final class MetricReservation extends RefCounted:
	final var _lease: ObservabilityProcessingLease[ObservabilityMetric]
	final var _has_metric_filter: bool
	final var _metric_filter: Callable[[ObservabilityMetric], bool]?

	func _init(
			p_lease: ObservabilityProcessingLease[ObservabilityMetric],
			p_has_metric_filter: bool,
			p_metric_filter: Callable[[ObservabilityMetric], bool]?,
	) -> void:
		_lease = p_lease
		_has_metric_filter = p_has_metric_filter
		_metric_filter = p_metric_filter

	func lease() -> ObservabilityProcessingLease[ObservabilityMetric]:
		return _lease

	func has_metric_filter() -> bool:
		return _has_metric_filter

	func metric_filter() -> Callable[[ObservabilityMetric], bool]?:
		return _metric_filter


var _runtime: ObservabilityRuntime
var _redactor: ObservabilityRedactor = ObservabilityRedactor.new()
var _event_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = []
var _log_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = []
var _metric_processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = []
var _has_metric_filter: bool = false
var _metric_filter: Callable[[ObservabilityMetric], bool]?
var _event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new()
var _event_limiter_mutex: Mutex = Mutex.new()
var _log_limiter_mutex: Mutex = Mutex.new()
var _metric_limiter_mutex: Mutex = Mutex.new()
var _config_generation: int = 0
var _operation_sequence: int = 0
var _active_operations: Dictionary[int, ActiveOperation] = {}
var _pending_provider_results: Dictionary[int, PendingProviderResult] = {}
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
	var processing: ObservabilityProcessingConfig = config.processing()
	if not _valid_sample_rate(processing.event_sample_rate()) \
			or not _valid_sample_rate(processing.log_sample_rate()) \
			or not _valid_sample_rate(processing.metric_sample_rate()):
		return Error.ERR_INVALID_PARAMETER

	var event_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = (
			processing.event_processors()
		)
	var log_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = (
			processing.log_processors()
		)
	var metric_processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = (
			processing.metric_processors()
		)
	if not _valid_event_processors(event_processors) \
			or not _valid_event_processors(log_processors) \
			or not _valid_metric_processors(metric_processors):
		return Error.ERR_INVALID_DATA
	var candidate_has_metric_filter: bool = processing.has_metric_filter()
	var candidate_metric_filter: Callable[[ObservabilityMetric], bool]? = null
	if candidate_has_metric_filter:
		candidate_metric_filter = processing.metric_filter()
		if candidate_metric_filter == null or not candidate_metric_filter.is_valid():
			return Error.ERR_INVALID_DATA
	var policy: ObservabilityRedactionPolicy = processing.redaction_policy()
	var event_limits: ObservabilitySignalLimits = processing.event_limits()
	var log_limits: ObservabilitySignalLimits = processing.log_limits()
	var metric_limits: ObservabilitySignalLimits = processing.metric_limits()
	if policy == null or not policy.is_valid() or not _valid_limits(event_limits) \
			or not _valid_limits(log_limits) or not _valid_limits(metric_limits):
		return Error.ERR_INVALID_DATA

	var candidate_redactor: ObservabilityRedactor = ObservabilityRedactor.new(policy)
	if not candidate_redactor.is_valid():
		return Error.ERR_INVALID_DATA
	var candidate_event_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			processing.event_sample_rate(),
			event_limits,
		)
	var candidate_log_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			processing.log_sample_rate(),
			log_limits,
			processing.log_rate_limit_per_second(),
		)
	var candidate_metric_limiter: ObservabilitySignalLimiter = ObservabilitySignalLimiter.new(
			processing.metric_sample_rate(),
			metric_limits,
		)
	var candidate_event_limiter_mutex: Mutex = Mutex.new()
	var candidate_log_limiter_mutex: Mutex = Mutex.new()
	var candidate_metric_limiter_mutex: Mutex = Mutex.new()

	_state_mutex.lock()
	_config_generation += 1
	_redactor = candidate_redactor
	_event_processors = _copy_event_processors(event_processors)
	_log_processors = _copy_event_processors(log_processors)
	_metric_processors = _copy_metric_processors(metric_processors)
	_has_metric_filter = candidate_has_metric_filter
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
func process_event(event: ObservabilityEvent) -> ObservabilityProcessingResult[ObservabilityEvent]:
	var p_signal: ObservabilitySignal = (
			ObservabilitySignal.LOG
			if event != null and event.kind() == &"log"
			else ObservabilitySignal.EVENT
		)
	return _process_event_signal(event, p_signal)


## Processes one custom metric independently from event/log state.
func process_metric(metric: ObservabilityMetric) -> ObservabilityProcessingResult[ObservabilityMetric]:
	return _process_metric_signal(metric)


## Rebuilds provider-owned contexts through the committed redactor.
func redact_contexts(contexts: Dictionary) -> ObservabilityProcessingResult[Dictionary]:
	var lease: ObservabilityProcessingLease[Dictionary]? = _reserve_state(_owner_id())
	if lease == null:
		return _state_dropped(
				ObservabilityProcessingReason.RECURSIVE,
				Error.ERR_BUSY,
			)
	var redacted: ObservabilityRedactionResult[Dictionary] = (
			lease.redactor().redact_contexts(contexts)
		)
	if not redacted.valid():
		return _finish_state_redaction_failure(
				lease,
				redacted.failed_rule_index(),
			)
	var payload: Dictionary? = redacted.value()
	if payload == null:
		return _finish_state_invalid(lease)
	return _finish_state_success(lease, payload)


## Rebuilds a provider-owned user through the committed redactor.
func redact_user(user: ObservabilityUser) -> ObservabilityProcessingResult[Dictionary]:
	var lease: ObservabilityProcessingLease[Dictionary]? = _reserve_state(_owner_id())
	if lease == null:
		return _state_dropped(
				ObservabilityProcessingReason.RECURSIVE,
				Error.ERR_BUSY,
			)
	var redacted: ObservabilityRedactionResult[ObservabilityUser] = (
			lease.redactor().redact_user(user)
		)
	if not redacted.valid():
		return _finish_state_redaction_failure(
				lease,
				redacted.failed_rule_index(),
			)
	var payload: ObservabilityUser? = redacted.value()
	if payload == null:
		return _finish_state_invalid(lease)
	return _finish_state_success(lease, {"user": payload})


## Rebuilds a provider-owned breadcrumb through the committed redactor.
func redact_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> ObservabilityProcessingResult[Dictionary]:
	var lease: ObservabilityProcessingLease[Dictionary]? = _reserve_state(_owner_id())
	if lease == null:
		return _state_dropped(
				ObservabilityProcessingReason.RECURSIVE,
				Error.ERR_BUSY,
			)
	var redacted: ObservabilityRedactionResult[ObservabilityBreadcrumb] = (
			lease.redactor().redact_breadcrumb(breadcrumb)
		)
	if not redacted.valid():
		return _finish_state_redaction_failure(
				lease,
				redacted.failed_rule_index(),
			)
	var payload: ObservabilityBreadcrumb? = redacted.value()
	if payload == null:
		return _finish_state_invalid(lease)
	return _finish_state_success(lease, {"breadcrumb": payload})


## Rebuilds a provider-owned attachment through the committed redactor.
func redact_attachment(attachment: ObservabilityAttachment) -> ObservabilityProcessingResult[Dictionary]:
	var lease: ObservabilityProcessingLease[Dictionary]? = _reserve_state(_owner_id())
	if lease == null:
		return _state_dropped(
				ObservabilityProcessingReason.RECURSIVE,
				Error.ERR_BUSY,
			)
	var redacted: ObservabilityRedactionResult[ObservabilityAttachment] = (
			lease.redactor().redact_attachment(attachment)
		)
	if not redacted.valid():
		return _finish_state_redaction_failure(
				lease,
				redacted.failed_rule_index(),
			)
	var payload: ObservabilityAttachment? = redacted.value()
	if payload == null:
		return _finish_state_invalid(lease)
	return _finish_state_success(lease, {"attachment": payload})


## Records a provider outcome only for a matching current pending processing result.
func record_provider_result(
		p_signal: ObservabilitySignal,
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
	var pending: PendingProviderResult = _pending_provider_results[resolved_token]
	if pending.generation() != _config_generation \
			or pending.processing_signal() != p_signal:
		_state_mutex.unlock()
		return
	_pending_provider_results.erase(resolved_token)
	if accepted:
		_publish_locked(
				p_signal,
				ObservabilityProcessingOutcome.ACCEPTED,
				ObservabilityProcessingReason.NONE,
				-1,
				-1,
				ObservabilityLimitKind.NONE,
				Error.OK,
			)
		_state_mutex.unlock()
		return
	var effective_error: int = Error.FAILED if error == Error.OK else error
	_publish_locked(
			p_signal,
			ObservabilityProcessingOutcome.DROPPED,
			ObservabilityProcessingReason.PROVIDER_REJECTED,
			-1,
			-1,
			ObservabilityLimitKind.NONE,
			effective_error,
		)
	_state_mutex.unlock()


## Returns an isolated payload-free diagnostic snapshot.
func last_diagnostic() -> ObservabilityProcessingDiagnostic?:
	_state_mutex.lock()
	var snapshot: ObservabilityProcessingDiagnostic? = (
			_last_diagnostic.duplicate() if _last_diagnostic != null else null
		)
	_state_mutex.unlock()
	return snapshot


## Returns the stable total of entries rejected for recursive processing.
func recursive_drop_count() -> int:
	_state_mutex.lock()
	var count: int = _recursive_drops
	_state_mutex.unlock()
	return count


func _process_event_signal(
		event: ObservabilityEvent,
		p_signal: ObservabilitySignal,
) -> ObservabilityProcessingResult[ObservabilityEvent]:
	var lease: ObservabilityProcessingLease[ObservabilityEvent]? = (
			_reserve_event(p_signal, _owner_id())
		)
	if lease == null:
		return ObservabilityProcessingResult[ObservabilityEvent].dropped(
				p_signal,
				ObservabilityProcessingReason.RECURSIVE,
			)
	if event == null:
		return _finish_invalid_payload[ObservabilityEvent](lease)

	var redactor: ObservabilityRedactor = lease.redactor()
	var redacted: ObservabilityRedactionResult[ObservabilityEvent] = (
			redactor.redact_event(event, p_signal)
		)
	if not redacted.valid():
		return _finish_redaction_failure[ObservabilityEvent](
				lease,
				redacted.failed_rule_index(),
			)
	var current: ObservabilityEvent? = redacted.value()
	if not _valid_event(current, p_signal):
		return _finish_invalid_payload[ObservabilityEvent](lease)

	var processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = (
			lease.processors()
		)
	for index: int in range(processors.size()):
		if not processors[index].is_valid():
			return _finish_invalid_processor[ObservabilityEvent](lease, index)
		var replacement: ObservabilityEvent? = processors[index].call(current)
		if replacement == null:
			return _finish_drop[ObservabilityEvent](
					lease,
					ObservabilityProcessingReason.PROCESSOR,
					index,
				)
		current = replacement
		if not _valid_event(current, p_signal):
			return _finish_invalid_processor[ObservabilityEvent](lease, index)

	redacted = redactor.redact_event(current, p_signal)
	if not redacted.valid():
		return _finish_redaction_failure[ObservabilityEvent](
				lease,
				redacted.failed_rule_index(),
			)
	current = redacted.value()
	if not _valid_event(current, p_signal):
		return _finish_invalid_payload[ObservabilityEvent](lease)

	var admission_rejection: ObservabilityProcessingResult[ObservabilityEvent]? = (
			_admission_rejection[ObservabilityEvent](
					lease,
					_event_identity(current, p_signal),
				)
		)
	if admission_rejection != null:
		return admission_rejection
	return _finish_success[ObservabilityEvent](lease, current)


func _process_metric_signal(
		metric: ObservabilityMetric,
) -> ObservabilityProcessingResult[ObservabilityMetric]:
	var reservation: MetricReservation? = _reserve_metric(_owner_id())
	if reservation == null:
		return ObservabilityProcessingResult[ObservabilityMetric].dropped(
				ObservabilitySignal.METRIC,
				ObservabilityProcessingReason.RECURSIVE,
			)
	var lease: ObservabilityProcessingLease[ObservabilityMetric] = reservation.lease()
	if not _valid_metric(metric):
		return _finish_invalid_payload[ObservabilityMetric](lease)

	var redactor: ObservabilityRedactor = lease.redactor()
	var redacted: ObservabilityRedactionResult[ObservabilityMetric] = (
			redactor.redact_metric(metric)
		)
	if not redacted.valid():
		return _finish_redaction_failure[ObservabilityMetric](
				lease,
				redacted.failed_rule_index(),
			)
	var current: ObservabilityMetric? = redacted.value()
	if not _valid_metric(current):
		return _finish_invalid_payload[ObservabilityMetric](lease)

	if reservation.has_metric_filter():
		var metric_filter: Callable[[ObservabilityMetric], bool]? = reservation.metric_filter()
		if metric_filter == null:
			return _finish_invalid_processor[ObservabilityMetric](lease, -1)
		if not metric_filter.is_valid():
			return _finish_invalid_processor[ObservabilityMetric](lease, -1)
		if not metric_filter.call(current):
			return _finish_drop[ObservabilityMetric](
					lease,
					ObservabilityProcessingReason.PROCESSOR,
					-1,
				)

	var processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = (
			lease.processors()
		)
	for index: int in range(processors.size()):
		if not processors[index].is_valid():
			return _finish_invalid_processor[ObservabilityMetric](lease, index)
		var replacement: ObservabilityMetric? = processors[index].call(current)
		if replacement == null:
			return _finish_drop[ObservabilityMetric](
					lease,
					ObservabilityProcessingReason.PROCESSOR,
					index,
				)
		current = replacement
		if not _valid_metric(current):
			return _finish_invalid_processor[ObservabilityMetric](lease, index)

	redacted = redactor.redact_metric(current)
	if not redacted.valid():
		return _finish_redaction_failure[ObservabilityMetric](
				lease,
				redacted.failed_rule_index(),
			)
	current = redacted.value()
	if not _valid_metric(current):
		return _finish_invalid_payload[ObservabilityMetric](lease)

	var admission_rejection: ObservabilityProcessingResult[ObservabilityMetric]? = (
			_admission_rejection[ObservabilityMetric](
					lease,
					_metric_identity(current),
				)
		)
	if admission_rejection != null:
		return admission_rejection
	return _finish_success[ObservabilityMetric](lease, current)


func _reserve_event(
		p_signal: ObservabilitySignal,
		owner_id: int,
) -> ObservabilityProcessingLease[ObservabilityEvent]?:
	_state_mutex.lock()
	var active: ActiveOperation? = _begin_reservation_locked(p_signal, owner_id)
	if active == null:
		_state_mutex.unlock()
		return null
	var processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = (
			_event_processors
			if p_signal == ObservabilitySignal.EVENT
			else _log_processors
		)
	var limiter: ObservabilitySignalLimiter = (
			_event_limiter
			if p_signal == ObservabilitySignal.EVENT
			else _log_limiter
		)
	var limiter_mutex: Mutex = (
			_event_limiter_mutex
			if p_signal == ObservabilitySignal.EVENT
			else _log_limiter_mutex
		)
	var lease: ObservabilityProcessingLease[ObservabilityEvent] = (
			ObservabilityProcessingLease[ObservabilityEvent].new(
					active.generation(),
					active.operation_token(),
					owner_id,
					p_signal,
					processors,
					_redactor,
					limiter,
					limiter_mutex,
					_runtime,
				)
		)
	_state_mutex.unlock()
	return lease


func _reserve_metric(owner_id: int) -> MetricReservation?:
	_state_mutex.lock()
	var active: ActiveOperation? = _begin_reservation_locked(
			ObservabilitySignal.METRIC,
			owner_id,
		)
	if active == null:
		_state_mutex.unlock()
		return null
	var lease: ObservabilityProcessingLease[ObservabilityMetric] = (
			ObservabilityProcessingLease[ObservabilityMetric].new(
					active.generation(),
					active.operation_token(),
					owner_id,
					ObservabilitySignal.METRIC,
					_metric_processors,
					_redactor,
					_metric_limiter,
					_metric_limiter_mutex,
					_runtime,
				)
		)
	var reservation: MetricReservation = MetricReservation.new(
			lease,
			_has_metric_filter,
			_metric_filter,
		)
	_state_mutex.unlock()
	return reservation


func _reserve_state(owner_id: int) -> ObservabilityProcessingLease[Dictionary]?:
	_state_mutex.lock()
	var active: ActiveOperation? = _begin_reservation_locked(
			ObservabilitySignal.STATE,
			owner_id,
		)
	if active == null:
		_state_mutex.unlock()
		return null
	var processors: Array[Callable[[Dictionary], Dictionary?]] = []
	var lease: ObservabilityProcessingLease[Dictionary] = (
			ObservabilityProcessingLease[Dictionary].new(
					active.generation(),
					active.operation_token(),
					owner_id,
					ObservabilitySignal.STATE,
					processors,
					_redactor,
					_event_limiter,
					_event_limiter_mutex,
					_runtime,
				)
		)
	_state_mutex.unlock()
	return lease


func _begin_reservation_locked(
		p_signal: ObservabilitySignal,
		owner_id: int,
) -> ActiveOperation?:
	if _active_operations.has(owner_id):
		_recursive_drops += 1
		var recursive_error: int = (
				Error.ERR_BUSY
				if p_signal == ObservabilitySignal.STATE
				else Error.OK
			)
		_publish_locked(
				p_signal,
				ObservabilityProcessingOutcome.DROPPED,
				ObservabilityProcessingReason.RECURSIVE,
				-1,
				-1,
				ObservabilityLimitKind.NONE,
				recursive_error,
			)
		return null
	_operation_sequence += 1
	var active: ActiveOperation = ActiveOperation.new(
			_operation_sequence,
			_config_generation,
		)
	_active_operations[owner_id] = active
	_processing_depth += 1
	return active


func _finish_state_success(
		lease: ObservabilityProcessingLease[Dictionary],
		value: Dictionary,
) -> ObservabilityProcessingResult[Dictionary]:
	_state_mutex.lock()
	var released: bool = _release_locked[Dictionary](lease)
	var current: bool = released and lease.generation() == _config_generation
	_state_mutex.unlock()
	if not current:
		return _state_dropped(
				ObservabilityProcessingReason.STALE_GENERATION,
				Error.ERR_BUSY,
			)
	return ObservabilityProcessingResult[Dictionary].accepted(
			ObservabilitySignal.STATE,
			value,
			lease.operation_token(),
		)


func _finish_state_invalid(
		lease: ObservabilityProcessingLease[Dictionary],
) -> ObservabilityProcessingResult[Dictionary]:
	return _finish_state_redaction_failure(lease, -1)


func _finish_state_redaction_failure(
		lease: ObservabilityProcessingLease[Dictionary],
		redaction_rule_index: int,
) -> ObservabilityProcessingResult[Dictionary]:
	_state_mutex.lock()
	var released: bool = _release_locked[Dictionary](lease)
	if released and lease.generation() == _config_generation:
		_publish_locked(
				ObservabilitySignal.STATE,
				ObservabilityProcessingOutcome.DROPPED,
				ObservabilityProcessingReason.REDACTION_FAILED,
				-1,
				redaction_rule_index,
				ObservabilityLimitKind.NONE,
				Error.ERR_INVALID_DATA,
			)
	_state_mutex.unlock()
	return ObservabilityProcessingResult[Dictionary].dropped(
			ObservabilitySignal.STATE,
			ObservabilityProcessingReason.REDACTION_FAILED,
			ObservabilityLimitKind.NONE,
			-1,
			redaction_rule_index,
			Error.ERR_INVALID_DATA,
		)


func _state_dropped(
		reason: ObservabilityProcessingReason,
		error: int,
) -> ObservabilityProcessingResult[Dictionary]:
	return ObservabilityProcessingResult[Dictionary].dropped(
			ObservabilitySignal.STATE,
			reason,
			ObservabilityLimitKind.NONE,
			-1,
			-1,
			error,
		)


func _release_locked[T](lease: ObservabilityProcessingLease[T]) -> bool:
	var owner_id: int = lease.owner_id()
	if not _active_operations.has(owner_id):
		return false
	var active: ActiveOperation = _active_operations[owner_id]
	if active.operation_token() != lease.operation_token() \
			or active.generation() != lease.generation():
		return false
	_active_operations.erase(owner_id)
	_processing_depth = maxi(0, _processing_depth - 1)
	return true


func _finish_success[T](
		lease: ObservabilityProcessingLease[T],
		value: T?,
) -> ObservabilityProcessingResult[T]:
	_state_mutex.lock()
	var released: bool = _release_locked[T](lease)
	if not released or lease.generation() != _config_generation:
		_state_mutex.unlock()
		return ObservabilityProcessingResult[T].dropped(
				lease.processing_signal(),
				ObservabilityProcessingReason.STALE_GENERATION,
			)
	_register_pending_result_locked(
			lease.operation_token(),
			lease.generation(),
			lease.processing_signal(),
			lease.owner_id(),
		)
	_state_mutex.unlock()
	return ObservabilityProcessingResult[T].accepted(
			lease.processing_signal(),
			value,
			lease.operation_token(),
		)


func _finish_invalid_payload[T](
		lease: ObservabilityProcessingLease[T],
) -> ObservabilityProcessingResult[T]:
	return _finish_failure[T](
			lease,
			ObservabilityProcessingReason.INVALID_PAYLOAD,
			Error.ERR_INVALID_DATA,
		)


func _finish_redaction_failure[T](
		lease: ObservabilityProcessingLease[T],
		redaction_rule_index: int,
) -> ObservabilityProcessingResult[T]:
	return _finish_failure[T](
			lease,
			ObservabilityProcessingReason.REDACTION_FAILED,
			Error.ERR_INVALID_DATA,
			-1,
			redaction_rule_index,
		)


func _finish_invalid_processor[T](
		lease: ObservabilityProcessingLease[T],
		processor_index: int,
) -> ObservabilityProcessingResult[T]:
	return _finish_failure[T](
			lease,
			ObservabilityProcessingReason.INVALID_PROCESSOR_RESULT,
			Error.ERR_INVALID_DATA,
			processor_index,
		)


func _finish_drop[T](
		lease: ObservabilityProcessingLease[T],
		reason: ObservabilityProcessingReason,
		processor_index: int = -1,
		limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
) -> ObservabilityProcessingResult[T]:
	_state_mutex.lock()
	var released: bool = _release_locked[T](lease)
	if released and lease.generation() == _config_generation:
		_publish_locked(
				lease.processing_signal(),
				ObservabilityProcessingOutcome.DROPPED,
				reason,
				processor_index,
				-1,
				limit_kind,
				Error.OK,
			)
	_state_mutex.unlock()
	return ObservabilityProcessingResult[T].dropped(
			lease.processing_signal(),
			reason,
			limit_kind,
			processor_index,
		)


func _finish_failure[T](
		lease: ObservabilityProcessingLease[T],
		reason: ObservabilityProcessingReason,
		error: int,
		processor_index: int = -1,
		redaction_rule_index: int = -1,
) -> ObservabilityProcessingResult[T]:
	_state_mutex.lock()
	var released: bool = _release_locked[T](lease)
	if released and lease.generation() == _config_generation:
		_publish_locked(
				lease.processing_signal(),
				ObservabilityProcessingOutcome.FAILED,
				reason,
				processor_index,
				redaction_rule_index,
				ObservabilityLimitKind.NONE,
				error,
			)
	_state_mutex.unlock()
	return ObservabilityProcessingResult[T].failed(
			lease.processing_signal(),
			reason,
			error,
			processor_index,
			redaction_rule_index,
		)


func _admission_rejection[T](
		lease: ObservabilityProcessingLease[T],
		identity: String,
) -> ObservabilityProcessingResult[T]?:
	var limiter_mutex: Mutex = lease.limiter_mutex()
	limiter_mutex.lock()
	var admission: ObservabilityAdmissionDecision = lease.limiter().admit(
			identity,
			lease.runtime().monotonic_time_msec(),
			lease.runtime().process_frame(),
		)
	limiter_mutex.unlock()
	if admission.accepted():
		return null
	return _finish_drop[T](
			lease,
			admission.reason(),
			-1,
			admission.limit_kind(),
		)


func _register_pending_result_locked(
		operation_token: int,
		generation: int,
		p_signal: ObservabilitySignal,
		owner_id: int,
) -> void:
	var oldest_token: int = operation_token
	for token: int in _pending_provider_results:
		var pending: PendingProviderResult = _pending_provider_results[token]
		if pending.owner_id() == owner_id \
				and pending.processing_signal() == p_signal:
			_pending_provider_results.erase(token)
			continue
		oldest_token = mini(oldest_token, token)
	_pending_provider_results[operation_token] = PendingProviderResult.new(
			generation,
			p_signal,
			owner_id,
		)
	if _pending_provider_results.size() > MAX_PENDING_PROVIDER_RESULTS:
		_pending_provider_results.erase(oldest_token)


func _unambiguous_pending_token_locked(
		owner_id: int,
		p_signal: ObservabilitySignal,
) -> int:
	var matched_token: int = -1
	for token: int in _pending_provider_results:
		var pending: PendingProviderResult = _pending_provider_results[token]
		if pending.generation() != _config_generation \
				or pending.owner_id() != owner_id \
				or pending.processing_signal() != p_signal:
			continue
		if matched_token >= 0:
			return -1
		matched_token = token
	return matched_token


func _publish_locked(
		p_signal: ObservabilitySignal,
		outcome: ObservabilityProcessingOutcome,
		reason: ObservabilityProcessingReason,
		processor_index: int,
		redaction_rule_index: int,
		limit_kind: ObservabilityLimitKind,
		error: int,
) -> void:
	_diagnostic_sequence += 1
	_last_diagnostic = ObservabilityProcessingDiagnostic.new(
			_diagnostic_sequence,
			p_signal,
			outcome,
			reason,
			processor_index,
			redaction_rule_index,
			limit_kind,
			error,
		)


func _valid_sample_rate(value: float) -> bool:
	return is_finite(value) and value >= 0.0 and value <= 1.0


func _valid_event_processors(
		processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]],
) -> bool:
	for processor: Callable[[ObservabilityEvent], ObservabilityEvent?] in processors:
		if not processor.is_valid():
			return false
	return true


func _valid_metric_processors(
		processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]],
) -> bool:
	for processor: Callable[[ObservabilityMetric], ObservabilityMetric?] in processors:
		if not processor.is_valid():
			return false
	return true


func _valid_limits(limits: ObservabilitySignalLimits) -> bool:
	return limits != null and limits.per_frame() >= 0 and limits.repeated_window_msec() >= 0 \
			and limits.window_count() >= 0 and limits.window_msec() >= 0


func _valid_signal(p_signal: ObservabilitySignal) -> bool:
	return p_signal == ObservabilitySignal.EVENT \
			or p_signal == ObservabilitySignal.LOG \
			or p_signal == ObservabilitySignal.METRIC


func _valid_event(
		event: ObservabilityEvent?,
		p_signal: ObservabilitySignal,
) -> bool:
	return event != null and event.kind() != &"" \
			and ((p_signal == ObservabilitySignal.LOG and event.kind() == &"log") \
			or (p_signal == ObservabilitySignal.EVENT and event.kind() != &"log"))


func _valid_metric(metric: ObservabilityMetric?) -> bool:
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


func _event_identity(
		event: ObservabilityEvent,
		p_signal: ObservabilitySignal,
) -> String:
	if p_signal == ObservabilitySignal.LOG:
		return JSON.stringify([String(event.source()), event.level(), event.message()])
	var exception_identity: Variant = null
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		exception_identity = [
			exception.type_name(),
			exception.message(),
			exception.stack_trace(),
		]
	return JSON.stringify([
		String(event.kind()),
		String(event.source()),
		event.level(),
		event.message(),
		exception_identity,
	])


func _metric_identity(metric: ObservabilityMetric) -> String:
	return JSON.stringify([metric.type(), metric.name(), metric.unit()])


func _owner_id() -> int:
	return _runtime.caller_id()


func _copy_event_processors(
		source: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]],
) -> Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]:
	var copied: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = []
	for processor: Callable[[ObservabilityEvent], ObservabilityEvent?] in source:
		copied.append(processor)
	return copied


func _copy_metric_processors(
		source: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]],
) -> Array[Callable[[ObservabilityMetric], ObservabilityMetric?]]:
	var copied: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = []
	for processor: Callable[[ObservabilityMetric], ObservabilityMetric?] in source:
		copied.append(processor)
	return copied
