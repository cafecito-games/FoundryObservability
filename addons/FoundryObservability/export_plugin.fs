@tool
namespace foundry.observability

extends EditorPlugin

## Registers the FoundryObservability autoload when the editor plugin is enabled.
func _enable_plugin() -> void:
	add_autoload_singleton(
			"FoundryObservability",
			"res://addons/FoundryObservability/FoundryObservability.fs")

## Removes the FoundryObservability autoload when the editor plugin is disabled.
func _disable_plugin() -> void:
	remove_autoload_singleton("FoundryObservability")
