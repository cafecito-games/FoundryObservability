namespace foundry.observability

## Immutable payload-free result emitted while processing an observability signal.
class_name ObservabilityProcessingDiagnostic
extends RefCounted

const EVENT: StringName = &"event"
const LOG: StringName = &"log"
const METRIC: StringName = &"metric"
const STATE: StringName = &"state"

const ACCEPTED: StringName = &"accepted"
const DROPPED: StringName = &"dropped"

const PROCESSOR: StringName = &"processor"
const SAMPLED: StringName = &"sampled"
const RATE_LIMITED: StringName = &"rate_limited"
const RECURSIVE: StringName = &"recursive"
const INVALID_PROCESSOR_RESULT: StringName = &"invalid_processor_result"
const REDACTION_FAILED: StringName = &"redaction_failed"
const INVALID_PAYLOAD: StringName = &"invalid_payload"
const PROVIDER_REJECTED: StringName = &"provider_rejected"

const PER_FRAME: StringName = &"per_frame"
const REPEATED: StringName = &"repeated"
const WINDOW: StringName = &"window"
const LEGACY_LOG_WINDOW: StringName = &"legacy_log_window"

final var _sequence: int
final var _signal: StringName
final var _outcome: StringName
final var _reason: StringName
final var _processor_index: int
final var _rule_index: int
final var _limit_kind: StringName
final var _error: int


func _init(
		p_sequence: int = 0,
		p_signal: StringName = EVENT,
		p_outcome: StringName = ACCEPTED,
		p_reason: StringName = &"",
		p_processor_index: int = -1,
		p_rule_index: int = -1,
		p_limit_kind: StringName = &"",
		p_error: int = Error.OK,
) -> void:
	_sequence = p_sequence
	_signal = p_signal
	_outcome = p_outcome
	_reason = p_reason
	_processor_index = p_processor_index
	_rule_index = p_rule_index
	_limit_kind = p_limit_kind
	_error = p_error


func sequence() -> int:
	return _sequence


## Returns the diagnostic signal; `signal` is a reserved FoundryScript keyword.
func processing_signal() -> StringName:
	return _signal


func outcome() -> StringName:
	return _outcome


func reason() -> StringName:
	return _reason


func processor_index() -> int:
	return _processor_index


func rule_index() -> int:
	return _rule_index


func limit_kind() -> StringName:
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
			_rule_index,
			_limit_kind,
			_error,
	)
