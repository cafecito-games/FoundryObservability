namespace foundry.observability

## Immutable structured stack trace configuration.
final class_name ObservabilityStackTraceConfig extends RefCounted

final var _source_context_enabled: bool
final var _variables_enabled: bool


func _init(
		p_source_context_enabled: bool = true,
		p_variables_enabled: bool = false,
) -> void:
	_source_context_enabled = p_source_context_enabled
	_variables_enabled = p_variables_enabled


func source_context_enabled() -> bool:
	return _source_context_enabled


func variables_enabled() -> bool:
	return _variables_enabled
