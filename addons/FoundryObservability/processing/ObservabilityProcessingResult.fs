namespace foundry.observability.processing

## Immutable typed processing outcome.
## Accepted results carry a positive operation token. Dropped and failed results use
## token -1; processor and redaction indices also use -1 when no index applies.
## Drops require a non-NONE reason, with a non-NONE limit exactly for RATE_LIMITED.
## Failures require a non-NONE reason, no limit, and a non-OK error. Invalid public
## construction is reported and canonicalized to a payload-free INVALID_PAYLOAD failure;
## an invalid input signal becomes EVENT so the fallback itself remains a valid closed shape.
final class_name ObservabilityProcessingResult[T] extends RefCounted

final var _outcome: ObservabilityProcessingOutcome
final var _signal: ObservabilitySignal
final var _value: T?
final var _operation_token: int
final var _reason: ObservabilityProcessingReason
final var _processor_index: int
final var _redaction_rule_index: int
final var _limit_kind: ObservabilityLimitKind
final var _error: int


func _init(
		p_outcome: ObservabilityProcessingOutcome,
		p_signal: ObservabilitySignal,
		p_value: T?,
		p_operation_token: int,
		p_reason: ObservabilityProcessingReason,
		p_processor_index: int,
		p_redaction_rule_index: int,
		p_limit_kind: ObservabilityLimitKind,
		p_error: int,
) -> void:
	## Invalid inputs are reported and canonicalized to a failed invalid-payload result.
	if not is_valid_state(
				p_outcome,
				p_signal,
				p_value,
				p_operation_token,
				p_reason,
				p_processor_index,
				p_redaction_rule_index,
				p_limit_kind,
				p_error,
			):
		push_error("ObservabilityProcessingResult requires a valid processing state.")
		var fallback_signal: ObservabilitySignal = p_signal
		if not _is_valid_signal(fallback_signal):
			fallback_signal = ObservabilitySignal.EVENT
		_outcome = ObservabilityProcessingOutcome.FAILED
		_signal = fallback_signal
		_value = null
		_operation_token = -1
		_reason = ObservabilityProcessingReason.INVALID_PAYLOAD
		_processor_index = -1
		_redaction_rule_index = -1
		_limit_kind = ObservabilityLimitKind.NONE
		_error = Error.ERR_INVALID_DATA
		assert(
				is_valid_state(
						_outcome,
						_signal,
						_value,
						_operation_token,
						_reason,
						_processor_index,
						_redaction_rule_index,
						_limit_kind,
						_error,
					),
				"ObservabilityProcessingResult fallback must remain valid.",
			)
		return
	_outcome = p_outcome
	_signal = p_signal
	_value = p_value
	_operation_token = p_operation_token
	_reason = p_reason
	_processor_index = p_processor_index
	_redaction_rule_index = p_redaction_rule_index
	_limit_kind = p_limit_kind
	_error = p_error


static func accepted(
		p_signal: ObservabilitySignal,
		p_value: T?,
		p_operation_token: int,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.ACCEPTED,
			p_signal,
			p_value,
			p_operation_token,
			ObservabilityProcessingReason.NONE,
			-1,
			-1,
			ObservabilityLimitKind.NONE,
			Error.OK,
		)


static func dropped(
		p_signal: ObservabilitySignal,
		p_reason: ObservabilityProcessingReason,
		p_limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
		p_processor_index: int = -1,
		p_redaction_rule_index: int = -1,
		p_error: int = Error.OK,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.DROPPED,
			p_signal,
			null,
			-1,
			p_reason,
			p_processor_index,
			p_redaction_rule_index,
			p_limit_kind,
			p_error,
		)


static func failed(
		p_signal: ObservabilitySignal,
		p_reason: ObservabilityProcessingReason,
		p_error: int,
		p_processor_index: int = -1,
		p_redaction_rule_index: int = -1,
) -> ObservabilityProcessingResult[T]:
	return ObservabilityProcessingResult[T].new(
			ObservabilityProcessingOutcome.FAILED,
			p_signal,
			null,
			-1,
			p_reason,
			p_processor_index,
			p_redaction_rule_index,
			ObservabilityLimitKind.NONE,
			p_error,
		)


## Returns whether fields form one of the closed processing result shapes.
static func is_valid_state(
		p_outcome: ObservabilityProcessingOutcome,
		p_signal: ObservabilitySignal,
		p_value: T?,
		p_operation_token: int,
		p_reason: ObservabilityProcessingReason,
		p_processor_index: int,
		p_redaction_rule_index: int,
		p_limit_kind: ObservabilityLimitKind,
		p_error: int,
) -> bool:
	if not _is_valid_outcome(p_outcome) \
			or not _is_valid_signal(p_signal) \
			or not _is_valid_reason(p_reason) \
			or not _is_valid_limit_kind(p_limit_kind) \
			or p_processor_index < -1 \
			or p_redaction_rule_index < -1:
		return false
	match p_outcome:
		ObservabilityProcessingOutcome.ACCEPTED:
			return p_value != null \
					and p_operation_token > 0 \
					and p_reason == ObservabilityProcessingReason.NONE \
					and p_processor_index == -1 \
					and p_redaction_rule_index == -1 \
					and p_limit_kind == ObservabilityLimitKind.NONE \
					and p_error == Error.OK
		ObservabilityProcessingOutcome.DROPPED:
			if p_value != null \
					or p_operation_token != -1 \
					or p_reason == ObservabilityProcessingReason.NONE:
				return false
			return (p_reason == ObservabilityProcessingReason.RATE_LIMITED) \
					== (p_limit_kind != ObservabilityLimitKind.NONE)
		ObservabilityProcessingOutcome.FAILED:
			return p_value == null \
					and p_operation_token == -1 \
					and p_reason != ObservabilityProcessingReason.NONE \
					and p_limit_kind == ObservabilityLimitKind.NONE \
					and p_error != Error.OK
	return false


static func _is_valid_outcome(p_outcome: ObservabilityProcessingOutcome) -> bool:
	return p_outcome == ObservabilityProcessingOutcome.ACCEPTED \
			or p_outcome == ObservabilityProcessingOutcome.DROPPED \
			or p_outcome == ObservabilityProcessingOutcome.FAILED


static func _is_valid_signal(p_signal: ObservabilitySignal) -> bool:
	return p_signal == ObservabilitySignal.EVENT \
			or p_signal == ObservabilitySignal.LOG \
			or p_signal == ObservabilitySignal.METRIC \
			or p_signal == ObservabilitySignal.STATE


static func _is_valid_reason(p_reason: ObservabilityProcessingReason) -> bool:
	return p_reason == ObservabilityProcessingReason.NONE \
			or p_reason == ObservabilityProcessingReason.PROCESSOR \
			or p_reason == ObservabilityProcessingReason.SAMPLED \
			or p_reason == ObservabilityProcessingReason.RATE_LIMITED \
			or p_reason == ObservabilityProcessingReason.RECURSIVE \
			or p_reason == ObservabilityProcessingReason.INVALID_PROCESSOR_RESULT \
			or p_reason == ObservabilityProcessingReason.REDACTION_FAILED \
			or p_reason == ObservabilityProcessingReason.INVALID_PAYLOAD \
			or p_reason == ObservabilityProcessingReason.PROVIDER_REJECTED \
			or p_reason == ObservabilityProcessingReason.STALE_GENERATION


static func _is_valid_limit_kind(p_limit_kind: ObservabilityLimitKind) -> bool:
	return p_limit_kind == ObservabilityLimitKind.NONE \
			or p_limit_kind == ObservabilityLimitKind.PER_FRAME \
			or p_limit_kind == ObservabilityLimitKind.REPEATED \
			or p_limit_kind == ObservabilityLimitKind.WINDOW \
			or p_limit_kind == ObservabilityLimitKind.LEGACY_LOG_WINDOW


func outcome() -> ObservabilityProcessingOutcome:
	return _outcome


## Returns the signal family; "signal" is a Foundry Script declaration keyword.
func processing_signal() -> ObservabilitySignal:
	return _signal


func value() -> T?:
	return _value


func operation_token() -> int:
	return _operation_token


func reason() -> ObservabilityProcessingReason:
	return _reason


func processor_index() -> int:
	return _processor_index


func redaction_rule_index() -> int:
	return _redaction_rule_index


func limit_kind() -> ObservabilityLimitKind:
	return _limit_kind


func error() -> int:
	return _error
