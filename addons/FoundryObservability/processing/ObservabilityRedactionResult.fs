namespace foundry.observability.processing

## Immutable typed redaction outcome.
## Successful values use rule index -1 and Error.OK; failures use a rule index
## greater than or equal to -1 and Error.ERR_INVALID_DATA. Invalid public
## construction is reported and canonicalized to a payload-free failure at index -1.
final class_name ObservabilityRedactionResult[T] extends RefCounted

final var _valid: bool
final var _value: T?
final var _failed_rule_index: int
final var _error: int


func _init(
		p_valid: bool,
		p_value: T?,
		p_failed_rule_index: int,
		p_error: int,
) -> void:
	## Invalid inputs are reported and canonicalized to a redaction failure.
	if not is_valid_state(p_valid, p_value, p_failed_rule_index, p_error):
		push_error("ObservabilityRedactionResult requires a valid redaction state.")
		_valid = false
		_value = null
		_failed_rule_index = -1
		_error = Error.ERR_INVALID_DATA
		assert(
				is_valid_state(_valid, _value, _failed_rule_index, _error),
				"ObservabilityRedactionResult fallback must remain valid.",
			)
		return
	_valid = p_valid
	_value = p_value
	_failed_rule_index = p_failed_rule_index
	_error = p_error


static func success(p_value: T?) -> ObservabilityRedactionResult[T]:
	return ObservabilityRedactionResult[T].new(true, p_value, -1, Error.OK)


static func failure(
		p_failed_rule_index: int = -1,
) -> ObservabilityRedactionResult[T]:
	return ObservabilityRedactionResult[T].new(
			false, null, p_failed_rule_index, Error.ERR_INVALID_DATA,
		)


## Returns whether fields form one of the closed redaction result shapes.
static func is_valid_state(
		p_valid: bool,
		p_value: T?,
		p_failed_rule_index: int,
		p_error: int,
) -> bool:
	if p_valid:
		return p_value != null and p_failed_rule_index == -1 and p_error == Error.OK
	return p_value == null \
			and p_failed_rule_index >= -1 \
			and p_error == Error.ERR_INVALID_DATA


func valid() -> bool:
	return _valid


func value() -> T?:
	return _value


func failed_rule_index() -> int:
	return _failed_rule_index


func error() -> int:
	return _error
