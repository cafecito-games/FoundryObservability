@tool
namespace games.cafecito.foundryobservability

extends EditorPlugin

func _enable_plugin() -> void:
	add_autoload_singleton(
			"FoundryObservability",
			"res://addons/FoundryObservability/FoundryObservability.fs")

func _disable_plugin() -> void:
	remove_autoload_singleton("FoundryObservability")
