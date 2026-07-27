namespace foundry.observability

## Immutable rule for removing or replacing a selected payload value.
class_name ObservabilityRedactionRule
extends RefCounted

const REMOVE_FIELD: int = 0
const REPLACE_VALUE: int = 1
const REPLACE_TEXT: int = 2

final var _path: PackedStringArray
final var _action: int
final var _pattern: String
final var _replacement: Variant
final var _replacement_is_cyclic: bool


func _init(
		p_path: PackedStringArray = PackedStringArray(),
		p_action: int = REMOVE_FIELD,
		p_pattern: String = "",
		p_replacement: Variant = null,
		p_replacement_is_cyclic: bool = false,
) -> void:
	_path = p_path.duplicate()
	_action = p_action
	_pattern = p_pattern
	var copied: Dictionary = _copy_replacement(p_replacement, [])
	_replacement_is_cyclic = p_replacement_is_cyclic or not copied["valid"]
	_replacement = null if _replacement_is_cyclic else copied["value"]


static func remove_field(p_path: PackedStringArray) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(p_path, REMOVE_FIELD)


static func replace_value(
		p_path: PackedStringArray,
		p_replacement: Variant,
) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(p_path, REPLACE_VALUE, "", p_replacement)


static func replace_text(
		p_path: PackedStringArray,
		p_pattern: String = "",
		p_replacement: String = "[REDACTED]",
) -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(
			p_path, REPLACE_TEXT, p_pattern, p_replacement)


static func sensitive_key(
		key: String,
		p_replacement: String = "[REDACTED]",
) -> ObservabilityRedactionRule:
	return replace_text(PackedStringArray(["**", key]), "", p_replacement)


func path() -> PackedStringArray:
	return _path.duplicate()


func action() -> int:
	return _action


func pattern() -> String:
	return _pattern


func replacement() -> Variant:
	if _replacement_is_cyclic:
		return null
	var copied: Dictionary = _copy_replacement(_replacement, [])
	return null if not copied["valid"] else copied["value"]


func duplicate() -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(
			_path,
			_action,
			_pattern,
			_replacement,
			_replacement_is_cyclic,
	)


func is_valid() -> bool:
	if _replacement_is_cyclic or not _is_valid_path() or not _is_valid_action():
		return false
	if _action == REMOVE_FIELD or _action == REPLACE_VALUE:
		return _pattern.is_empty()
	if not (_replacement is String):
		return false
	var expression: RegEx = RegEx.new()
	return expression.compile(_pattern) == Error.OK


func _is_valid_path() -> bool:
	if _path.is_empty():
		return false
	for segment: String in _path:
		if segment.is_empty() or segment.strip_edges() != segment:
			return false
	return true


func _is_valid_action() -> bool:
	return _action == REMOVE_FIELD \
			or _action == REPLACE_VALUE \
			or _action == REPLACE_TEXT


func _copy_replacement(value: Variant, active_containers: Array) -> Dictionary:
	if value is Dictionary:
		if _is_active_container(value, active_containers):
			return {"valid": false}
		active_containers.append(value)
		var copied: Dictionary = value.duplicate()
		for key: Variant in value:
			var nested: Dictionary = _copy_replacement(value[key], active_containers)
			if not nested["valid"]:
				active_containers.pop_back()
				return {"valid": false}
			copied[key] = nested["value"]
		active_containers.pop_back()
		return {"valid": true, "value": copied}
	if value is Array:
		if _is_active_container(value, active_containers):
			return {"valid": false}
		active_containers.append(value)
		var copied: Array = value.duplicate()
		for index: int in range(value.size()):
			var nested: Dictionary = _copy_replacement(value[index], active_containers)
			if not nested["valid"]:
				active_containers.pop_back()
				return {"valid": false}
			copied[index] = nested["value"]
		active_containers.pop_back()
		return {"valid": true, "value": copied}
	if value is PackedByteArray:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedInt32Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedInt64Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedFloat32Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedFloat64Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedStringArray:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedVector2Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedVector3Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedVector4Array:
		return {"valid": true, "value": value.duplicate()}
	if value is PackedColorArray:
		return {"valid": true, "value": value.duplicate()}
	return {"valid": true, "value": value}


func _is_active_container(value: Variant, active_containers: Array) -> bool:
	for active_container: Variant in active_containers:
		if is_same(value, active_container):
			return true
	return false
