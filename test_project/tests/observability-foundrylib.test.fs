namespace games.cafecito.foundryobservability.tests

import foundry.logging
import foundry.testlib
import games.cafecito.foundryobservability
import games.cafecito.foundryobservability.foundrylib

class_name ObservabilityFoundryLibTests
extends RefCounted
uses Test


func test_maps_structured_logs_to_observability_events() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var fields: Dictionary = {"id": 7, "weapon": "axe"}

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(service, ObservabilityLevel.INFO)
	var record: LogRecord = LogRecord.new(LogLevel.WARN, "combat", "player {id} missed", fields, 99)
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
	service.shutdown()


func test_filters_records_below_minimum_level() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(service, ObservabilityLevel.ERROR)
	sink.emit(LogRecord.new(LogLevel.WARN, "combat", "ignored", {}, 1))
	sink.emit(LogRecord.new(LogLevel.ERROR, "combat", "kept", {}, 2))

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("kept")
	service.shutdown()


func test_flush_forwards_to_observability_service() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	provider.flush_result = Error.FAILED
	var sink: FoundryLibObservabilitySink = FoundryLibObservabilitySink.new(service)
	sink.flush()

	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(provider.last_flush_timeout_msec).to_equal(2000)
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability
