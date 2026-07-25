namespace foundry.observability

## Provider-neutral configuration shared by all integrations.
class_name ObservabilityConfig
extends RefCounted

## Enables provider capture after configuration when true.
var enabled: bool = true
## Identifies the deployment environment, such as production or staging.
var environment: String = ""
## Identifies the game release associated with captured events.
var release: String = ""
## Identifies an optional distribution variant of the release.
var dist: String = ""
## Enables structured log capture independently from messages and exceptions.
var logs_enabled: bool = true
## Filters structured logs below this normalized severity.
var log_minimum_level: int = ObservabilityLevel.TRACE
## Limits accepted logs per one-second monotonic engine-tick window; zero delegates to the provider.
var log_rate_limit_per_second: int = 0
## Enables custom metric capture independently from events and logs.
var metrics_enabled: bool = true
## Deterministic fraction of otherwise accepted metrics to retain.
var metric_sample_rate: float = 1.0
## Optional predicate receiving each normalized metric before sampling.
var metric_filter: Callable = Callable()
## Enables automatic engine error and message capture after configuration.
var automatic_capture_enabled: bool = true
## Selects engine error categories captured as events.
var automatic_event_mask: int = ObservabilityCaptureMask.DEFAULT_EVENTS
## Selects engine error and message categories captured as breadcrumbs.
var automatic_breadcrumb_mask: int = ObservabilityCaptureMask.DEFAULT_BREADCRUMBS
## Selects engine error and message categories captured as structured logs.
var automatic_log_mask: int = ObservabilityCaptureMask.NONE
## Limits automatic error events accepted during one processed frame; zero disables the limit.
var automatic_events_per_frame: int = 5
## Suppresses identical automatic errors inside this window; zero disables suppression.
var automatic_repeated_error_window_msec: int = 1000
## Limits automatic error events inside the sliding window; zero disables the limit.
var automatic_event_throttle_count: int = 20
## Defines the automatic event sliding window; zero disables the limit.
var automatic_event_throttle_window_msec: int = 10000
var _global_attributes: Dictionary = {}
var _provider_options: Dictionary = {}
var _automatic_message_filter_prefixes: PackedStringArray = PackedStringArray()


## Creates configuration with enabled metadata, copied attributes, and opaque provider options.
func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0,
		p_metrics_enabled: bool = true,
		p_metric_sample_rate: float = 1.0,
		p_metric_filter: Callable = Callable(),
		p_automatic_capture_enabled: bool = true,
		p_automatic_event_mask: int = ObservabilityCaptureMask.DEFAULT_EVENTS,
		p_automatic_breadcrumb_mask: int = ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
		p_automatic_log_mask: int = ObservabilityCaptureMask.NONE,
		p_automatic_events_per_frame: int = 5,
		p_automatic_repeated_error_window_msec: int = 1000,
		p_automatic_event_throttle_count: int = 20,
		p_automatic_event_throttle_window_msec: int = 10000,
		p_automatic_message_filter_prefixes: PackedStringArray = PackedStringArray(
				["FoundryObservability: "]),
) -> void:
	enabled = p_enabled
	environment = p_environment
	release = p_release
	dist = p_dist
	logs_enabled = p_logs_enabled
	log_minimum_level = p_log_minimum_level
	log_rate_limit_per_second = maxi(0, p_log_rate_limit_per_second)
	metrics_enabled = p_metrics_enabled
	metric_sample_rate = p_metric_sample_rate
	metric_filter = p_metric_filter
	automatic_capture_enabled = p_automatic_capture_enabled
	automatic_event_mask = p_automatic_event_mask
	automatic_breadcrumb_mask = p_automatic_breadcrumb_mask
	automatic_log_mask = p_automatic_log_mask
	automatic_events_per_frame = maxi(0, p_automatic_events_per_frame)
	automatic_repeated_error_window_msec = maxi(0, p_automatic_repeated_error_window_msec)
	automatic_event_throttle_count = maxi(0, p_automatic_event_throttle_count)
	automatic_event_throttle_window_msec = maxi(0, p_automatic_event_throttle_window_msec)
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)
	_automatic_message_filter_prefixes = p_automatic_message_filter_prefixes.duplicate()


## Returns a deep copy of attributes applied by provider integrations.
func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


## Returns a deep copy of backend-specific options not interpreted by the core.
func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)


## Returns copied prefixes excluded from automatic output-message capture.
func automatic_message_filter_prefixes() -> PackedStringArray:
	return _automatic_message_filter_prefixes.duplicate()
