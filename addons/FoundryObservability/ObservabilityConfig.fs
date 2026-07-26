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
## Retains bounded source context around captured stack frames when true.
var stack_trace_source_context_enabled: bool = true
## Retains recursively normalized local stack-frame variables when true.
var stack_trace_variables_enabled: bool = false
## Enables native main-thread hang detection on supported Apple targets.
var application_hang_detection_enabled: bool = true
## Defines the Apple main-thread hang threshold in milliseconds.
var application_hang_timeout_msec: int = 5000
## Enables native Application Not Responding detection on Android.
var android_anr_detection_enabled: bool = true
## Defines the Android watchdog threshold in milliseconds where supported.
var android_anr_timeout_msec: int = 5000
## Attaches an Android operating-system ANR thread dump when available.
var android_anr_attach_thread_dump: bool = false
## Maximum retained breadcrumbs; zero disables breadcrumb storage.
var max_breadcrumbs: int = 100
## Maximum accepted attachment payload size; zero disables byte-size acceptance.
var max_attachment_bytes: int = 20 * 1024 * 1024
## Attaches the current game log to provider captures when supported.
var attach_game_log: bool = false
## Attaches a screenshot to provider captures when supported.
var attach_screenshot: bool = false
## Attaches the current scene tree to provider captures when supported.
var attach_scene_tree: bool = false
## Deterministic fraction of otherwise accepted events to retain.
var event_sample_rate: float = 1.0
## Deterministic fraction of otherwise accepted logs to retain.
var log_sample_rate: float = 1.0
var _global_attributes: Dictionary = {}
var _provider_options: Dictionary = {}
var _automatic_message_filter_prefixes: PackedStringArray = PackedStringArray()
var _event_processors: Array[Callable] = []
var _log_processors: Array[Callable] = []
var _metric_processors: Array[Callable] = []
var _event_limits: ObservabilitySignalLimits
var _has_explicit_event_limits: bool = false
var _log_limits: ObservabilitySignalLimits
var _metric_limits: ObservabilitySignalLimits
var _redaction_policy: ObservabilityRedactionPolicy


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
		p_stack_trace_source_context_enabled: bool = true,
		p_stack_trace_variables_enabled: bool = false,
		p_automatic_message_filter_prefixes: PackedStringArray = PackedStringArray(
				["FoundryObservability: "]),
		p_application_hang_detection_enabled: bool = true,
		p_application_hang_timeout_msec: int = 5000,
		p_android_anr_detection_enabled: bool = true,
		p_android_anr_timeout_msec: int = 5000,
		p_android_anr_attach_thread_dump: bool = false,
		p_max_breadcrumbs: int = 100,
		p_max_attachment_bytes: int = 20 * 1024 * 1024,
		p_attach_game_log: bool = false,
		p_attach_screenshot: bool = false,
		p_attach_scene_tree: bool = false,
		p_event_sample_rate: float = 1.0,
		p_log_sample_rate: float = 1.0,
		p_event_processors: Array[Callable] = [],
		p_log_processors: Array[Callable] = [],
		p_metric_processors: Array[Callable] = [],
		p_event_limits: ObservabilitySignalLimits? = null,
		p_log_limits: ObservabilitySignalLimits? = null,
		p_metric_limits: ObservabilitySignalLimits? = null,
		p_redaction_policy: ObservabilityRedactionPolicy? = null,
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
	stack_trace_source_context_enabled = p_stack_trace_source_context_enabled
	stack_trace_variables_enabled = p_stack_trace_variables_enabled
	application_hang_detection_enabled = p_application_hang_detection_enabled
	application_hang_timeout_msec = maxi(1000, p_application_hang_timeout_msec)
	android_anr_detection_enabled = p_android_anr_detection_enabled
	android_anr_timeout_msec = maxi(1000, p_android_anr_timeout_msec)
	android_anr_attach_thread_dump = p_android_anr_attach_thread_dump
	max_breadcrumbs = maxi(0, p_max_breadcrumbs)
	max_attachment_bytes = maxi(0, p_max_attachment_bytes)
	attach_game_log = p_attach_game_log
	attach_screenshot = p_attach_screenshot
	attach_scene_tree = p_attach_scene_tree
	event_sample_rate = p_event_sample_rate
	log_sample_rate = p_log_sample_rate
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)
	_automatic_message_filter_prefixes = p_automatic_message_filter_prefixes.duplicate()
	_event_processors = _copy_processors(p_event_processors)
	_log_processors = _copy_processors(p_log_processors)
	_metric_processors = _copy_processors(p_metric_processors)
	_has_explicit_event_limits = p_event_limits != null
	_event_limits = ObservabilitySignalLimits.new(
			automatic_events_per_frame,
			automatic_repeated_error_window_msec,
			automatic_event_throttle_count,
			automatic_event_throttle_window_msec,
	) if p_event_limits == null else p_event_limits.duplicate()
	_log_limits = ObservabilitySignalLimits.new() \
			if p_log_limits == null else p_log_limits.duplicate()
	_metric_limits = ObservabilitySignalLimits.new() \
			if p_metric_limits == null else p_metric_limits.duplicate()
	_redaction_policy = ObservabilityRedactionPolicy.new() \
			if p_redaction_policy == null else p_redaction_policy.duplicate()


## Returns a deep copy of attributes applied by provider integrations.
func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


## Returns a deep copy of backend-specific options not interpreted by the core.
func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)


## Returns copied prefixes excluded from automatic output-message capture.
func automatic_message_filter_prefixes() -> PackedStringArray:
	return _automatic_message_filter_prefixes.duplicate()


## Returns copied event processors for later atomic configuration validation.
func event_processors() -> Array[Callable]:
	return _copy_processors(_event_processors)


## Returns copied log processors for later atomic configuration validation.
func log_processors() -> Array[Callable]:
	return _copy_processors(_log_processors)


## Returns copied metric processors for later atomic configuration validation.
func metric_processors() -> Array[Callable]:
	return _copy_processors(_metric_processors)


## Returns a copy of the effective event limits.
func event_limits() -> ObservabilitySignalLimits:
	if not _has_explicit_event_limits:
		return ObservabilitySignalLimits.new(
				automatic_events_per_frame,
				automatic_repeated_error_window_msec,
				automatic_event_throttle_count,
				automatic_event_throttle_window_msec,
		)
	return _event_limits.duplicate()


## Returns copied log limits, with all limits disabled by default.
func log_limits() -> ObservabilitySignalLimits:
	return _log_limits.duplicate()


## Returns copied metric limits, with all limits disabled by default.
func metric_limits() -> ObservabilitySignalLimits:
	return _metric_limits.duplicate()


## Returns a copy of the ordered redaction policy.
func redaction_policy() -> ObservabilityRedactionPolicy:
	return _redaction_policy.duplicate()


func _copy_processors(source: Array[Callable]) -> Array[Callable]:
	var copied: Array[Callable] = []
	for processor: Callable in source:
		copied.append(processor)
	return copied
