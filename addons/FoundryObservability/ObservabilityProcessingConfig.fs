namespace foundry.observability

import foundry.observability.processing

## Immutable provider-neutral processing configuration.
final class_name ObservabilityProcessingConfig extends RefCounted

final var _logs_enabled: bool
final var _log_minimum_level: int
final var _log_rate_limit_per_second: int
final var _metrics_enabled: bool
final var _event_sample_rate: float
final var _log_sample_rate: float
final var _metric_sample_rate: float
final var _has_metric_filter: bool
final var _metric_filter: Callable[[ObservabilityMetric], bool]?
final var _event_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]
final var _log_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]
final var _metric_processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]]
final var _event_limits: ObservabilitySignalLimits
final var _log_limits: ObservabilitySignalLimits
final var _metric_limits: ObservabilitySignalLimits
final var _redaction_policy: ObservabilityRedactionPolicy


func _init(
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0,
		p_metrics_enabled: bool = true,
		p_event_sample_rate: float = 1.0,
		p_log_sample_rate: float = 1.0,
		p_metric_sample_rate: float = 1.0,
		p_metric_filter: Callable[[ObservabilityMetric], bool]? = null,
		p_event_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = [],
		p_log_processors: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = [],
		p_metric_processors: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = [],
		p_event_limits: ObservabilitySignalLimits? = null,
		p_log_limits: ObservabilitySignalLimits? = null,
		p_metric_limits: ObservabilitySignalLimits? = null,
		p_redaction_policy: ObservabilityRedactionPolicy? = null,
) -> void:
	_logs_enabled = p_logs_enabled
	_log_minimum_level = p_log_minimum_level
	_log_rate_limit_per_second = maxi(0, p_log_rate_limit_per_second)
	_metrics_enabled = p_metrics_enabled
	_event_sample_rate = p_event_sample_rate
	_log_sample_rate = p_log_sample_rate
	_metric_sample_rate = p_metric_sample_rate
	## A null nullable Callable arrives as an invalid Callable value at this boundary.
	## Capture presence while a live target is still distinguishable; the bit remains
	## true if that target expires later so configure() can reject transactionally.
	_has_metric_filter = p_metric_filter.is_valid()
	_metric_filter = p_metric_filter
	_event_processors = _copy_event_processors(p_event_processors)
	_log_processors = _copy_event_processors(p_log_processors)
	_metric_processors = _copy_metric_processors(p_metric_processors)
	_event_limits = ObservabilitySignalLimits.new(5, 1000, 20, 10000) \
			if p_event_limits == null else p_event_limits.duplicate()
	_log_limits = ObservabilitySignalLimits.new() \
			if p_log_limits == null else p_log_limits.duplicate()
	_metric_limits = ObservabilitySignalLimits.new() \
			if p_metric_limits == null else p_metric_limits.duplicate()
	_redaction_policy = ObservabilityRedactionPolicy.new() \
			if p_redaction_policy == null else p_redaction_policy.duplicate()


func logs_enabled() -> bool:
	return _logs_enabled


func log_minimum_level() -> int:
	return _log_minimum_level


func log_rate_limit_per_second() -> int:
	return _log_rate_limit_per_second


func metrics_enabled() -> bool:
	return _metrics_enabled


func event_sample_rate() -> float:
	return _event_sample_rate


func log_sample_rate() -> float:
	return _log_sample_rate


func metric_sample_rate() -> float:
	return _metric_sample_rate


## Avoids crossing a nullable Callable return boundary when no filter is configured.
func has_metric_filter() -> bool:
	return _has_metric_filter


func metric_filter() -> Callable[[ObservabilityMetric], bool]?:
	return _metric_filter


func event_processors() -> Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]:
	return _copy_event_processors(_event_processors)


func log_processors() -> Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]:
	return _copy_event_processors(_log_processors)


func metric_processors() -> Array[Callable[[ObservabilityMetric], ObservabilityMetric?]]:
	return _copy_metric_processors(_metric_processors)


func event_limits() -> ObservabilitySignalLimits:
	return _event_limits.duplicate()


func log_limits() -> ObservabilitySignalLimits:
	return _log_limits.duplicate()


func metric_limits() -> ObservabilitySignalLimits:
	return _metric_limits.duplicate()


func redaction_policy() -> ObservabilityRedactionPolicy:
	return _redaction_policy.duplicate()


func _copy_event_processors(
		source: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]],
) -> Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]:
	var copied: Array[Callable[[ObservabilityEvent], ObservabilityEvent?]] = []
	for processor: Callable[[ObservabilityEvent], ObservabilityEvent?] in source:
		copied.append(processor)
	return copied


func _copy_metric_processors(
		source: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]],
) -> Array[Callable[[ObservabilityMetric], ObservabilityMetric?]]:
	var copied: Array[Callable[[ObservabilityMetric], ObservabilityMetric?]] = []
	for processor: Callable[[ObservabilityMetric], ObservabilityMetric?] in source:
		copied.append(processor)
	return copied
