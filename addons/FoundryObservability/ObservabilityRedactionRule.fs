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


func _init(
		p_path: PackedStringArray = PackedStringArray(),
		p_action: int = REMOVE_FIELD,
		p_pattern: String = "",
		p_replacement: Variant = null,
) -> void:
	_path = p_path.duplicate()
	_action = p_action
	_pattern = p_pattern
	_replacement = _copy_replacement(p_replacement)


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
	return _copy_replacement(_replacement)


func duplicate() -> ObservabilityRedactionRule:
	return ObservabilityRedactionRule.new(_path, _action, _pattern, _replacement)


func is_valid() -> bool:
	if not _is_valid_path() or not _is_valid_action():
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


func _copy_replacement(value: Variant) -> Variant:
	if value is Dictionary:
		return value.duplicate(true)
	if value is Array:
		return value.duplicate(true)
	if value is PackedByteArray:
		return value.duplicate()
	if value is PackedInt32Array:
		return value.duplicate()
	if value is PackedInt64Array:
		return value.duplicate()
	if value is PackedFloat32Array:
		return value.duplicate()
	if value is PackedFloat64Array:
		return value.duplicate()
	if value is PackedStringArray:
		return value.duplicate()
	if value is PackedVector2Array:
		return value.duplicate()
	if value is PackedVector3Array:
		return value.duplicate()
	if value is PackedVector4Array:
		return value.duplicate()
	if value is PackedColorArray:
		return value.duplicate()
	return value
