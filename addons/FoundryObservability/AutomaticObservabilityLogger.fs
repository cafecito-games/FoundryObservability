namespace foundry.observability

import foundry.observability.processing
import foundry.observability.runtime

## Converts engine logger callbacks into provider-neutral observability records.
class_name AutomaticObservabilityLogger
extends Logger

const _ORIGIN: String = "auto.log.foundry"

var _service: FoundryObservability
var _config: ObservabilityAutomaticCaptureConfig
var _runtime: ObservabilityRuntime
var _capture_mutex: Mutex = Mutex.new()
var _capture_owner: int = -1
var _provider_call: ObservabilityProviderCall?
var _installed: bool = false
var _detached: bool = false


## Creates a logger with a shared observability runtime.
func _init(
		service: FoundryObservability,
		config: ObservabilityAutomaticCaptureConfig,
		runtime: ObservabilityRuntime,
) -> void:
	assert(runtime != null, "AutomaticObservabilityLogger requires a runtime.")
	_service = service
	_config = config
	_runtime = runtime


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
	if _service == null or not _try_begin_capture():
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
	_end_capture()


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
	if not _capture_is_current():
		return

	var category_mask: int = _category_mask(error_type)
	var level: int = _error_level(error_type)
	var type_name: String = _error_type_name(error_type)
	var message: String = rationale if not rationale.is_empty() else code
	var engine_ticks_msec: int = _runtime.monotonic_time_msec()
	var as_event: bool = (_config.event_mask() & category_mask) != 0
	var as_breadcrumb: bool = (_config.breadcrumb_mask() & category_mask) != 0
	var as_log: bool = (_config.log_mask() & category_mask) != 0
	if not as_event and not as_breadcrumb and not as_log:
		return

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

	if as_event:
		_service.capture_event(ObservabilityEvent.new(
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
			))
	if as_breadcrumb:
		_service._capture_automatic_breadcrumb(
				ObservabilityBreadcrumb.new(
						p_message = message,
						p_level = level,
						p_category = &"error",
						p_timestamp_msec = engine_ticks_msec,
						p_attributes = attributes,
					),
			)
	if as_log:
		_service.capture_log(
				message,
				level,
				&"foundry.engine",
				ObservabilityEvent.UNASSIGNED_TIMESTAMP,
				attributes,
				engine_ticks_msec,
			)


## Receives ordinary engine output messages.
func _log_message(message: String, error: bool) -> void:
	if _service == null or not _try_begin_capture():
		return
	_capture_message(message, error)
	_end_capture()


func _capture_message(message: String, error: bool) -> void:
	if not _capture_is_current():
		return
	if (_config.breadcrumb_mask() & ObservabilityCaptureMask.MESSAGE) == 0 \
			and (_config.log_mask() & ObservabilityCaptureMask.MESSAGE) == 0:
		return

	var processed_message: String = _strip_invisible(message)
	if processed_message.is_empty() or _has_filtered_prefix(processed_message):
		return

	var level: int = ObservabilityLevel.ERROR if error else ObservabilityLevel.INFO
	var engine_ticks_msec: int = _runtime.monotonic_time_msec()
	var attributes: Dictionary = {
		"log.error_stream": error,
		"observability.origin": _ORIGIN,
	}
	if (_config.breadcrumb_mask() & ObservabilityCaptureMask.MESSAGE) != 0:
		_service._capture_automatic_breadcrumb(ObservabilityBreadcrumb.new(
				p_message = processed_message,
				p_level = level,
				p_category = &"log",
				p_timestamp_msec = engine_ticks_msec,
				p_attributes = attributes,
			))
	if (_config.log_mask() & ObservabilityCaptureMask.MESSAGE) != 0:
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


## Installs this logger once.
func install() -> void:
	_capture_mutex.lock()
	if _installed or _detached:
		_capture_mutex.unlock()
		return
	OS.add_logger(self)
	_installed = true
	_capture_mutex.unlock()


## Unregisters and permanently rejects callbacks already queued by the engine.
func remove() -> void:
	_capture_mutex.lock()
	if _detached:
		_capture_mutex.unlock()
		return
	_detached = true
	if _installed:
		OS.remove_logger(self)
		_installed = false
	_capture_mutex.unlock()


## Unregisters without detaching for tests that invoke callbacks directly.
func uninstall_for_testing() -> void:
	_capture_mutex.lock()
	if not _installed:
		_capture_mutex.unlock()
		return
	OS.remove_logger(self)
	_installed = false
	_capture_mutex.unlock()


## Replaces automatic routing policy.
func reconfigure(config: ObservabilityAutomaticCaptureConfig) -> void:
	_config = config


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
	for prefix: String in _config.message_filter_prefixes():
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


func _try_begin_capture() -> bool:
	if not _capture_mutex.try_lock():
		return false
	if _detached:
		_capture_mutex.unlock()
		return false
	var owner: int = _runtime.caller_id()
	if _capture_owner != -1:
		_capture_mutex.unlock()
		return false
	var provider_call: ObservabilityProviderCall = (
			_service._begin_automatic_capture()
		)
	if not provider_call.accepted():
		_capture_mutex.unlock()
		return false
	_capture_owner = owner
	_provider_call = provider_call
	_capture_mutex.unlock()
	return true


func _capture_is_current() -> bool:
	_capture_mutex.lock()
	var current: bool = not _detached \
			and _capture_owner != -1 \
			and _provider_call != null
	_capture_mutex.unlock()
	return current


func _end_capture() -> void:
	_capture_mutex.lock()
	var provider_call: ObservabilityProviderCall? = _provider_call
	_provider_call = null
	_capture_owner = -1
	_capture_mutex.unlock()
	if provider_call != null:
		_service._end_automatic_capture(provider_call)
