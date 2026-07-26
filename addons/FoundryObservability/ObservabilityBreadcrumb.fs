namespace foundry.observability

## Provider-neutral breadcrumb payload.
class_name ObservabilityBreadcrumb
extends RefCounted

final var _message: String
final var _level: int
final var _category: StringName
final var _timestamp_msec: int
final var _attributes: Dictionary
final var _type: StringName


## Creates a breadcrumb with copied attributes.
func _init(
		p_message: String = "",
		p_level: int = ObservabilityLevel.INFO,
		p_category: StringName = &"",
		p_timestamp_msec: int = 0,
		p_attributes: Dictionary = {},
		p_type: StringName = &"default",
) -> void:
	_message = p_message
	_level = p_level
	_category = p_category
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)
	_type = p_type


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


## Returns the provider-neutral breadcrumb type.
func type() -> StringName:
	return _type
