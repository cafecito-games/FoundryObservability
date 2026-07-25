namespace foundry.observability.tests

## Mutable deterministic clock and frame supplier for automatic logger tests.
class_name AutomaticCaptureTime
extends RefCounted

var now_msec: int
var frame_index: int


func _init(p_now_msec: int, p_frame_index: int) -> void:
	now_msec = p_now_msec
	frame_index = p_frame_index


func now() -> int:
	return now_msec


func frame() -> int:
	return frame_index
