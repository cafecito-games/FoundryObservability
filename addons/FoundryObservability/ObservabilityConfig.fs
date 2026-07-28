namespace foundry.observability

## Immutable aggregate of deployment metadata and focused policy objects.
## Null focused arguments are replaced with their immutable default instances.
final class_name ObservabilityConfig extends RefCounted

final var _enabled: bool
final var _environment: String
final var _release: String
final var _dist: String
final var _global_attributes: Dictionary
final var _provider_options: Dictionary
final var _processing: ObservabilityProcessingConfig
final var _automatic_capture: ObservabilityAutomaticCaptureConfig
final var _attachments: ObservabilityAttachmentConfig
final var _stack_traces: ObservabilityStackTraceConfig
final var _mobile_diagnostics: ObservabilityMobileDiagnosticsConfig


func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_processing: ObservabilityProcessingConfig? = null,
		p_automatic_capture: ObservabilityAutomaticCaptureConfig? = null,
		p_attachments: ObservabilityAttachmentConfig? = null,
		p_stack_traces: ObservabilityStackTraceConfig? = null,
		p_mobile_diagnostics: ObservabilityMobileDiagnosticsConfig? = null,
) -> void:
	_enabled = p_enabled
	_environment = p_environment
	_release = p_release
	_dist = p_dist
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)
	_processing = p_processing if p_processing != null else ObservabilityProcessingConfig.new()
	_automatic_capture = p_automatic_capture if p_automatic_capture != null else ObservabilityAutomaticCaptureConfig.new()
	_attachments = p_attachments if p_attachments != null else ObservabilityAttachmentConfig.new()
	_stack_traces = p_stack_traces if p_stack_traces != null else ObservabilityStackTraceConfig.new()
	_mobile_diagnostics = (
			p_mobile_diagnostics
			if p_mobile_diagnostics != null
			else ObservabilityMobileDiagnosticsConfig.new()
		)


func enabled() -> bool:
	return _enabled


func environment() -> String:
	return _environment


func release() -> String:
	return _release


func dist() -> String:
	return _dist


func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)


func processing() -> ObservabilityProcessingConfig:
	return _processing


func automatic_capture() -> ObservabilityAutomaticCaptureConfig:
	return _automatic_capture


func attachments() -> ObservabilityAttachmentConfig:
	return _attachments


func stack_traces() -> ObservabilityStackTraceConfig:
	return _stack_traces


func mobile_diagnostics() -> ObservabilityMobileDiagnosticsConfig:
	return _mobile_diagnostics
