namespace foundry.observability

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
	_variables = p_variables.duplicate(true)


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
	var budget: Dictionary = {"remaining": maxi(0, max_total_items)}
	return _bounded_sanitized_variable_dictionary(
			_variables,
			0,
			maxi(0, max_container_depth),
			budget,
	)


final func _bounded_sanitized_variable_dictionary(
		source_variables: Dictionary,
		container_depth: int,
		max_container_depth: int,
		budget: Dictionary,
) -> Dictionary:
	var sanitized: Dictionary = {}
	for key: Variant in source_variables:
		if not _consume_bounded_variable_item(budget):
			break
		if not (key is String) and not (key is StringName):
			continue
		var value: Variant = source_variables[key]
		if _is_supported_bounded_variable(value, container_depth + 1, max_container_depth):
			sanitized[str(key)] = _bounded_sanitized_variable(
					value,
					container_depth + 1,
					max_container_depth,
					budget,
			)
	return sanitized


final func _consume_bounded_variable_item(budget: Dictionary) -> bool:
	var remaining: int = budget["remaining"]
	if remaining <= 0:
		return false
	budget["remaining"] = remaining - 1
	return true


final func _is_supported_bounded_variable(
		value: Variant,
		container_depth: int,
		max_container_depth: int,
) -> bool:
	if value is bool or value is int or value is String or value is StringName:
		return true
	if value is float:
		return is_finite(value)
	return (value is Array or value is Dictionary) \
			and container_depth <= max_container_depth


final func _bounded_sanitized_variable(
		value: Variant,
		container_depth: int,
		max_container_depth: int,
		budget: Dictionary,
) -> Variant:
	if value is StringName:
		return str(value)
	if value is Array:
		return _bounded_sanitized_variable_array(
				value,
				container_depth,
				max_container_depth,
				budget,
		)
	if value is Dictionary:
		return _bounded_sanitized_variable_dictionary(
				value,
				container_depth,
				max_container_depth,
				budget,
		)
	return value


final func _bounded_sanitized_variable_array(
		values: Array,
		container_depth: int,
		max_container_depth: int,
		budget: Dictionary,
) -> Array:
	var sanitized: Array = []
	for value: Variant in values:
		if not _consume_bounded_variable_item(budget):
			break
		if _is_supported_bounded_variable(value, container_depth + 1, max_container_depth):
			sanitized.append(_bounded_sanitized_variable(
					value,
					container_depth + 1,
					max_container_depth,
					budget,
			))
	return sanitized
