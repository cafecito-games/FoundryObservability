namespace foundry.observability

## Provider-neutral exception payload.
class_name ObservabilityException
extends RefCounted

final var _type_name: String
final var _message: String
final var _stack_trace: String
final var _attributes: Dictionary
final var _frames: Array[ObservabilityStackFrame]


## Creates an exception payload from type, message, stack, copied attributes, and copied structured frames.
func _init(
		p_type_name: String = "Error",
		p_message: String = "",
		p_stack_trace: String = "",
		p_attributes: Dictionary = {},
		p_frames: Array[ObservabilityStackFrame] = [],
) -> void:
	_type_name = p_type_name
	_message = p_message
	_stack_trace = p_stack_trace
	_attributes = p_attributes.duplicate(true)
	_frames = p_frames.duplicate()


## Returns the source exception type name.
func type_name() -> String:
	return _type_name


## Returns the exception message.
func message() -> String:
	return _message


## Returns the captured stack trace, if available.
func stack_trace() -> String:
	return _stack_trace


## Returns a deep copy of exception attributes.
func attributes() -> Dictionary:
	return _attributes.duplicate(true)


## Returns a copy of the structured stack frames, ordered oldest-to-newest.
func frames() -> Array[ObservabilityStackFrame]:
	return _frames.duplicate()


## Returns an isolated exception payload with copied attributes and frame storage.
func duplicate() -> ObservabilityException:
	return ObservabilityException.new(
			p_type_name = _type_name,
			p_message = _message,
			p_stack_trace = _stack_trace,
			p_attributes = _attributes,
			p_frames = _frames,
		)
