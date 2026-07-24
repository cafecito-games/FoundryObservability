namespace foundry.observability.foundrylib

import foundry.logging
import foundry.observability

## Forwards filtered FoundryLib records into the core observability API.
class_name FoundryLibObservabilitySink
extends RefCounted
uses LogSink

var _service: FoundryObservabilityApi
var _minimum_level: int


## Creates a sink targeting service and filtering records below p_minimum_level.
func _init(p_service: FoundryObservabilityApi, p_minimum_level: int = ObservabilityLevel.ERROR) -> void:
	_service = p_service
	_minimum_level = p_minimum_level


## Renders, enriches, maps, and forwards one eligible log record without recursive failure logging.
func emit(record: LogRecord) -> void:
	if record == null or record.level < _minimum_level or _service == null:
		return

	var attributes: Dictionary = record.fields.duplicate(true)
	attributes["logger_name"] = record.logger_name
	var event_level: int = _map_level(record.level)
	_service.capture_log(
			LogFormatter.render_message(record),
			event_level,
			&"foundry.logging",
			record.timestamp_msec,
			attributes,
		)


## Forwards a default service flush request when a target service is available.
func flush() -> void:
	if _service == null:
		return
	var _result: int = _service.flush()


func _map_level(level: int) -> int:
	match level:
		LogLevel.TRACE:
			return ObservabilityLevel.TRACE
		LogLevel.DEBUG:
			return ObservabilityLevel.DEBUG
		LogLevel.INFO:
			return ObservabilityLevel.INFO
		LogLevel.WARN:
			return ObservabilityLevel.WARN
		LogLevel.ERROR:
			return ObservabilityLevel.ERROR
		LogLevel.FATAL:
			return ObservabilityLevel.FATAL
	return ObservabilityLevel.INFO
