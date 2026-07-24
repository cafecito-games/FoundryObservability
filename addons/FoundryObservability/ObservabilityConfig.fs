namespace foundry.observability

## Provider-neutral configuration shared by all integrations.
class_name ObservabilityConfig
extends RefCounted

## Enables provider capture after configuration when true.
var enabled: bool = true
## Identifies the deployment environment, such as production or staging.
var environment: String = ""
## Identifies the game release associated with captured events.
var release: String = ""
## Identifies an optional distribution variant of the release.
var dist: String = ""
var _global_attributes: Dictionary = {}
var _provider_options: Dictionary = {}


## Creates configuration with enabled metadata, copied attributes, and opaque provider options.
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


## Returns a deep copy of attributes applied by provider integrations.
func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


## Returns a deep copy of backend-specific options not interpreted by the core.
func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)
