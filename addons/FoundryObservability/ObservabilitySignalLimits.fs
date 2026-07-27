namespace foundry.observability

## Immutable limits independently applied to one observability signal.
class_name ObservabilitySignalLimits
extends RefCounted

final var _per_frame: int
final var _repeated_window_msec: int
final var _window_count: int
final var _window_msec: int


func _init(
		p_per_frame: int = 0,
		p_repeated_window_msec: int = 0,
		p_window_count: int = 0,
		p_window_msec: int = 0,
) -> void:
	_per_frame = maxi(0, p_per_frame)
	_repeated_window_msec = maxi(0, p_repeated_window_msec)
	_window_count = maxi(0, p_window_count)
	_window_msec = maxi(0, p_window_msec)


func per_frame() -> int:
	return _per_frame


func repeated_window_msec() -> int:
	return _repeated_window_msec


func window_count() -> int:
	return _window_count


func window_msec() -> int:
	return _window_msec


func duplicate() -> ObservabilitySignalLimits:
	return ObservabilitySignalLimits.new(
			_per_frame,
			_repeated_window_msec,
			_window_count,
			_window_msec,
	)
