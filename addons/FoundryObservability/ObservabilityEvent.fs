namespace foundry.observability

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


## Creates an event with kind, severity, source, timestamp, copied attributes, and optional exception data.
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


## Returns the event kind, normally message, exception, or log.
func kind() -> StringName:
	return _kind


## Returns the event severity value.
func level() -> int:
	return _level


## Returns the human-readable event message.
func message() -> String:
	return _message


## Returns the subsystem or producer that created the event.
func source() -> StringName:
	return _source


## Returns the event timestamp in engine milliseconds.
func timestamp_msec() -> int:
	return _timestamp_msec


## Returns a deep copy of structured event attributes.
func attributes() -> Dictionary:
	return _attributes.duplicate(true)


## Returns the optional exception payload associated with the event.
func exception() -> ObservabilityException?:
	return _exception
