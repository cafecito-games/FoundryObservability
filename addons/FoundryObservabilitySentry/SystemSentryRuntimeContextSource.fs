namespace foundry.observability.sentry

## Reads typed runtime snapshots from Godot engine services.
final class_name SystemSentryRuntimeContextSource
extends RefCounted
uses SentryRuntimeContextSource


func stable_snapshot() -> SentryRuntimeSnapshot:
	var platform_name: String = OS.get_name()
	var version: Dictionary = Engine.get_version_info()
	var display_name: String = DisplayServer.get_name()
	var primary_screen: int = DisplayServer.get_primary_screen()
	var primary_size: Vector2i = DisplayServer.screen_get_size(primary_screen)
	var driver_info: PackedStringArray = OS.get_video_adapter_driver_info()
	var driver_name: String = ""
	var driver_version: String = ""
	if driver_info.size() >= 2:
		driver_name = driver_info[0]
		driver_version = driver_info[1]
	var memory: Dictionary = {}
	if platform_name != "iOS":
		memory = OS.get_memory_info()
	var start_unix_seconds: int = floori(
			Time.get_unix_time_from_system() - Time.get_ticks_msec() * 0.001,
		)
	return SentryRuntimeSnapshot.new(
			platform_name,
			SentryRuntimeSnapshot.Application.new(
				str(ProjectSettings.get_setting("application/config/name", "")),
				str(ProjectSettings.get_setting("application/config/version", "")),
				Time.get_datetime_string_from_unix_time(start_unix_seconds, true),
				Engine.get_architecture_name(),
			),
			SentryRuntimeSnapshot.EngineRuntime.new(
				str(version.get("string", "")),
				str(version.get("hash", "")),
				Engine.get_architecture_name(),
				Engine.is_editor_hint(),
				OS.is_debug_build(),
				display_name.to_lower() == "headless",
				OS.has_feature("dedicated_server"),
			),
			SentryRuntimeSnapshot.Device.new(
				OS.get_model_name(),
				OS.get_processor_name(),
				OS.get_processor_count(),
				_memory_value(memory, "physical"),
				_memory_value(memory, "free"),
				_memory_value(memory, "available"),
			),
			SentryRuntimeSnapshot.Display.new(
				display_name,
				DisplayServer.get_screen_count(),
				DisplayServer.is_touchscreen_available(),
				primary_size.x,
				primary_size.y,
				DisplayServer.screen_get_dpi(primary_screen),
				DisplayServer.screen_get_refresh_rate(primary_screen),
				_orientation_name(DisplayServer.screen_get_orientation(primary_screen)),
			),
			SentryRuntimeSnapshot.Gpu.new(
				RenderingServer.get_video_adapter_name(),
				RenderingServer.get_video_adapter_vendor(),
				RenderingServer.get_video_adapter_api_version(),
				_gpu_device_type_name(RenderingServer.get_video_adapter_type()),
				driver_name,
				driver_version,
				RenderingServer.get_current_rendering_method(),
			),
			SentryRuntimeSnapshot.Runtime.new(
				OS.is_sandboxed(),
				OS.is_userfs_persistent(),
			),
			SentryRuntimeSnapshot.Privacy.new(),
			_free_storage(),
		)


func volatile_snapshot() -> SentryRuntimeSnapshot:
	var platform_name: String = OS.get_name()
	var memory: Dictionary = {}
	if platform_name != "iOS":
		memory = OS.get_memory_info()
	var primary_screen: int = DisplayServer.get_primary_screen()
	return SentryRuntimeSnapshot.new(
			platform_name,
			SentryRuntimeSnapshot.Application.new(),
			SentryRuntimeSnapshot.EngineRuntime.new(),
			SentryRuntimeSnapshot.Device.new(
				p_free_memory = _memory_value(memory, "free"),
				p_usable_memory = _memory_value(memory, "available"),
			),
			SentryRuntimeSnapshot.Display.new(
				p_primary_orientation = _orientation_name(
					DisplayServer.screen_get_orientation(primary_screen),
				),
			),
			SentryRuntimeSnapshot.Gpu.new(),
			SentryRuntimeSnapshot.Runtime.new(),
			SentryRuntimeSnapshot.Privacy.new(),
			_free_storage(),
		)


func privacy_snapshot() -> SentryRuntimeSnapshot.Privacy:
	return SentryRuntimeSnapshot.Privacy.new(
			OS.get_unique_id(),
			OS.get_locale(),
			str(Time.get_time_zone_from_system().get("name", "")),
		)


func _free_storage() -> int:
	var directory: DirAccess = DirAccess.open("user://")
	if directory == null:
		return -1
	return directory.get_space_left()


func _memory_value(memory: Dictionary, key: String) -> int:
	var value: Variant = memory.get(key)
	return value if value is int else -1


func _orientation_name(orientation: int) -> String:
	match orientation:
		DisplayServer.SCREEN_LANDSCAPE:
			return "landscape"
		DisplayServer.SCREEN_PORTRAIT:
			return "portrait"
		DisplayServer.SCREEN_REVERSE_LANDSCAPE:
			return "reverse_landscape"
		DisplayServer.SCREEN_REVERSE_PORTRAIT:
			return "reverse_portrait"
		DisplayServer.SCREEN_SENSOR_LANDSCAPE:
			return "sensor_landscape"
		DisplayServer.SCREEN_SENSOR_PORTRAIT:
			return "sensor_portrait"
		DisplayServer.SCREEN_SENSOR:
			return "sensor"
		_:
			return ""


func _gpu_device_type_name(device_type: int) -> String:
	match device_type:
		RenderingDevice.DEVICE_TYPE_OTHER:
			return "other"
		RenderingDevice.DEVICE_TYPE_INTEGRATED_GPU:
			return "integrated_gpu"
		RenderingDevice.DEVICE_TYPE_DISCRETE_GPU:
			return "discrete_gpu"
		RenderingDevice.DEVICE_TYPE_VIRTUAL_GPU:
			return "virtual_gpu"
		RenderingDevice.DEVICE_TYPE_CPU:
			return "cpu"
		_:
			return ""
