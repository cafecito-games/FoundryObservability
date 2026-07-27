namespace foundry.observability.tests

import foundry.observability.runtime

## Mutable deterministic runtime for core tests.
class_name FakeObservabilityRuntime extends RefCounted
uses ObservabilityRuntime

var monotonic_msec: int
var unix_msec: int
var frame: int
var caller: int
var main_thread: int
var monotonic_call_count: int = 0
var unix_call_count: int = 0


func _init(
		p_monotonic_msec: int = 0,
		p_unix_msec: int = 0,
		p_frame: int = 0,
		p_caller: int = 0,
		p_main_thread: int = 0,
) -> void:
	monotonic_msec = p_monotonic_msec
	unix_msec = p_unix_msec
	frame = p_frame
	caller = p_caller
	main_thread = p_main_thread


func monotonic_time_msec() -> int:
	monotonic_call_count += 1
	return monotonic_msec


func unix_time_msec() -> int:
	unix_call_count += 1
	return unix_msec


func process_frame() -> int:
	return frame


func caller_id() -> int:
	return caller


func main_thread_id() -> int:
	return main_thread
