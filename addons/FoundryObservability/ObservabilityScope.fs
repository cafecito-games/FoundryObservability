namespace foundry.observability

import foundry.observability.processing

## Provider-neutral event-local tags and structured contexts.
class_name ObservabilityScope
extends RefCounted

const MAX_CONTAINER_DEPTH: int = 8
const MAX_CONTAINER_ITEMS: int = 256

final var _tags: Dictionary = {}
final var _contexts: Dictionary = {}


class ScopeValuePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	func visit(
			_path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		if value == null or value is bool or value is int or value is String:
			return ObservabilityValueVisitDecision.keep(value)
		if value is StringName:
			return ObservabilityValueVisitDecision.keep(str(value))
		if value is float and is_finite(value):
			return ObservabilityValueVisitDecision.keep(value)
		return ObservabilityValueVisitDecision.reject()

	func visit_dictionary_key(
			_path: PackedStringArray,
			key: Variant,
	) -> ObservabilityValueVisitDecision:
		if key is String or key is StringName:
			return ObservabilityValueVisitDecision.keep(str(key))
		return ObservabilityValueVisitDecision.reject()


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
	var normalized: ObservabilityRedactionResult[Variant] = (
			ObservabilityValueWalker.new(
				MAX_CONTAINER_DEPTH,
				MAX_CONTAINER_ITEMS + 1,
			).walk(
				value,
				ScopeValuePolicy.new(),
			)
		)
	if not normalized.valid() or not (normalized.value() is Dictionary):
		return false
	@warning_ignore("unsafe_cast")
	_contexts[name] = normalized.value() as Dictionary
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
