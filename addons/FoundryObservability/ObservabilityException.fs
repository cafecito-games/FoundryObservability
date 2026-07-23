namespace games.cafecito.foundryobservability

## Provider-neutral exception payload.
class_name ObservabilityException
extends RefCounted

var _type_name: String = "Error"
var _message: String = ""
var _stack_trace: String = ""
var _attributes: Dictionary = {}


## Creates an exception payload from script or native failure data.
func _init(
		p_type_name: String = "Error",
		p_message: String = "",
		p_stack_trace: String = "",
		p_attributes: Dictionary = {}
) -> void:
	_type_name = p_type_name
	_message = p_message
	_stack_trace = p_stack_trace
	_attributes = p_attributes.duplicate(true)


func type_name() -> String:
	return _type_name


func message() -> String:
	return _message


func stack_trace() -> String:
	return _stack_trace


func attributes() -> Dictionary:
	return _attributes.duplicate(true)
