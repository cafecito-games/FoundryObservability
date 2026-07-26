namespace foundry.observability

## Provider-neutral event payload.
class_name ObservabilityEvent
extends RefCounted

## Marks a wall-clock timestamp that the service must resolve at capture time.
const UNASSIGNED_TIMESTAMP: int = -1

final var _kind: StringName
final var _level: int
final var _message: String
final var _source: StringName
final var _timestamp_msec: int
final var _attributes: Dictionary
final var _exception: ObservabilityException?
final var _engine_ticks_msec: int
final var _scope: ObservabilityScope?


## Creates an event with kind, severity, source, timestamp, copied attributes, and optional exception data.
func _init(
		p_kind: StringName = &"message",
		p_level: int = ObservabilityLevel.INFO,
		p_message: String = "",
		p_source: StringName = &"",
		p_timestamp_msec: int = UNASSIGNED_TIMESTAMP,
		p_attributes: Dictionary = {},
		p_exception: ObservabilityException? = null,
		p_engine_ticks_msec: int = -1,
		p_scope: ObservabilityScope? = null,
) -> void:
	_kind = p_kind
	_level = p_level
	_message = p_message
	_source = p_source
	_timestamp_msec = p_timestamp_msec
	_attributes = p_attributes.duplicate(true)
	_exception = p_exception
	_engine_ticks_msec = p_engine_ticks_msec
	_scope = null if p_scope == null else p_scope.duplicate()


## Returns the event kind, normally message, exception, or log.
func kind() -> StringName:
	return _kind


## Returns the event severity value.
func level() -> int:
	return _level


## Returns the human-readable event message.
func message() -> String:
	return _message


## Returns the subsystem or producer that created the event.
func source() -> StringName:
	return _source


## Returns the wall-clock occurrence time in Unix epoch milliseconds, or -1 when unspecified.
func timestamp_msec() -> int:
	return _timestamp_msec


## Returns the original monotonic engine tick in milliseconds, or -1 when unavailable.
func engine_ticks_msec() -> int:
	return _engine_ticks_msec


## Returns a deep copy of structured event attributes.
func attributes() -> Dictionary:
	return _attributes.duplicate(true)


## Returns the optional exception payload associated with the event.
func exception() -> ObservabilityException?:
	return _exception


## Returns an isolated copy of the optional event-local scope.
func scope() -> ObservabilityScope?:
	return null if _scope == null else _scope.duplicate()
