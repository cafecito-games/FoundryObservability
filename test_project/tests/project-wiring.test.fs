namespace foundry.observability.tests

import foundry.testlib

class_name ProjectWiringTests
extends RefCounted
uses Test

func test_project_registers_foundry_observability_autoload() -> void:
	var autoload_path: String = ProjectSettings.get_setting(
			"autoload/FoundryObservability", "")
	Expect.that(
			autoload_path.ends_with(
					"addons/FoundryObservability/FoundryObservability.fs")).to_be_true()

func test_project_enables_foundry_observability_editor_plugin() -> void:
	var enabled_plugins: PackedStringArray = ProjectSettings.get_setting(
			"editor_plugins/enabled", PackedStringArray())
	Expect.that(
			"res://addons/FoundryObservability/plugin.cfg" in enabled_plugins).to_be_true()

func test_project_uses_foundrylib_adapter_from_core_addon() -> void:
	var sink_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs")
	Expect.that(sink_source).to_contain(
			"namespace foundry.observability.foundrylib")
	Expect.that(sink_source).to_contain(
			"class_name FoundryLibObservabilitySink")
	Expect.that(sink_source).to_contain("uses LogSink")
	var packages_source: String = FileAccess.get_file_as_string(
			"res://packages.toml")
	Expect.that(packages_source).to_contain("[packages.foundrylib]")
	Expect.that(packages_source).to_contain(
			"url = \"https://github.com/cafecito-games/FoundryLib.git\"")
	var enabled_plugins: PackedStringArray = ProjectSettings.get_setting(
			"editor_plugins/enabled", PackedStringArray())
	Expect.that(
			"res://addons/FoundryObservability/plugin.cfg" in enabled_plugins).to_be_true()
	Expect.that(
			"res://addons/FoundryObservabilityFoundryLib/plugin.cfg" in enabled_plugins).to_be_false()
	Expect.that(FileAccess.file_exists(
			"res://addons/FoundryObservabilityFoundryLib/plugin.cfg")).to_be_false()

func test_project_uses_foundry_observability_namespace() -> void:
	var core_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservability.fs")
	var api_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservabilityApi.fs")
	var adapter_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs")
	Expect.that(core_source).to_contain("namespace foundry.observability")
	Expect.that(api_source).to_contain("namespace foundry.observability")
	Expect.that(adapter_source).to_contain(
			"namespace foundry.observability.foundrylib")
	Expect.that(core_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)
	Expect.that(api_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)
	Expect.that(adapter_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)

func test_project_enforces_strict_foundry_script_warnings() -> void:
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/inferred_declaration", 0)).to_equal(1)
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/untyped_declaration", 0)).to_equal(2)
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/unsafe_property_access", 0)).to_equal(2)
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/unsafe_method_access", 0)).to_equal(2)
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/unsafe_cast", 0)).to_equal(2)
	Expect.that(ProjectSettings.get_setting(
			"debug/foundry_script/warnings/unsafe_call_argument", 0)).to_equal(2)

func test_project_uses_foundry_script_configuration_without_legacy_warning_keys() -> void:
	var project_source: String = FileAccess.get_file_as_string("res://project.foundry")
	Expect.that(project_source).to_contain("foundry_script/warnings/directory_rules")
	for old_key: String in [
		"debug/gdscript/warnings/enable",
		"debug/gdscript/warnings/exclude_addons",
		"debug/gdscript/warnings/inferred_declaration",
		"debug/gdscript/warnings/untyped_declaration",
		"debug/gdscript/warnings/unsafe_property_access",
		"debug/gdscript/warnings/unsafe_method_access",
		"debug/gdscript/warnings/unsafe_cast",
		"debug/gdscript/warnings/unsafe_call_argument",
	]:
		Expect.that(ProjectSettings.has_setting(old_key)).to_be_false()
		Expect.that(project_source.find(old_key)).to_equal(-1)
