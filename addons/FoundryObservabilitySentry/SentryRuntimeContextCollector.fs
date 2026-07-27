namespace foundry.observability.sentry

## Normalizes stable and capture-time runtime values into Sentry contexts.
class_name SentryRuntimeContextCollector
extends RefCounted

var _probe: Object


func _init(p_probe: Object) -> void:
	_probe = p_probe


func stable_contexts(environment: String, send_default_pii: bool) -> Dictionary:
	var application_values: Dictionary = _probe.call("application_values")
	var app: Dictionary = {}
	_copy_nonempty_string(app, "name", application_values.get("name"))
	_copy_nonempty_string(app, "version", application_values.get("version"))
	_copy_nonempty_string(app, "start_time", application_values.get("start_time"))
	_copy_nonempty_string(app, "architecture", application_values.get("architecture"))

	var engine_values: Dictionary = _probe.call("engine_values")
	var mode: String = _runtime_mode(engine_values)
	var engine: Dictionary = {}
	_copy_nonempty_string(engine, "version", engine_values.get("version"))
	_copy_nonempty_string(engine, "version_commit", engine_values.get("version_commit"))
	_copy_nonempty_string(engine, "architecture", engine_values.get("architecture"))
	_copy_nonempty_string(engine, "runtime_mode", mode)
	_copy_bool(engine, "editor", engine_values.get("editor"))
	_copy_bool(engine, "debug_build", engine_values.get("debug_build"))
	_copy_bool(engine, "headless", engine_values.get("headless"))

	var device: Dictionary = _stable_device_context(send_default_pii)
	var display_values: Dictionary = _probe.call("display_values")
	var display: Dictionary = _display_context(display_values)
	var gpu_values: Dictionary = _probe.call("gpu_values")
	var gpu: Dictionary = _gpu_context(gpu_values)
	var runtime_values: Dictionary = _probe.call("runtime_values")
	var runtime: Dictionary = {}
	_copy_nonempty_string(runtime, "environment", environment)
	_copy_nonempty_string(runtime, "mode", mode)
	_copy_bool(runtime, "sandboxed", runtime_values.get("sandboxed"))
	_copy_bool(runtime, "userfs_persistent", runtime_values.get("userfs_persistent"))

	var contexts: Dictionary = {}
	_add_nonempty_context(contexts, "foundry_app", app)
	_add_nonempty_context(contexts, "foundry_engine", engine)
	_add_nonempty_context(contexts, "foundry_device", device)
	_add_nonempty_context(contexts, "display", display)
	_add_nonempty_context(contexts, "gpu", gpu)
	_add_nonempty_context(contexts, "foundry_runtime", runtime)
	return contexts


func volatile_contexts() -> Dictionary:
	var device: Dictionary = {}
	if str(_probe.call("platform_name")) != "iOS":
		var memory: Dictionary = _probe.call("memory_values")
		_copy_nonnegative_int(device, "free_memory", memory.get("free"))
		_copy_nonnegative_int(device, "usable_memory", memory.get("available"))
	_copy_nonnegative_int(device, "free_storage", _probe.call("free_storage"))

	var display: Dictionary = {}
	_copy_nonempty_string(
			display,
			"primary_orientation",
			_probe.call("primary_orientation"),
		)

	var contexts: Dictionary = {}
	_add_nonempty_context(contexts, "foundry_device", device)
	_add_nonempty_context(contexts, "display", display)
	return contexts


func contexts_for_capture(stable: Dictionary) -> Dictionary:
	return merge_contexts(stable, volatile_contexts())


## Merges isolated context snapshots with volatile fields taking precedence.
func merge_contexts(stable: Dictionary, volatile: Dictionary) -> Dictionary:
	var merged: Dictionary = stable.duplicate(true)
	for context_key: Variant in volatile.keys():
		if not (context_key is String) or not (volatile[context_key] is Dictionary):
			continue
		var update: Dictionary = volatile[context_key]
		if update.is_empty():
			continue
		var context: Dictionary = {}
		var existing: Variant = merged.get(context_key)
		if existing is Dictionary:
			var existing_dictionary: Dictionary = existing
			context = existing_dictionary.duplicate(true)
		for field_key: Variant in update.keys():
			context[field_key] = update[field_key]
		merged[context_key] = context
	return merged


func _stable_device_context(send_default_pii: bool) -> Dictionary:
	var values: Dictionary = _probe.call("device_values")
	var device: Dictionary = {}
	var model: String = str(values.get("model", ""))
	if model != "GenericDevice":
		_copy_nonempty_string(device, "model", model)
	_copy_nonempty_string(device, "type", _device_type(str(_probe.call("platform_name"))))
	var app_values: Dictionary = _probe.call("application_values")
	_copy_nonempty_string(device, "architecture", app_values.get("architecture"))
	_copy_nonempty_string(device, "processor_name", values.get("processor_name"))
	_copy_positive_number(device, "processor_count", values.get("processor_count"))

	if str(_probe.call("platform_name")) != "iOS":
		var memory: Dictionary = _probe.call("memory_values")
		_copy_positive_number(device, "memory_size", memory.get("physical"))
		_copy_nonnegative_int(device, "free_memory", memory.get("free"))
		_copy_nonnegative_int(device, "usable_memory", memory.get("available"))
	_copy_nonnegative_int(device, "free_storage", _probe.call("free_storage"))

	if send_default_pii:
		var privacy: Dictionary = _probe.call("privacy_values")
		_copy_nonempty_string(
				device,
				"unique_identifier",
				privacy.get("unique_identifier"),
			)
		_copy_nonempty_string(device, "locale", privacy.get("locale"))
		_copy_nonempty_string(device, "timezone", privacy.get("timezone"))
	return device


func _display_context(values: Dictionary) -> Dictionary:
	var display: Dictionary = {}
	_copy_nonempty_string(display, "server", values.get("server"))
	_copy_positive_number(display, "screen_count", values.get("screen_count"))
	_copy_bool(display, "touchscreen_available", values.get("touchscreen_available"))
	_copy_positive_number(
			display,
			"primary_width_pixels",
			values.get("primary_width_pixels"),
		)
	_copy_positive_number(
			display,
			"primary_height_pixels",
			values.get("primary_height_pixels"),
		)
	_copy_positive_number(display, "primary_dpi", values.get("primary_dpi"))
	_copy_positive_number(
			display,
			"primary_refresh_rate",
			values.get("primary_refresh_rate"),
		)
	_copy_nonempty_string(
			display,
			"primary_orientation",
			values.get("primary_orientation"),
		)
	return display


func _gpu_context(values: Dictionary) -> Dictionary:
	var name: String = str(values.get("name", ""))
	if name.is_empty():
		return {}
	var gpu: Dictionary = {"name": name}
	for key: String in [
		"vendor_name",
		"api_version",
		"device_type",
		"driver_name",
		"driver_version",
		"rendering_method",
	]:
		_copy_nonempty_string(gpu, key, values.get(key))
	return gpu


func _runtime_mode(values: Dictionary) -> String:
	if values.get("headless") == true or values.get("dedicated_server") == true:
		return "headless"
	if values.get("editor") == true:
		return "editor"
	if values.get("debug_build") == true:
		return "debug_export"
	return "release_export"


func _device_type(platform: String) -> String:
	match platform:
		"macOS":
			return "desktop"
		"iOS", "Android":
			return "handheld"
		_:
			return ""


func _copy_nonempty_string(target: Dictionary, key: String, value: Variant) -> void:
	if value is String and not value.is_empty():
		target[key] = value


func _copy_bool(target: Dictionary, key: String, value: Variant) -> void:
	if value is bool:
		target[key] = value


func _copy_positive_number(target: Dictionary, key: String, value: Variant) -> void:
	if (value is int or value is float) and value > 0:
		target[key] = value


func _copy_nonnegative_int(target: Dictionary, key: String, value: Variant) -> void:
	if value is int and value >= 0:
		target[key] = value


func _add_nonempty_context(contexts: Dictionary, key: String, context: Dictionary) -> void:
	if not context.is_empty():
		contexts[key] = context
