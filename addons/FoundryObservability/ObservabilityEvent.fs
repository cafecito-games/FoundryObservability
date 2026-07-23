namespace games.cafecito.foundryobservability

## Provider-neutral event payload.
class_name ObservabilityEvent
extends RefCounted

var _kind: StringName = &"message"
var _level: int = ObservabilityLevel.INFO
var _message: String = ""
var _source: StringName = &""
var _timestamp_msec: int = 0
var _attributes: Dictionary = {}
var _exception: ObservabilityException? = null


## Creates an event payload with optional exception data.
func _init(
		p_kind: StringName = &"message",
		p_level: int = ObservabilityLevel.INFO,
		p_message: String = "",
		p_source: StringName = &"",
		p_timestamp_msec: int = 0,
		p_attributes: Dictionary = {},
		p_exception: ObservabilityException? = null
) -> void:
	_kind = p_kind
	_level = p_level
	_message = p_message
	_source = p_source
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)
	_exception = p_exception


func kind() -> StringName:
	return _kind


func level() -> int:
	return _level


func message() -> String:
	return _message


func source() -> StringName:
	return _source


func timestamp_msec() -> int:
	return _timestamp_msec


func attributes() -> Dictionary:
	return _attributes.duplicate(true)


func exception() -> ObservabilityException?:
	return _exception
