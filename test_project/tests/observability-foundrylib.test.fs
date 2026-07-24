namespace foundry.observability.tests

import foundry.logging
import foundry.testlib
import foundry.observability
import foundry.observability.foundrylib

class_name ObservabilityFoundryLibTests
extends RefCounted
uses Test


func test_maps_structured_logs_to_observability_events() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var fields: Dictionary = {"id": 7, "weapon": "axe"}

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(
			p_service = service,
			p_minimum_level = ObservabilityLevel.INFO,
		)
	var record: LogRecord = LogRecord.new(
			p_level = LogLevel.WARN,
			p_logger_name = "combat",
			p_message_template = "player {id} missed",
			p_fields = fields,
			p_timestamp_msec = 99,
		)
	sink.emit(record)
	fields["id"] = 8

	Expect.that(provider.events()).to_have_size(1)
	var event: ObservabilityEvent = provider.events()[0]
	Expect.that(event.kind()).to_equal(&"log")
	Expect.that(event.level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(event.message()).to_equal("player 7 missed")
	Expect.that(event.source()).to_equal(&"foundry.logging")
	Expect.that(event.timestamp_msec()).to_equal(99)
	Expect.that(event.attributes()).to_equal({
			"logger_name": "combat", "id": 7, "weapon": "axe"
		})
	Expect.that(event.source()).to_equal(&"foundry.logging")
	Expect.that(event.attributes()["logger_name"]).to_equal("combat")
	Expect.that(event.attributes()["id"]).to_equal(7)
	service.shutdown()


func test_filters_records_below_minimum_level() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(
			p_service = service,
			p_minimum_level = ObservabilityLevel.ERROR,
		)
	sink.emit(LogRecord.new(
			p_level = LogLevel.WARN,
			p_logger_name = "combat",
			p_message_template = "ignored",
			p_fields = {},
			p_timestamp_msec = 1,
		))
	sink.emit(LogRecord.new(
			p_level = LogLevel.ERROR,
			p_logger_name = "combat",
			p_message_template = "kept",
			p_fields = {},
			p_timestamp_msec = 2,
		))

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("kept")
	service.shutdown()


func test_sink_uses_service_log_filtering() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_log_minimum_level = ObservabilityLevel.ERROR,
		))).to_equal(Error.OK)
	var sink := FoundryLibObservabilitySink.new(
			p_service = service,
			p_minimum_level = ObservabilityLevel.TRACE,
		)
	sink.emit(LogRecord.new(LogLevel.WARN, "combat", "filtered", {}, 10))
	sink.emit(LogRecord.new(LogLevel.ERROR, "combat", "kept", {}, 20))

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("kept")
	service.shutdown()


func test_sink_calls_first_class_log_method() -> void:
	var recording := RecordingObservabilityApi.new()
	var sink := FoundryLibObservabilitySink.new(
			p_service = recording,
			p_minimum_level = ObservabilityLevel.TRACE,
		)
	sink.emit(LogRecord.new(
			p_level = LogLevel.WARN,
			p_logger_name = "combat",
			p_message_template = "player {id} missed",
			p_fields = {"id": 7},
			p_timestamp_msec = 99,
		))

	Expect.that(recording.captured_events).to_have_size(0)
	Expect.that(recording.captured_logs).to_have_size(1)
	Expect.that(recording.captured_logs[0]).to_equal({
			"message": "player 7 missed",
			"level": ObservabilityLevel.WARN,
			"source": &"foundry.logging",
			"timestamp_msec": 99,
			"attributes": {"logger_name": "combat", "id": 7},
		})


func test_flush_forwards_to_observability_service() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	provider.flush_result = Error.FAILED
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(p_service = service)
	sink.flush()

	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(provider.last_flush_timeout_msec).to_equal(2000)
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability
