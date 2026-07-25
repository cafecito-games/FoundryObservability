namespace foundry.observability

## Converts engine logger callbacks into provider-neutral observability records.
class_name AutomaticObservabilityLogger
extends Logger

const _ORIGIN: String = "auto.log.foundry"

var _service: FoundryObservability
var _config: ObservabilityConfig
var _clock: Callable
var _frame: Callable
var _state_mutex: Mutex = Mutex.new()
var _error_timepoints: Dictionary = {}
var _event_timepoints: Array[int] = []
var _current_frame: int = -1
var _frame_event_count: int = 0


## Creates a logger with optional deterministic clock and frame suppliers.
func _init(
		service: FoundryObservability,
		config: ObservabilityConfig,
		clock: Callable = Callable(),
		frame: Callable = Callable(),
) -> void:
	_service = service
	_config = config
	if clock.is_valid():
		_clock = clock
	else:
		_clock = func() -> int: return Time.get_ticks_msec()
	if frame.is_valid():
		_frame = frame
	else:
		_frame = func() -> int: return Engine.get_process_frames()


## Receives structured engine diagnostics.
func _log_error(
		function_name: String,
		file: String,
		line: int,
		code: String,
		rationale: String,
		editor_notify: bool,
		error_type: int,
		script_backtraces: Array[ScriptBacktrace],
) -> void:
	if _service == null or not _service.try_begin_automatic_capture():
		return
	_capture_error(
			function_name,
			file,
			line,
			code,
			rationale,
			editor_notify,
			error_type,
			script_backtraces,
		)
	_service.end_automatic_capture()


func _capture_error(
		function_name: String,
		file: String,
		line: int,
		code: String,
		rationale: String,
		editor_notify: bool,
		error_type: int,
		script_backtraces: Array[ScriptBacktrace],
) -> void:

	var category_mask: int = _category_mask(error_type)
	var level: int = _error_level(error_type)
	var type_name: String = _error_type_name(error_type)
	var message: String = rationale if not rationale.is_empty() else code
	var engine_ticks_msec: int = _now_msec()
	var frame_index: int = _frame_index()
	var as_event: bool = (_config.automatic_event_mask & category_mask) != 0
	var as_breadcrumb: bool = (_config.automatic_breadcrumb_mask & category_mask) != 0
	var as_log: bool = (_config.automatic_log_mask & category_mask) != 0
	if not as_event and not as_breadcrumb and not as_log:
		return

	var error_key: String = JSON.stringify([message, file, line, error_type])
	_state_mutex.lock()
	if _error_timepoints.size() > 100:
		_error_timepoints.clear()
	var previous_timestamp: Variant = _error_timepoints.get(error_key, null)
	if _config.automatic_repeated_error_window_msec > 0 \
			and previous_timestamp is int:
		var previous_engine_ticks_msec: int = previous_timestamp
		if engine_ticks_msec - previous_engine_ticks_msec \
				< _config.automatic_repeated_error_window_msec:
			_state_mutex.unlock()
			return

	if frame_index != _current_frame:
		_current_frame = frame_index
		_frame_event_count = 0
	_prune_event_timepoints(engine_ticks_msec)
	if as_event and _config.automatic_events_per_frame > 0 \
			and _frame_event_count >= _config.automatic_events_per_frame:
		as_event = false
	if as_event and _config.automatic_event_throttle_count > 0 \
			and _config.automatic_event_throttle_window_msec > 0 \
			and _event_timepoints.size() >= _config.automatic_event_throttle_count:
		as_event = false
	_state_mutex.unlock()

	var backtrace_payload: Dictionary = _serialize_backtraces(script_backtraces)
	var attributes: Dictionary = {
		"error.function": function_name,
		"error.file": file,
		"error.line": line,
		"error.code": code,
		"error.rationale": rationale,
		"error.type": type_name,
		"error.editor_notify": editor_notify,
		"error.script_backtraces": backtrace_payload["backtraces"],
		"observability.origin": _ORIGIN,
	}

	var event_accepted: bool = false
	var breadcrumb_accepted: bool = false
	var log_accepted: bool = false
	if as_event:
		event_accepted = not _service.capture_event(ObservabilityEvent.new(
				p_kind = &"exception",
				p_level = level,
				p_message = message,
				p_source = &"foundry.engine",
				p_attributes = attributes,
				p_exception = ObservabilityException.new(
						p_type_name = type_name,
						p_message = message,
						p_stack_trace = str(backtrace_payload["stack_trace"]),
						p_attributes = attributes,
					),
				p_engine_ticks_msec = engine_ticks_msec,
			)).is_empty()
	if as_breadcrumb:
		breadcrumb_accepted = _service._capture_automatic_breadcrumb(
				ObservabilityBreadcrumb.new(
						p_message = message,
						p_level = level,
						p_category = &"error",
						p_timestamp_msec = engine_ticks_msec,
						p_attributes = attributes,
					),
			)
	if as_log:
		log_accepted = not _service.capture_log(
				message,
				level,
				&"foundry.engine",
				ObservabilityEvent.UNASSIGNED_TIMESTAMP,
				attributes,
				engine_ticks_msec,
			).is_empty()

	if not event_accepted and not breadcrumb_accepted and not log_accepted:
		return
	_state_mutex.lock()
	if event_accepted:
		_frame_event_count += 1
		_event_timepoints.append(engine_ticks_msec)
	_error_timepoints[error_key] = engine_ticks_msec
	_state_mutex.unlock()


## Receives ordinary engine output messages.
func _log_message(message: String, error: bool) -> void:
	if _service == null or not _service.try_begin_automatic_capture():
		return
	_capture_message(message, error)
	_service.end_automatic_capture()


func _capture_message(message: String, error: bool) -> void:
	if (_config.automatic_breadcrumb_mask & ObservabilityCaptureMask.MESSAGE) == 0 \
			and (_config.automatic_log_mask & ObservabilityCaptureMask.MESSAGE) == 0:
		return

	var processed_message: String = _strip_invisible(message)
	if processed_message.is_empty() or _has_filtered_prefix(processed_message):
		return

	var level: int = ObservabilityLevel.ERROR if error else ObservabilityLevel.INFO
	var engine_ticks_msec: int = _now_msec()
	var attributes: Dictionary = {
		"log.error_stream": error,
		"observability.origin": _ORIGIN,
	}
	if (_config.automatic_breadcrumb_mask & ObservabilityCaptureMask.MESSAGE) != 0:
		_service._capture_automatic_breadcrumb(ObservabilityBreadcrumb.new(
				p_message = processed_message,
				p_level = level,
				p_category = &"log",
				p_timestamp_msec = engine_ticks_msec,
				p_attributes = attributes,
			))
	if (_config.automatic_log_mask & ObservabilityCaptureMask.MESSAGE) != 0:
		_service.capture_log(
				processed_message,
				level,
				&"foundry.engine",
				ObservabilityEvent.UNASSIGNED_TIMESTAMP,
				attributes,
				engine_ticks_msec,
			)


func _category_mask(error_type: int) -> int:
	match error_type:
		Logger.ERROR_TYPE_WARNING:
			return ObservabilityCaptureMask.WARNING
		Logger.ERROR_TYPE_SCRIPT:
			return ObservabilityCaptureMask.SCRIPT
		Logger.ERROR_TYPE_SHADER:
			return ObservabilityCaptureMask.SHADER
	return ObservabilityCaptureMask.ERROR


func _error_level(error_type: int) -> int:
	if error_type == Logger.ERROR_TYPE_WARNING:
		return ObservabilityLevel.WARN
	if error_type == Logger.ERROR_TYPE_FATAL:
		return ObservabilityLevel.FATAL
	return ObservabilityLevel.ERROR


func _error_type_name(error_type: int) -> String:
	match error_type:
		Logger.ERROR_TYPE_WARNING:
			return "WARNING"
		Logger.ERROR_TYPE_SCRIPT:
			return "SCRIPT ERROR"
		Logger.ERROR_TYPE_SHADER:
			return "SHADER ERROR"
		Logger.ERROR_TYPE_FATAL:
			return "FATAL"
	return "ERROR"


func _now_msec() -> int:
	var result: Variant = _clock.call()
	if result is int:
		var timestamp_msec: int = result
		return timestamp_msec
	return Time.get_ticks_msec()


func _frame_index() -> int:
	var result: Variant = _frame.call()
	if result is int:
		var frame_index: int = result
		return frame_index
	return Engine.get_process_frames()


func _prune_event_timepoints(engine_ticks_msec: int) -> void:
	if _config.automatic_event_throttle_window_msec <= 0:
		_event_timepoints.clear()
		return
	while not _event_timepoints.is_empty() \
			and engine_ticks_msec - _event_timepoints[0] \
				>= _config.automatic_event_throttle_window_msec:
		_event_timepoints.pop_front()


## Clears duplicate, frame, and sliding-window state.
func reset() -> void:
	_state_mutex.lock()
	_error_timepoints.clear()
	_event_timepoints.clear()
	_current_frame = -1
	_frame_event_count = 0
	_state_mutex.unlock()


## Replaces automatic policy and clears all throttle state.
func reconfigure(config: ObservabilityConfig) -> void:
	_config = config
	reset()


func _serialize_backtraces(script_backtraces: Array[ScriptBacktrace]) -> Dictionary:
	var serialized: Array = []
	var stack_lines: PackedStringArray = PackedStringArray()
	for backtrace: ScriptBacktrace in script_backtraces:
		if backtrace == null:
			continue
		var language: String = backtrace.get_language_name()
		var frames: Array = []
		for frame_index: int in range(backtrace.get_frame_count()):
			var frame_function: String = backtrace.get_frame_function(frame_index)
			var frame_file: String = backtrace.get_frame_file(frame_index)
			var frame_line: int = backtrace.get_frame_line(frame_index)
			frames.append({
				"function": frame_function,
				"file": frame_file,
				"line": frame_line,
			})
			stack_lines.append(
					"%s %s (%s:%s)" % [
						language, frame_function, frame_file, frame_line,
					])
		serialized.append({"language": language, "frames": frames})
	return {
		"backtraces": serialized,
		"stack_trace": "\n".join(stack_lines),
	}


func _has_filtered_prefix(message: String) -> bool:
	for prefix: String in _config.automatic_message_filter_prefixes():
		if message.begins_with(prefix):
			return true
	return false


func _strip_invisible(message: String) -> String:
	var output: String = ""
	var index: int = 0
	while index < message.length():
		var codepoint: int = message.unicode_at(index)
		if codepoint == 27 and index + 1 < message.length() \
				and message.unicode_at(index + 1) == 91:
			index += 2
			while index < message.length():
				var escape_codepoint: int = message.unicode_at(index)
				index += 1
				if escape_codepoint >= 64 and escape_codepoint <= 126:
					break
			continue
		if codepoint < 32 or codepoint == 127:
			index += 1
			continue
		output += message[index]
		index += 1
	return output
