namespace foundry.observability.sentry

## Reads raw Godot runtime values for automatic Sentry context collection.
class_name SentryRuntimeContextProbe
extends RefCounted


func platform_name() -> String:
	return OS.get_name()


func application_values() -> Dictionary:
	var start_unix_seconds: int = floori(
			Time.get_unix_time_from_system() - Time.get_ticks_msec() * 0.001,
		)
	return {
			"name": str(ProjectSettings.get_setting("application/config/name", "")),
			"version": str(ProjectSettings.get_setting("application/config/version", "")),
			"start_time": Time.get_datetime_string_from_unix_time(start_unix_seconds, true),
			"architecture": Engine.get_architecture_name(),
		}


func engine_values() -> Dictionary:
	var version: Dictionary = Engine.get_version_info()
	var display_name: String = DisplayServer.get_name()
	return {
			"version": str(version.get("string", "")),
			"version_commit": str(version.get("hash", "")),
			"architecture": Engine.get_architecture_name(),
			"editor": Engine.is_editor_hint(),
			"debug_build": OS.is_debug_build(),
			"headless": display_name.to_lower() == "headless",
			"dedicated_server": OS.has_feature("dedicated_server"),
		}


func device_values() -> Dictionary:
	return {
			"model": OS.get_model_name(),
			"processor_name": OS.get_processor_name(),
			"processor_count": OS.get_processor_count(),
		}


func memory_values() -> Dictionary:
	return OS.get_memory_info()


func free_storage() -> int:
	var directory: DirAccess = DirAccess.open("user://")
	if directory == null:
		return -1
	return directory.get_space_left()


func display_values() -> Dictionary:
	var primary_screen: int = DisplayServer.get_primary_screen()
	var size: Vector2i = DisplayServer.screen_get_size(primary_screen)
	return {
			"server": DisplayServer.get_name(),
			"screen_count": DisplayServer.get_screen_count(),
			"touchscreen_available": DisplayServer.is_touchscreen_available(),
			"primary_width_pixels": size.x,
			"primary_height_pixels": size.y,
			"primary_dpi": DisplayServer.screen_get_dpi(primary_screen),
			"primary_refresh_rate": DisplayServer.screen_get_refresh_rate(primary_screen),
			"primary_orientation": _orientation_name(
					DisplayServer.screen_get_orientation(primary_screen),
				),
		}


func primary_orientation() -> String:
	return _orientation_name(
			DisplayServer.screen_get_orientation(DisplayServer.get_primary_screen()),
		)


func gpu_values() -> Dictionary:
	var driver_info: PackedStringArray = OS.get_video_adapter_driver_info()
	var driver_name: String = ""
	var driver_version: String = ""
	if driver_info.size() >= 2:
		driver_name = driver_info[0]
		driver_version = driver_info[1]
	return {
			"name": RenderingServer.get_video_adapter_name(),
			"vendor_name": RenderingServer.get_video_adapter_vendor(),
			"api_version": RenderingServer.get_video_adapter_api_version(),
			"device_type": _gpu_device_type_name(RenderingServer.get_video_adapter_type()),
			"driver_name": driver_name,
			"driver_version": driver_version,
			"rendering_method": RenderingServer.get_current_rendering_method(),
		}


func runtime_values() -> Dictionary:
	return {
			"sandboxed": OS.is_sandboxed(),
			"userfs_persistent": OS.is_userfs_persistent(),
		}


func privacy_values() -> Dictionary:
	return {
			"unique_identifier": OS.get_unique_id(),
			"locale": OS.get_locale(),
			"timezone": str(Time.get_time_zone_from_system().get("name", "")),
		}


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
