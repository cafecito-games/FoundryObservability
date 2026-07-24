@tool
namespace foundry.observability.sentry

extends EditorPlugin

var _ios_export_plugin: IOSExportPlugin


func _enter_tree() -> void:
	_ios_export_plugin = IOSExportPlugin.new()
	add_export_plugin(_ios_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(_ios_export_plugin)
	_ios_export_plugin = null


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
