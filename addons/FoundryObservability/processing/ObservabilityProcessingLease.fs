namespace foundry.observability.processing

import foundry.observability
import foundry.observability.runtime

## Immutable per-operation snapshot of processing dependencies and ordering identity.
final class_name ObservabilityProcessingLease[T] extends RefCounted

final var _generation: int
final var _operation_token: int
final var _owner_id: int
final var _signal: ObservabilitySignal
final var _processors: Array[Callable[[T], T?]]
final var _redactor: ObservabilityRedactor
final var _limiter: ObservabilitySignalLimiter
final var _limiter_mutex: Mutex
final var _runtime: ObservabilityRuntime


func _init(
		p_generation: int,
		p_operation_token: int,
		p_owner_id: int,
		p_signal: ObservabilitySignal,
		p_processors: Array[Callable[[T], T?]],
		p_redactor: ObservabilityRedactor,
		p_limiter: ObservabilitySignalLimiter,
		p_limiter_mutex: Mutex,
		p_runtime: ObservabilityRuntime,
) -> void:
	assert(p_generation >= 0, "ObservabilityProcessingLease requires a generation.")
	assert(p_operation_token > 0, "ObservabilityProcessingLease requires an operation token.")
	assert(p_owner_id >= 0, "ObservabilityProcessingLease requires an owner.")
	assert(
			p_signal == ObservabilitySignal.EVENT \
					or p_signal == ObservabilitySignal.LOG \
					or p_signal == ObservabilitySignal.METRIC \
					or p_signal == ObservabilitySignal.STATE,
			"ObservabilityProcessingLease requires a valid signal.",
		)
	assert(p_redactor != null, "ObservabilityProcessingLease requires a redactor.")
	assert(p_limiter != null, "ObservabilityProcessingLease requires a limiter.")
	assert(p_limiter_mutex != null, "ObservabilityProcessingLease requires a limiter mutex.")
	assert(p_runtime != null, "ObservabilityProcessingLease requires a runtime.")
	_generation = p_generation
	_operation_token = p_operation_token
	_owner_id = p_owner_id
	_signal = p_signal
	_processors = _copy_processors(p_processors)
	_redactor = p_redactor
	_limiter = p_limiter
	_limiter_mutex = p_limiter_mutex
	_runtime = p_runtime


func generation() -> int:
	return _generation


func operation_token() -> int:
	return _operation_token


func owner_id() -> int:
	return _owner_id


func processing_signal() -> ObservabilitySignal:
	return _signal


func processors() -> Array[Callable[[T], T?]]:
	return _copy_processors(_processors)


func redactor() -> ObservabilityRedactor:
	return _redactor


func limiter() -> ObservabilitySignalLimiter:
	return _limiter


func limiter_mutex() -> Mutex:
	return _limiter_mutex


func runtime() -> ObservabilityRuntime:
	return _runtime


func _copy_processors(
		source: Array[Callable[[T], T?]],
) -> Array[Callable[[T], T?]]:
	var copied: Array[Callable[[T], T?]] = []
	for processor: Callable[[T], T?] in source:
		copied.append(processor)
	return copied
