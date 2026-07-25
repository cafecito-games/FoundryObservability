namespace foundry.observability

## Provider-neutral exception payload.
class_name ObservabilityException
extends RefCounted

var _type_name: String = "Error"
var _message: String = ""
var _stack_trace: String = ""
var _attributes: Dictionary = {}
var _frames: Array[ObservabilityStackFrame] = []


## Creates an exception payload from type, message, stack, and copied attributes.
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
