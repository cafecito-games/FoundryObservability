namespace foundry.observability

import foundry.observability.processing

## Immutable payload-free result emitted while processing an observability signal.
final class_name ObservabilityProcessingDiagnostic extends RefCounted

final var _sequence: int
final var _signal: ObservabilitySignal
final var _outcome: ObservabilityProcessingOutcome
final var _reason: ObservabilityProcessingReason
final var _processor_index: int
final var _redaction_rule_index: int
final var _limit_kind: ObservabilityLimitKind
final var _error: int


func _init(
		p_sequence: int = 0,
		p_signal: ObservabilitySignal = ObservabilitySignal.EVENT,
		p_outcome: ObservabilityProcessingOutcome = ObservabilityProcessingOutcome.ACCEPTED,
		p_reason: ObservabilityProcessingReason = ObservabilityProcessingReason.NONE,
		p_processor_index: int = -1,
		p_redaction_rule_index: int = -1,
		p_limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
		p_error: int = Error.OK,
) -> void:
	_sequence = p_sequence
	_signal = p_signal
	_outcome = p_outcome
	_reason = p_reason
	_processor_index = p_processor_index
	_redaction_rule_index = p_redaction_rule_index
	_limit_kind = p_limit_kind
	_error = p_error


func sequence() -> int:
	return _sequence


## Returns the diagnostic signal; `signal` is a reserved Foundry Script keyword.
func processing_signal() -> ObservabilitySignal:
	return _signal


func outcome() -> ObservabilityProcessingOutcome:
	return _outcome


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


func duplicate() -> ObservabilityProcessingDiagnostic:
	return ObservabilityProcessingDiagnostic.new(
			_sequence,
			_signal,
			_outcome,
			_reason,
			_processor_index,
			_redaction_rule_index,
			_limit_kind,
			_error,
		)
