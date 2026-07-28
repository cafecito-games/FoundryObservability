namespace foundry.observability

import foundry.observability.processing

## Provider-neutral structured exception stack frame.
class_name ObservabilityStackFrame
extends RefCounted

final var _file: String
final var _function: String
final var _line: int
final var _language: String
final var _in_app: bool
final var _context_line: String
final var _pre_context: PackedStringArray
final var _post_context: PackedStringArray
final var _variables: Dictionary

## Maximum nested Array and Dictionary depth retained from frame variables.
const MAX_VARIABLE_CONTAINER_DEPTH: int = 8
## Maximum total Dictionary entries and Array elements examined per frame.
const MAX_VARIABLE_ITEMS: int = 256


class OwnedVariablePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	func visit(
			_path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		return ObservabilityValueVisitDecision.keep(value)

	func visit_dictionary_key(
			_path: PackedStringArray,
			key: Variant,
	) -> ObservabilityValueVisitDecision:
		if key is Dictionary or key is Array:
			return ObservabilityValueVisitDecision.reject()
		return ObservabilityValueVisitDecision.keep(key)

	func reject_is_failure() -> bool:
		return false

	func invalid_container_is_failure() -> bool:
		return false

	func item_limit_is_failure() -> bool:
		return false


class SanitizedVariablePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	func visit(
			_path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		if value is bool or value is int or value is String:
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

	func reject_is_failure() -> bool:
		return false

	func invalid_container_is_failure() -> bool:
		return false

	func item_limit_is_failure() -> bool:
		return false


## Creates a structured stack frame with defensively copied contextual data.
func _init(
		p_file: String = "",
		p_function: String = "",
		p_line: int = -1,
		p_language: String = "",
		p_in_app: bool = true,
		p_context_line: String = "",
		p_pre_context: PackedStringArray = PackedStringArray(),
		p_post_context: PackedStringArray = PackedStringArray(),
		p_variables: Dictionary = {},
) -> void:
	_file = p_file
	_function = p_function
	_line = p_line
	_language = p_language
	_in_app = p_in_app
	_context_line = p_context_line
	_pre_context = p_pre_context.duplicate()
	_post_context = p_post_context.duplicate()
	_variables = _walk_variables(
			p_variables,
			MAX_VARIABLE_CONTAINER_DEPTH,
			MAX_VARIABLE_ITEMS,
			OwnedVariablePolicy.new(),
		)


## Returns the source file path, if available.
func file() -> String:
	return _file


## Returns the function name, if available.
func function() -> String:
	return _function


## Returns the one-based source line, or -1 if unavailable.
func line() -> int:
	return _line


## Returns the source language identifier, if available.
func language() -> String:
	return _language


## Returns whether this frame belongs to application code.
func in_app() -> bool:
	return _in_app


## Returns the source line associated with this frame, if available.
func context_line() -> String:
	return _context_line


## Returns a copy of the source lines preceding this frame.
func pre_context() -> PackedStringArray:
	return _pre_context.duplicate()


## Returns a copy of the source lines following this frame.
func post_context() -> PackedStringArray:
	return _post_context.duplicate()


## Returns a deep copy of the frame variables.
func variables() -> Dictionary:
	return _variables.duplicate(true)


## Internal capture support that returns a provider-safe bounded copy of stored variables.
## This method never returns or mutates the raw `_variables` container.
final func _bounded_sanitized_variables(
		max_container_depth: int,
		max_total_items: int,
) -> Dictionary:
	return _bounded_sanitized_variable_source(
			_variables,
			max_container_depth,
			max_total_items,
		)


## Internal capture support for sanitizing an arbitrary variable source without exposing it.
## Unsupported values, cycles, over-depth containers, and over-budget tails are omitted.
final func _bounded_sanitized_variable_source(
		source_variables: Dictionary,
		max_container_depth: int,
		max_total_items: int,
) -> Dictionary:
	return _walk_variables(
			source_variables,
			max_container_depth,
			max_total_items,
			SanitizedVariablePolicy.new(),
		)


final func _walk_variables(
		source_variables: Dictionary,
		max_container_depth: int,
		max_total_items: int,
		policy: ObservabilityValuePolicy,
) -> Dictionary:
	## The root container was not charged by the original frame-variable contract.
	var walked: ObservabilityRedactionResult[Variant] = (
			ObservabilityValueWalker.new(
				maxi(0, max_container_depth),
				maxi(0, max_total_items) + 1,
			).walk(source_variables, policy)
		)
	if not walked.valid() or not (walked.value() is Dictionary):
		return {}
	@warning_ignore("unsafe_cast")
	return walked.value() as Dictionary
