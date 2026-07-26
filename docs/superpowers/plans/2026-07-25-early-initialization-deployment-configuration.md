# Early Initialization and Deployment Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Initialize the optional Sentry provider from project settings during `FoundryObservability` autoload construction, with deterministic defaults, skip controls, safe diagnostics, and idempotent lifecycle behavior.

**Architecture:** Add small provider-neutral startup status and settings-resolver classes to the core addon. The autoload uses those classes to validate configuration and lazily instantiate the optional Sentry provider from its conventional script path before the main scene loads. The existing `configure()` transaction and Sentry native lifecycle remain the sole owners of provider activation, replacement, and shutdown.

**Tech Stack:** FoundryScript, Foundry project settings and autoload lifecycle, FoundryLib test runner, shell packaging contracts, Swift/XCTest, Java/JUnit, Task.

---

## File Structure

- Create `addons/FoundryObservability/ObservabilityStartupStatus.fs`: stable provider-neutral startup status constants.
- Create `addons/FoundryObservability/ObservabilityStartupStatus.fs.uid`: tracked resource UID.
- Create `addons/FoundryObservability/ObservabilityStartupSettings.fs`: project-setting registration, source precedence, defaults, validation, runtime classification, and `ObservabilityConfig` construction.
- Create `addons/FoundryObservability/ObservabilityStartupSettings.fs.uid`: tracked resource UID.
- Modify `addons/FoundryObservability/FoundryObservability.fs`: synchronous autoload startup, lazy optional-provider loading, startup diagnostics, and retained startup provider.
- Modify `addons/FoundryObservability/FoundryObservabilityApi.fs`: expose project-settings initialization and startup diagnostics.
- Modify `addons/FoundryObservability/export_plugin.fs`: register startup settings when the editor plugin loads.
- Modify `test_project/tests/observability-core.test.fs`: deterministic resolver and service lifecycle coverage.
- Modify `test_project/tests/project-wiring.test.fs`: settings, autoload, and source-wiring coverage.
- Modify `test_project/tests/support/recording_observability_api.notest.fs`: satisfy the expanded public trait in test fixtures.
- Create `test_project/tests/support/startup_order_probe.notest.fs`: later-autoload probe for the real startup ordering guarantee.
- Create `test_project/tests/support/startup_order_probe.notest.fs.uid`: tracked test resource UID.
- Modify `test_project/project.foundry`: order the startup probe after `FoundryObservability`.
- Modify `scripts/test-foundry-script`: require and lint the startup resources.
- Modify `scripts/test-package`: require startup resources in the core package.
- Modify `README.md`: minimal automatic-startup setup and earliest safe capture point.
- Modify `docs/API.md`: complete settings, precedence, status, lifecycle, and ordering contract.
- Modify `docs/NATIVE_CRASH_VALIDATION.md`: use project-settings startup as the preferred crash-validation path.
- Modify `CHANGELOG.md`: record the new public startup behavior.

### Task 1: Establish the startup status and resource contracts

**Files:**

- Create: `addons/FoundryObservability/ObservabilityStartupStatus.fs`
- Create: `addons/FoundryObservability/ObservabilityStartupStatus.fs.uid`
- Create: `addons/FoundryObservability/ObservabilityStartupSettings.fs`
- Create: `addons/FoundryObservability/ObservabilityStartupSettings.fs.uid`
- Modify: `test_project/tests/project-wiring.test.fs`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Write the failing resource-contract tests**

Append this test to `test_project/tests/project-wiring.test.fs`:

```foundryscript
func test_project_contains_startup_status_and_settings_resources() -> void:
	for resource_path: String in [
		"res://addons/FoundryObservability/ObservabilityStartupStatus.fs",
		"res://addons/FoundryObservability/ObservabilityStartupSettings.fs",
	]:
		Expect.that(FileAccess.file_exists(resource_path)).to_be_true()
		Expect.that(FileAccess.file_exists(resource_path + ".uid")).to_be_true()
```

Add these checks beside the existing core resource checks in
`scripts/test-foundry-script`:

```bash
[[ -f "$addon/ObservabilityStartupStatus.fs" ]] \
	|| fail "ObservabilityStartupStatus.fs is missing"
[[ -f "$addon/ObservabilityStartupSettings.fs" ]] \
	|| fail "ObservabilityStartupSettings.fs is missing"
```

- [ ] **Step 2: Run the focused contracts and verify they fail**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: FAIL in
`test_project_contains_startup_status_and_settings_resources` because the new
resources do not exist.

Run:

```bash
scripts/test-foundry-script
```

Expected: FAIL with `ObservabilityStartupStatus.fs is missing`.

- [ ] **Step 3: Add the minimal status and settings resources**

Create `addons/FoundryObservability/ObservabilityStartupStatus.fs`:

```foundryscript
namespace foundry.observability

## Stable results from the project-settings startup path.
class_name ObservabilityStartupStatus
extends RefCounted

const NOT_STARTED: StringName = &"not_started"
const INITIALIZED: StringName = &"initialized"
const DISABLED: StringName = &"disabled"
const SKIPPED_EDITOR: StringName = &"skipped_editor"
const SKIPPED_EDITOR_PLAY: StringName = &"skipped_editor_play"
const SKIPPED_DEBUG: StringName = &"skipped_debug"
const MISSING_DSN: StringName = &"missing_dsn"
const PROVIDER_UNAVAILABLE: StringName = &"provider_unavailable"
const CONFIGURATION_FAILED: StringName = &"configuration_failed"
```

Create `addons/FoundryObservability/ObservabilityStartupStatus.fs.uid`:

```text
uid://c5n9y7hj3m2qa
```

Create the initial
`addons/FoundryObservability/ObservabilityStartupSettings.fs`:

```foundryscript
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
```

Create `addons/FoundryObservability/ObservabilityStartupSettings.fs.uid`:

```text
uid://d8r4pk6w2t1zs
```

- [ ] **Step 4: Run resource validation and verify it passes**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
```

Expected: both commands exit 0 and the Foundry project reports all tests
passing.

- [ ] **Step 5: Commit the resource contract**

```bash
git add \
  addons/FoundryObservability/ObservabilityStartupStatus.fs \
  addons/FoundryObservability/ObservabilityStartupStatus.fs.uid \
  addons/FoundryObservability/ObservabilityStartupSettings.fs \
  addons/FoundryObservability/ObservabilityStartupSettings.fs.uid \
  test_project/tests/project-wiring.test.fs \
  scripts/test-foundry-script
git commit -m "feat: define observability startup contracts"
```

### Task 2: Resolve deployment settings deterministically

**Files:**

- Modify: `addons/FoundryObservability/ObservabilityStartupSettings.fs`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing precedence, defaults, skip, and validation tests**

Append these tests to `test_project/tests/observability-core.test.fs`:

```foundryscript
func test_startup_settings_resolve_project_environment_and_default_precedence() -> void:
	var explicit := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.DSN: " https://project@example/1 ",
				ObservabilityStartupSettings.RELEASE: "{app_name}-custom-{app_version}",
				ObservabilityStartupSettings.ENVIRONMENT: " staging ",
				ObservabilityStartupSettings.DIST: " ios ",
			},
			{
				"SENTRY_DSN": "https://environment@example/2",
				"SENTRY_RELEASE": "environment-release",
				"SENTRY_ENVIRONMENT": "environment-name",
			},
			{
				"app_name": "Oakhaven",
				"app_version": "1.2.3",
				"debug_build": true,
			},
		)
	var explicit_config: ObservabilityConfig = explicit.observability_config()

	Expect.that(explicit_config.provider_options()["dsn"]).to_equal(
			"https://project@example/1",
		)
	Expect.that(explicit_config.release).to_equal("Oakhaven-custom-1.2.3")
	Expect.that(explicit_config.environment).to_equal("staging")
	Expect.that(explicit_config.dist).to_equal("ios")

	var environment := ObservabilityStartupSettings.from_sources(
			{},
			{
				"SENTRY_DSN": "https://environment@example/2",
				"SENTRY_RELEASE": "environment-release",
				"SENTRY_ENVIRONMENT": "environment-name",
			},
			{"app_name": "Oakhaven", "app_version": "1.2.3"},
		)
	var environment_config: ObservabilityConfig = environment.observability_config()
	Expect.that(environment_config.provider_options()["dsn"]).to_equal(
			"https://environment@example/2",
		)
	Expect.that(environment_config.release).to_equal("environment-release")
	Expect.that(environment_config.environment).to_equal("environment-name")

	var defaults := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{
				"app_name": "Oakhaven",
				"app_version": "1.2.3",
				"debug_build": false,
			},
		)
	var default_config: ObservabilityConfig = defaults.observability_config()
	Expect.that(default_config.release).to_equal("Oakhaven@1.2.3")
	Expect.that(default_config.environment).to_equal("export_release")


func test_startup_settings_classify_runtime_and_skip_contexts() -> void:
	var dedicated := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{"dedicated_server": true, "editor_hint": true, "debug_build": true},
		)
	Expect.that(dedicated.observability_config().environment).to_equal(
			"dedicated_server",
		)
	Expect.that(dedicated.skip_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_EDITOR,
		)

	var editor_play := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.SKIP_EDITOR_PLAY: true},
			{},
			{"editor_feature": true, "debug_build": true},
		)
	Expect.that(editor_play.skip_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_EDITOR_PLAY,
		)
	Expect.that(editor_play.observability_config().environment).to_equal(
			"editor_dev_run",
		)

	var debug_export := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.SKIP_DEBUG_EXPORTS: true},
			{},
			{"debug_build": true},
		)
	Expect.that(debug_export.skip_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_DEBUG,
		)
	Expect.that(debug_export.observability_config().environment).to_equal(
			"export_debug",
		)

	var disabled := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.AUTO_INIT: false},
			{},
			{"editor_hint": true},
		)
	Expect.that(disabled.skip_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)


func test_startup_settings_validate_and_merge_provider_options() -> void:
	var options := {"dsn": "wrong", "debug": false, "send_default_pii": true}
	var settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.DSN: "https://public@example/1",
				ObservabilityStartupSettings.DEBUG_DIAGNOSTICS:
						ObservabilityStartupSettings.DEBUG_ON,
				ObservabilityStartupSettings.PROVIDER_OPTIONS: options,
			},
			{},
			{"debug_build": false},
		)
	options["send_default_pii"] = false
	var resolved: Dictionary = settings.observability_config().provider_options()

	Expect.that(settings.validation_error()).to_equal(Error.OK)
	Expect.that(settings.has_dsn()).to_be_true()
	Expect.that(settings.debug_enabled()).to_be_true()
	Expect.that(resolved["dsn"]).to_equal("https://public@example/1")
	Expect.that(resolved["debug"]).to_be_true()
	Expect.that(resolved["send_default_pii"]).to_be_true()

	var invalid_mode := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.DEBUG_DIAGNOSTICS: 99},
		)
	Expect.that(invalid_mode.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)

	var invalid_options := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.PROVIDER_OPTIONS: Callable()},
		)
	Expect.that(invalid_options.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)

	var nested_callable := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.PROVIDER_OPTIONS: {
				"nested": {"callback": Callable()},
			}},
		)
	Expect.that(nested_callable.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)


func test_startup_settings_register_project_defaults_idempotently() -> void:
	ObservabilityStartupSettings.register_project_settings()
	ObservabilityStartupSettings.register_project_settings()
	var defaults: Dictionary = ObservabilityStartupSettings.project_setting_defaults()

	for setting_name: String in defaults:
		Expect.that(ProjectSettings.has_setting(setting_name)).to_be_true()
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.AUTO_INIT)).to_be_true()
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)).to_equal(
					ObservabilityStartupSettings.DEBUG_AUTO,
				)
```

- [ ] **Step 2: Run the project tests and verify the new tests fail**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: FAIL because `from_sources`, `observability_config`,
`skip_status`, `validation_error`, and project-setting registration are not
implemented.

- [ ] **Step 3: Implement the settings resolver**

Replace `ObservabilityStartupSettings.fs` with:

```foundryscript
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
				"debug_build": Engine.is_debug_build(),
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
	if depth > _MAX_PROVIDER_OPTION_DEPTH:
		return false
	match typeof(value):
		TYPE_NIL, TYPE_BOOL, TYPE_INT, TYPE_STRING, TYPE_STRING_NAME:
			return true
		TYPE_FLOAT:
			return is_finite(value)
		TYPE_ARRAY:
			return _is_valid_provider_option_array(value, depth)
		TYPE_DICTIONARY:
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
```

- [ ] **Step 4: Run resolver tests and lint**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
```

Expected: both commands exit 0. The project suite includes the four new
startup-settings tests.

- [ ] **Step 5: Commit deterministic configuration resolution**

```bash
git add \
  addons/FoundryObservability/ObservabilityStartupSettings.fs \
  test_project/tests/observability-core.test.fs
git commit -m "feat: resolve observability startup settings"
```

### Task 3: Initialize the provider during autoload construction

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `test_project/tests/observability-core.test.fs`
- Modify: `test_project/tests/support/recording_observability_api.notest.fs`
- Create: `test_project/tests/support/startup_order_probe.notest.fs`
- Create: `test_project/tests/support/startup_order_probe.notest.fs.uid`
- Modify: `test_project/project.foundry`

- [ ] **Step 1: Write a failing public startup API contract**

Append this test to `test_project/tests/project-wiring.test.fs`:

```foundryscript
func test_project_exposes_provider_neutral_startup_api() -> void:
	var service_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservability.fs")
	var api_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservabilityApi.fs")
	for method_signature: String in [
		"func initialize_from_project_settings() -> int:",
		"func startup_status() -> StringName:",
		"func startup_message() -> String:",
	]:
		Expect.that(service_source).to_contain(method_signature)
		Expect.that(api_source).to_contain(
				"abstract " + method_signature.trim_suffix(":"))
```

- [ ] **Step 2: Run the API contract and verify it fails**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: FAIL in `test_project_exposes_provider_neutral_startup_api` because
the service and trait do not expose startup methods.

- [ ] **Step 3: Add compilable startup API scaffolding**

Add these methods after `configure()` in
`addons/FoundryObservability/FoundryObservabilityApi.fs`:

```foundryscript
## Rereads project settings and initializes the supported startup provider.
abstract func initialize_from_project_settings() -> int
## Returns the stable status of the latest project-settings startup attempt.
abstract func startup_status() -> StringName
## Returns the human-readable diagnostic for the latest startup attempt.
abstract func startup_message() -> String
```

Add these fields near the existing state in `FoundryObservability.fs`:

```foundryscript
const _SENTRY_PROVIDER_PATH: String = (
	"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs"
)

var _startup_provider: ObservabilityProvider
var _startup_provider_path: String = _SENTRY_PROVIDER_PATH
var _startup_status: StringName = ObservabilityStartupStatus.NOT_STARTED
var _startup_message: String = "Startup has not run."
```

Replace `_init()` temporarily with this compilable scaffold:

```foundryscript
func _init(
		_startup_settings: ObservabilityStartupSettings? = null,
		startup_provider_path: String = _SENTRY_PROVIDER_PATH,
) -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_startup_provider_path = startup_provider_path
	_reset_log_rate_limit()
	_reset_metric_sampling()
```

Add these temporary method bodies after `_init()`:

```foundryscript
func initialize_from_project_settings() -> int:
	return Error.OK


func startup_status() -> StringName:
	return _startup_status


func startup_message() -> String:
	return _startup_message


func _initialize_startup(_settings: ObservabilityStartupSettings) -> int:
	return Error.OK
```

Add these trait implementations after `configure()` in
`test_project/tests/support/recording_observability_api.notest.fs`:

```foundryscript
func initialize_from_project_settings() -> int:
	return Error.OK


func startup_status() -> StringName:
	return ObservabilityStartupStatus.INITIALIZED


func startup_message() -> String:
	return "recording startup initialized"
```

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: exit 0. The new API contract passes while startup behavior remains
unimplemented.

- [ ] **Step 4: Write failing autoload startup and lifecycle tests**

Append these tests to `test_project/tests/observability-core.test.fs`:

```foundryscript
func test_startup_initializes_before_immediate_capture() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.DSN: "https://public@example/1",
				ObservabilityStartupSettings.ENVIRONMENT: "production",
				ObservabilityStartupSettings.RELEASE: "1.2.3",
				ObservabilityStartupSettings.PROVIDER_OPTIONS: {
					"send_default_pii": true,
				},
			},
			{},
			{"debug_build": false},
		)
	var service := FoundryObservability.new(settings)

	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.INITIALIZED,
		)
	Expect.that(service.startup_message()).to_contain("initialized")
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.capture_message("startup event")).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	Expect.that(bridge.configured_payload["environment"]).to_equal("production")
	Expect.that(bridge.configured_payload["release"]).to_equal("1.2.3")
	Expect.that(
			bridge.configured_payload["provider_options"]["send_default_pii"],
		).to_be_true()

	service.shutdown()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_autoload_startup_completes_before_later_autoload() -> void:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	var service: Node = tree.root.get_node("FoundryObservability")
	var probe: Node? = tree.root.get_node_or_null(
			"FoundryObservabilityStartupProbe")

	Expect.that(probe).to_not_be_null()
	if probe == null:
		return
	Expect.that(service.get_index()).to_be_less_than(probe.get_index())
	Expect.that(probe.get("observed_status")).to_not_equal(
			ObservabilityStartupStatus.NOT_STARTED,
		)


func test_startup_reports_safe_disabled_missing_and_unavailable_states() -> void:
	var disabled := FoundryObservability.new(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.ENABLED: false,
			}),
		)
	Expect.that(disabled.startup_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(disabled.last_error()).to_equal(Error.OK)
	Expect.that(disabled.provider_name()).to_equal(&"null")
	disabled.shutdown()

	var missing_dsn := FoundryObservability.new(
			ObservabilityStartupSettings.from_sources(),
		)
	Expect.that(missing_dsn.startup_status()).to_equal(
			ObservabilityStartupStatus.MISSING_DSN,
		)
	Expect.that(missing_dsn.last_error()).to_equal(Error.ERR_UNCONFIGURED)
	Expect.that(missing_dsn.provider_name()).to_equal(&"null")
	missing_dsn.shutdown()

	var unavailable := FoundryObservability.new(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
			"res://addons/FoundryObservability/MissingProvider.fs",
		)
	Expect.that(unavailable.startup_status()).to_equal(
			ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
		)
	Expect.that(unavailable.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(unavailable.provider_name()).to_equal(&"null")
	unavailable.shutdown()


func test_startup_reuses_provider_for_reconfiguration_and_restart() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var first_settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
		ObservabilityStartupSettings.ENVIRONMENT: "production",
	})
	var service := FoundryObservability.new(first_settings)
	var first_owner: String = bridge.active_owner

	Expect.that(service._initialize_startup(first_settings)).to_equal(Error.OK)
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(bridge.configured_payloads).to_have_size(2)

	var changed_settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
		ObservabilityStartupSettings.ENVIRONMENT: "staging",
	})
	Expect.that(service._initialize_startup(changed_settings)).to_equal(Error.OK)
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(bridge.configured_payload["environment"]).to_equal("staging")

	service.shutdown()
	Expect.that(service._initialize_startup(changed_settings)).to_equal(Error.OK)
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_available()).to_be_true()

	service.shutdown()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_startup_failure_preserves_working_provider_and_diagnostics() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var service := FoundryObservability.new(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
		)
	bridge.configure_result = Error.FAILED
	var failed := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/2",
	})

	Expect.that(service._initialize_startup(failed)).to_equal(Error.FAILED)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.CONFIGURATION_FAILED,
		)
	Expect.that(service.startup_message()).to_contain("failed")
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_available()).to_be_true()

	service.shutdown()
	Engine.unregister_singleton("SentryObservabilityBridge")
```

- [ ] **Step 5: Run the behavior tests and verify they fail**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: the tests run but FAIL because the scaffold leaves startup at
`not_started`, keeps the null provider, and does not configure the fake bridge.

- [ ] **Step 6: Implement synchronous startup in the autoload**

Replace `_init()` with:

```foundryscript
func _init(
		startup_settings: ObservabilityStartupSettings? = null,
		startup_provider_path: String = _SENTRY_PROVIDER_PATH,
) -> void:
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_startup_provider_path = startup_provider_path
	_reset_log_rate_limit()
	_reset_metric_sampling()
	if startup_settings == null:
		initialize_from_project_settings()
	else:
		_initialize_startup(startup_settings)
```

Add these methods immediately after `_init()`:

```foundryscript
## Rereads project settings and runs the supported startup path.
func initialize_from_project_settings() -> int:
	return _initialize_startup(ObservabilityStartupSettings.from_project_settings())


## Returns the latest startup-settings result.
func startup_status() -> StringName:
	return _startup_status


## Returns a concise explanation of the latest startup-settings result.
func startup_message() -> String:
	return _startup_message


func _initialize_startup(settings: ObservabilityStartupSettings) -> int:
	if settings == null:
		return _record_startup(
				ObservabilityStartupStatus.CONFIGURATION_FAILED,
				"Startup configuration is invalid.",
				Error.ERR_INVALID_PARAMETER,
				false,
			)

	var skip_status: StringName = settings.skip_status()
	if skip_status != ObservabilityStartupStatus.NOT_STARTED:
		return _record_startup(
				skip_status,
				_startup_skip_message(skip_status),
				Error.OK,
				settings.debug_enabled(),
			)
	if settings.validation_error() != Error.OK:
		return _record_startup(
				ObservabilityStartupStatus.CONFIGURATION_FAILED,
				"Startup configuration contains invalid values.",
				settings.validation_error(),
				settings.debug_enabled(),
			)
	if not settings.has_dsn():
		return _record_startup(
				ObservabilityStartupStatus.MISSING_DSN,
				"Startup is disabled because no DSN is configured.",
				Error.ERR_UNCONFIGURED,
				settings.debug_enabled(),
			)

	var startup_provider: ObservabilityProvider? = _load_startup_provider()
	if startup_provider == null:
		return _record_startup(
				ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
				"The optional Sentry startup provider is unavailable.",
				Error.ERR_UNAVAILABLE,
				settings.debug_enabled(),
			)

	var result: int = configure(startup_provider, settings.observability_config())
	if result == Error.OK:
		return _record_startup(
				ObservabilityStartupStatus.INITIALIZED,
				"Startup provider initialized.",
				Error.OK,
				settings.debug_enabled(),
			)
	var failed_status: StringName = ObservabilityStartupStatus.CONFIGURATION_FAILED
	if result == Error.ERR_UNAVAILABLE:
		failed_status = ObservabilityStartupStatus.PROVIDER_UNAVAILABLE
	return _record_startup(
			failed_status,
			"Startup provider configuration failed with Error %s." % result,
			result,
			settings.debug_enabled(),
		)


func _load_startup_provider() -> ObservabilityProvider?:
	if _startup_provider != null:
		return _startup_provider
	if not ResourceLoader.exists(_startup_provider_path):
		return null
	var provider_script: Script = ResourceLoader.load(_startup_provider_path) as Script
	if provider_script == null or not provider_script.can_instantiate():
		return null
	var candidate: Variant = provider_script.new()
	if not (candidate is ObservabilityProvider):
		return null
	_startup_provider = candidate as ObservabilityProvider
	return _startup_provider


func _record_startup(
		status: StringName,
		message: String,
		result: int,
		print_diagnostics: bool,
) -> int:
	_startup_status = status
	_startup_message = message
	_last_error = result
	if print_diagnostics:
		print("FoundryObservability: " + message)
	return result


func _startup_skip_message(status: StringName) -> String:
	match status:
		ObservabilityStartupStatus.DISABLED:
			return "Automatic startup is disabled."
		ObservabilityStartupStatus.SKIPPED_EDITOR:
			return "Automatic startup is skipped in the editor."
		ObservabilityStartupStatus.SKIPPED_EDITOR_PLAY:
			return "Automatic startup is skipped for editor play."
		ObservabilityStartupStatus.SKIPPED_DEBUG:
			return "Automatic startup is skipped for debug exports."
		_:
			return "Automatic startup was skipped."
```

Keep `_startup_provider` unchanged in `shutdown()`. The existing method already
resets the active provider and service state without clearing that new field.

Create `test_project/tests/support/startup_order_probe.notest.fs`:

```foundryscript
@autoload
namespace foundry.observability.tests

import foundry.observability

## Records whether observability startup completed before this later autoload.
class_name ObservabilityStartupOrderProbe
extends Node

var observed_status: StringName = ObservabilityStartupStatus.NOT_STARTED


func _init() -> void:
	observed_status = FoundryObservability.startup_status()
```

Create `test_project/tests/support/startup_order_probe.notest.fs.uid`:

```text
uid://b7q2mv5x9k4hc
```

Add the probe after the service in `test_project/project.foundry`:

```ini
[autoload]

FoundryObservability="*res://addons/FoundryObservability/FoundryObservability.fs"
FoundryObservabilityStartupProbe="*res://tests/support/startup_order_probe.notest.fs"
```

- [ ] **Step 7: Run startup lifecycle tests and lint**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
```

Expected: both commands exit 0. Immediate capture reaches the fake bridge,
repeated initialization keeps one owner, changed settings reconfigure it,
shutdown permits restart, and failed replacement preserves availability.

- [ ] **Step 8: Commit synchronous startup**

```bash
git add \
  addons/FoundryObservability/FoundryObservability.fs \
  addons/FoundryObservability/FoundryObservabilityApi.fs \
  test_project/tests/observability-core.test.fs \
  test_project/tests/support/recording_observability_api.notest.fs \
  test_project/tests/support/startup_order_probe.notest.fs \
  test_project/tests/support/startup_order_probe.notest.fs.uid \
  test_project/project.foundry
git commit -m "feat: initialize observability before the main scene"
```

### Task 4: Register and package the project settings

**Files:**

- Modify: `addons/FoundryObservability/export_plugin.fs`
- Modify: `test_project/tests/project-wiring.test.fs`
- Modify: `scripts/test-package`

- [ ] **Step 1: Write failing editor-registration and packaging contracts**

Append this test to `test_project/tests/project-wiring.test.fs`:

```foundryscript
func test_project_registers_observability_startup_settings() -> void:
	var defaults: Dictionary = ObservabilityStartupSettings.project_setting_defaults()
	for setting_name: String in defaults:
		Expect.that(ProjectSettings.has_setting(setting_name)).to_be_true()
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.AUTO_INIT)).to_be_true()
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.ENABLED)).to_be_true()
```

Add these core archive checks after the existing public API resource checks in
`scripts/test-package`:

```bash
grep -qx 'addons/FoundryObservability/ObservabilityStartupStatus.fs' <<<"$listing" \
	|| fail "package is missing startup status constants"
grep -qx 'addons/FoundryObservability/ObservabilityStartupSettings.fs' <<<"$listing" \
	|| fail "package is missing startup settings resolution"
```

Add this source-wiring assertion to
`test_project_contains_startup_status_and_settings_resources`:

```foundryscript
	var plugin_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/export_plugin.fs")
	Expect.that(plugin_source).to_contain(
			"ObservabilityStartupSettings.register_project_settings()")
```

- [ ] **Step 2: Run tests and verify editor registration is absent**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-package
```

Expected: project tests FAIL because `export_plugin.fs` does not register the
settings. Packaging may already include the files because the packager copies
the complete core addon; its new explicit assertions must pass.

- [ ] **Step 3: Register settings from the editor plugin**

Add this method before `_enable_plugin()` in
`addons/FoundryObservability/export_plugin.fs`:

```foundryscript
func _enter_tree() -> void:
	ObservabilityStartupSettings.register_project_settings()
```

The runtime path remains independently safe because
`ObservabilityStartupSettings.from_project_settings()` also registers defaults
before reading them.

- [ ] **Step 4: Run wiring, package, and UID contracts**

Run:

```bash
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-package
```

Expected: every command exits 0. The startup resources and UID companions are
present in the package and the project settings are registered idempotently.

- [ ] **Step 5: Commit project-setting integration**

```bash
git add \
  addons/FoundryObservability/export_plugin.fs \
  test_project/tests/project-wiring.test.fs \
  scripts/test-package
git commit -m "feat: register observability startup settings"
```

### Task 5: Document startup, ordering, and crash recovery

**Files:**

- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `docs/NATIVE_CRASH_VALIDATION.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Write failing documentation contracts**

Add these checks to `scripts/test-foundry-script` before the final lint runs:

```bash
rg -Fq 'foundry_observability/startup/auto_init' "$repo_root/README.md" \
	|| fail "README is missing automatic startup setup"
rg -Fq 'startup_status()' "$repo_root/docs/API.md" \
	|| fail "API docs are missing startup diagnostics"
rg -Fq 'before the main scene' "$repo_root/docs/API.md" \
	|| fail "API docs are missing the startup ordering guarantee"
rg -Fq 'project-settings initialization' \
	"$repo_root/docs/NATIVE_CRASH_VALIDATION.md" \
	|| fail "native crash validation is missing project-settings startup"
```

- [ ] **Step 2: Run the documentation contract and verify it fails**

Run:

```bash
scripts/test-foundry-script
```

Expected: FAIL with `README is missing automatic startup setup`.

- [ ] **Step 3: Update the public documentation**

Add a concise automatic-startup example to `README.md`:

```ini
[foundry_observability]

startup/auto_init=true
startup/enabled=true

[foundry_observability/options]

dsn="https://public@example/1"
environment="production"
release="Oakhaven@1.2.3"
dist="ios"
debug_diagnostics=2
provider_options={
"send_default_pii": false
}
```

State directly after the example:

```text
Project-settings initialization completes while the FoundryObservability
autoload is constructed, before the main scene and before autoloads ordered
after it. Those later startup hooks may capture immediately. Code in an
autoload ordered earlier is outside this guarantee.
```

Add this complete section to `docs/API.md`:

````markdown
## Project-settings startup

`FoundryObservability` automatically reads deployment configuration while its
autoload is constructed. The supported settings are:

| Setting | Default | Meaning |
| --- | --- | --- |
| `foundry_observability/startup/auto_init` | `true` | Run automatic startup. |
| `foundry_observability/startup/enabled` | `true` | Enable capture after valid startup. |
| `foundry_observability/startup/skip_editor_play` | `false` | Skip games launched from an editor build. |
| `foundry_observability/startup/skip_debug_exports` | `false` | Skip remaining debug exports. |
| `foundry_observability/options/dsn` | empty | Sentry DSN. |
| `foundry_observability/options/environment` | empty | Deployment environment override. |
| `foundry_observability/options/release` | empty | Release override. |
| `foundry_observability/options/dist` | empty | Optional distribution. |
| `foundry_observability/options/debug_diagnostics` | `2` (`Auto`) | Off, On, or debug-build-aware diagnostic output. |
| `foundry_observability/options/provider_options` | `{}` | Additional data-only Sentry options. |

DSN resolution uses the project setting, then `SENTRY_DSN`. Release resolution
uses the project setting, `SENTRY_RELEASE`, then
`{app_name}@{app_version}`. Release templates expand the application's
`application/config/name` and `application/config/version`; missing values use
`Unknown Foundry project` and `noversion`. Environment resolution uses the
project setting, `SENTRY_ENVIRONMENT`, then one of `dedicated_server`,
`editor_dev`, `editor_dev_run`, `export_debug`, or `export_release`.

`provider_options` accepts nested dictionaries and arrays containing null,
booleans, integers, finite floats, strings, and string names. Dictionary keys
must be strings or string names and nesting is bounded. The typed DSN and
debug-diagnostics settings overwrite `dsn` and `debug` entries in that
dictionary.

The editor process is always skipped. `skip_editor_play` applies next, followed
by `skip_debug_exports`. Disabled and intentionally skipped startup returns
`Error.OK` while leaving the null provider active.

Startup methods:

```foundryscript
func initialize_from_project_settings() -> int
func startup_status() -> StringName
func startup_message() -> String
```

`initialize_from_project_settings()` rereads current settings and environment
variables. `startup_status()` returns the result of the latest startup attempt:

| Status | Meaning |
| --- | --- |
| `not_started` | No startup attempt has completed. |
| `initialized` | The provider and native bridge accepted configuration. |
| `disabled` | Automatic startup or capture is disabled. |
| `skipped_editor` | Startup ran in the editor process. |
| `skipped_editor_play` | Editor-play startup was explicitly skipped. |
| `skipped_debug` | Debug-export startup was explicitly skipped. |
| `missing_dsn` | No project or environment DSN was available. |
| `provider_unavailable` | The optional provider or native bridge was unavailable. |
| `configuration_failed` | Validation or provider startup failed. |

Missing DSN returns `Error.ERR_UNCONFIGURED`; invalid settings return
`Error.ERR_INVALID_PARAMETER`; missing provider code or bridge returns
`Error.ERR_UNAVAILABLE`; and native startup returns its provider error.
`startup_message()` supplies the corresponding human-readable explanation.
The status and message remain available even when diagnostic printing is off.

Repeated initialization reuses the startup provider. Equivalent native
configuration does not restart the SDK, while changed settings use the
owner-aware replacement lifecycle. A failed replacement preserves the working
provider. Manual `configure()` remains authoritative; a later explicit
`initialize_from_project_settings()` intentionally selects the startup
provider again. Repeated `shutdown()` is safe, and explicit initialization may
restart the retained startup provider afterward.

Project-settings initialization completes during construction of the
`FoundryObservability` autoload, before the main scene and before autoloads
ordered after it. Those later startup hooks may capture immediately. Native
failures before extension loading and code in an earlier autoload are outside
this guarantee.
````

Replace the manual-only startup guidance in
`docs/NATIVE_CRASH_VALIDATION.md` with project-settings initialization as the
preferred procedure using this text:

```markdown
Use project-settings initialization for the validation build unless the test
specifically exercises manual configuration. Set
`foundry_observability/startup/auto_init` and
`foundry_observability/startup/enabled` to `true`, provide
`foundry_observability/options/dsn`, and set explicit release and environment
values for the run. Confirm `FoundryObservability.startup_status()` is
`initialized` before triggering the destructive crash.

Manual `FoundryObservability.configure()` remains supported when validation
needs to construct configuration in code. In either path, the native backend
must be active before the crash. The durable crash created by run A is
discovered and processed when project-settings initialization starts the
native backend during run B.
```

Add this entry under the current unreleased section of `CHANGELOG.md`:

```markdown
- Added project-settings Sentry initialization during
  `FoundryObservability` autoload construction, with deployment defaults,
  runtime skip controls, startup diagnostics, and idempotent reinitialization.
```

- [ ] **Step 4: Verify documentation and formatting**

Run:

```bash
scripts/test-foundry-script
prek run --all-files
```

Expected: both commands exit 0, including the new documentation contracts.

- [ ] **Step 5: Commit public documentation**

```bash
git add \
  README.md \
  docs/API.md \
  docs/NATIVE_CRASH_VALIDATION.md \
  CHANGELOG.md \
  scripts/test-foundry-script
git commit -m "docs: explain early observability startup"
```

### Task 6: Verify, review, publish, and merge

**Files:**

- Verify all files changed in Tasks 1-5.

- [ ] **Step 1: Run the complete validation gate**

Run:

```bash
task test
```

Expected: exit 0 with:

- all Foundry project tests passing;
- all FoundryScript and UID contracts passing;
- Swift tests passing;
- Android debug and release unit tests passing;
- iOS and Android build contracts passing;
- packaging and lint passing.

- [ ] **Step 2: Verify the branch diff and issue coverage**

Run:

```bash
git diff --check origin/main...HEAD
git status --short --branch
git diff --stat origin/main...HEAD
```

Expected: no whitespace errors, no uncommitted tracked changes, and a focused
diff containing the design, plan, startup implementation, tests, contracts, and
documentation.

Check each requirement from issue #9 against a test or documentation section:

- pre-main-scene initialization;
- immediate startup capture;
- previous-run native crash processing;
- typed and opaque deployment configuration;
- safe missing or invalid configuration;
- derived and overridden release/environment;
- editor and development skips;
- idempotent initialization, reconfiguration, shutdown, and restart;
- macOS, iOS, and Android support;
- provider-neutral public lifecycle.

- [ ] **Step 3: Run the supervised adversarial review**

Run:

```bash
python3 ~/.claude/scripts/codex_review/await_review.py start-wait \
  --cwd /Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-9 \
  --scope branch \
  --base origin/main \
  --deadline 540
```

Expected: `verdict.result: clean`. If findings are returned, triage every
finding, add a failing regression test for each in-scope behavior defect, fix
it, rerun `task test`, commit the fix, and start a fresh review against the new
HEAD. File a GitHub issue for each real out-of-scope finding.

- [ ] **Step 4: Push and open the pull request**

Run:

```bash
git push -u origin issue-9
gh pr create \
  --repo cafecito-games/FoundryObservability \
  --base main \
  --head issue-9 \
  --title "Add early initialization and deployment configuration" \
  --body "$(printf '%s\n\n%s\n\n%s' \
    '## Summary' \
    '- initialize the optional Sentry provider from project settings before the main scene
- derive and validate deployment configuration with safe startup diagnostics
- document ordering, reconfiguration, shutdown, and previous-run crash recovery' \
    'Closes #9')"
```

Expected: a new pull request URL targeting `main`.

- [ ] **Step 5: Enable squash auto-merge and monitor completion**

Run:

```bash
gh pr merge --repo cafecito-games/FoundryObservability --squash --auto
gh pr checks --repo cafecito-games/FoundryObservability --watch
```

Expected: required checks pass and GitHub merges the pull request.

- [ ] **Step 6: Clean up only after merge**

Confirm merge:

```bash
gh pr view --repo cafecito-games/FoundryObservability \
  --json state,mergedAt,url
```

Expected: `state` is `MERGED` and `mergedAt` is non-null.

Then run from the main checkout:

```bash
git fetch origin main
git worktree remove \
  /Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-9
git branch -D issue-9
```

Expected: the merged worktree and local issue branch are removed. If auto-merge
is still pending, leave both in place.
