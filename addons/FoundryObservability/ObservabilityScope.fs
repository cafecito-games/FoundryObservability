namespace foundry.observability

## Provider-neutral event-local tags and structured contexts.
class_name ObservabilityScope
extends RefCounted

const MAX_CONTAINER_DEPTH: int = 8
const MAX_CONTAINER_ITEMS: int = 256

final var _tags: Dictionary = {}
final var _contexts: Dictionary = {}


func tags() -> Dictionary:
	return _tags.duplicate(true)


func contexts() -> Dictionary:
	return _contexts.duplicate(true)


func set_tag(key: String, value: String) -> bool:
	if not _is_valid_name(key):
		return false
	_tags[key] = value
	return true


func remove_tag(key: String) -> bool:
	if not _is_valid_name(key) or not _tags.has(key):
		return false
	_tags.erase(key)
	return true


func clear_tags() -> void:
	_tags.clear()


func set_context(name: String, value: Dictionary) -> bool:
	if not _is_valid_name(name):
		return false
	var budget: Dictionary = {"remaining": MAX_CONTAINER_ITEMS}
	var active_containers: Array = [value]
	var normalized: Dictionary = _normalize_dictionary(
			value,
			0,
			budget,
			active_containers,
	)
	if not normalized["valid"]:
		return false
	_contexts[name] = normalized["value"]
	return true


func remove_context(name: String) -> bool:
	if not _is_valid_name(name) or not _contexts.has(name):
		return false
	_contexts.erase(name)
	return true


func clear_contexts() -> void:
	_contexts.clear()


func is_empty() -> bool:
	return _tags.is_empty() and _contexts.is_empty()


func duplicate() -> ObservabilityScope:
	var copied: ObservabilityScope = ObservabilityScope.new()
	for key: String in _tags:
		copied._tags[key] = _tags[key]
	for name: String in _contexts:
		var context: Dictionary = _contexts[name]
		copied._contexts[name] = context.duplicate(true)
	return copied


func _normalize_dictionary(
		source: Dictionary,
		container_depth: int,
		budget: Dictionary,
		active_containers: Array,
) -> Dictionary:
	var normalized: Dictionary = {}
	for key: Variant in source:
		if not _consume_item(budget):
			return {"valid": false}
		if not (key is String) and not (key is StringName):
			return {"valid": false}
		var value_result: Dictionary = _normalize_value(
				source[key],
				container_depth,
				budget,
				active_containers,
		)
		if not value_result["valid"]:
			return {"valid": false}
		normalized[str(key)] = value_result["value"]
	return {"valid": true, "value": normalized}


func _normalize_array(
		source: Array,
		container_depth: int,
		budget: Dictionary,
		active_containers: Array,
) -> Dictionary:
	var normalized: Array = []
	for value: Variant in source:
		if not _consume_item(budget):
			return {"valid": false}
		var value_result: Dictionary = _normalize_value(
				value,
				container_depth,
				budget,
				active_containers,
		)
		if not value_result["valid"]:
			return {"valid": false}
		normalized.append(value_result["value"])
	return {"valid": true, "value": normalized}


func _normalize_value(
		value: Variant,
		container_depth: int,
		budget: Dictionary,
		active_containers: Array,
) -> Dictionary:
	if value == null:
		return {"valid": true, "value": null}
	if value is bool or value is int or value is String:
		return {"valid": true, "value": value}
	if value is StringName:
		return {"valid": true, "value": str(value)}
	if value is float:
		return {"valid": is_finite(value), "value": value}
	if value is Array:
		if container_depth + 1 > MAX_CONTAINER_DEPTH \
				or not _enter_container(value, active_containers):
			return {"valid": false}
		var array_result: Dictionary = _normalize_array(
				value,
				container_depth + 1,
				budget,
				active_containers,
		)
		active_containers.pop_back()
		return array_result
	if value is Dictionary:
		if container_depth + 1 > MAX_CONTAINER_DEPTH \
				or not _enter_container(value, active_containers):
			return {"valid": false}
		var dictionary_result: Dictionary = _normalize_dictionary(
				value,
				container_depth + 1,
				budget,
				active_containers,
		)
		active_containers.pop_back()
		return dictionary_result
	return {"valid": false}


func _consume_item(budget: Dictionary) -> bool:
	var remaining: int = budget["remaining"]
	if remaining <= 0:
		return false
	budget["remaining"] = remaining - 1
	return true


func _enter_container(container: Variant, active_containers: Array) -> bool:
	for active_container: Variant in active_containers:
		if is_same(container, active_container):
			return false
	active_containers.append(container)
	return true


func _is_valid_name(value: String) -> bool:
	return not value.is_empty() \
			and value.strip_edges() == value \
			and not _has_surrounding_whitespace(value) \
			and not _has_control_character(value)


func _has_surrounding_whitespace(value: String) -> bool:
	return _is_whitespace(value.unicode_at(0)) \
			or _is_whitespace(value.unicode_at(value.length() - 1))


func _is_whitespace(codepoint: int) -> bool:
	return (codepoint >= 9 and codepoint <= 13) \
			or codepoint == 32 \
			or codepoint == 133 \
			or codepoint == 160 \
			or codepoint == 5760 \
			or (codepoint >= 8192 and codepoint <= 8202) \
			or codepoint == 8232 \
			or codepoint == 8233 \
			or codepoint == 8239 \
			or codepoint == 8287 \
			or codepoint == 12288


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or (codepoint >= 127 and codepoint <= 159):
			return true
	return false
