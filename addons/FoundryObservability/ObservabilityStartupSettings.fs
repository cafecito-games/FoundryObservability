namespace foundry.observability

## Resolves provider-neutral startup configuration from project and runtime data.
class_name ObservabilityStartupSettings
extends RefCounted

const AUTO_INIT: String = "foundry_observability/startup/auto_init"
const ENABLED: String = "foundry_observability/startup/enabled"
const SKIP_EDITOR_PLAY: String = "foundry_observability/startup/skip_editor_play"
const SKIP_DEBUG_EXPORTS: String = "foundry_observability/startup/skip_debug_exports"
const DSN: String = "foundry_observability/options/dsn"
const ENVIRONMENT: String = "foundry_observability/options/environment"
const RELEASE: String = "foundry_observability/options/release"
const DIST: String = "foundry_observability/options/dist"
const DEBUG_DIAGNOSTICS: String = "foundry_observability/options/debug_diagnostics"
const PROVIDER_OPTIONS: String = "foundry_observability/options/provider_options"

const DEBUG_OFF: int = 0
const DEBUG_ON: int = 1
const DEBUG_AUTO: int = 2


static func from_sources(
		_values: Dictionary = {},
		_environment_variables: Dictionary = {},
		_runtime: Dictionary = {},
) -> ObservabilityStartupSettings:
	return ObservabilityStartupSettings.new()


static func from_project_settings() -> ObservabilityStartupSettings:
	return ObservabilityStartupSettings.new()


static func project_setting_defaults() -> Dictionary:
	return {
		AUTO_INIT: true,
		ENABLED: true,
		SKIP_EDITOR_PLAY: false,
		SKIP_DEBUG_EXPORTS: false,
		DSN: "",
		ENVIRONMENT: "",
		RELEASE: "",
		DIST: "",
		DEBUG_DIAGNOSTICS: DEBUG_AUTO,
		PROVIDER_OPTIONS: {},
	}


static func register_project_settings() -> void:
	pass


func validation_error() -> int:
	return Error.OK


func skip_status() -> StringName:
	return ObservabilityStartupStatus.NOT_STARTED


func has_dsn() -> bool:
	return false


func debug_enabled() -> bool:
	return false


func observability_config() -> ObservabilityConfig:
	return ObservabilityConfig.new(p_enabled = false)
