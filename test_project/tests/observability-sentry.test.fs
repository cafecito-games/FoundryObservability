namespace foundry.observability.sentry.tests

import foundry.observability
import foundry.observability.sentry
import foundry.testlib

class_name ObservabilitySentryTests
extends RefCounted
uses Test


class FakeRuntimeContextSource extends RefCounted:
	uses SentryRuntimeContextSource

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
	var privacy_call_count: int = 0
	var unique_identifier: String = "private-device-id"
	var locale: String = "en_US"
	var timezone: String = "America/New_York"

	func stable_snapshot() -> SentryRuntimeSnapshot:
		return _snapshot(false)

	func volatile_snapshot() -> SentryRuntimeSnapshot:
		memory_call_count += 1
		return _snapshot(true)

	func privacy_snapshot() -> SentryRuntimeSnapshot.Privacy:
		privacy_call_count += 1
		return SentryRuntimeSnapshot.Privacy.new(
				unique_identifier,
				locale,
				timezone,
			)

	func _snapshot(volatile: bool) -> SentryRuntimeSnapshot:
		var physical_memory: int = memory_size if platform != "iOS" else -1
		var free_memory: int = volatile_free_memory if platform != "iOS" else -1
		var usable_memory: int = volatile_usable_memory if platform != "iOS" else -1
		if volatile:
			return SentryRuntimeSnapshot.new(
					platform,
					SentryRuntimeSnapshot.Application.new(),
					SentryRuntimeSnapshot.EngineRuntime.new(),
					SentryRuntimeSnapshot.Device.new(
						p_free_memory = free_memory,
						p_usable_memory = usable_memory,
					),
					SentryRuntimeSnapshot.Display.new(
						p_primary_orientation = volatile_orientation,
					),
					SentryRuntimeSnapshot.Gpu.new(),
					SentryRuntimeSnapshot.Runtime.new(),
					SentryRuntimeSnapshot.Privacy.new(),
					volatile_free_storage,
				)
		return SentryRuntimeSnapshot.new(
				platform,
				SentryRuntimeSnapshot.Application.new(
					app_name,
					app_version,
					"2026-07-25T12:00:00Z",
					"arm64",
				),
				SentryRuntimeSnapshot.EngineRuntime.new(
					"4.5.stable",
					"abc123",
					"arm64",
					engine_editor,
					engine_debug_build,
					engine_headless,
					engine_dedicated_server,
				),
				SentryRuntimeSnapshot.Device.new(
					device_model,
					"Apple M4",
					processor_count,
					physical_memory,
					free_memory,
					usable_memory,
				),
				SentryRuntimeSnapshot.Display.new(
					"macOS",
					display_screen_count,
					false,
					display_width,
					display_height,
					display_dpi,
					display_refresh_rate,
					volatile_orientation,
				),
				SentryRuntimeSnapshot.Gpu.new(
					gpu_name,
					"Apple",
					"Metal 3",
					"integrated_gpu",
					"Metal",
					"1",
					"gl_compatibility",
				),
				SentryRuntimeSnapshot.Runtime.new(true, true),
				SentryRuntimeSnapshot.Privacy.new(),
				volatile_free_storage,
			)


class RawMalformedSentryNativeBridge extends RefCounted:
	var lifecycle_result: Variant = 1
	var configure_result: Variant = Error.OK
	var availability_result: Variant = true
	var capture_result: Variant = "sentry:1"
	var log_result: Variant = "sentry-log:1"
	var scope_result: Variant = true
	var breadcrumb_result: Variant = true
	var clear_result: Variant = true
	var feedback_result: Variant = "sentry-feedback:1"
	var metric_result: Variant = true
	var replace_result: Variant = true
	var attachment_capture_result: Variant = "sentry:1"
	var flush_result: Variant = Error.OK
	var configure_mutates_session: bool = false
	var clear_mutates_trail: bool = false
	var active_owner: String = ""
	var current_breadcrumb_payloads: Array[Dictionary] = []
	var shutdown_count: int = 0

	func lifecycleVersion() -> Variant:
		return lifecycle_result

	func configure(payload: Dictionary) -> Variant:
		if (configure_result is int and configure_result == Error.OK) \
				or configure_mutates_session:
			active_owner = str(payload.get("lifecycle_owner", ""))
		return configure_result

	func isAvailable(owner: String) -> Variant:
		if owner != active_owner:
			return false
		return availability_result

	func capture(_payload: Dictionary) -> Variant:
		return capture_result

	func captureLog(_payload: Dictionary) -> Variant:
		return log_result

	func applyScope(_payload: Dictionary) -> Variant:
		return scope_result

	func captureBreadcrumb(payload: Dictionary) -> Variant:
		current_breadcrumb_payloads.append(payload.duplicate(true))
		return breadcrumb_result

	func clearBreadcrumbs() -> Variant:
		if (clear_result is bool and clear_result == true) or clear_mutates_trail:
			current_breadcrumb_payloads = []
		return clear_result

	func captureFeedback(_payload: Dictionary) -> Variant:
		return feedback_result

	func captureMetric(_payload: Dictionary) -> Variant:
		return metric_result

	func replaceAttachments(_payloads: Array) -> Variant:
		return replace_result

	func captureWithAttachments(_payload: Dictionary) -> Variant:
		return attachment_capture_result

	func flush(_owner: String, _timeout_msec: int) -> Variant:
		return flush_result

	func shutdown(_owner: String) -> void:
		active_owner = ""
		current_breadcrumb_payloads = []
		shutdown_count += 1


class RawPartialSentryNativeBridge extends RefCounted:
	func applyScope(_payload: Dictionary) -> bool:
		return true

	func captureBreadcrumb(_payload: Dictionary) -> bool:
		return true

	func captureFeedback(_payload: Dictionary) -> String:
		return "feedback"

	func captureMetric(_payload: Dictionary) -> bool:
		return true

	func replaceAttachments(_payloads: Array) -> bool:
		return true


class RawIncompleteCoreSentryNativeBridge extends RefCounted:
	var configure_count: int = 0

	func lifecycleVersion() -> int:
		return 1

	func configure(_payload: Dictionary) -> int:
		configure_count += 1
		return Error.OK

	func isAvailable(_owner: String) -> bool:
		return true


class RawCoreOnlySentryNativeBridge extends RefCounted:
	var active_owner: String = ""
	var configure_count: int = 0
	var capture_count: int = 0

	func lifecycleVersion() -> int:
		return 1

	func configure(payload: Dictionary) -> int:
		configure_count += 1
		active_owner = str(payload.get("lifecycle_owner", ""))
		return Error.OK

	func isAvailable(owner: String) -> bool:
		return owner == active_owner

	func capture(_payload: Dictionary) -> String:
		capture_count += 1
		return "sentry:%s" % capture_count

	func flush(_owner: String, _timeout_msec: int) -> int:
		return Error.OK

	func shutdown(_owner: String) -> void:
		active_owner = ""


class FakeAttachmentSource extends RefCounted:
	uses SentryAttachmentSource

	var main_thread: bool = true
	var headless: bool = false
	var frame: int = 1
	var screenshot: PackedByteArray = PackedByteArray([1, 2, 3])
	var screenshot_calls: int = 0
	var tree: Node? = null
	var game_log: String = ""

	func is_main_thread() -> bool:
		return main_thread

	func is_headless() -> bool:
		return headless

	func frames_drawn() -> int:
		return frame

	func scene_root() -> Node?:
		return tree

	func screenshot_png() -> PackedByteArray:
		screenshot_calls += 1
		return screenshot.duplicate()

	func game_log_path() -> String:
		return game_log


func test_sentry_provider_accepts_typed_bridge() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example.invalid/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"message",
			p_message = "typed bridge",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	provider.shutdown()


func test_dynamic_bridge_adapter_rejects_missing_required_contract() -> void:
	var adapter := DynamicSentryNativeBridgeAdapter.new(RefCounted.new())

	Expect.that(adapter.supports_core()).to_be_false()
	Expect.that(adapter.contract_valid()).to_be_true()
	Expect.that(adapter.lifecycle_version()).to_equal(-1)
	Expect.that(adapter.contract_valid()).to_be_false()
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(adapter.is_available("owner")).to_be_false()


func test_dynamic_bridge_adapter_rejects_every_malformed_return_family() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	native.lifecycle_result = true
	Expect.that(adapter.lifecycle_version()).to_equal(-1)

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.configure_result = false
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.availability_result = 1
	Expect.that(adapter.is_available("owner")).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.capture_result = &"event"
	Expect.that(adapter.capture({})).to_equal("")

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.log_result = 1
	Expect.that(adapter.capture_log({})).to_equal("")

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.scope_result = "true"
	Expect.that(adapter.apply_scope({})).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.breadcrumb_result = 1
	Expect.that(adapter.capture_breadcrumb({})).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.clear_result = 1
	Expect.that(adapter.clear_breadcrumbs()).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.feedback_result = &"feedback"
	Expect.that(adapter.capture_feedback({})).to_equal("")

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.metric_result = 1
	Expect.that(adapter.capture_metric({})).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.replace_result = 1
	Expect.that(adapter.replace_attachments([])).to_be_false()

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.attachment_capture_result = &"attachment"
	Expect.that(adapter.capture_with_attachments({})).to_equal("")

	native = RawMalformedSentryNativeBridge.new()
	adapter = DynamicSentryNativeBridgeAdapter.new(native)
	native.flush_result = false
	Expect.that(adapter.flush("owner", 50)).to_equal(Error.ERR_UNAVAILABLE)
	adapter.shutdown("owner")
	Expect.that(native.shutdown_count).to_equal(1)


func test_dynamic_bridge_adapter_requires_complete_optional_families() -> void:
	var adapter := DynamicSentryNativeBridgeAdapter.new(
			RawPartialSentryNativeBridge.new(),
		)

	Expect.that(adapter.supports_scope()).to_be_true()
	Expect.that(adapter.supports_breadcrumbs()).to_be_false()
	Expect.that(adapter.supports_feedback()).to_be_true()
	Expect.that(adapter.supports_metrics()).to_be_true()
	Expect.that(adapter.supports_attachments()).to_be_false()
	Expect.that(adapter.supports_logs()).to_be_false()
	Expect.that(adapter.contract_valid()).to_be_true()


func test_mutating_malformed_configure_fails_closed_through_dynamic_adapter() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	native.configure_result = "activated"
	native.configure_mutates_session = true
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	var provider := SentryObservabilityProvider.new(p_bridge = adapter)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example.invalid/1"},
			))).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(adapter.contract_valid()).to_be_false()
	Expect.that(native.shutdown_count).to_equal(1)
	Expect.that(native.active_owner).to_equal("")
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "stale session",
		))).to_equal("")


func test_mutating_malformed_clear_fails_closed_through_dynamic_adapter() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	var provider := SentryObservabilityProvider.new(p_bridge = adapter)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example.invalid/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "will be cleared",
		))).to_be_true()
	Expect.that(native.current_breadcrumb_payloads).to_have_size(1)
	native.clear_result = "cleared"
	native.clear_mutates_trail = true

	Expect.that(provider.configure(config)).to_equal(Error.FAILED)
	Expect.that(adapter.contract_valid()).to_be_false()
	Expect.that(native.shutdown_count).to_equal(1)
	Expect.that(native.active_owner).to_equal("")
	Expect.that(native.current_breadcrumb_payloads).to_have_size(0)
	Expect.that(provider.is_available()).to_be_false()


func test_missing_required_core_contract_is_rejected_before_configure() -> void:
	var native := RawIncompleteCoreSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	var provider := SentryObservabilityProvider.new(p_bridge = adapter)

	Expect.that(adapter.supports_core()).to_be_false()
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example.invalid/1"},
			))).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(native.configure_count).to_equal(0)
	Expect.that(adapter.contract_valid()).to_be_true()


func test_missing_log_contract_is_rejected_only_when_logs_are_enabled() -> void:
	var enabled_native := RawCoreOnlySentryNativeBridge.new()
	var enabled_adapter := DynamicSentryNativeBridgeAdapter.new(enabled_native)
	var enabled_provider := SentryObservabilityProvider.new(p_bridge = enabled_adapter)

	Expect.that(enabled_adapter.supports_core()).to_be_true()
	Expect.that(enabled_adapter.supports_logs()).to_be_false()
	Expect.that(enabled_provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example.invalid/1"},
			))).to_equal(Error.FAILED)
	Expect.that(enabled_native.configure_count).to_equal(0)

	var disabled_native := RawCoreOnlySentryNativeBridge.new()
	var disabled_adapter := DynamicSentryNativeBridgeAdapter.new(disabled_native)
	var disabled_provider := SentryObservabilityProvider.new(p_bridge = disabled_adapter)
	Expect.that(disabled_provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example.invalid/1"},
				p_processing = ObservabilityProcessingConfig.new(
					p_logs_enabled = false,
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
				),
			))).to_equal(Error.OK)
	Expect.that(disabled_native.configure_count).to_equal(1)
	Expect.that(disabled_provider.capture(ObservabilityEvent.new(
			p_message = "core event",
		))).to_equal("sentry:1")
	disabled_provider.shutdown()


func test_ordinary_capability_rejections_preserve_an_active_session() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example.invalid/1"},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	var active_owner: String = bridge.active_owner
	Expect.that(bridge.configured_payloads).to_have_size(1)

	bridge.core_supported = false
	Expect.that(provider.configure(config)).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(bridge.configured_payloads).to_have_size(1)
	Expect.that(bridge.active_owner).to_equal(active_owner)
	bridge.core_supported = true
	Expect.that(provider.is_available()).to_be_true()

	bridge.logs_supported = false
	Expect.that(provider.configure(config)).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payloads).to_have_size(1)
	Expect.that(bridge.active_owner).to_equal(active_owner)
	bridge.logs_supported = true
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "retained active session",
		))).to_equal("sentry:1")
	provider.shutdown()


func test_sentry_attachment_collection_isolates_inputs_and_accessor_outputs() -> void:
	var payload_bytes: PackedByteArray = PackedByteArray([1, 2, 3])
	var payload: Dictionary = {
		"bytes": payload_bytes,
		"metadata": {"labels": ["diagnostic"]},
	}
	var failure := ObservabilityAttachmentFailure.new(
			"built-in:screenshot",
			"screenshot.png",
			ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
			Error.ERR_UNAVAILABLE,
		)
	var attachments: Array[Dictionary] = [payload]
	var failures: Array[ObservabilityAttachmentFailure] = [failure]
	var collection := SentryAttachmentCollection.new(attachments, failures)

	payload_bytes[0] = 9
	payload["metadata"]["labels"][0] = "mutated-input"
	attachments.clear()
	failures.clear()
	var first_attachments: Array[Dictionary] = collection.attachments()
	var first_failures: Array[ObservabilityAttachmentFailure] = collection.failures()
	Expect.that(first_attachments[0]["bytes"]).to_equal(PackedByteArray([1, 2, 3]))
	Expect.that(first_attachments[0]["metadata"]["labels"]).to_equal(["diagnostic"])
	Expect.that(first_failures).to_have_size(1)
	Expect.that(first_failures[0]).to_not_equal(failure)
	var first_failure: ObservabilityAttachmentFailure = first_failures[0]

	var exposed_bytes: PackedByteArray = first_attachments[0]["bytes"]
	exposed_bytes[1] = 8
	first_attachments[0]["metadata"]["labels"][0] = "mutated-output"
	first_attachments.clear()
	first_failures.clear()
	var second_attachments: Array[Dictionary] = collection.attachments()
	var second_failures: Array[ObservabilityAttachmentFailure] = collection.failures()
	Expect.that(second_attachments[0]["bytes"]).to_equal(PackedByteArray([1, 2, 3]))
	Expect.that(second_attachments[0]["metadata"]["labels"]).to_equal(["diagnostic"])
	Expect.that(second_failures).to_have_size(1)
	Expect.that(second_failures[0]).to_not_equal(failure)
	Expect.that(second_failures[0]).to_not_equal(first_failure)


func test_sentry_runtime_snapshot_defaults_construct_fresh_empty_components() -> void:
	var first := SentryRuntimeSnapshot.new()
	var second := SentryRuntimeSnapshot.new()

	Expect.that(first.application).to_not_equal(second.application)
	Expect.that(first.engine).to_not_equal(second.engine)
	Expect.that(first.device).to_not_equal(second.device)
	Expect.that(first.display).to_not_equal(second.display)
	Expect.that(first.gpu).to_not_equal(second.gpu)
	Expect.that(first.runtime).to_not_equal(second.runtime)
	Expect.that(first.privacy).to_not_equal(second.privacy)
	Expect.that(first.platform_name).to_equal("")
	Expect.that(first.device.physical_memory).to_equal(-1)
	Expect.that(first.device.free_memory).to_equal(-1)
	Expect.that(first.device.usable_memory).to_equal(-1)
	Expect.that(first.free_storage).to_equal(-1)


func test_sentry_runtime_snapshot_positional_constructors_map_every_field() -> void:
	var application := SentryRuntimeSnapshot.Application.new(
			"app-name",
			"app-version",
			"app-start",
			"app-architecture",
		)
	var engine := SentryRuntimeSnapshot.EngineRuntime.new(
			"engine-version",
			"engine-commit",
			"engine-architecture",
			true,
			false,
			true,
			false,
		)
	var device := SentryRuntimeSnapshot.Device.new(
			"device-model",
			"processor-name",
			31,
			3201,
			3202,
			3203,
		)
	var display := SentryRuntimeSnapshot.Display.new(
			"display-server",
			41,
			true,
			4201,
			4202,
			4203,
			42.04,
			"display-orientation",
		)
	var gpu := SentryRuntimeSnapshot.Gpu.new(
			"gpu-name",
			"gpu-vendor",
			"gpu-api",
			"gpu-device-type",
			"gpu-driver",
			"gpu-driver-version",
			"gpu-rendering-method",
		)
	var runtime := SentryRuntimeSnapshot.Runtime.new(true, false)
	var privacy := SentryRuntimeSnapshot.Privacy.new(
			"privacy-id",
			"privacy-locale",
			"privacy-timezone",
		)
	var snapshot := SentryRuntimeSnapshot.new(
			"platform-name",
			application,
			engine,
			device,
			display,
			gpu,
			runtime,
			privacy,
			8101,
		)

	Expect.that([
		snapshot.platform_name,
		snapshot.application.name,
		snapshot.application.version,
		snapshot.application.start_time,
		snapshot.application.architecture,
		snapshot.engine.version,
		snapshot.engine.version_commit,
		snapshot.engine.architecture,
		snapshot.engine.editor,
		snapshot.engine.debug_build,
		snapshot.engine.headless,
		snapshot.engine.dedicated_server,
		snapshot.device.model,
		snapshot.device.processor_name,
		snapshot.device.processor_count,
		snapshot.device.physical_memory,
		snapshot.device.free_memory,
		snapshot.device.usable_memory,
		snapshot.display.server,
		snapshot.display.screen_count,
		snapshot.display.touchscreen_available,
		snapshot.display.primary_width_pixels,
		snapshot.display.primary_height_pixels,
		snapshot.display.primary_dpi,
		snapshot.display.primary_refresh_rate,
		snapshot.display.primary_orientation,
		snapshot.gpu.name,
		snapshot.gpu.vendor_name,
		snapshot.gpu.api_version,
		snapshot.gpu.device_type,
		snapshot.gpu.driver_name,
		snapshot.gpu.driver_version,
		snapshot.gpu.rendering_method,
		snapshot.runtime.sandboxed,
		snapshot.runtime.userfs_persistent,
		snapshot.privacy.unique_identifier,
		snapshot.privacy.locale,
		snapshot.privacy.timezone,
		snapshot.free_storage,
	]).to_equal([
		"platform-name",
		"app-name",
		"app-version",
		"app-start",
		"app-architecture",
		"engine-version",
		"engine-commit",
		"engine-architecture",
		true,
		false,
		true,
		false,
		"device-model",
		"processor-name",
		31,
		3201,
		3202,
		3203,
		"display-server",
		41,
		true,
		4201,
		4202,
		4203,
		42.04,
		"display-orientation",
		"gpu-name",
		"gpu-vendor",
		"gpu-api",
		"gpu-device-type",
		"gpu-driver",
		"gpu-driver-version",
		"gpu-rendering-method",
		true,
		false,
		"privacy-id",
		"privacy-locale",
		"privacy-timezone",
		8101,
	])


func test_runtime_context_collector_builds_stable_context_without_pii() -> void:
	var probe := FakeRuntimeContextSource.new()
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
	Expect.that(probe.privacy_call_count).to_equal(0)


func test_runtime_context_collector_includes_identifying_values_only_when_opted_in() -> void:
	var probe := FakeRuntimeContextSource.new()
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("production", true)

	Expect.that(stable["foundry_device"]["unique_identifier"]).to_equal(
			"private-device-id",
		)
	Expect.that(stable["foundry_device"]["locale"]).to_equal("en_US")
	Expect.that(stable["foundry_device"]["timezone"]).to_equal("America/New_York")
	Expect.that(probe.privacy_call_count).to_equal(1)


func test_runtime_context_collector_omits_memory_values_on_ios() -> void:
	var probe := FakeRuntimeContextSource.new()
	probe.platform = "iOS"
	var collector := SentryRuntimeContextCollector.new(probe)

	var stable: Dictionary = collector.stable_contexts("production", true)
	var volatile: Dictionary = collector.volatile_contexts()
	var stable_device: Dictionary = stable["foundry_device"]
	var volatile_device: Dictionary = volatile["foundry_device"]

	Expect.that(probe.memory_call_count).to_equal(1)
	Expect.that(stable_device.has("memory_size")).to_be_false()
	Expect.that(stable_device.has("free_memory")).to_be_false()
	Expect.that(volatile_device.has("free_memory")).to_be_false()


func test_runtime_context_collector_refreshes_volatile_values_without_mutating_stable() -> void:
	var probe := FakeRuntimeContextSource.new()
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
	var probe := FakeRuntimeContextSource.new()
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
	var probe := FakeRuntimeContextSource.new()
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
	var bridge := FakeSentryNativeBridge.new()
	var probe := FakeRuntimeContextSource.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = probe,
		)
	var config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"send_default_pii": true,
		},
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(
			bridge.configured_payload["stable_contexts"]["foundry_app"]["name"],
		).to_equal("Oakhaven")
	Expect.that(probe.privacy_call_count).to_equal(1)

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
	Expect.that(probe.privacy_call_count).to_equal(1)
	provider.shutdown()


func test_provider_redacts_stable_and_volatile_runtime_contexts_across_sessions() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var probe := FakeRuntimeContextSource.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = probe,
		)
	var original_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["contexts", "foundry_app", "name"]),
				"Oakhaven",
				"safe-app",
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["contexts", "display", "primary_orientation"]),
				"secret",
				"safe",
			),
	])
	var original_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_processing = ObservabilityProcessingConfig.new(
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = original_policy,
		),
	)
	Expect.that(provider.configure(original_config)).to_equal(Error.OK)
	Expect.that(
			bridge.configured_payload["stable_contexts"]["foundry_app"]["name"],
		).to_equal("safe-app")

	probe.volatile_orientation = "secret-old"
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "original session",
		))).to_equal("sentry:1")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["display"]["primary_orientation"],
		).to_equal("safe-old")

	probe.volatile_orientation = "landscape"
	bridge.configure_result = Error.FAILED
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "production",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["contexts", "foundry_app", "name"]),
								"Oakhaven",
								"safe-app",
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(
									["contexts", "display", "primary_orientation"],
								),
								"secret",
								"candidate",
							),
						],
					),
				),
			))).to_equal(Error.FAILED)
	probe.volatile_orientation = "secret-old"
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "restored session",
		))).to_equal("sentry:2")
	if bridge.captured_payloads.size() > 1:
		Expect.that(
				bridge.captured_payloads[1]["contexts"]["display"]["primary_orientation"],
			).to_equal("safe-old")

	probe.app_name = "Replacement"
	bridge.configure_result = Error.OK
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "staging",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/2"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(
									["contexts", "display", "primary_orientation"],
								),
								"secret",
								"replacement",
							),
						],
					),
				),
			))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "replacement session",
		))).to_equal("sentry:3")
	Expect.that(
			bridge.captured_payloads[2]["contexts"]["display"]["primary_orientation"],
		).to_equal("replacement-old")

	provider.shutdown()
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/3"},
			))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "fresh unredacted session",
		))).to_equal("sentry:4")
	Expect.that(
			bridge.captured_payloads[3]["contexts"]["display"]["primary_orientation"],
		).to_equal("secret-old")
	provider.shutdown()


func test_provider_redacts_stable_and_volatile_runtime_contexts_once_each() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var probe := FakeRuntimeContextSource.new()
	probe.app_name = "a"
	probe.volatile_orientation = "a"
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = probe,
		)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["contexts", "foundry_app", "name"]),
								"a",
								"aa",
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(
									["contexts", "display", "primary_orientation"],
								),
								"a",
								"aa",
							),
						],
					),
				),
			))).to_equal(Error.OK)
	Expect.that(
			bridge.configured_payload["stable_contexts"]["foundry_app"]["name"],
		).to_equal("aa")

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "single-pass contexts",
		))).to_equal("sentry:1")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["foundry_app"]["name"],
		).to_equal("aa")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["display"]["primary_orientation"],
		).to_equal("aa")
	provider.shutdown()


func test_provider_redacts_global_attributes_before_native_config_and_capture_paths() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var raw_global_attributes: Dictionary = {
		"build": 42,
		"password": "root-secret",
		"nested": {"PASSWORD": "nested-secret"},
		"remove_me": "private",
	}
	var config := ObservabilityConfig.new(
		p_global_attributes = raw_global_attributes,
		p_provider_options = {"dsn": "https://public@example/1"},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_processing = ObservabilityProcessingConfig.new(
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new(
				[
					ObservabilityRedactionRule.sensitive_key("password"),
					ObservabilityRedactionRule.remove_field(
						PackedStringArray(
							[
								"contexts",
								"global_attributes",
								"remove_me",
							],
						),
					),
				],
			),
		),
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	var expected_global_attributes: Dictionary = {
		"build": 42,
		"password": "[REDACTED]",
		"nested": {"PASSWORD": "[REDACTED]"},
	}
	Expect.that(bridge.configured_payload["global_attributes"]).to_equal(
			expected_global_attributes,
		)
	Expect.that(
			bridge.active_configuration()["global_attributes"],
		).to_equal(expected_global_attributes)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "event path",
		))).to_equal("sentry:1")
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"log",
			p_message = "log path",
		))).to_equal("sentry-log:1")
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "breadcrumb path",
		))).to_be_true()
	# The native event, log, breadcrumb, and crash-context mappers all consume
	# this one committed configuration snapshot.
	Expect.that(
			bridge.active_configuration()["global_attributes"],
		).to_equal(expected_global_attributes)
	Expect.that(raw_global_attributes["password"]).to_equal("root-secret")
	provider.shutdown()


func test_invalid_global_attribute_redaction_preserves_committed_session_and_policy() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var probe := FakeRuntimeContextSource.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = probe,
		)
	var initial_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.sensitive_key("password"),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["contexts", "foundry_app", "name"]),
				"",
				"committed-app",
			),
	])
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {"password": "old-secret"},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = initial_policy,
				),
			))).to_equal(Error.OK)
	var configure_count: int = bridge.configured_payloads.size()

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {"password": "new-secret"},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.remove_field(
								PackedStringArray(
									[
										"contexts",
										"global_attributes",
									],
								),
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["contexts", "foundry_app", "name"]),
								"",
								"candidate-app",
							),
						],
					),
				),
			))).to_equal(Error.ERR_INVALID_DATA)
	Expect.that(bridge.configured_payloads.size()).to_equal(configure_count)
	Expect.that(
			bridge.active_configuration()["global_attributes"],
		).to_equal({"password": "[REDACTED]"})

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "committed session",
		))).to_equal("sentry:1")
	Expect.that(
			bridge.captured_payloads[0]["contexts"]["foundry_app"]["name"],
		).to_equal("committed-app")
	provider.shutdown()


func test_failed_session_reset_never_retains_raw_or_candidate_global_attributes() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.sensitive_key("password"),
	])
	var initial_config := ObservabilityConfig.new(
		p_global_attributes = {"build": 42, "password": "old-secret"},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_processing = ObservabilityProcessingConfig.new(
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = policy,
		),
	)
	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "retained breadcrumb",
		))).to_be_true()
	var retained_trail: Array[Dictionary] = (
			bridge.current_breadcrumb_payloads.duplicate(true)
		)
	bridge.apply_scope_results = [false, true]

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {"build": 42, "password": "new-secret"},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = policy,
				),
			))).to_equal(Error.FAILED)
	for payload: Dictionary in bridge.configured_payloads:
		Expect.that(payload["global_attributes"]).to_equal({
			"build": 42,
			"password": "[REDACTED]",
		})
	Expect.that(
			bridge.active_configuration()["global_attributes"],
		).to_equal({"build": 42, "password": "[REDACTED]"})
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({"region": "iad"})
	Expect.that(bridge.current_breadcrumb_payloads).to_equal(retained_trail)
	provider.shutdown()


func test_provider_failed_reconfigure_preserves_last_stable_runtime_context() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.breadcrumbs_supported = false
	bridge.metrics_supported = false
	bridge.attachments_supported = false
	var probe := FakeRuntimeContextSource.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = probe,
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
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var enabled_config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(enabled_config)).to_equal(Error.OK)
	Expect.that(provider.configure(ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
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
	var provider := SentryObservabilityProvider.new(p_bridge = FakeSentryNativeBridge.new())
	Expect.that(provider.provider_name()).to_equal(&"sentry")


func test_enabled_configuration_requires_compatible_native_bridge_and_dsn() -> void:
	var missing_dsn := SentryObservabilityProvider.new(p_bridge = FakeSentryNativeBridge.new())
	Expect.that(missing_dsn.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {},
			))).to_equal(Error.FAILED)

	var incompatible_bridge := SentryObservabilityProvider.new(
			p_bridge = DynamicSentryNativeBridgeAdapter.new(RefCounted.new()),
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


func test_non_boolean_native_availability_is_rejected() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	native.availability_result = 1
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	Expect.that(adapter.is_available("owner")).to_be_false()


func test_disabled_configuration_is_safe_without_native_bridge() -> void:
	var provider := SentryObservabilityProvider.new()

	Expect.that(provider.configure(ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "ignored"))).to_equal("")


func test_resolves_registered_engine_singleton() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var native := FakeSentryNativeObject.new(bridge)
	Engine.register_singleton("SentryObservabilityBridge", native)
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
	bridge.breadcrumbs_supported = false
	bridge.metrics_supported = false
	bridge.attachments_supported = false
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
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_environment = "production",
		p_release = "1.2.3",
		p_dist = "ios",
		p_global_attributes = {"build": 42},
		p_provider_options = {"dsn": "https://public@example/1", "debug": true},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
			p_max_breadcrumbs = 37,
		),
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
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
	Expect.that(provider.configure(initial_config)).to_equal(Error.FAILED)
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
	var bridge := FakeSentryNativeBridge.new()
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


func test_breadcrumbless_failed_replacement_reapplies_retained_scope() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.breadcrumbs_supported = false
	bridge.metrics_supported = false
	bridge.attachments_supported = false
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


func test_changed_config_scope_reset_failure_fails_closed() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {"build": 1},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"debug": false,
		},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "lost during native restart",
	))).to_be_true()
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "staging",
				p_global_attributes = {"build": 2},
				p_provider_options = {
					"dsn": "https://public@example/2",
					"debug": true,
				},
			))).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payloads.back()["environment"]).to_equal("staging")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.set_tag("mode", "ranked")).to_be_false()


func test_equivalent_config_scope_reset_failure_restores_scope_and_breadcrumbs() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var config := ObservabilityConfig.new(
		p_global_attributes = {"build": {"number": 42}},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "primary"},
		},
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "retained session",
	))).to_be_true()
	var retained_trail: Array[Dictionary] = bridge.current_breadcrumb_payloads.duplicate(true)
	bridge.apply_scope_results = [false, true]

	Expect.that(provider.configure(config)).to_equal(Error.FAILED)
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({"region": "iad"})
	Expect.that(bridge.current_breadcrumb_payloads).to_equal(retained_trail)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	provider.shutdown()


func test_initial_scope_reset_failure_shuts_down_orphan_native_session() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "production",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.FAILED)

	Expect.that(provider.is_available()).to_be_false()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})
	Expect.that(bridge.shutdown_count).to_equal(1)


func test_scope_reset_rollback_configure_failure_fails_closed_and_can_recover() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.set_context("match", {"round": 3})).to_be_true()
	Expect.that(provider.set_user(ObservabilityUser.new(
			p_application_user_id = "player-1",
	))).to_be_true()
	var configure_count_before_failure: int = bridge.configured_payloads.size()
	bridge.configure_results = [Error.OK, Error.FAILED]
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(initial_config)).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payloads.size()).to_equal(
			configure_count_before_failure + 2,
		)
	Expect.that(bridge.configured_payloads.slice(-2)[0]["environment"]).to_equal(
			"production",
		)
	Expect.that(bridge.configured_payloads.slice(-2)[1]["environment"]).to_equal(
			"production",
		)

	Expect.that(provider.is_available()).to_be_false()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "failed closed",
	))).to_equal("")
	Expect.that(provider.set_tag("mode", "ranked")).to_be_false()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "failed closed",
	))).to_be_false()

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.set_tag("fresh", "scope")).to_be_true()
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {"fresh": "scope"},
			"contexts": {},
		})
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "recovered",
	))).to_equal("sentry:1")
	provider.shutdown()


func test_scope_reset_retained_scope_reapply_failure_fails_closed() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	var configure_count_before_failure: int = bridge.configured_payloads.size()
	bridge.configure_results = [Error.OK, Error.OK]
	bridge.apply_scope_results = [false, false]

	Expect.that(provider.configure(initial_config)).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payloads.size()).to_equal(
			configure_count_before_failure + 2,
		)
	Expect.that(bridge.configured_payloads.slice(-2)[0]["environment"]).to_equal(
			"production",
		)
	Expect.that(bridge.configured_payloads.slice(-2)[1]["environment"]).to_equal(
			"production",
		)
	Expect.that(bridge.applied_scope_payloads.slice(-2)).to_equal([
		{
			"tags": {},
			"contexts": {},
		},
		{
			"tags": {"region": "iad"},
			"contexts": {},
		},
	])

	Expect.that(provider.is_available()).to_be_false()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "failed closed",
	))).to_equal("")
	Expect.that(provider.set_tag("mode", "ranked")).to_be_false()
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	provider.shutdown()


func test_failed_configure_scope_resync_failure_fails_closed_with_original_error() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	bridge.configure_result = Error.ERR_INVALID_PARAMETER
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(initial_config)).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "failed closed",
	))).to_equal("")
	Expect.that(provider.set_tag("mode", "ranked")).to_be_false()
	Expect.that(provider.clear_breadcrumbs()).to_be_false()

	bridge.configure_result = Error.OK
	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.set_tag("fresh", "scope")).to_be_true()
	provider.shutdown()


func test_scope_reset_failure_rolls_back_to_committed_disabled_configuration() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var enabled_config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(enabled_config)).to_equal(Error.OK)
	Expect.that(provider.configure(ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(enabled_config)).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payload["enabled"]).to_be_false()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})


func test_scope_operations_require_enabled_available_native_capability() -> void:
	var disabled_bridge := FakeSentryNativeBridge.new()
	var disabled := SentryObservabilityProvider.new(p_bridge = disabled_bridge)
	Expect.that(disabled.configure(ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
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

	var unsupported_bridge := FakeSentryNativeBridge.new()
	unsupported_bridge.scope_supported = false
	var unsupported := SentryObservabilityProvider.new(p_bridge = unsupported_bridge)
	Expect.that(unsupported.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(unsupported.set_tag("region", "iad")).to_be_false()
	Expect.that(unsupported.capture(ObservabilityEvent.new(
			p_message = "events remain available",
		))).to_equal("sentry:1")
	unsupported.shutdown()

	var shutdown_bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
	bridge.scope_supported = false
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
	Expect.that(bridge.captured_payloads).to_have_size(0)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "supported unscoped event",
	))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	provider.shutdown()


func test_forwards_mobile_diagnostic_config_to_native_bridge() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_mobile_diagnostics = ObservabilityMobileDiagnosticsConfig.new(
			p_application_hang_detection_enabled = false,
			p_application_hang_timeout_msec = 3200,
			p_android_anr_detection_enabled = false,
			p_android_anr_timeout_msec = 6400,
			p_android_anr_attach_thread_dump = true,
		),
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.configured_payload["application_hang_detection_enabled"]).to_be_false()
	Expect.that(bridge.configured_payload["application_hang_timeout_msec"]).to_equal(3200)
	Expect.that(bridge.configured_payload["android_anr_detection_enabled"]).to_be_false()
	Expect.that(bridge.configured_payload["android_anr_timeout_msec"]).to_equal(6400)
	Expect.that(bridge.configured_payload["android_anr_attach_thread_dump"]).to_be_true()
	provider.shutdown()


func test_mobile_diagnostic_timeouts_are_normalized_before_provider_configuration() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_mobile_diagnostics = ObservabilityMobileDiagnosticsConfig.new(
			p_application_hang_timeout_msec = 0,
			p_android_anr_timeout_msec = -25,
		),
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.configured_payload["application_hang_timeout_msec"]).to_equal(1000)
	Expect.that(bridge.configured_payload["android_anr_timeout_msec"]).to_equal(1000)
	provider.shutdown()


func test_service_forwards_normalized_structured_exception_frames_to_native_bridge() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryNativeBridge.new()
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
				p_stack_traces = ObservabilityStackTraceConfig.new(
					p_variables_enabled = true,
				),
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
	var bridge := FakeSentryNativeBridge.new()
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


func test_service_delivers_processor_replacement_event_to_sentry_bridge() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [
						Callable(self, "_replace_service_sentry_event") as Callable[[ObservabilityEvent], ObservabilityEvent?],
					],
					p_log_processors = [],
					p_metric_processors = [],
				),
			))).to_equal(Error.OK)
	Expect.that(service.capture_message("original event")).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	Expect.that(bridge.captured_payloads[0]["message"]).to_equal("processed sentry event")
	service.shutdown()


func test_direct_provider_skips_null_exception_frames() -> void:
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_global_attributes = {"build": 42},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_processing = ObservabilityProcessingConfig.new(
			p_logs_enabled = true,
			p_log_minimum_level = ObservabilityLevel.TRACE,
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
		),
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
	var bridge := FakeSentryNativeBridge.new()
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
	Expect.that(bridge.current_breadcrumb_payloads).to_equal(
			bridge.captured_breadcrumb_payloads,
		)
	provider.shutdown()


func test_enabled_configure_starts_with_an_explicitly_cleared_breadcrumb_trail() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)

	Expect.that(bridge.clear_breadcrumbs_count).to_equal(1)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	provider.shutdown()


func test_identical_successful_configure_clears_only_the_live_breadcrumb_trail() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "retained call history",
	))).to_be_true()
	Expect.that(bridge.current_breadcrumb_payloads).to_have_size(1)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(bridge.captured_breadcrumb_payloads).to_have_size(1)
	provider.shutdown()


func test_changed_successful_configure_starts_a_fresh_breadcrumb_trail() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "production",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "old session",
	))).to_be_true()

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "staging",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/2"},
			))).to_equal(Error.OK)
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(bridge.captured_breadcrumb_payloads).to_have_size(1)
	provider.shutdown()


func test_changed_failed_configure_fails_closed_and_can_recover() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)
	var replacement_config := ObservabilityConfig.new(
		p_environment = "staging",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/2"},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "prior session",
	))).to_be_true()
	bridge.configure_result = Error.ERR_INVALID_PARAMETER

	Expect.that(provider.configure(replacement_config)).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(1)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "failed closed",
	))).to_equal("")

	bridge.configure_result = Error.OK
	Expect.that(provider.configure(replacement_config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "recovered",
	))).to_equal("sentry:1")
	provider.shutdown()


func test_equivalent_failed_configure_preserves_breadcrumb_trail_and_scope() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {"build": {"number": 42}},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "primary"},
		},
	)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "prior session",
	))).to_be_true()
	var retained_trail: Array[Dictionary] = bridge.current_breadcrumb_payloads.duplicate(true)
	var configure_count_before_failure: int = bridge.configured_payloads.size()
	bridge.configure_result = Error.ERR_INVALID_PARAMETER

	Expect.that(provider.configure(config)).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(bridge.configured_payloads.size()).to_equal(
			configure_count_before_failure + 1,
		)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal(retained_trail)
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({"region": "iad"})
	Expect.that(provider.is_available()).to_be_true()
	provider.shutdown()


func test_dynamic_adapter_rejects_malformed_configure_result() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	native.configure_result = "activated"
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)


func test_dynamic_adapter_remains_invalid_after_malformed_configure_result() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	native.configure_result = "failed"
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(adapter.contract_valid()).to_be_false()
	native.configure_result = Error.OK
	Expect.that(adapter.configure({})).to_equal(Error.ERR_UNAVAILABLE)


func test_equivalent_reconfigure_clear_rejection_restores_scope_and_trail() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_global_attributes = {
			"build": {"number": 42},
			"channels": ["stable"],
		},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "primary"},
		},
	)
	var equivalent_config := ObservabilityConfig.new(
		p_global_attributes = {
			"build": {"number": 42},
			"channels": ["stable"],
		},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "primary"},
		},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "prior session",
	))).to_be_true()
	var retained_trail: Array[Dictionary] = bridge.current_breadcrumb_payloads.duplicate(true)
	bridge.configured_payload["global_attributes"]["build"]["number"] = 999
	bridge.configured_payload["provider_options"]["transport"]["tunnel"] = "mutated"
	bridge.clear_breadcrumbs_result = false

	Expect.that(provider.configure(equivalent_config)).to_equal(Error.FAILED)
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	Expect.that(bridge.current_breadcrumb_payloads).to_equal(retained_trail)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.set_tag("mode", "ranked")).to_be_true()
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({
			"region": "iad",
			"mode": "ranked",
		})
	provider.shutdown()


func test_changed_config_false_clear_result_fails_closed_and_can_recover() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_environment = "production",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)
	var replacement_config := ObservabilityConfig.new(
		p_environment = "staging",
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/2"},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "destroyed by restart",
	))).to_be_true()
	bridge.clear_breadcrumbs_result = false

	Expect.that(provider.configure(replacement_config)).to_equal(Error.FAILED)
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "failed closed",
	))).to_equal("")
	Expect.that(provider.set_tag("mode", "ranked")).to_be_false()

	bridge.clear_breadcrumbs_result = true
	Expect.that(provider.configure(replacement_config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.set_tag("fresh", "scope")).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "recovered",
	))).to_equal("sentry:1")
	provider.shutdown()


func test_dynamic_adapter_remains_invalid_after_malformed_clear_result() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	native.clear_result = "false"
	Expect.that(adapter.clear_breadcrumbs()).to_be_false()
	Expect.that(adapter.contract_valid()).to_be_false()
	native.clear_result = true
	Expect.that(adapter.clear_breadcrumbs()).to_be_false()


func test_nested_config_change_is_not_equivalent_for_clear_recovery() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var initial_config := ObservabilityConfig.new(
		p_global_attributes = {"build": {"number": 42}},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "primary"},
		},
	)
	var nested_change_config := ObservabilityConfig.new(
		p_global_attributes = {"build": {"number": 42}},
		p_provider_options = {
			"dsn": "https://public@example/1",
			"transport": {"tunnel": "replacement"},
		},
	)

	Expect.that(provider.configure(initial_config)).to_equal(Error.OK)
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "destroyed by nested config restart",
	))).to_be_true()
	bridge.clear_breadcrumbs_result = false

	Expect.that(provider.configure(nested_change_config)).to_equal(Error.FAILED)
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_breadcrumb_payloads).to_equal([])
	Expect.that(provider.is_available()).to_be_false()


func test_dynamic_adapter_rejects_malformed_clear_result() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	native.clear_result = "true"
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	Expect.that(adapter.clear_breadcrumbs()).to_be_false()


func test_clear_breadcrumbs_returns_explicit_native_result_and_respects_lifecycle() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(0)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(1)
	Expect.that(provider.clear_breadcrumbs()).to_be_true()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(2)
	bridge.clear_breadcrumbs_result = false
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(3)
	provider.shutdown()
	Expect.that(provider.clear_breadcrumbs()).to_be_false()
	Expect.that(bridge.clear_breadcrumbs_count).to_equal(3)


func test_dynamic_adapter_rejects_malformed_scope_and_clear_results() -> void:
	var native := RawMalformedSentryNativeBridge.new()
	var adapter := DynamicSentryNativeBridgeAdapter.new(native)
	native.scope_result = "true"
	native.clear_result = "true"
	Expect.that(adapter.apply_scope({})).to_be_false()
	Expect.that(adapter.clear_breadcrumbs()).to_be_false()


func test_missing_native_breadcrumb_capability_preserves_event_capture() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.breadcrumbs_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
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


func test_incomplete_breadcrumb_capability_does_not_block_a_fresh_session() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.breadcrumbs_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "available",
	))).to_equal("sentry:1")
	Expect.that(provider.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "unavailable",
	))).to_be_false()


func test_captures_feedback_with_only_explicit_optional_fields() -> void:
	var bridge := FakeSentryNativeBridge.new()
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
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_processing = ObservabilityProcessingConfig.new(
			p_metrics_enabled = true,
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
		),
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
	var bridge := FakeSentryNativeBridge.new()
	bridge.metrics_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_processing = ObservabilityProcessingConfig.new(
					p_metrics_enabled = true,
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
				),
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
	var bridge := FakeSentryNativeBridge.new()
	bridge.feedback_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

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


func test_typed_bridge_rejects_enabled_logs_without_structured_log_capability() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.logs_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_processing = ObservabilityProcessingConfig.new(
					p_logs_enabled = true,
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
				),
			))).to_equal(Error.FAILED)
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(bridge.configured_payloads).to_have_size(0)
	provider.shutdown()


func test_service_rejects_typed_bridge_without_enabled_log_capability() -> void:
	var service: FoundryObservability = _service()
	var bridge := FakeSentryNativeBridge.new()
	bridge.logs_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.FAILED)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.capture_log("unsupported")).to_equal("")
	service.shutdown()


func test_shutdown_is_idempotent() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(provider.set_tag("region", "iad")).to_be_true()
	Expect.that(bridge.active_configuration().is_empty()).to_be_false()
	Expect.that(bridge.current_scope_payload["tags"]).to_equal({"region": "iad"})
	provider.shutdown()
	provider.shutdown()

	Expect.that(bridge.shutdown_count).to_equal(1)
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})
	Expect.that(bridge.current_scope_payload).to_equal({
			"tags": {},
			"contexts": {},
		})


func test_shutdown_discards_committed_configuration_rollback_state() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "production",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	provider.shutdown()
	bridge.apply_scope_results = [false]

	Expect.that(provider.configure(ObservabilityConfig.new(
				p_environment = "staging",
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/2"},
			))).to_equal(Error.FAILED)
	Expect.that(bridge.configured_payloads.size()).to_equal(2)
	Expect.that(bridge.shutdown_count).to_equal(2)
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.active_configuration()).to_equal({})


func test_attachment_candidates_replace_native_snapshot_atomically() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(bridge.replaced_attachment_payloads).to_equal([[]])

	var bytes_attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([1, 2, 3]),
			"state.bin",
		)
	Expect.that(bytes_attachment).not_().to_be_null()
	var first_handle: String = provider.add_attachment(bytes_attachment)
	Expect.that(first_handle.is_empty()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)

	var file: FileAccess = FileAccess.open("user://sentry-attachment.txt", FileAccess.WRITE)
	file.store_string("diagnostic")
	file.close()
	var path_attachment := ObservabilityAttachment.from_path(
			"user://sentry-attachment.txt",
			"renamed.txt",
			"text/plain",
		)
	var second_handle: String = provider.add_attachment(path_attachment)
	Expect.that(second_handle.is_empty()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(2)
	Expect.that(bridge.current_attachment_payloads[1]["path"]).to_equal(
			ProjectSettings.globalize_path("user://sentry-attachment.txt"),
		)

	Expect.that(provider.remove_attachment(first_handle)).to_equal(Error.OK)
	Expect.that(provider.remove_attachment(first_handle)).to_equal(Error.ERR_DOES_NOT_EXIST)
	bridge.replace_attachments_results = [false, false]
	Expect.that(provider.clear_attachments()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	var retained_payload: Array = bridge.current_attachment_payloads.duplicate(true)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "after rejected attachment mutation",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_native_attachment_payloads[0]).to_equal(retained_payload)
	Expect.that(provider.clear_attachments()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "after malformed attachment mutation",
		))).to_equal("sentry:2")
	Expect.that(bridge.captured_native_attachment_payloads[1]).to_equal(retained_payload)
	Expect.that(provider.remove_attachment(second_handle)).to_equal(Error.OK)


func test_provider_redacts_persistent_and_capture_local_builtin_attachment_metadata() -> void:
	var path: String = "user://secret-game.log"
	var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
	file.store_string("game output")
	file.close()
	var probe := FakeAttachmentSource.new()
	probe.game_log = path
	var root := Node.new()
	root.name = "Root"
	probe.tree = root
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_game_log = true,
					p_attach_screenshot = true,
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["attachments", "filename"]),
								"secret-game",
								"safe-game",
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["attachments", "filename"]),
								"screenshot",
								"safe-screen",
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["attachments", "content_type"]),
								"text/plain",
								"text/safe",
							),
							ObservabilityRedactionRule.replace_text(
								PackedStringArray(["attachments", "content_type"]),
								"image/png",
								"image/safe",
							),
						],
					),
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(bridge.current_attachment_payloads[0]["filename"]).to_equal(
			"safe-game.log",
		)
	Expect.that(bridge.current_attachment_payloads[0]["content_type"]).to_equal(
			"text/safe",
		)
	Expect.that(probe.game_log).to_equal(path)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "redacted built-ins",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0]["attachments"]).to_have_size(1)
	Expect.that(bridge.captured_payloads[0]["attachments"][0]["filename"]).to_equal(
			"safe-screen.png",
		)
	Expect.that(
			bridge.captured_payloads[0]["attachments"][0]["content_type"],
		).to_equal("image/safe")
	root.free()
	provider.shutdown()


func test_invalid_redacted_builtin_attachment_is_omitted_without_dropping_event() -> void:
	var probe := FakeAttachmentSource.new()
	var root := Node.new()
	root.name = "Root"
	probe.tree = root
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_screenshot = true,
				),
				p_processing = ObservabilityProcessingConfig.new(
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
					p_redaction_policy = ObservabilityRedactionPolicy.new(
						[
							ObservabilityRedactionRule.replace_value(
								PackedStringArray(["attachments", "filename"]),
								7,
							),
						],
					),
				),
			))).to_equal(Error.OK)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "partial redaction failure",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0].has("attachments")).to_be_false()
	var failures: Array = provider.last_attachment_failures()
	Expect.that(failures).to_have_size(1)
	if not failures.is_empty():
		var failure: ObservabilityAttachmentFailure = failures[0]
		Expect.that(failure.handle()).to_equal("built-in:screenshot")
		Expect.that(failure.filename()).to_equal("screenshot.png")
		Expect.that(failure.reason()).to_equal(
				ObservabilityAttachmentFailure.REDACTED,
			)
		Expect.that(failure.error()).to_equal(Error.ERR_INVALID_DATA)
	root.free()
	provider.shutdown()


func test_invalid_persistent_builtin_redaction_survives_configure_and_combines() -> void:
	var path: String = "user://persistent-secret.log"
	var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
	file.store_string("game output")
	file.close()
	var probe := FakeAttachmentSource.new()
	probe.game_log = path
	var root := Node.new()
	root.name = "Root"
	probe.tree = root
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_attach_game_log = true,
			p_attach_screenshot = true,
		),
		p_processing = ObservabilityProcessingConfig.new(
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new(
				[
					ObservabilityRedactionRule.replace_text(
						PackedStringArray(["attachments", "filename"]),
						"persistent-secret.log",
						"",
					),
					ObservabilityRedactionRule.replace_text(
						PackedStringArray(["attachments", "filename"]),
						"screenshot.png",
						"",
					),
				],
			),
		),
	)
	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(0)
	var configured_failures: Array = provider.last_attachment_failures()
	Expect.that(configured_failures).to_have_size(1)
	if not configured_failures.is_empty():
		var configured_failure: ObservabilityAttachmentFailure = configured_failures[0]
		Expect.that(configured_failure.handle()).to_equal("built-in:game-log")
		Expect.that(configured_failure.filename()).to_equal("persistent-secret.log")
		Expect.that(configured_failure.reason()).to_equal(
				ObservabilityAttachmentFailure.REDACTED,
			)
		Expect.that(configured_failure.error()).to_equal(Error.ERR_INVALID_DATA)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "persistent and local failures",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0].has("attachments")).to_be_false()
	var capture_failures: Array = provider.last_attachment_failures()
	Expect.that(capture_failures).to_have_size(2)
	if capture_failures.size() == 2:
		var persistent_failure: ObservabilityAttachmentFailure = capture_failures[0]
		var local_failure: ObservabilityAttachmentFailure = capture_failures[1]
		Expect.that(persistent_failure.handle()).to_equal("built-in:game-log")
		Expect.that(local_failure.handle()).to_equal("built-in:screenshot")
		Expect.that(local_failure.reason()).to_equal(
				ObservabilityAttachmentFailure.REDACTED,
			)
		Expect.that(local_failure.error()).to_equal(Error.ERR_INVALID_DATA)

	provider.shutdown()
	Expect.that(provider.last_attachment_failures()).to_have_size(0)
	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.last_attachment_failures()).to_have_size(1)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(provider.last_attachment_failures()).to_have_size(0)
	root.free()
	provider.shutdown()


func test_attachment_bridge_methods_are_optional_until_feature_or_api_is_used() -> void:
	var bridge := FakeSentryNativeBridge.new()
	bridge.attachments_supported = false
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([1]),
			"unsupported.bin",
		))).to_equal("")
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "legacy bridge still captures",
		))).to_equal("sentry:1")

	var feature_bridge := FakeSentryNativeBridge.new()
	feature_bridge.attachments_supported = false
	var feature_provider := SentryObservabilityProvider.new(p_bridge = feature_bridge)
	Expect.that(feature_provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_screenshot = true,
				),
			))).to_equal(Error.FAILED)


func test_capture_materializes_only_res_paths_into_event_local_attachments() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
			))).to_equal(Error.OK)
	var resource := ObservabilityAttachment.from_path(
			"res://tests/support/fake_sentry_native_bridge.notest.fs",
			"bridge.fs",
			"text/plain",
		)
	var global := ObservabilityAttachment.from_bytes(
			PackedByteArray([7, 8]),
			"native.bin",
		)
	Expect.that(provider.add_attachment(resource).is_empty()).to_be_false()
	Expect.that(provider.add_attachment(global).is_empty()).to_be_false()

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "attachments",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0]["attachments"]).to_have_size(1)
	Expect.that(bridge.captured_payloads[0]["attachments"][0]["filename"]).to_equal(
			"bridge.fs",
		)
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(bridge.current_attachment_payloads[0]["filename"]).to_equal("native.bin")


func test_attachment_preflight_failures_do_not_block_events_and_clear_on_success() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_max_bytes = 2,
				),
			))).to_equal(Error.OK)
	var missing_handle: String = provider.add_attachment(
			ObservabilityAttachment.from_path(
				"user://definitely-missing-sentry-file.log",
				"missing.log",
			),
		)
	Expect.that(missing_handle.is_empty()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "partial"))).to_equal(
			"sentry:1",
		)
	var missing_failures: Array = provider.last_attachment_failures()
	Expect.that(missing_failures).to_have_size(1)
	var missing_failure: ObservabilityAttachmentFailure = missing_failures[0]
	Expect.that(missing_failure.handle()).to_equal(missing_handle)
	Expect.that(missing_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE,
		)

	Expect.that(provider.remove_attachment(missing_handle)).to_equal(Error.OK)
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([1, 2]),
			"valid.bin",
		)).is_empty()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "valid"))).to_equal(
			"sentry:2",
		)
	Expect.that(provider.last_attachment_failures()).to_have_size(0)


func test_attachment_management_preserves_latest_event_failure_history() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
			))).to_equal(Error.OK)
	var missing_path: String = (
			"user://sentry-management-missing-%s.log" % bridge.get_instance_id()
		)
	var absolute_path: String = ProjectSettings.globalize_path(missing_path)
	if FileAccess.file_exists(absolute_path):
		DirAccess.remove_absolute(absolute_path)
	var missing_handle: String = provider.add_attachment(
			ObservabilityAttachment.from_path(missing_path, "missing.log"),
		)
	Expect.that(missing_handle.is_empty()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "establish event failure",
		))).to_equal("sentry:1")
	_expect_missing_attachment_failure(provider, missing_handle)

	var successful_handle: String = provider.add_attachment(
			ObservabilityAttachment.from_bytes(
				PackedByteArray([1]),
				"successful.bin",
			),
		)
	Expect.that(successful_handle.is_empty()).to_be_false()
	_expect_missing_attachment_failure(provider, missing_handle)
	Expect.that(provider.remove_attachment(successful_handle)).to_equal(Error.OK)
	_expect_missing_attachment_failure(provider, missing_handle)
	Expect.that(provider.clear_attachments()).to_be_true()
	_expect_missing_attachment_failure(provider, missing_handle)

	bridge.replace_attachments_results = [false]
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([2]),
			"rejected-add.bin",
		))).to_equal("")
	_expect_missing_attachment_failure(provider, missing_handle)

	var retained_handle: String = provider.add_attachment(
			ObservabilityAttachment.from_bytes(
				PackedByteArray([3]),
				"retained.bin",
			),
		)
	Expect.that(retained_handle.is_empty()).to_be_false()
	_expect_missing_attachment_failure(provider, missing_handle)
	bridge.replace_attachments_results = [false]
	Expect.that(provider.remove_attachment(retained_handle)).to_equal(Error.FAILED)
	_expect_missing_attachment_failure(provider, missing_handle)
	bridge.replace_attachments_results = [false]
	Expect.that(provider.clear_attachments()).to_be_false()
	_expect_missing_attachment_failure(provider, missing_handle)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "next applicable event",
		))).to_equal("sentry:2")
	Expect.that(provider.last_attachment_failures()).to_have_size(0)


func test_oversized_global_path_failure_survives_logs_until_applicable_capture() -> void:
	var file: FileAccess = FileAccess.open(
			"user://sentry-oversized-global.log",
			FileAccess.WRITE,
		)
	file.store_buffer(PackedByteArray([1, 2, 3]))
	file.close()
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_processing = ObservabilityProcessingConfig.new(
					p_logs_enabled = true,
					p_event_processors = [],
					p_log_processors = [],
					p_metric_processors = [],
				),
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_max_bytes = 2,
				),
			))).to_equal(Error.OK)
	var handle: String = provider.add_attachment(ObservabilityAttachment.from_path(
			"user://sentry-oversized-global.log",
			"oversized.log",
			"text/plain",
		))
	Expect.that(handle.is_empty()).to_be_false()

	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "oversized"))).to_equal(
			"sentry:1",
		)
	var oversized_failures: Array = provider.last_attachment_failures()
	Expect.that(oversized_failures).to_have_size(1)
	var oversized_failure: ObservabilityAttachmentFailure = oversized_failures[0]
	Expect.that(oversized_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.OVERSIZED,
		)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"log",
			p_message = "non-applicable",
		))).to_equal("sentry-log:1")
	Expect.that(provider.last_attachment_failures()[0]).not_().to_be_null()
	Expect.that(provider.last_attachment_failures()).to_have_size(1)

	file = FileAccess.open("user://sentry-oversized-global.log", FileAccess.WRITE)
	file.store_buffer(PackedByteArray([1, 2]))
	file.close()
	Expect.that(provider.capture(ObservabilityEvent.new(p_message = "now valid"))).to_equal(
			"sentry:2",
		)
	Expect.that(provider.last_attachment_failures()).to_have_size(0)


func test_attachment_reconfigure_invalidates_handles_and_restores_equivalent_failures() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			p_bridge = bridge,
			p_runtime_context_source = FakeRuntimeContextSource.new(),
		)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)
	Expect.that(provider.configure(config)).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([9]),
			"retained.bin",
		)
	var retained_handle: String = provider.add_attachment(attachment)
	var retained_snapshot: Array = bridge.current_attachment_payloads.duplicate(true)

	bridge.replace_attachments_results = [false, true]
	Expect.that(provider.configure(config)).to_equal(Error.FAILED)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(bridge.current_attachment_payloads).to_equal(retained_snapshot)
	Expect.that(provider.remove_attachment(retained_handle)).to_equal(Error.OK)

	var invalidated_handle: String = provider.add_attachment(attachment)
	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.remove_attachment(invalidated_handle)).to_equal(
			Error.ERR_DOES_NOT_EXIST,
		)
	Expect.that(bridge.current_attachment_payloads).to_have_size(0)


func test_attachment_restore_rejection_fails_closed() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {"dsn": "https://public@example/1"},
	)
	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([1]),
			"state.bin",
		)).is_empty()).to_be_false()

	bridge.replace_attachments_results = [false, false]
	Expect.that(provider.configure(config)).to_equal(Error.FAILED)
	Expect.that(provider.is_available()).to_be_false()
	Expect.that(provider.capture(ObservabilityEvent.new())).to_equal("")


func test_built_in_attachment_collection_is_independent_cached_and_bounded() -> void:
	var probe := FakeAttachmentSource.new()
	var root := Node.new()
	root.name = "Root"
	for index: int in range(SentryBuiltInAttachmentCollector.MAX_SCENE_NODES + 20):
		var child := Node.new()
		child.name = "Child%s" % index
		root.add_child(child)
	probe.tree = root
	var collector := SentryBuiltInAttachmentCollector.new(probe)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_attach_screenshot = true,
			p_attach_scene_tree = true,
			p_max_bytes = 1024 * 1024,
		),
	)

	var first: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			config,
		)
	var second: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			config,
		)
	Expect.that(first.attachments()).to_have_size(2)
	Expect.that(second.attachments()).to_have_size(2)
	Expect.that(probe.screenshot_calls).to_equal(1)
	var hierarchy_payload: Dictionary = first.attachments()[1]
	var hierarchy_bytes: PackedByteArray = hierarchy_payload["bytes"]
	var hierarchy: Dictionary = JSON.parse_string(hierarchy_bytes.get_string_from_utf8())
	var hierarchy_children: Array = hierarchy["children"]
	Expect.that(hierarchy_children.size()).to_equal(
			SentryBuiltInAttachmentCollector.MAX_SCENE_NODES - 1,
		)

	probe.frame += 1
	probe.main_thread = false
	var skipped: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			config,
		)
	Expect.that(skipped.attachments()).to_have_size(0)
	Expect.that(skipped.failures()).to_have_size(2)
	probe.main_thread = true
	probe.headless = true
	var headless: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			config,
		)
	Expect.that(headless.attachments()).to_have_size(1)
	Expect.that(headless.failures()).to_have_size(1)
	root.free()


func test_built_in_attachment_toggles_and_size_limits_are_independent() -> void:
	var probe := FakeAttachmentSource.new()
	var root := Node.new()
	root.name = "Root"
	probe.tree = root
	var collector := SentryBuiltInAttachmentCollector.new(probe)
	var screenshot_config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_attach_screenshot = true,
		),
	)
	var screenshot_only: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			screenshot_config,
		)
	Expect.that(screenshot_only.attachments()).to_have_size(1)
	Expect.that(screenshot_only.attachments()[0]["filename"]).to_equal(
			"screenshot.png",
		)

	var scene_config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_attach_scene_tree = true,
		),
	)
	var scene_only: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			scene_config,
		)
	Expect.that(scene_only.attachments()).to_have_size(1)
	Expect.that(scene_only.attachments()[0]["filename"]).to_equal(
			"view-hierarchy.json",
		)

	var oversized_config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_max_bytes = 2,
			p_attach_screenshot = true,
		),
	)
	var oversized: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			oversized_config,
		)
	Expect.that(oversized.attachments()).to_have_size(0)
	Expect.that(oversized.failures()).to_have_size(1)
	var failure: ObservabilityAttachmentFailure = oversized.failures()[0]
	Expect.that(failure.reason()).to_equal(ObservabilityAttachmentFailure.OVERSIZED)
	root.free()


func test_scene_hierarchy_respects_maximum_depth() -> void:
	var probe := FakeAttachmentSource.new()
	var root := Node.new()
	root.name = "Depth0"
	var parent: Node = root
	for depth: int in range(SentryBuiltInAttachmentCollector.MAX_SCENE_DEPTH + 10):
		var child := Node.new()
		child.name = "Depth%s" % (depth + 1)
		parent.add_child(child)
		parent = child
	probe.tree = root
	var collector := SentryBuiltInAttachmentCollector.new(probe)
	var config := ObservabilityConfig.new(
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
			p_message_filter_prefixes = PackedStringArray(),
		),
		p_attachments = ObservabilityAttachmentConfig.new(
			p_max_bytes = 1024 * 1024,
			p_attach_scene_tree = true,
		),
	)

	var result: SentryAttachmentCollection = collector.collect(
			ObservabilityEvent.new(),
			config,
		)
	var hierarchy_payload: Dictionary = result.attachments()[0]
	var hierarchy_bytes: PackedByteArray = hierarchy_payload["bytes"]
	var cursor: Dictionary = JSON.parse_string(hierarchy_bytes.get_string_from_utf8())
	var captured_depth: int = 0
	var children: Array = cursor["children"]
	while not children.is_empty():
		captured_depth += 1
		cursor = children[0]
		children = cursor["children"]
	Expect.that(captured_depth).to_equal(
			SentryBuiltInAttachmentCollector.MAX_SCENE_DEPTH,
		)
	root.free()


func test_zero_attachment_limit_disables_every_sentry_delivery_path() -> void:
	var user_file: FileAccess = FileAccess.open(
			"user://sentry-zero-user.log",
			FileAccess.WRITE,
		)
	user_file.close()
	var game_file: FileAccess = FileAccess.open(
			"user://sentry-zero-game.log",
			FileAccess.WRITE,
		)
	game_file.close()
	var probe := FakeAttachmentSource.new()
	var root := Node.new()
	root.name = "Root"
	probe.tree = root
	probe.game_log = "user://sentry-zero-game.log"
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_max_bytes = 0,
					p_attach_game_log = true,
					p_attach_screenshot = true,
					p_attach_scene_tree = true,
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(0)
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray(),
			"empty.bin",
		)).is_empty()).to_be_false()
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_path(
			"user://sentry-zero-user.log",
			"empty.log",
		)).is_empty()).to_be_false()
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_path(
			"res://tests/support/fake_sentry_native_bridge.notest.fs",
			"resource.fs",
		)).is_empty()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(0)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "zero disables delivery",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0].has("attachments")).to_be_false()
	Expect.that(bridge.captured_native_attachment_payloads[0]).to_have_size(0)
	var failures: Array = provider.last_attachment_failures()
	Expect.that(failures).to_have_size(6)
	for failure: ObservabilityAttachmentFailure in failures:
		Expect.that(failure.reason()).to_equal(
				ObservabilityAttachmentFailure.OVERSIZED,
			)
	Expect.that(probe.screenshot_calls).to_equal(0)
	root.free()


func test_game_log_path_is_registered_lazily_before_the_file_exists() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var path: String = "user://sentry-lazy-game-%s.log" % bridge.get_instance_id()
	var absolute_path: String = ProjectSettings.globalize_path(path)
	if FileAccess.file_exists(absolute_path):
		DirAccess.remove_absolute(absolute_path)
	var probe := FakeAttachmentSource.new()
	probe.game_log = path
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_game_log = true,
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(bridge.current_attachment_payloads[0]["path"]).to_equal(absolute_path)

	var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
	file.store_string("late log")
	file.close()
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "lazy game log",
		))).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads[0].has("attachments")).to_be_false()
	Expect.that(bridge.captured_native_attachment_payloads[0]).to_have_size(1)
	Expect.that(provider.last_attachment_failures()).to_have_size(0)


func test_empty_or_disabled_game_log_path_reports_missing_file() -> void:
	var probe := FakeAttachmentSource.new()
	probe.game_log = ""
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_game_log = true,
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(0)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "missing game log",
		))).to_equal("sentry:1")
	var failures: Array = provider.last_attachment_failures()
	Expect.that(failures).to_have_size(1)
	var failure: ObservabilityAttachmentFailure = failures[0]
	Expect.that(failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE,
		)
	Expect.that(failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)


func test_missing_configured_game_log_is_preflighted_at_event_time() -> void:
	var bridge := FakeSentryNativeBridge.new()
	var path: String = "user://sentry-missing-game-%s.log" % bridge.get_instance_id()
	var absolute_path: String = ProjectSettings.globalize_path(path)
	if FileAccess.file_exists(absolute_path):
		DirAccess.remove_absolute(absolute_path)
	var probe := FakeAttachmentSource.new()
	probe.game_log = path
	var provider := SentryObservabilityProvider.new(bridge, null, probe)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_game_log = true,
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)

	Expect.that(provider.capture(ObservabilityEvent.new(
			p_message = "still missing game log",
		))).to_equal("sentry:1")
	var failures: Array = provider.last_attachment_failures()
	Expect.that(failures).to_have_size(1)
	var failure: ObservabilityAttachmentFailure = failures[0]
	Expect.that(failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE,
		)
	Expect.that(failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)
	Expect.that(bridge.captured_payloads[0].has("attachments")).to_be_false()


func test_user_clear_preserves_configured_game_log_attachment() -> void:
	var file: FileAccess = FileAccess.open("user://sentry-game.log", FileAccess.WRITE)
	file.store_string("game output")
	file.close()
	var probe := FakeAttachmentSource.new()
	probe.game_log = "user://sentry-game.log"
	var bridge := FakeSentryNativeBridge.new()
	var provider := SentryObservabilityProvider.new(
			bridge,
			null,
			probe,
		)
	Expect.that(provider.configure(ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {"dsn": "https://public@example/1"},
				p_automatic_capture = ObservabilityAutomaticCaptureConfig.new(
					p_message_filter_prefixes = PackedStringArray(),
				),
				p_attachments = ObservabilityAttachmentConfig.new(
					p_attach_game_log = true,
				),
			))).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(provider.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([4]),
			"user.bin",
		)).is_empty()).to_be_false()
	Expect.that(bridge.current_attachment_payloads).to_have_size(2)
	Expect.that(provider.clear_attachments()).to_be_true()
	Expect.that(bridge.current_attachment_payloads).to_have_size(1)
	Expect.that(bridge.current_attachment_payloads[0]["filename"]).to_equal(
			"sentry-game.log",
		)


func _expect_missing_attachment_failure(
		provider: SentryObservabilityProvider,
		expected_handle: String,
) -> void:
	var failures: Array = provider.last_attachment_failures()
	Expect.that(failures).to_have_size(1)
	if failures.is_empty():
		return
	var failure: ObservabilityAttachmentFailure = failures[0]
	Expect.that(failure.handle()).to_equal(expected_handle)
	Expect.that(failure.filename()).to_equal("missing.log")
	Expect.that(failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE,
		)
	Expect.that(failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability


func _replace_service_sentry_event(event: ObservabilityEvent) -> ObservabilityEvent:
	return ObservabilityEvent.new(
			event.kind(), event.level(), "processed sentry event", event.source(),
			event.timestamp_msec(), event.attributes(), event.exception(),
			event.engine_ticks_msec(), event.scope())
