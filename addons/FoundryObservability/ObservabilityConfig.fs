namespace foundry.observability

## Provider-neutral configuration shared by all integrations.
class_name ObservabilityConfig
extends RefCounted

var enabled: bool = true
var environment: String = ""
var release: String = ""
var dist: String = ""
var _global_attributes: Dictionary = {}
var _provider_options: Dictionary = {}


## Creates configuration with opaque provider options.
func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {}
) -> void:
	enabled = p_enabled
	environment = p_environment
	release = p_release
	dist = p_dist
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)


func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)
