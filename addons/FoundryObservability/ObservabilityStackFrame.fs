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
