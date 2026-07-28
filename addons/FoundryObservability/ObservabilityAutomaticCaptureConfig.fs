namespace foundry.observability

## Immutable engine logger routing, retention, and message-filter configuration.
final class_name ObservabilityAutomaticCaptureConfig extends RefCounted

final var _enabled: bool
final var _event_mask: int
final var _breadcrumb_mask: int
final var _log_mask: int
final var _max_breadcrumbs: int
final var _message_filter_prefixes: PackedStringArray


func _init(
		p_enabled: bool = true,
		p_event_mask: int = ObservabilityCaptureMask.DEFAULT_EVENTS,
		p_breadcrumb_mask: int = ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
		p_log_mask: int = ObservabilityCaptureMask.NONE,
		p_max_breadcrumbs: int = 100,
		p_message_filter_prefixes: PackedStringArray = PackedStringArray(
				["FoundryObservability: "],
		),
) -> void:
	_enabled = p_enabled
	_event_mask = p_event_mask
	_breadcrumb_mask = p_breadcrumb_mask
	_log_mask = p_log_mask
	_max_breadcrumbs = maxi(0, p_max_breadcrumbs)
	_message_filter_prefixes = p_message_filter_prefixes.duplicate()


func enabled() -> bool:
	return _enabled


func event_mask() -> int:
	return _event_mask


func breadcrumb_mask() -> int:
	return _breadcrumb_mask


func log_mask() -> int:
	return _log_mask


func max_breadcrumbs() -> int:
	return _max_breadcrumbs


func message_filter_prefixes() -> PackedStringArray:
	return _message_filter_prefixes.duplicate()
