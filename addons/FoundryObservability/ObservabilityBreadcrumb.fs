namespace foundry.observability

## Provider-neutral breadcrumb payload.
class_name ObservabilityBreadcrumb
extends RefCounted

var _message: String = ""
var _level: int = ObservabilityLevel.INFO
var _category: StringName = &""
var _timestamp_msec: int = 0
var _attributes: Dictionary = {}


## Creates a breadcrumb with copied attributes.
func _init(
		p_message: String = "",
		p_level: int = ObservabilityLevel.INFO,
		p_category: StringName = &"",
		p_timestamp_msec: int = 0,
		p_attributes: Dictionary = {},
) -> void:
	_message = p_message
	_level = p_level
	_category = p_category
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)


## Returns the human-readable breadcrumb message.
func message() -> String:
	return _message


## Returns the normalized breadcrumb severity.
func level() -> int:
	return _level


## Returns the stable breadcrumb category.
func category() -> StringName:
	return _category


## Returns the engine timestamp in milliseconds.
func timestamp_msec() -> int:
	return _timestamp_msec


## Returns a deep copy of structured breadcrumb attributes.
func attributes() -> Dictionary:
	return _attributes.duplicate(true)
