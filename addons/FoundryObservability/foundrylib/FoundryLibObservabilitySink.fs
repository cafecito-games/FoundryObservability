namespace games.cafecito.foundryobservability.foundrylib

import foundry.logging
import games.cafecito.foundryobservability

## Forwards selected FoundryLib records into the core observability API.
class_name FoundryLibObservabilitySink
extends RefCounted
uses LogSink

var _service: FoundryObservabilityApi
var _minimum_level: int


func _init(p_service: FoundryObservabilityApi, p_minimum_level: int = ObservabilityLevel.ERROR) -> void:
	_service = p_service
	_minimum_level = p_minimum_level


func emit(record: LogRecord) -> void:
	if record == null or record.level < _minimum_level or _service == null:
		return

	var attributes: Dictionary = record.fields.duplicate(true)
	attributes["logger_name"] = record.logger_name
	var event_level: int = _map_level(record.level)
	var event: ObservabilityEvent = ObservabilityEvent.new(
			&"log",
			event_level,
			LogFormatter.render_message(record),
			&"foundry.logging",
			record.timestamp_msec,
			attributes,
		)
	_service.capture_event(event)


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
