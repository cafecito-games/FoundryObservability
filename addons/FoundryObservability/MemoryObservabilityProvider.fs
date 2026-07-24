namespace foundry.observability

## Deterministic provider for tests and local integration work.
class_name MemoryObservabilityProvider
extends RefCounted
uses ObservabilityProvider

## Result returned by the next configure call.
var configure_result: int = Error.OK
## Result returned by flush calls.
var flush_result: int = Error.OK
## Timeout passed to the most recent flush call.
var last_flush_timeout_msec: int = 0
## Number of flush calls received by this provider.
var flush_count: int = 0
## Number of effective shutdown calls received by this provider.
var shutdown_count: int = 0

var _events: Array[ObservabilityEvent] = []
var _event_sequence: int = 0
var _enabled: bool = false
var _shutdown: bool = false


## Returns the memory provider identifier.
func provider_name() -> StringName:
	return &"memory"


## Always returns true because this provider is local and deterministic.
func is_available() -> bool:
	return true


## Applies the configured test result and enables capture when config.enabled is true.
func configure(config: ObservabilityConfig) -> int:
	if configure_result != Error.OK:
		return configure_result
	_enabled = config.enabled
	_shutdown = false
	return Error.OK


## Stores an event and returns a sequential memory event ID when enabled.
func capture(event: ObservabilityEvent) -> String:
	if not _enabled or _shutdown:
		return ""
	_events.append(event)
	_event_sequence += 1
	return "memory:%s" % _event_sequence


## Records the timeout and returns flush_result.
func flush(timeout_msec: int = 2000) -> int:
	last_flush_timeout_msec = timeout_msec
	flush_count += 1
	return flush_result


## Marks the provider shut down and increments shutdown_count once.
func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	shutdown_count += 1


## Returns a shallow copy of the captured event list.
func events() -> Array[ObservabilityEvent]:
	return _events.duplicate()


## Removes captured events without changing provider configuration.
func clear() -> void:
	_events.clear()
