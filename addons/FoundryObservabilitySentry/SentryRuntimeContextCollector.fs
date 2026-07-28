namespace foundry.observability.sentry

## Normalizes typed stable and capture-time snapshots into Sentry contexts.
class_name SentryRuntimeContextCollector
extends RefCounted

var _source: SentryRuntimeContextSource


func _init(p_source: SentryRuntimeContextSource) -> void:
	_source = p_source


func stable_contexts(environment: String, send_default_pii: bool) -> Dictionary:
	var snapshot: SentryRuntimeSnapshot = _source.stable_snapshot()
	var app: Dictionary = {}
	_copy_nonempty_string(app, "name", snapshot.application.name)
	_copy_nonempty_string(app, "version", snapshot.application.version)
	_copy_nonempty_string(app, "start_time", snapshot.application.start_time)
	_copy_nonempty_string(app, "architecture", snapshot.application.architecture)

	var mode: String = _runtime_mode(snapshot.engine)
	var engine: Dictionary = {}
	_copy_nonempty_string(engine, "version", snapshot.engine.version)
	_copy_nonempty_string(engine, "version_commit", snapshot.engine.version_commit)
	_copy_nonempty_string(engine, "architecture", snapshot.engine.architecture)
	_copy_nonempty_string(engine, "runtime_mode", mode)
	_copy_bool(engine, "editor", snapshot.engine.editor)
	_copy_bool(engine, "debug_build", snapshot.engine.debug_build)
	_copy_bool(engine, "headless", snapshot.engine.headless)

	var device: Dictionary = _stable_device_context(snapshot, send_default_pii)
	var display: Dictionary = _display_context(snapshot.display)
	var gpu: Dictionary = _gpu_context(snapshot.gpu)
	var runtime: Dictionary = {}
	_copy_nonempty_string(runtime, "environment", environment)
	_copy_nonempty_string(runtime, "mode", mode)
	_copy_bool(runtime, "sandboxed", snapshot.runtime.sandboxed)
	_copy_bool(runtime, "userfs_persistent", snapshot.runtime.userfs_persistent)

	var contexts: Dictionary = {}
	_add_nonempty_context(contexts, "foundry_app", app)
	_add_nonempty_context(contexts, "foundry_engine", engine)
	_add_nonempty_context(contexts, "foundry_device", device)
	_add_nonempty_context(contexts, "display", display)
	_add_nonempty_context(contexts, "gpu", gpu)
	_add_nonempty_context(contexts, "foundry_runtime", runtime)
	return contexts


func volatile_contexts() -> Dictionary:
	var snapshot: SentryRuntimeSnapshot = _source.volatile_snapshot()
	var device: Dictionary = {}
	if snapshot.platform_name != "iOS":
		_copy_nonnegative_int(device, "free_memory", snapshot.device.free_memory)
		_copy_nonnegative_int(device, "usable_memory", snapshot.device.usable_memory)
	_copy_nonnegative_int(device, "free_storage", snapshot.free_storage)

	var display: Dictionary = {}
	_copy_nonempty_string(
			display,
			"primary_orientation",
			snapshot.display.primary_orientation,
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


func _stable_device_context(
		snapshot: SentryRuntimeSnapshot,
		send_default_pii: bool,
) -> Dictionary:
	var device: Dictionary = {}
	if snapshot.device.model != "GenericDevice":
		_copy_nonempty_string(device, "model", snapshot.device.model)
	_copy_nonempty_string(device, "type", _device_type(snapshot.platform_name))
	_copy_nonempty_string(device, "architecture", snapshot.application.architecture)
	_copy_nonempty_string(device, "processor_name", snapshot.device.processor_name)
	_copy_positive_int(device, "processor_count", snapshot.device.processor_count)

	if snapshot.platform_name != "iOS":
		_copy_positive_int(device, "memory_size", snapshot.device.physical_memory)
		_copy_nonnegative_int(device, "free_memory", snapshot.device.free_memory)
		_copy_nonnegative_int(device, "usable_memory", snapshot.device.usable_memory)
	_copy_nonnegative_int(device, "free_storage", snapshot.free_storage)

	if send_default_pii:
		var privacy: SentryRuntimeSnapshot.Privacy = _source.privacy_snapshot()
		_copy_nonempty_string(
				device,
				"unique_identifier",
				privacy.unique_identifier,
			)
		_copy_nonempty_string(device, "locale", privacy.locale)
		_copy_nonempty_string(device, "timezone", privacy.timezone)
	return device


func _display_context(values: SentryRuntimeSnapshot.Display) -> Dictionary:
	var display: Dictionary = {}
	_copy_nonempty_string(display, "server", values.server)
	_copy_positive_int(display, "screen_count", values.screen_count)
	_copy_bool(display, "touchscreen_available", values.touchscreen_available)
	_copy_positive_int(display, "primary_width_pixels", values.primary_width_pixels)
	_copy_positive_int(display, "primary_height_pixels", values.primary_height_pixels)
	_copy_positive_int(display, "primary_dpi", values.primary_dpi)
	_copy_positive_float(
			display,
			"primary_refresh_rate",
			values.primary_refresh_rate,
		)
	_copy_nonempty_string(
			display,
			"primary_orientation",
			values.primary_orientation,
		)
	return display


func _gpu_context(values: SentryRuntimeSnapshot.Gpu) -> Dictionary:
	if values.name.is_empty():
		return {}
	var gpu: Dictionary = {"name": values.name}
	_copy_nonempty_string(gpu, "vendor_name", values.vendor_name)
	_copy_nonempty_string(gpu, "api_version", values.api_version)
	_copy_nonempty_string(gpu, "device_type", values.device_type)
	_copy_nonempty_string(gpu, "driver_name", values.driver_name)
	_copy_nonempty_string(gpu, "driver_version", values.driver_version)
	_copy_nonempty_string(gpu, "rendering_method", values.rendering_method)
	return gpu


func _runtime_mode(values: SentryRuntimeSnapshot.EngineRuntime) -> String:
	if values.headless or values.dedicated_server:
		return "headless"
	if values.editor:
		return "editor"
	if values.debug_build:
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


func _copy_nonempty_string(
		target: Dictionary,
		key: String,
		value: String,
) -> void:
	if not value.is_empty():
		target[key] = value


func _copy_bool(target: Dictionary, key: String, value: bool) -> void:
	target[key] = value


func _copy_positive_int(target: Dictionary, key: String, value: int) -> void:
	if value > 0:
		target[key] = value


func _copy_positive_float(target: Dictionary, key: String, value: float) -> void:
	if value > 0.0:
		target[key] = value


func _copy_nonnegative_int(target: Dictionary, key: String, value: int) -> void:
	if value >= 0:
		target[key] = value


func _add_nonempty_context(
		contexts: Dictionary,
		key: String,
		context: Dictionary,
) -> void:
	if not context.is_empty():
		contexts[key] = context
