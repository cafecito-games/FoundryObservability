@tool
namespace foundry.observability.sentry

extends EditorPlugin

var _ios_export_plugin: IOSExportPlugin
var _android_export_plugin: AndroidExportPlugin


func _enter_tree() -> void:
	_android_export_plugin = AndroidExportPlugin.new()
	add_export_plugin(_android_export_plugin)
	_ios_export_plugin = IOSExportPlugin.new()
	add_export_plugin(_ios_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(_android_export_plugin)
	_android_export_plugin = null
	remove_export_plugin(_ios_export_plugin)
	_ios_export_plugin = null


class AndroidExportPlugin extends EditorExportPlugin:
	const _DEPENDENCIES_FILE: String = (
			"res://addons/FoundryObservabilitySentry/"
			+ "AndroidFoundryObservabilitySentry/android-dependencies.txt"
	)
	const _ANDROID_DEBUG_AAR: String = (
			"res://addons/FoundryObservabilitySentry/bin/android/debug/"
			+ "FoundryObservabilitySentry-debug.aar"
	)
	const _ANDROID_RELEASE_AAR: String = (
			"res://addons/FoundryObservabilitySentry/bin/android/release/"
			+ "FoundryObservabilitySentry-release.aar"
	)


	func _get_name() -> String:
		return "FoundryObservabilitySentryAndroid"


	func _get_android_libraries(
			_platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		if debug:
			return PackedStringArray([_ANDROID_DEBUG_AAR])
		return PackedStringArray([_ANDROID_RELEASE_AAR])


	func _get_android_dependencies(
			_platform: EditorExportPlatform, _debug: bool) -> PackedStringArray:
		return _read_dependency_lines()


	func _read_dependency_lines() -> PackedStringArray:
		var coordinates: PackedStringArray = PackedStringArray()
		var file: FileAccess = FileAccess.open(_DEPENDENCIES_FILE, FileAccess.READ)
		if file == null:
			push_error("FoundryObservabilitySentry: cannot read " + _DEPENDENCIES_FILE)
			return coordinates
		while not file.eof_reached():
			var line: String = file.get_line().strip_edges()
			if not line.is_empty() and not line.begins_with("#"):
				coordinates.append(line)
		return coordinates


	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform.get_os_name() == "Android"


class IOSExportPlugin extends EditorExportPlugin:
	const _IOS_FRAMEWORK: String = (
			"res://addons/FoundryObservabilitySentry/bin/ios/"
			+ "FoundryObservabilitySentry.xcframework"
	)


	func _get_name() -> String:
		return "FoundryObservabilitySentryIOS"


	func _get_ios_frameworks() -> PackedStringArray:
		return PackedStringArray([_IOS_FRAMEWORK])


	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform.get_os_name() == "iOS"
