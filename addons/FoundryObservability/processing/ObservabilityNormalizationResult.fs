namespace foundry.observability.processing

## Immutable typed result for provider-neutral input normalization.
## Invalid public construction is reported and canonicalized to a payload-free failure.
final class_name ObservabilityNormalizationResult[T] extends RefCounted

final var _valid: bool
final var _value: T?
final var _error: int


func _init(
		p_valid: bool,
		p_value: T?,
		p_error: int,
) -> void:
	if not is_valid_state(p_valid, p_value, p_error):
		push_error("ObservabilityNormalizationResult requires a valid normalization state.")
		_valid = false
		_value = null
		_error = Error.ERR_INVALID_DATA
		return
	_valid = p_valid
	_value = p_value
	_error = p_error


@warning_ignore("shadowed_variable")
static func success(value: T) -> ObservabilityNormalizationResult[T]:
	return ObservabilityNormalizationResult[T].new(true, value, Error.OK)


@warning_ignore("shadowed_variable")
static func failure(error: int) -> ObservabilityNormalizationResult[T]:
	return ObservabilityNormalizationResult[T].new(false, null, error)


## Returns whether fields form one of the two closed normalization result shapes.
static func is_valid_state(p_valid: bool, p_value: T?, p_error: int) -> bool:
	if p_valid:
		return p_value != null and p_error == Error.OK
	return p_value == null and p_error != Error.OK


func valid() -> bool:
	return _valid


func value() -> T?:
	return _value


func error() -> int:
	return _error
