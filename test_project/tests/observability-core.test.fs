namespace games.cafecito.foundryobservability.tests

import foundry.testlib
import games.cafecito.foundryobservability

class_name ObservabilityCoreTests
extends RefCounted
uses Test


func test_levels_are_ordered_and_named() -> void:
	Expect.that(ObservabilityLevel.TRACE).to_be_less_than(ObservabilityLevel.DEBUG)
	Expect.that(ObservabilityLevel.DEBUG).to_be_less_than(ObservabilityLevel.INFO)
	Expect.that(ObservabilityLevel.INFO).to_be_less_than(ObservabilityLevel.WARN)
	Expect.that(ObservabilityLevel.WARN).to_be_less_than(ObservabilityLevel.ERROR)
	Expect.that(ObservabilityLevel.ERROR).to_be_less_than(ObservabilityLevel.FATAL)
	Expect.that(ObservabilityLevel.name(ObservabilityLevel.ERROR)).to_equal("ERROR")


func test_exception_and_event_copy_attributes() -> void:
	var source := {"request_id": "abc", "nested": {"attempt": 1}}
	var exception := ObservabilityException.new("InvalidState", "bad state", "stack", source)
	source["request_id"] = "changed"
	var event_source := {"scene": "battle"}
	var event := ObservabilityEvent.new(
			&"exception", ObservabilityLevel.ERROR, "bad state",
			&"game", 1234, event_source, exception)
	event_source["scene"] = "changed"
	var exposed: Dictionary = event.attributes()
	exposed["new_field"] = true

	Expect.that(exception.attributes()).to_equal({
			"request_id": "abc", "nested": {"attempt": 1}
		})
	Expect.that(event.kind()).to_equal(&"exception")
	Expect.that(event.exception()).to_equal(exception)
	Expect.that(event.timestamp_msec()).to_equal(1234)
	Expect.that(event.attributes()).to_equal({"scene": "battle"})


func test_config_copies_attributes_and_options() -> void:
	var attributes := {"build": 42}
	var options := {"provider_key": "value"}
	var config := ObservabilityConfig.new(
			true, "production", "1.2.3", "arm64", attributes, options)
	attributes["build"] = 99
	options["provider_key"] = "changed"

	Expect.that(config.enabled).to_be_true()
	Expect.that(config.environment).to_equal("production")
	Expect.that(config.global_attributes()).to_equal({"build": 42})
	Expect.that(config.provider_options()).to_equal({"provider_key": "value"})


func test_default_null_provider_is_safe() -> void:
	var service: FoundryObservability = _service()

	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(service.is_available()).to_be_false()
	Expect.that(service.capture_message("ignored")).to_equal("")
	Expect.that(service.flush()).to_equal(Error.OK)
	service.shutdown()


func test_memory_provider_captures_messages_and_exceptions() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.provider_name()).to_equal(&"memory")
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(service.is_available()).to_be_true()
	Expect.that(service.capture_message("hello", ObservabilityLevel.WARN, {"screen": "title"})).to_equal("memory:1")
	Expect.that(service.capture_exception(ObservabilityException.new("Error", "boom", "trace"))).to_equal("memory:2")
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.events()[0].kind()).to_equal(&"message")
	Expect.that(provider.events()[0].level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(provider.events()[0].source()).to_equal(&"game")
	Expect.that(provider.events()[0].attributes()).to_equal({"screen": "title"})
	Expect.that(provider.events()[1].kind()).to_equal(&"exception")
	Expect.that(provider.events()[1].message()).to_equal("boom")
	Expect.that(provider.events()[1].exception()).to_not_be_null()
	service.shutdown()


func test_disabled_capture_and_flush_are_forwarded() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var disabled: ObservabilityConfig = ObservabilityConfig.new(false)

	Expect.that(service.configure(provider, disabled)).to_equal(Error.OK)
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(service.capture_message("ignored")).to_equal("")
	Expect.that(provider.events()).to_have_size(0)

	provider.flush_result = Error.FAILED
	Expect.that(service.flush(321)).to_equal(Error.FAILED)
	Expect.that(provider.last_flush_timeout_msec).to_equal(321)
	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func test_enabled_unavailable_provider_reports_capture_failure() -> void:
	var service: FoundryObservability = _service()
	var provider: NullObservabilityProvider = NullObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(true))).to_equal(Error.OK)
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(service.is_available()).to_be_false()
	Expect.that(service.capture_message("unavailable")).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func test_failed_replacement_keeps_working_provider() -> void:
	var service: FoundryObservability = _service()
	var working: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var failing: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(working, ObservabilityConfig.new())).to_equal(Error.OK)
	failing.configure_result = Error.FAILED
	Expect.that(service.configure(failing, ObservabilityConfig.new())).to_equal(Error.FAILED)
	Expect.that(service.provider_name()).to_equal(&"memory")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.capture_message("still working")).to_equal("memory:1")
	Expect.that(working.events()).to_have_size(1)
	Expect.that(working.shutdown_count).to_equal(0)
	service.shutdown()


func test_active_provider_reconfiguration_does_not_shutdown_it() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var disabled: ObservabilityConfig = ObservabilityConfig.new(false)
	Expect.that(service.configure(provider, disabled)).to_equal(Error.OK)
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(provider.shutdown_count).to_equal(0)
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(provider.shutdown_count).to_equal(0)
	service.shutdown()


func test_shutdown_is_idempotent() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	service.shutdown()
	service.shutdown()

	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(provider.shutdown_count).to_equal(1)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability
