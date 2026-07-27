namespace foundry.observability.runtime

## Engine-backed production observability runtime.
final class_name SystemObservabilityRuntime extends RefCounted
uses ObservabilityRuntime


func monotonic_time_msec() -> int:
	return Time.get_ticks_msec()


func unix_time_msec() -> int:
	return floori(Time.get_unix_time_from_system() * 1000.0)


func process_frame() -> int:
	return Engine.get_process_frames()


func caller_id() -> int:
	return OS.get_thread_caller_id()


func main_thread_id() -> int:
	return OS.get_main_thread_id()
