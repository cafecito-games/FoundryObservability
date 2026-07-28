namespace foundry.observability.sentry

## Typed runtime facts collected for Sentry context mapping.
final class_name SentryRuntimeSnapshot
extends RefCounted


final class Application extends RefCounted:
	final var name: String
	final var version: String
	final var start_time: String
	final var architecture: String

	func _init(
			p_name: String = "",
			p_version: String = "",
			p_start_time: String = "",
			p_architecture: String = "",
	) -> void:
		name = p_name
		version = p_version
		start_time = p_start_time
		architecture = p_architecture


final class EngineRuntime extends RefCounted:
	final var version: String
	final var version_commit: String
	final var architecture: String
	final var editor: bool
	final var debug_build: bool
	final var headless: bool
	final var dedicated_server: bool

	func _init(
			p_version: String = "",
			p_version_commit: String = "",
			p_architecture: String = "",
			p_editor: bool = false,
			p_debug_build: bool = false,
			p_headless: bool = false,
			p_dedicated_server: bool = false,
	) -> void:
		version = p_version
		version_commit = p_version_commit
		architecture = p_architecture
		editor = p_editor
		debug_build = p_debug_build
		headless = p_headless
		dedicated_server = p_dedicated_server


final class Device extends RefCounted:
	final var model: String
	final var processor_name: String
	final var processor_count: int
	final var physical_memory: int
	final var free_memory: int
	final var usable_memory: int

	func _init(
			p_model: String = "",
			p_processor_name: String = "",
			p_processor_count: int = 0,
			p_physical_memory: int = -1,
			p_free_memory: int = -1,
			p_usable_memory: int = -1,
	) -> void:
		model = p_model
		processor_name = p_processor_name
		processor_count = p_processor_count
		physical_memory = p_physical_memory
		free_memory = p_free_memory
		usable_memory = p_usable_memory


final class Display extends RefCounted:
	final var server: String
	final var screen_count: int
	final var touchscreen_available: bool
	final var primary_width_pixels: int
	final var primary_height_pixels: int
	final var primary_dpi: int
	final var primary_refresh_rate: float
	final var primary_orientation: String

	func _init(
			p_server: String = "",
			p_screen_count: int = 0,
			p_touchscreen_available: bool = false,
			p_primary_width_pixels: int = 0,
			p_primary_height_pixels: int = 0,
			p_primary_dpi: int = 0,
			p_primary_refresh_rate: float = 0.0,
			p_primary_orientation: String = "",
	) -> void:
		server = p_server
		screen_count = p_screen_count
		touchscreen_available = p_touchscreen_available
		primary_width_pixels = p_primary_width_pixels
		primary_height_pixels = p_primary_height_pixels
		primary_dpi = p_primary_dpi
		primary_refresh_rate = p_primary_refresh_rate
		primary_orientation = p_primary_orientation


final class Gpu extends RefCounted:
	final var name: String
	final var vendor_name: String
	final var api_version: String
	final var device_type: String
	final var driver_name: String
	final var driver_version: String
	final var rendering_method: String

	func _init(
			p_name: String = "",
			p_vendor_name: String = "",
			p_api_version: String = "",
			p_device_type: String = "",
			p_driver_name: String = "",
			p_driver_version: String = "",
			p_rendering_method: String = "",
	) -> void:
		name = p_name
		vendor_name = p_vendor_name
		api_version = p_api_version
		device_type = p_device_type
		driver_name = p_driver_name
		driver_version = p_driver_version
		rendering_method = p_rendering_method


final class Runtime extends RefCounted:
	final var sandboxed: bool
	final var userfs_persistent: bool

	func _init(
			p_sandboxed: bool = false,
			p_userfs_persistent: bool = false,
	) -> void:
		sandboxed = p_sandboxed
		userfs_persistent = p_userfs_persistent


final class Privacy extends RefCounted:
	final var unique_identifier: String
	final var locale: String
	final var timezone: String

	func _init(
			p_unique_identifier: String = "",
			p_locale: String = "",
			p_timezone: String = "",
	) -> void:
		unique_identifier = p_unique_identifier
		locale = p_locale
		timezone = p_timezone


final var platform_name: String
final var application: Application
final var engine: EngineRuntime
final var device: Device
final var display: Display
final var gpu: Gpu
final var runtime: Runtime
final var privacy: Privacy
final var free_storage: int


func _init(
		p_platform_name: String = "",
		p_application: Application = Application.new(),
		p_engine: EngineRuntime = EngineRuntime.new(),
		p_device: Device = Device.new(),
		p_display: Display = Display.new(),
		p_gpu: Gpu = Gpu.new(),
		p_runtime: Runtime = Runtime.new(),
		p_privacy: Privacy = Privacy.new(),
		p_free_storage: int = -1,
) -> void:
	platform_name = p_platform_name
	application = p_application
	engine = p_engine
	device = p_device
	display = p_display
	gpu = p_gpu
	runtime = p_runtime
	privacy = p_privacy
	free_storage = p_free_storage
