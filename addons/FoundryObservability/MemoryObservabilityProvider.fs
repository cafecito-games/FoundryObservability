namespace games.cafecito.foundryobservability

## Deterministic provider for tests and local integration work.
class_name MemoryObservabilityProvider
extends RefCounted
uses ObservabilityProvider

var configure_result: int = Error.OK
var flush_result: int = Error.OK
var last_flush_timeout_msec: int = 0
var flush_count: int = 0
var shutdown_count: int = 0

var _events: Array[ObservabilityEvent] = []
var _event_sequence: int = 0
var _enabled: bool = false
var _shutdown: bool = false


func provider_name() -> StringName:
	return &"memory"


func is_available() -> bool:
	return true


func configure(config: ObservabilityConfig) -> int:
	if configure_result != Error.OK:
		return configure_result
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


func capture(event: ObservabilityEvent) -> String:
	if not _enabled or _shutdown:
		return ""
	_events.append(event)
	_event_sequence += 1
	return "memory:%s" % _event_sequence


func flush(timeout_msec: int = 2000) -> int:
	last_flush_timeout_msec = timeout_msec
	flush_count += 1
	return flush_result


func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	shutdown_count += 1


func events() -> Array[ObservabilityEvent]:
	return _events.duplicate()


func clear() -> void:
	_events.clear()
