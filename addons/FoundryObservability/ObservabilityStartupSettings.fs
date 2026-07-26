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
const _MAX_PROVIDER_OPTION_DEPTH: int = 8

var _auto_init: bool = true
var _enabled: bool = true
var _skip_editor_play: bool = false
var _skip_debug_exports: bool = false
var _dsn: String = ""
var _environment: String = ""
var _release: String = ""
var _dist: String = ""
var _debug_mode: int = DEBUG_AUTO
var _debug_build: bool = false
var _editor_hint: bool = false
var _editor_feature: bool = false
var _provider_options: Dictionary = {}
var _validation_error: int = Error.OK


func _init(
		values: Dictionary = {},
		environment_variables: Dictionary = {},
		runtime: Dictionary = {},
) -> void:
	var defaults: Dictionary = project_setting_defaults()
	_auto_init = values.get(AUTO_INIT, defaults[AUTO_INIT]) == true
	_enabled = values.get(ENABLED, defaults[ENABLED]) == true
	_skip_editor_play = values.get(
			SKIP_EDITOR_PLAY, defaults[SKIP_EDITOR_PLAY]) == true
	_skip_debug_exports = values.get(
			SKIP_DEBUG_EXPORTS, defaults[SKIP_DEBUG_EXPORTS]) == true
	_debug_build = runtime.get("debug_build", false) == true
	_editor_hint = runtime.get("editor_hint", false) == true
	_editor_feature = runtime.get("editor_feature", false) == true

	var raw_debug_mode: Variant = values.get(
			DEBUG_DIAGNOSTICS, defaults[DEBUG_DIAGNOSTICS])
	if not (raw_debug_mode is int) or raw_debug_mode < DEBUG_OFF \
			or raw_debug_mode > DEBUG_AUTO:
		_validation_error = Error.ERR_INVALID_PARAMETER
	else:
		_debug_mode = raw_debug_mode

	var raw_options: Variant = values.get(PROVIDER_OPTIONS, defaults[PROVIDER_OPTIONS])
	if not (raw_options is Dictionary) \
			or not _is_valid_provider_option(raw_options, 0):
		_validation_error = Error.ERR_INVALID_PARAMETER
	else:
		@warning_ignore("unsafe_method_access")
		_provider_options = raw_options.duplicate(true)

	_dsn = _first_nonempty(
			str(values.get(DSN, defaults[DSN])),
			str(environment_variables.get("SENTRY_DSN", "")),
		)
	_environment = _first_nonempty(
			str(values.get(ENVIRONMENT, defaults[ENVIRONMENT])),
			str(environment_variables.get("SENTRY_ENVIRONMENT", "")),
		)
	if _environment.is_empty():
		_environment = _detected_environment(runtime)

	var app_name: String = str(
			runtime.get("app_name", "Unknown Foundry project")).strip_edges()
	if app_name.is_empty():
		app_name = "Unknown Foundry project"
	var app_version: String = str(runtime.get("app_version", "noversion")).strip_edges()
	if app_version.is_empty():
		app_version = "noversion"
	var release_template: String = _first_nonempty(
			str(values.get(RELEASE, defaults[RELEASE])),
			str(environment_variables.get("SENTRY_RELEASE", "")),
		)
	if release_template.is_empty():
		release_template = "{app_name}@{app_version}"
	_release = release_template.replace("{app_name}", app_name).replace(
			"{app_version}", app_version)
	_dist = str(values.get(DIST, defaults[DIST])).strip_edges()

	_provider_options["dsn"] = _dsn
	_provider_options["debug"] = debug_enabled()

static func from_sources(
		values: Dictionary = {},
		environment_variables: Dictionary = {},
		runtime: Dictionary = {},
) -> ObservabilityStartupSettings:
	return ObservabilityStartupSettings.new(
			values.duplicate(true),
			environment_variables.duplicate(true),
			runtime.duplicate(true),
		)


static func from_project_settings() -> ObservabilityStartupSettings:
	register_project_settings()
	var defaults: Dictionary = project_setting_defaults()
	var values: Dictionary = {}
	for setting_name: String in defaults:
		values[setting_name] = ProjectSettings.get_setting(
				setting_name, defaults[setting_name])
	return from_sources(
			values,
			{
				"SENTRY_DSN": OS.get_environment("SENTRY_DSN"),
				"SENTRY_RELEASE": OS.get_environment("SENTRY_RELEASE"),
				"SENTRY_ENVIRONMENT": OS.get_environment("SENTRY_ENVIRONMENT"),
			},
			{
				"app_name": ProjectSettings.get_setting(
						"application/config/name", "Unknown Foundry project"),
				"app_version": ProjectSettings.get_setting(
						"application/config/version", "noversion"),
				"dedicated_server": OS.has_feature("dedicated_server"),
				"editor_hint": Engine.is_editor_hint(),
				"editor_feature": OS.has_feature("editor"),
				"debug_build": OS.is_debug_build(),
			},
		)


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
	var defaults: Dictionary = project_setting_defaults()
	for setting_name: String in defaults:
		var default_value: Variant = defaults[setting_name]
		if not ProjectSettings.has_setting(setting_name):
			ProjectSettings.set_setting(setting_name, default_value)
		ProjectSettings.set_initial_value(setting_name, default_value)
		ProjectSettings.set_as_basic(setting_name, true)
		ProjectSettings.set_restart_if_changed(setting_name, false)
	ProjectSettings.add_property_info({
		"name": DEBUG_DIAGNOSTICS,
		"type": TYPE_INT,
		"hint": PROPERTY_HINT_ENUM,
		"hint_string": "Off,On,Auto",
	})


func validation_error() -> int:
	return _validation_error


func skip_status() -> StringName:
	if not _auto_init or not _enabled:
		return ObservabilityStartupStatus.DISABLED
	if _editor_hint:
		return ObservabilityStartupStatus.SKIPPED_EDITOR
	if _editor_feature and _skip_editor_play:
		return ObservabilityStartupStatus.SKIPPED_EDITOR_PLAY
	if _debug_build and _skip_debug_exports:
		return ObservabilityStartupStatus.SKIPPED_DEBUG
	return ObservabilityStartupStatus.NOT_STARTED


func has_dsn() -> bool:
	return not _dsn.is_empty()


func debug_enabled() -> bool:
	return _debug_mode == DEBUG_ON \
			or (_debug_mode == DEBUG_AUTO and _debug_build)


func observability_config() -> ObservabilityConfig:
	return ObservabilityConfig.new(
			p_enabled = _enabled,
			p_environment = _environment,
			p_release = _release,
			p_dist = _dist,
			p_global_attributes = {},
			p_provider_options = _provider_options,
		)


static func _first_nonempty(primary: String, fallback: String) -> String:
	var resolved: String = primary.strip_edges()
	if not resolved.is_empty():
		return resolved
	return fallback.strip_edges()


static func _detected_environment(runtime: Dictionary) -> String:
	if runtime.get("dedicated_server", false) == true:
		return "dedicated_server"
	if runtime.get("editor_hint", false) == true:
		return "editor_dev"
	if runtime.get("editor_feature", false) == true:
		return "editor_dev_run"
	if runtime.get("debug_build", false) == true:
		return "export_debug"
	return "export_release"


static func _is_valid_provider_option(value: Variant, depth: int) -> bool:
	match typeof(value):
		TYPE_NIL, TYPE_BOOL, TYPE_INT, TYPE_STRING, TYPE_STRING_NAME:
			return true
		TYPE_FLOAT:
			@warning_ignore("unsafe_call_argument")
			return is_finite(value)
		TYPE_ARRAY:
			if depth > _MAX_PROVIDER_OPTION_DEPTH:
				return false
			@warning_ignore("unsafe_call_argument")
			return _is_valid_provider_option_array(value, depth)
		TYPE_DICTIONARY:
			if depth > _MAX_PROVIDER_OPTION_DEPTH:
				return false
			@warning_ignore("unsafe_call_argument")
			return _is_valid_provider_option_dictionary(value, depth)
		_:
			return false


static func _is_valid_provider_option_array(values: Array, depth: int) -> bool:
	for element: Variant in values:
		if not _is_valid_provider_option(element, depth + 1):
			return false
	return true


static func _is_valid_provider_option_dictionary(
		values: Dictionary,
		depth: int,
) -> bool:
	for key: Variant in values:
		if not (key is String) and not (key is StringName):
			return false
		if not _is_valid_provider_option(values[key], depth + 1):
			return false
	return true
