namespace foundry.observability.sentry.tests

import foundry.observability
import foundry.observability.sentry
import foundry.testlib

class_name ObservabilitySentryTests
extends RefCounted
uses Test


class FakeRuntimeContextProbe extends RefCounted:
	var platform: String = "macOS"
	var app_name: String = "Oakhaven"
	var app_version: String = "1.2.3"
	var engine_editor: bool = false
	var engine_debug_build: bool = true
	var engine_headless: bool = false
	var engine_dedicated_server: bool = false
	var device_model: String = "Mac16,1"
	var processor_count: int = 10
	var memory_size: int = 17179869184
	var memory_call_count: int = 0
	var volatile_free_memory: int = 2048
	var volatile_usable_memory: int = 4096
	var volatile_free_storage: int = 8192
	var display_screen_count: int = 1
	var display_width: int = 3024
	var display_height: int = 1964
	var display_dpi: int = 254
	var display_refresh_rate: float = 120.0
	var volatile_orientation: String = "landscape"
	var gpu_name: String = "Apple M4"
	var unique_identifier: String = "private-device-id"
	var locale: String = "en_US"
	var timezone: String = "America/New_York"

	func platform_name() -> String:
		return platform

	func application_values() -> Dictionary:
		return {
				"name": app_name,
				"version": app_version,
				"start_time": "2026-07-25T12:00:00Z",
				"architecture": "arm64",
			}

	func engine_values() -> Dictionary:
		return {
				"version": "4.5.stable",
				"version_commit": "abc123",
				"architecture": "arm64",
				"editor": engine_editor,
				"debug_build": engine_debug_build,
				"headless": engine_headless,
				"dedicated_server": engine_dedicated_server,
			}

	func device_values() -> Dictionary:
		return {
				"model": device_model,
				"processor_name": "Apple M4",
				"processor_count": processor_count,
			}

	func memory_values() -> Dictionary:
		memory_call_count += 1
		return {
				"physical": memory_size,
				"free": volatile_free_memory,
				"available": volatile_usable_memory,
			}

	func free_storage() -> int:
		return volatile_free_storage

	func display_values() -> Dictionary:
		return {
				"server": "macOS",
				"screen_count": display_screen_count,
				"touchscreen_available": false,
				"primary_width_pixels": display_width,
				"primary_height_pixels": display_height,
				"primary_dpi": display_dpi,
				"primary_refresh_rate": display_refresh_rate,
				"primary_orientation": volatile_orientation,
			}

	func primary_orientation() -> String:
		return volatile_orientation

	func gpu_values() -> Dictionary:
		return {
				"name": gpu_name,
				"vendor_name": "Apple",
				"api_version": "Metal 3",
				"device_type": "integrated_gpu",
				"driver_name": "Metal",
				"driver_version": "1",
				"rendering_method": "gl_compatibility",
			}

	func runtime_values() -> Dictionary:
		return {"sandboxed": true, "userfs_persistent": true}

	func privacy_values() -> Dictionary:
		return {
				"unique_identifier": unique_identifier,
				"locale": locale,
				"timezone": timezone,
			}


class CountingBreadcrumblessSentryBridge extends \
		"res://tests/support/breadcrumbless_sentry_bridge.notest.fs":
	var capture_count: int = 0

	func capture(_payload: Dictionary) -> String:
		capture_count += 1
		return "sentry:%s" % capture_count


class MalformedScopeSentryBridge extends \
		"res://tests/support/breadcrumbless_sentry_bridge.notest.fs":
	var apply_scope_result: Variant = true
	var clear_breadcrumbs_result: Variant = true
	var applied_scope_payloads: Array[Dictionary] = []
	var clear_breadcrumbs_count: int = 0

	func applyScope(payload: Dictionary) -> Variant:
		applied_scope_payloads.append(payload.duplicate(true))
		return apply_scope_result

	func clearBreadcrumbs() -> Variant:
		clear_breadcrumbs_count += 1
		return clear_breadcrumbs_result


func test_runtime_context_collector_builds_stable_context_without_pii() -> void:
	var probe := FakeRuntimeContextProbe.new()
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("production", false)
	var device: Dictionary = stable["foundry_device"]

	Expect.that(stable["foundry_app"]["name"]).to_equal("Oakhaven")
	Expect.that(stable["foundry_app"]["version"]).to_equal("1.2.3")
	Expect.that(stable["foundry_engine"]["runtime_mode"]).to_equal("debug_export")
	Expect.that(stable["foundry_device"]["type"]).to_equal("desktop")
	Expect.that(stable["foundry_device"]["memory_size"]).to_equal(17179869184)
	Expect.that(stable["display"]["primary_width_pixels"]).to_equal(3024)
	Expect.that(stable["gpu"]["name"]).to_equal("Apple M4")
	Expect.that(stable["foundry_runtime"]["environment"]).to_equal("production")
	Expect.that(device.has("unique_identifier")).to_be_false()
	Expect.that(device.has("locale")).to_be_false()
	Expect.that(device.has("timezone")).to_be_false()


func test_runtime_context_collector_includes_identifying_values_only_when_opted_in() -> void:
	var probe := FakeRuntimeContextProbe.new()
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("production", true)

	Expect.that(stable["foundry_device"]["unique_identifier"]).to_equal(
			"private-device-id",
		)
	Expect.that(stable["foundry_device"]["locale"]).to_equal("en_US")
	Expect.that(stable["foundry_device"]["timezone"]).to_equal("America/New_York")


func test_runtime_context_collector_skips_memory_api_on_ios() -> void:
	var probe := FakeRuntimeContextProbe.new()
	probe.platform = "iOS"
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("production", true)
	var volatile: Dictionary = collector.volatile_contexts()
	var stable_device: Dictionary = stable["foundry_device"]
	var volatile_device: Dictionary = volatile["foundry_device"]

	Expect.that(probe.memory_call_count).to_equal(0)
	Expect.that(stable_device.has("memory_size")).to_be_false()
	Expect.that(stable_device.has("free_memory")).to_be_false()
	Expect.that(volatile_device.has("free_memory")).to_be_false()


func test_runtime_context_collector_refreshes_volatile_values_without_mutating_stable() -> void:
	var probe := FakeRuntimeContextProbe.new()
	var collector := SentryRuntimeContextCollector.new(probe)
	var stable: Dictionary = collector.stable_contexts("production", false)

	probe.volatile_free_memory = 1024
	probe.volatile_usable_memory = 3072
	probe.volatile_free_storage = 4096
	probe.volatile_orientation = "portrait"
	var merged: Dictionary = collector.contexts_for_capture(stable)

	Expect.that(merged["foundry_device"]["free_memory"]).to_equal(1024)
	Expect.that(merged["foundry_device"]["usable_memory"]).to_equal(3072)
	Expect.that(merged["foundry_device"]["free_storage"]).to_equal(4096)
	Expect.that(merged["display"]["primary_orientation"]).to_equal("portrait")
	Expect.that(stable["foundry_device"]["free_memory"]).to_equal(2048)
	Expect.that(stable["display"]["primary_orientation"]).to_equal("landscape")


func test_runtime_context_collector_classifies_runtime_modes_by_precedence() -> void:
	var probe := FakeRuntimeContextProbe.new()
	var collector := SentryRuntimeContextCollector.new(probe)

	probe.engine_headless = true
	probe.engine_editor = true
	Expect.that(
			collector.stable_contexts("", false)["foundry_engine"]["runtime_mode"],
		).to_equal("headless")

	probe.engine_headless = false
	Expect.that(
			collector.stable_contexts("", false)["foundry_engine"]["runtime_mode"],
		).to_equal("editor")

	probe.engine_editor = false
	Expect.that(
			collector.stable_contexts("", false)["foundry_engine"]["runtime_mode"],
		).to_equal("debug_export")

	probe.engine_debug_build = false
	Expect.that(
			collector.stable_contexts("", false)["foundry_engine"]["runtime_mode"],
		).to_equal("release_export")


func test_runtime_context_collector_omits_invalid_and_unsupported_values() -> void:
	var probe := FakeRuntimeContextProbe.new()
	probe.app_version = ""
	probe.device_model = "GenericDevice"
	probe.processor_count = 0
	probe.memory_size = 0
	probe.volatile_free_memory = -1
	probe.volatile_usable_memory = -1
	probe.volatile_free_storage = -1
	probe.display_screen_count = 0
	probe.display_width = 0
	probe.display_height = -1
	probe.display_dpi = 0
	probe.display_refresh_rate = -1.0
	probe.gpu_name = ""
	probe.unique_identifier = ""
	probe.locale = ""
	probe.timezone = ""
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("", true)
	var app: Dictionary = stable["foundry_app"]
	var device: Dictionary = stable["foundry_device"]
	var display: Dictionary = stable["display"]

	Expect.that(app.has("version")).to_be_false()
	Expect.that(device.has("model")).to_be_false()
	Expect.that(device.has("processor_count")).to_be_false()
	Expect.that(device.has("memory_size")).to_be_false()
	Expect.that(device.has("free_memory")).to_be_false()
	Expect.that(device.has("usable_memory")).to_be_false()
	Expect.that(device.has("free_storage")).to_be_false()
	Expect.that(display.has("screen_count")).to_be_false()
	Expect.that(display.has("primary_width_pixels")).to_be_false()
	Expect.that(stable.has("gpu")).to_be_false()
	Expect.that(device.has("unique_identifier")).to_be_false()


func test_provider_forwards_stable_and_capture_time_runtime_context() -> void:
	var bridge := FakeSentryBridge.new()
	var probe := FakeRuntimeContextProbe.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_probe = probe,
		)
	var config := ObservabilityConfig.new(
			p_environment = "production",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(
			bridge.configured_payload["stable_contexts"]["foundry_app"]["name"],
		).to_equal("Oakhaven")

	probe.volatile_free_memory = 777
	var event := ObservabilityEvent.new(
			p_message = "context capture",
			p_attributes = {"explicit": "preserved"},
		)
	Expect.that(provider.capture(event)).to_equal("sentry:1")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["foundry_device"]["free_memory"],
		).to_equal(777)
	Expect.that(bridge.captured_payloads[0]["attributes"]).to_equal(
			{"explicit": "preserved"},
		)
	provider.shutdown()


func test_provider_failed_reconfigure_preserves_last_stable_runtime_context() -> void:
	var bridge := FakeSentryBridge.new()
	var probe := FakeRuntimeContextProbe.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_probe = probe,
		)
	var initial_config := ObservabilityConfig.new(
			p_environment = "production",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	probe.app_name = "Replacement"
	bridge.configure_result = Error.FAILED
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_environment = "staging",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/2"},
		))).to_equal(Error.FAILED)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "restored context",
		))).to_equal("sentry:1")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["foundry_app"]["name"],
		).to_equal("Oakhaven")
	provider.shutdown()


func test_provider_disabled_configuration_and_shutdown_do_not_capture_stale_context() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_probe = FakeRuntimeContextProbe.new(),
		)
	var enabled_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(enabled_config)).to_equal(Error.OK)
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_enabled = false,
		))).to_equal(Error.OK)
	Expect.that(bridge.configured_payload.has("stable_contexts")).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "disabled",
		))).to_equal("")
	Expect.that(bridge.captured_payloads).to_equal([])

	Expect.that(provider.configure(enabled_config)).to_equal(Error.OK)
	provider.shutdown()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "shutdown",
		))).to_equal("")
	Expect.that(bridge.captured_payloads).to_equal([])


func test_provider_name_is_sentry() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = FakeSentryBridge.new())
	Expect.that(provider.provider_name()).to_equal(&"sentry")


func test_enabled_configuration_requires_compatible_native_bridge_and_dsn() -> void:
	var missing_dsn := SentryObservabilityProvider.new(p_bridge = FakeSentryBridge.new())
	Expect.that(missing_dsn.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
		))).to_equal(Error.FAILED)

	var incompatible_bridge := SentryObservabilityProvider.new(
			p_bridge = IncompatibleSentryBridge.new(),
		)
	Expect.that(incompatible_bridge.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.ERR_UNAVAILABLE)


func test_enabled_configuration_reports_native_bridge_availability() -> void:
	var provider := SentryObservabilityProvider.new()

	var result: int = provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))
	if ClassDB.class_exists("SentryObservabilityBridge") \
			and ClassDB.can_instantiate("SentryObservabilityBridge"):
		Expect.that(result).to_equal(Error.OK)
		Expect.that(provider.is_available()).to_be_true()
		provider.shutdown()
	else:
		Expect.that(result).to_equal(Error.ERR_UNAVAILABLE)


func test_disabled_configuration_is_safe_without_native_bridge() -> void:
	var provider := SentryObservabilityProvider.new()

	Expect.that(provider.configure(ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "ignored"))).to_equal("")


func test_resolves_registered_engine_singleton() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var provider := SentryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "singleton"))).to_equal("sentry:1")

	provider.shutdown()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_forwards_stable_lifecycle_owner_to_bridge_calls() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	var owner: String = bridge.configured_payload["lifecycle_owner"]

	Expect.that(owner.is_empty()).to_be_false()
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.flush(321)).to_equal(Error.OK)
	provider.shutdown()

	Expect.that(bridge.flush_owners).to_equal([owner])
	Expect.that(bridge.shutdown_owners).to_equal([owner])


func test_replaced_sentry_provider_ignores_stale_shutdown() -> void:
	var bridge := FakeSentryBridge.new()
	var first := SentryObservabilityProvider.new(p_bridge = bridge)
	var second := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(first.configure(config)).to_equal(Error.OK)
	var first_owner: String = bridge.active_owner
	Expect.that(second.configure(config)).to_equal(Error.OK)
	var second_owner: String = bridge.active_owner
	Expect.that(second_owner).to_not_equal(first_owner)
	first.shutdown()

	Expect.that(bridge.active_owner).to_equal(second_owner)
	Expect.that(second.is_available()).to_be_true()
	second.shutdown()


func test_failed_reconfigure_preserves_restored_native_session() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
			p_environment = "production",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	bridge.configure_result = Error.FAILED

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_environment = "staging",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/2"},
		))).to_equal(Error.FAILED)

	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "restored session",
		))).to_equal("sentry:1")
	provider.shutdown()


func test_forwards_config_event_and_flush_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_environment = "production",
			p_release = "1.2.3",
			p_dist = "ios",
			p_global_attributes = {"build": 42},
			p_provider_options = {"dsn": "https://public@example/1", "debug": true},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_max_breadcrumbs = 37,
		)
	var exception := ObservabilityException.new(
			p_type_name = "InvalidState",
			p_message = "boom",
			p_stack_trace = "trace",
		)
	var event := ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = "boom",
			p_source = &"game",
			p_timestamp_msec = 1721865600123,
			p_attributes = {"screen": "title"},
			p_exception = exception,
			p_engine_ticks_msec = 4567,
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(event)).to_equal("sentry:1")
	Expect.that(bridge.configured_payload["environment"]).to_equal("production")
	Expect.that(bridge.configured_payload["global_attributes"]).to_equal({"build": 42})
	Expect.that(bridge.configured_payload["max_breadcrumbs"]).to_equal(37)
	Expect.that(bridge.captured_payloads[0]["kind"]).to_equal("exception")
	Expect.that(bridge.captured_payloads[0]["timestamp_msec"]).to_equal(1721865600123)
	Expect.that(bridge.captured_payloads[0]["engine_ticks_msec"]).to_equal(4567)
	Expect.that(bridge.captured_payloads[0]["exception"]["type_name"]).to_equal("InvalidState")
	Expect.that(provider.flush(321)).to_equal(Error.OK)
	Expect.that(bridge.flush_timeouts).to_equal([321])


func test_scope_mutations_forward_complete_candidate_payloads_and_nested_copies() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var context: Dictionary = {
			"round": {
				"waves": [1, {"boss": true}],
			},
		}

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_context("match", context)).to_be_true()
	context["round"]["waves"][1]["boss"] = false
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()

	Expect.that(bridge.applied_scope_payloads).to_equal([
		{
			"tags": {"region": "iad"},
			"contexts": {},
		},
		{
			"tags": {"region": "iad"},
			"contexts": {
				"match": {
					"round": {
						"waves": [1, {"boss": true}],
					},
				},
			},
		},
		{
			"tags": {"region": "iad", "mode": "ranked"},
			"contexts": {
				"match": {
					"round": {
						"waves": [1, {"boss": true}],
					},
				},
			},
		},
	])
	provider.shutdown()


func test_scope_remove_and_clear_operations_forward_complete_candidates() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	Expect.that(provider.set_context("match", {"round": 3})).to_be_true()
	Expect.that(provider.set_context("device", {"class": "desktop"})).to_be_true()
	Expect.that(provider.remove_tag("region")).to_be_true()
	Expect.that(provider.remove_context("match")).to_be_true()
	Expect.that(provider.clear_tags()).to_be_true()
	Expect.that(provider.clear_contexts()).to_be_true()

	Expect.that(bridge.applied_scope_payloads.slice(4)).to_equal([
		{
			"tags": {"mode": "ranked"},
			"contexts": {
				"match": {"round": 3},
				"device": {"class": "desktop"},
			},
		},
		{
			"tags": {"mode": "ranked"},
			"contexts": {"device": {"class": "desktop"}},
		},
		{
			"tags": {},
			"contexts": {"device": {"class": "desktop"}},
		},
		{
			"tags": {},
			"contexts": {},
		},
	])
	provider.shutdown()


func test_scope_user_replacement_and_removal_use_exact_native_mapping() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
			p_display_name = "Player One",
			p_contact_email = "player@example.com",
	))).to_be_true()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-2",
			p_display_name = "",
			p_contact_email = "",
	))).to_be_true()
	Expect.that(provider.remove_user()).to_be_true()

	Expect.that(bridge.applied_scope_payloads).to_equal([
		{
			"tags": {},
			"contexts": {},
			"user": {
				"id": "player-1",
				"display_name": "Player One",
				"contact_email": "player@example.com",
			},
		},
		{
			"tags": {},
			"contexts": {},
			"user": {
				"id": "player-2",
				"display_name": "",
				"contact_email": "",
			},
		},
		{
			"tags": {},
			"contexts": {},
		},
	])
	provider.shutdown()


func test_rejected_scope_candidate_rolls_back_provider_state() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	bridge.apply_scope_result = false
	Expect.that(provider.set_context("rejected", {"value": 1})).to_be_false()
	bridge.apply_scope_result = true
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()

	Expect.that(bridge.applied_scope_payloads[1]).to_equal({
			"tags": {"region": "iad"},
			"contexts": {"rejected": {"value": 1}},
		})
	Expect.that(bridge.applied_scope_payloads[2]).to_equal({
			"tags": {"region": "iad", "mode": "ranked"},
			"contexts": {},
		})
	provider.shutdown()


func test_reconfigure_scope_reset_is_atomic_across_success_and_failure() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_context("match", {"round": 3})).to_be_true()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
	))).to_be_true()

	bridge.configure_result = Error.FAILED
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_environment = "failed",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/2"},
	))).to_equal(Error.FAILED)
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	Expect.that(bridge.applied_scope_payloads.back()).to_equal({
			"tags": {"region": "iad", "mode": "ranked"},
			"contexts": {"match": {"round": 3}},
			"user": {
				"id": "player-1",
				"display_name": "",
				"contact_email": "",
			},
		})

	bridge.configure_result = Error.OK
	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("fresh", "scope")).to_be_true()
	Expect.that(bridge.applied_scope_payloads.back()).to_equal({
			"tags": {"fresh": "scope"},
			"contexts": {},
		})
	provider.shutdown()


func test_same_configuration_success_clears_native_and_local_scope_immediately() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_context("match", {"round": 3})).to_be_true()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
	))).to_be_true()

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})

	Expect.that(provider.set_tag("fresh", "scope")).to_be_true()
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {"fresh": "scope"},
			"contexts": {},
		})
	provider.shutdown()


func test_failed_replacement_reapplies_retained_scope_to_restored_native_session() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)
	var retained_scope: Dictionary = {
			"tags": {"region": "iad"},
			"contexts": {"match": {"round": 3}},
			"user": {
				"id": "player-1",
				"display_name": "",
				"contact_email": "",
			},
		}

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_context("match", {"round": 3})).to_be_true()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
	))).to_be_true()
	Expect.that(bridge.current_scope_payload).to_equal(retained_scope)

	bridge.configure_result = Error.FAILED
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_environment = "replacement",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/2"},
	))).to_equal(Error.FAILED)
	Expect.that(bridge.current_scope_payload).to_equal(retained_scope)

	bridge.configure_result = Error.OK
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({
			"region": "iad",
			"mode": "ranked",
		})
	provider.shutdown()


func test_rejected_configure_scope_reset_fails_and_restores_prior_scope() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)
	var retained_scope: Dictionary = {
			"tags": {"region": "iad"},
			"contexts": {},
		}

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	bridge.apply_scope_results = [false, true]

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_environment = "replacement",
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/2"},
	))).to_equal(Error.FAILED)
	Expect.that(bridge.applied_scope_payloads.slice(-2)).to_equal([
		{
			"tags": {},
			"contexts": {},
		},
		retained_scope,
	])
	Expect.that(bridge.current_scope_payload).to_equal(retained_scope)

	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({
			"region": "iad",
			"mode": "ranked",
		})
	provider.shutdown()


func test_scope_operations_require_enabled_available_native_capability() -> void:
	var disabled_bridge := FakeSentryBridge.new()
	var disabled := SentryObservabilityProvider.new(p_bridge = disabled_bridge)
	Expect.that(disabled.configure(ObservabilityConfig.new(
			p_enabled = false,
	))).to_equal(Error.OK)
	Expect.that(disabled.set_tag("region", "iad")).to_be_false()
	Expect.that(disabled.set_context("match", {"round": 3})).to_be_false()
	Expect.that(disabled.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
	))).to_be_false()
	Expect.that(disabled.remove_tag("region")).to_be_false()
	Expect.that(disabled.remove_context("match")).to_be_false()
	Expect.that(disabled.remove_user()).to_be_false()
	Expect.that(disabled.clear_tags()).to_be_false()
	Expect.that(disabled.clear_contexts()).to_be_false()
	Expect.that(disabled_bridge.applied_scope_payloads).to_equal([])

	var unsupported := SentryObservabilityProvider.new(
			p_bridge = BreadcrumblessSentryBridge.new(),
		)
	Expect.that(unsupported.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	Expect.that(unsupported.set_tag("region", "iad")).to_be_false()
	Expect.that(unsupported.capture(ObservabilityEvent.new(
			p_message = "events remain available",
		))).to_equal("sentry:1")
	unsupported.shutdown()

	var shutdown_bridge := FakeSentryBridge.new()
	var shutdown_provider := SentryObservabilityProvider.new(p_bridge = shutdown_bridge)
	Expect.that(shutdown_provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	shutdown_bridge.applied_scope_payloads.clear()
	Expect.that(shutdown_provider.set_tag("before", "shutdown")).to_be_true()
	shutdown_provider.shutdown()
	shutdown_provider.shutdown()
	Expect.that(shutdown_provider.set_tag("after", "shutdown")).to_be_false()
	Expect.that(shutdown_bridge.applied_scope_payloads).to_have_size(1)


func test_event_local_scope_is_forwarded_once_without_mutating_global_scope() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var local_scope := ObservabilityScope.new()
	Expect.that(local_scope.set_tag("region", "iad")).to_be_true()
	Expect.that(local_scope.set_context("match", {
			"round": 3,
			"players": [{"id": 7}],
		})).to_be_true()

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "scoped",
			p_attributes = {},
			p_scope = local_scope,
	))).to_equal("sentry:1")
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "unscoped",
	))).to_equal("sentry:2")

	Expect.that(bridge.captured_payloads[0]["scope"]).to_equal({
			"tags": {"region": "iad"},
			"contexts": {
				"match": {
					"round": 3,
					"players": [{"id": 7}],
				},
			},
		})
	Expect.that(bridge.captured_payloads[1].has("scope")).to_be_false()
	Expect.that(bridge.applied_scope_payloads).to_equal([])
	provider.shutdown()


func test_event_local_scope_requires_native_scope_capability_before_capture() -> void:
	var bridge := CountingBreadcrumblessSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var local_scope := ObservabilityScope.new()
	Expect.that(local_scope.set_tag("region", "iad")).to_be_true()

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "unsupported scoped event",
			p_attributes = {},
			p_scope = local_scope,
	))).to_equal("")
	Expect.that(bridge.capture_count).to_equal(0)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "supported unscoped event",
	))).to_equal("sentry:1")
	Expect.that(bridge.capture_count).to_equal(1)
	provider.shutdown()


func test_forwards_mobile_diagnostic_config_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_application_hang_detection_enabled = false,
			p_application_hang_timeout_msec = 3200,
			p_android_anr_detection_enabled = false,
			p_android_anr_timeout_msec = 6400,
			p_android_anr_attach_thread_dump = true,
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.configured_payload["application_hang_detection_enabled"]).to_be_false()
	Expect.that(bridge.configured_payload["application_hang_timeout_msec"]).to_equal(3200)
	Expect.that(bridge.configured_payload["android_anr_detection_enabled"]).to_be_false()
	Expect.that(bridge.configured_payload["android_anr_timeout_msec"]).to_equal(6400)
	Expect.that(bridge.configured_payload["android_anr_attach_thread_dump"]).to_be_true()
	provider.shutdown()


func test_normalizes_mutated_mobile_diagnostic_timeouts_at_provider_boundary() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)
	config.application_hang_timeout_msec = 0
	config.android_anr_timeout_msec = -25

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.configured_payload["application_hang_timeout_msec"]).to_equal(1000)
	Expect.that(bridge.configured_payload["android_anr_timeout_msec"]).to_equal(1000)
	provider.shutdown()


func test_service_forwards_normalized_structured_exception_frames_to_native_bridge() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_function = "attack",
			p_line = 42,
			p_language = "foundryscript",
			p_in_app = true,
			p_context_line = "deal_damage()",
			p_pre_context = PackedStringArray(["before"]),
			p_post_context = PackedStringArray(["after"]),
			p_variables = {"damage": 10},
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_stack_trace_variables_enabled = true,
		))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("sentry:1")

	var exception_payload: Dictionary = bridge.captured_payloads[0]["exception"]
	Expect.that(exception_payload["stack_trace"]).to_equal("formatted fallback")
	Expect.that(exception_payload.has("frames")).to_be_true()
	var frames: Array = exception_payload["frames"]
	Expect.that(frames).to_have_size(1)
	var captured_frame: Dictionary = frames[0]
	Expect.that(captured_frame).to_equal({
			"file": "res://player.fs",
			"function": "attack",
			"line": 42,
			"language": "foundryscript",
			"in_app": true,
			"context_line": "deal_damage()",
			"pre_context": ["before"],
			"post_context": ["after"],
			"variables": {"damage": 10},
		})
	Expect.that(captured_frame["pre_context"] is Array).to_be_true()
	Expect.that(captured_frame["post_context"] is Array).to_be_true()
	service.shutdown()


func test_service_preserves_legacy_exception_bridge_payload_without_frames() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
	))).to_equal("sentry:1")

	var exception_payload: Dictionary = bridge.captured_payloads[0]["exception"]
	Expect.that(exception_payload["stack_trace"]).to_equal("formatted fallback")
	Expect.that(exception_payload.has("frames")).to_be_false()
	service.shutdown()


func test_direct_provider_skips_null_exception_frames() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var frames: Array[ObservabilityStackFrame] = []
	frames.append(null)
	frames.append(null)
	var exception := ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = frames,
		)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"exception",
			p_message = "attack failed",
			p_attributes = {},
			p_exception = exception,
		))).to_equal("sentry:1")

	var exception_payload: Dictionary = bridge.captured_payloads[0]["exception"]
	Expect.that(exception_payload["stack_trace"]).to_equal("formatted fallback")
	Expect.that(exception_payload.has("frames")).to_be_false()
	provider.shutdown()


func test_service_omits_empty_structured_frame_context_from_native_bridge() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var frame := ObservabilityStackFrame.new(
			p_file = "res://empty.fs",
			p_function = "idle",
			p_line = 7,
			p_language = "foundryscript",
			p_in_app = false,
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("sentry:1")

	var frames: Array = bridge.captured_payloads[0]["exception"]["frames"]
	Expect.that(frames[0]).to_equal({
			"file": "res://empty.fs",
			"function": "idle",
			"line": 7,
			"language": "foundryscript",
			"in_app": false,
		})
	service.shutdown()


func test_routes_log_events_to_native_structured_log_method() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {"build": 42},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_logs_enabled = true,
			p_log_minimum_level = ObservabilityLevel.TRACE,
		)
	var event := ObservabilityEvent.new(
			p_kind = &"log",
			p_level = ObservabilityLevel.WARN,
			p_message = "missed",
			p_source = &"foundry.logging",
			p_timestamp_msec = 1721865600123,
			p_attributes = {"logger_name": "combat", "id": 7},
			p_exception = null,
			p_engine_ticks_msec = 4567,
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture(event)).to_equal("sentry-log:1")
	Expect.that(bridge.captured_log_payloads[0]["kind"]).to_equal("log")
	Expect.that(bridge.captured_log_payloads[0]["timestamp_msec"]).to_equal(1721865600123)
	Expect.that(bridge.captured_log_payloads[0]["engine_ticks_msec"]).to_equal(4567)
	Expect.that(bridge.configured_payload["logs_enabled"]).to_be_true()
	provider.shutdown()


func test_forwards_normalized_breadcrumbs_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "entered arena",
			p_level = ObservabilityLevel.INFO,
			p_category = &"navigation",
			p_timestamp_msec = 1234,
			p_attributes = {"scene": "arena"},
			p_type = &"navigation",
		))).to_be_true()
	Expect.that(bridge.captured_breadcrumb_payloads).to_equal([{
			"message": "entered arena",
			"level": ObservabilityLevel.INFO,
			"category": "navigation",
			"timestamp_msec": 1234,
			"attributes": {"scene": "arena"},
			"type": "navigation",
		}])
	provider.shutdown()


func test_clear_breadcrumbs_returns_explicit_native_result_and_respects_lifecycle() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(0)
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	Expect.that(provider.clear_breadcrumbs()).to_be_true()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(1)
	bridge.clear_breadcrumbs_result = false
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	provider.shutdown()
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)


func test_non_boolean_native_scope_and_clear_results_are_rejected() -> void:
	var bridge := MalformedScopeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
	))).to_equal(Error.OK)
	bridge.applied_scope_payloads.clear()

	bridge.apply_scope_result = "true"
	Expect.that(provider.set_tag("rejected", "value")).to_be_false()
	bridge.apply_scope_result = true
	Expect.that(provider.set_tag("accepted", "value")).to_be_true()
	Expect.that(bridge.applied_scope_payloads.back()).to_equal({
			"tags": {"accepted": "value"},
			"contexts": {},
		})

	bridge.clear_breadcrumbs_result = "true"
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(1)
	bridge.clear_breadcrumbs_result = true
	Expect.that(provider.clear_breadcrumbs()).to_be_true()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	provider.shutdown()


func test_missing_native_breadcrumb_capability_preserves_event_capture() -> void:
	var provider := SentryObservabilityProvider.new(
			p_bridge = BreadcrumblessSentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)

	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "unsupported breadcrumb",
		))).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "ordinary event remains supported",
		))).to_equal("sentry:1")
	provider.shutdown()


func test_captures_feedback_with_only_explicit_optional_fields() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {
				"dsn": "https://public@example/1",
				"send_default_pii": true,
			},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture_feedback(ObservabilityFeedback.new(
			p_message = "Anonymous feedback",
		))).to_equal("sentry-feedback:1")
	var anonymous_payload: Dictionary = bridge.captured_feedback_payloads[0]
	Expect.that(anonymous_payload["message"]).to_equal("Anonymous feedback")
	Expect.that(anonymous_payload.has("name")).to_be_false()
	Expect.that(anonymous_payload.has("contact_email")).to_be_false()
	Expect.that(anonymous_payload.has("associated_event_id")).to_be_false()
	Expect.that(bridge.configured_payload["provider_options"]["send_default_pii"]).to_be_true()

	Expect.that(provider.capture_feedback(ObservabilityFeedback.new(
			p_message = "Identified feedback",
			p_name = "Player One",
			p_contact_email = "player@example.com",
			p_associated_event_id = "event-123",
		))).to_equal("sentry-feedback:2")
	var identified_payload: Dictionary = bridge.captured_feedback_payloads[1]
	Expect.that(identified_payload).to_equal({
			"message": "Identified feedback",
			"name": "Player One",
			"contact_email": "player@example.com",
			"associated_event_id": "event-123",
		})
	provider.shutdown()


func test_forwards_normalized_custom_metrics_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_metrics_enabled = true,
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE,
			p_name = "players.active",
			p_value = 7.0,
			p_unit = "player",
			p_attributes = {"region": "iad"},
		))).to_be_true()
	Expect.that(bridge.configured_payload["metrics_enabled"]).to_be_true()
	Expect.that(bridge.captured_metric_payloads[0]).to_equal({
			"type": ObservabilityMetricType.GAUGE,
			"name": "players.active",
			"value": 7.0,
			"unit": "player",
			"attributes": {"region": "iad"},
		})
	provider.shutdown()


func test_missing_native_metric_capability_preserves_event_capture() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = MetriclessSentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_metrics_enabled = true,
		))).to_equal(Error.OK)

	Expect.that(provider.capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.COUNTER,
			p_name = "unsupported.metric",
			p_value = 1.0,
		))).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "ordinary event remains supported",
		))).to_equal("sentry:1")
	provider.shutdown()


func test_rejects_bridge_without_feedback_method() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = FeedbacklessSentryBridge.new())

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "ordinary event remains supported",
		))).to_equal("sentry:1")
	Expect.that(provider.capture_feedback(ObservabilityFeedback.new(
			p_message = "unsupported",
		))).to_equal("")
	provider.shutdown()


func test_rejects_bridge_without_structured_log_method() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = EventOnlySentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
			p_logs_enabled = true,
		))).to_equal(Error.FAILED)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"log",
			p_message = "unsupported",
		))).to_equal("")
	provider.shutdown()


func test_service_reports_structured_log_bridge_mismatch() -> void:
	var service: FoundryObservability = _service()
	var provider := SentryObservabilityProvider.new(p_bridge = EventOnlySentryBridge.new())

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.FAILED)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.capture_log("unsupported")).to_equal("")
	provider.shutdown()
	service.shutdown()


func test_shutdown_is_idempotent() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	provider.shutdown()
	provider.shutdown()

	Expect.that(bridge.shutdown_count).to_equal(1)


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability
