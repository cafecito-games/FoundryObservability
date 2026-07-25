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

## Maximum nested Array and Dictionary depth retained from frame variables.
const MAX_VARIABLE_CONTAINER_DEPTH: int = 8
## Maximum total Dictionary entries and Array elements examined per frame.
const MAX_VARIABLE_ITEMS: int = 256


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
	_variables = _bounded_owned_variables(p_variables)


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
	var visited_containers: Array = []
	if not _visit_variable_container(_variables, visited_containers):
		return {}
	return _bounded_sanitized_variable_dictionary(
			_variables,
			0,
			maxi(0, max_container_depth),
			budget,
			visited_containers,
	)


final func _bounded_owned_variables(source_variables: Dictionary) -> Dictionary:
	var budget: Dictionary = {"remaining": MAX_VARIABLE_ITEMS}
	var visited_containers: Array = []
	if not _visit_variable_container(source_variables, visited_containers):
		return {}
	return _bounded_owned_variable_dictionary(
			source_variables,
			0,
			budget,
			visited_containers,
	)


final func _bounded_owned_variable_dictionary(
		source_variables: Dictionary,
		container_depth: int,
		budget: Dictionary,
		visited_containers: Array,
) -> Dictionary:
	var owned: Dictionary = {}
	for key: Variant in source_variables:
		if not _consume_bounded_variable_item(budget):
			break
		if key is Array or key is Dictionary:
			continue
		var value: Variant = source_variables[key]
		if value is Array:
			if container_depth + 1 <= MAX_VARIABLE_CONTAINER_DEPTH \
					and _visit_variable_container(value, visited_containers):
				owned[key] = _bounded_owned_variable_array(
						value,
						container_depth + 1,
						budget,
						visited_containers,
				)
		elif value is Dictionary:
			if container_depth + 1 <= MAX_VARIABLE_CONTAINER_DEPTH \
					and _visit_variable_container(value, visited_containers):
				owned[key] = _bounded_owned_variable_dictionary(
						value,
						container_depth + 1,
						budget,
						visited_containers,
				)
		else:
			owned[key] = value
	return owned


final func _bounded_owned_variable_array(
		source_values: Array,
		container_depth: int,
		budget: Dictionary,
		visited_containers: Array,
) -> Array:
	var owned: Array = []
	for value: Variant in source_values:
		if not _consume_bounded_variable_item(budget):
			break
		if value is Array:
			if container_depth + 1 <= MAX_VARIABLE_CONTAINER_DEPTH \
					and _visit_variable_container(value, visited_containers):
				owned.append(_bounded_owned_variable_array(
						value,
						container_depth + 1,
						budget,
						visited_containers,
				))
		elif value is Dictionary:
			if container_depth + 1 <= MAX_VARIABLE_CONTAINER_DEPTH \
					and _visit_variable_container(value, visited_containers):
				owned.append(_bounded_owned_variable_dictionary(
						value,
						container_depth + 1,
						budget,
						visited_containers,
				))
		else:
			owned.append(value)
	return owned


final func _bounded_sanitized_variable_dictionary(
		source_variables: Dictionary,
		container_depth: int,
		max_container_depth: int,
		budget: Dictionary,
		visited_containers: Array,
) -> Dictionary:
	var sanitized: Dictionary = {}
	for key: Variant in source_variables:
		if not _consume_bounded_variable_item(budget):
			break
		if not (key is String) and not (key is StringName):
			continue
		var value: Variant = source_variables[key]
		if value is Array:
			if container_depth + 1 <= max_container_depth \
					and _visit_variable_container(value, visited_containers):
				sanitized[str(key)] = _bounded_sanitized_variable_array(
						value,
						container_depth + 1,
						max_container_depth,
						budget,
						visited_containers,
				)
		elif value is Dictionary:
			if container_depth + 1 <= max_container_depth \
					and _visit_variable_container(value, visited_containers):
				sanitized[str(key)] = _bounded_sanitized_variable_dictionary(
						value,
						container_depth + 1,
						max_container_depth,
						budget,
						visited_containers,
				)
		elif _is_supported_bounded_variable_scalar(value):
			sanitized[str(key)] = _bounded_sanitized_variable_scalar(value)
	return sanitized


final func _consume_bounded_variable_item(budget: Dictionary) -> bool:
	var remaining: int = budget["remaining"]
	if remaining <= 0:
		return false
	budget["remaining"] = remaining - 1
	return true


final func _is_supported_bounded_variable_scalar(value: Variant) -> bool:
	if value is bool or value is int or value is String or value is StringName:
		return true
	if value is float:
		return is_finite(value)
	return false


final func _bounded_sanitized_variable_scalar(value: Variant) -> Variant:
	if value is StringName:
		return str(value)
	return value


final func _bounded_sanitized_variable_array(
		source_values: Array,
		container_depth: int,
		max_container_depth: int,
		budget: Dictionary,
		visited_containers: Array,
) -> Array:
	var sanitized: Array = []
	for value: Variant in source_values:
		if not _consume_bounded_variable_item(budget):
			break
		if value is Array:
			if container_depth + 1 <= max_container_depth \
					and _visit_variable_container(value, visited_containers):
				sanitized.append(_bounded_sanitized_variable_array(
						value,
						container_depth + 1,
						max_container_depth,
						budget,
						visited_containers,
				))
		elif value is Dictionary:
			if container_depth + 1 <= max_container_depth \
					and _visit_variable_container(value, visited_containers):
				sanitized.append(_bounded_sanitized_variable_dictionary(
						value,
						container_depth + 1,
						max_container_depth,
						budget,
						visited_containers,
				))
		elif _is_supported_bounded_variable_scalar(value):
			sanitized.append(_bounded_sanitized_variable_scalar(value))
	return sanitized


final func _visit_variable_container(container: Variant, visited_containers: Array) -> bool:
	for visited_container: Variant in visited_containers:
		if is_same(container, visited_container):
			return false
	visited_containers.append(container)
	return true
