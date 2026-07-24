namespace foundry.observability.tests

import foundry.testlib
import foundry.observability

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
	var exception := ObservabilityException.new(
			p_type_name = "InvalidState",
			p_message = "bad state",
			p_stack_trace = "stack",
			p_attributes = source,
		)
	source["request_id"] = "changed"
	var event_source := {"scene": "battle"}
	var event := ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = "bad state",
			p_source = &"game",
			p_timestamp_msec = 1234,
			p_attributes = event_source,
			p_exception = exception,
		)
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
			p_enabled = true,
			p_environment = "production",
			p_release = "1.2.3",
			p_dist = "arm64",
			p_global_attributes = attributes,
			p_provider_options = options,
		)
	attributes["build"] = 99
	options["provider_key"] = "changed"

	Expect.that(config.enabled).to_be_true()
	Expect.that(config.environment).to_equal("production")
	Expect.that(config.global_attributes()).to_equal({"build": 42})
	Expect.that(config.provider_options()).to_equal({"provider_key": "value"})


func test_metric_types_and_value_copy_attributes() -> void:
	var source := {"region": "iad", "nested": {"attempt": 1}}
	var metric := ObservabilityMetric.new(
			p_type = ObservabilityMetricType.DISTRIBUTION,
			p_name = "match.duration",
			p_value = 125.5,
			p_unit = "millisecond",
			p_attributes = source,
		)
	source["region"] = "changed"
	var exposed: Dictionary = metric.attributes()
	exposed["region"] = "also changed"

	Expect.that(ObservabilityMetricType.COUNTER).to_equal(0)
	Expect.that(ObservabilityMetricType.GAUGE).to_equal(1)
	Expect.that(ObservabilityMetricType.DISTRIBUTION).to_equal(2)
	Expect.that(metric.type()).to_equal(ObservabilityMetricType.DISTRIBUTION)
	Expect.that(metric.name()).to_equal("match.duration")
	Expect.that(metric.value()).to_be_close_to(125.5)
	Expect.that(metric.unit()).to_equal("millisecond")
	Expect.that(metric.attributes()).to_equal({
			"region": "iad", "nested": {"attempt": 1},
		})


func test_metric_convenience_methods_store_normalized_payloads() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {"build": 42, "shared": "global"},
		))).to_equal(Error.OK)

	Expect.that(service.capture_counter(
			"match.started", 2, {"shared": "metric"},
		)).to_be_true()
	Expect.that(service.capture_gauge(
			"players.active", 7.0, "player",
		)).to_be_true()
	Expect.that(service.capture_distribution(
			"match.duration", 125.5, "millisecond", {"region": "iad"},
		)).to_be_true()

	Expect.that(provider.metrics()).to_have_size(3)
	Expect.that(provider.metrics()[0].type()).to_equal(ObservabilityMetricType.COUNTER)
	Expect.that(provider.metrics()[0].attributes()).to_equal({
			"build": 42, "shared": "metric",
		})
	Expect.that(provider.metrics()[1].type()).to_equal(ObservabilityMetricType.GAUGE)
	Expect.that(provider.metrics()[2].type()).to_equal(ObservabilityMetricType.DISTRIBUTION)
	Expect.that(provider.metrics()[2].unit()).to_equal("millisecond")
	service.shutdown()


func test_metrics_reject_invalid_names_values_units_and_attributes() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)

	Expect.that(service.capture_counter("", 1)).to_be_false()
	Expect.that(service.capture_counter(" padded", 1)).to_be_false()
	Expect.that(service.capture_counter(_repeated("x", 201), 1)).to_be_false()
	Expect.that(service.capture_counter("match.started", -1)).to_be_false()
	Expect.that(service.capture_gauge("players.active", NAN)).to_be_false()
	Expect.that(service.capture_distribution("match.duration", INF)).to_be_false()
	Expect.that(service.capture_gauge("players.active", 1.0, "player count")).to_be_false()
	Expect.that(service.capture_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.COUNTER,
			p_name = "match.started",
			p_value = 1.0,
			p_unit = "item",
		))).to_be_false()
	Expect.that(service.capture_counter(
			"match.started", 1, {"nested": {"unsupported": true}},
		)).to_be_false()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {42: "unsupported key"},
		))).to_equal(Error.OK)
	Expect.that(service.capture_counter("match.started")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(provider.metrics()).to_have_size(0)
	Expect.that(service.capture_message("events still work")).to_equal("memory:1")
	service.shutdown()


func test_metrics_honor_disabled_configuration_and_filter() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metrics_enabled = false,
		))).to_equal(Error.OK)
	Expect.that(service.capture_counter("combat.hit")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(provider.metrics()).to_have_size(0)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_filter = Callable(self, "_keep_combat_metric"),
		))).to_equal(Error.OK)
	Expect.that(service.capture_counter("menu.opened")).to_be_false()
	Expect.that(service.capture_counter("combat.hit")).to_be_true()
	Expect.that(provider.metrics()).to_have_size(1)
	service.shutdown()


func test_metrics_apply_deterministic_sampling_after_filtering() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_sample_rate = 0.25,
		))).to_equal(Error.OK)

	for index: int in range(8):
		service.capture_counter("sampled.metric", index + 1)

	Expect.that(provider.metrics()).to_have_size(2)
	Expect.that(provider.metrics()[0].value()).to_equal(4.0)
	Expect.that(provider.metrics()[1].value()).to_equal(8.0)
	service.shutdown()


func test_metricless_provider_keeps_event_capture_operational() -> void:
	var service: FoundryObservability = _service()
	var provider := MetriclessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)

	Expect.that(service.capture_counter("unsupported.metric")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("ordinary event")).to_equal("metricless:1")
	service.shutdown()


func test_metrics_do_not_affect_events_feedback_logs_or_flush() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		))).to_equal(Error.OK)

	Expect.that(service.capture_counter("match.started")).to_be_true()
	Expect.that(service.capture_log(
			"first", ObservabilityLevel.INFO, &"game", 1000,
		)).to_equal("memory:1")
	Expect.that(service.capture_log(
			"dropped", ObservabilityLevel.INFO, &"game", 1000,
		)).to_equal("")
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "feedback",
		))).to_equal("memory-feedback:1")
	Expect.that(provider.metrics()).to_have_size(1)
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.feedback()).to_have_size(1)
	Expect.that(service.flush(321)).to_equal(Error.OK)
	Expect.that(provider.last_flush_timeout_msec).to_equal(321)
	service.shutdown()


func test_invalid_metric_configuration_keeps_active_provider() -> void:
	var service: FoundryObservability = _service()
	var working := MemoryObservabilityProvider.new()
	var candidate := MemoryObservabilityProvider.new()
	Expect.that(service.configure(working, ObservabilityConfig.new())).to_equal(Error.OK)

	Expect.that(service.configure(candidate, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_sample_rate = 1.5,
		))).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_message("still active")).to_equal("memory:1")
	Expect.that(working.events()).to_have_size(1)
	Expect.that(candidate.events()).to_have_size(0)
	service.shutdown()


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
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "Error",
			p_message = "boom",
			p_stack_trace = "trace",
		))).to_equal("memory:2")
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.events()[0].kind()).to_equal(&"message")
	Expect.that(provider.events()[0].level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(provider.events()[0].source()).to_equal(&"game")
	Expect.that(provider.events()[0].attributes()).to_equal({"screen": "title"})
	Expect.that(provider.events()[1].kind()).to_equal(&"exception")
	Expect.that(provider.events()[1].message()).to_equal("boom")
	Expect.that(provider.events()[1].exception()).to_not_be_null()
	service.shutdown()


func test_feedback_value_preserves_fields() -> void:
	var feedback := ObservabilityFeedback.new(
			p_message = "The tutorial was confusing.",
			p_name = "Player One",
			p_contact_email = "player@example.com",
			p_associated_event_id = "event-123",
		)

	Expect.that(feedback.message()).to_equal("The tutorial was confusing.")
	Expect.that(feedback.name()).to_equal("Player One")
	Expect.that(feedback.contact_email()).to_equal("player@example.com")
	Expect.that(feedback.associated_event_id()).to_equal("event-123")


func test_memory_provider_captures_feedback_separately_from_events() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var feedback := ObservabilityFeedback.new(p_message = "Please add remappable controls.")

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_feedback(feedback)).to_equal("memory-feedback:1")
	Expect.that(provider.events()).to_have_size(0)
	Expect.that(provider.feedback()).to_have_size(1)
	Expect.that(provider.feedback()[0].message()).to_equal("Please add remappable controls.")
	service.shutdown()


func test_feedback_accepts_anonymous_and_identified_submissions() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Anonymous feedback",
		))).to_equal("memory-feedback:1")
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Identified feedback",
			p_name = "Player One",
			p_contact_email = "player@example.com",
			p_associated_event_id = "event-123",
		))).to_equal("memory-feedback:2")
	Expect.that(provider.feedback()[1].name()).to_equal("Player One")
	Expect.that(provider.feedback()[1].contact_email()).to_equal("player@example.com")
	Expect.that(provider.feedback()[1].associated_event_id()).to_equal("event-123")
	service.shutdown()


func test_feedback_rejects_invalid_message_and_optional_values() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)

	Expect.that(service.capture_feedback(ObservabilityFeedback.new(p_message = ""))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(p_message = "   "))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = _repeated("x", 4097),
		))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Valid message",
			p_contact_email = "not-an-email",
		))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Valid message",
			p_contact_email = "player @example.com",
		))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Valid message",
			p_name = "Player\nOne",
		))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(provider.feedback()).to_have_size(0)
	service.shutdown()


func test_feedback_does_not_collect_when_disabled_or_unavailable() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var feedback := ObservabilityFeedback.new(p_message = "Do not send this while disabled.")

	Expect.that(service.configure(provider, ObservabilityConfig.new(p_enabled = false))).to_equal(Error.OK)
	Expect.that(service.capture_feedback(feedback)).to_equal("")
	Expect.that(provider.feedback()).to_have_size(0)
	service.shutdown()

	var unavailable_service: FoundryObservability = _service()
	var unavailable: NullObservabilityProvider = NullObservabilityProvider.new()
	Expect.that(unavailable_service.configure(unavailable, ObservabilityConfig.new(p_enabled = true))).to_equal(Error.OK)
	Expect.that(unavailable_service.capture_feedback(feedback)).to_equal("")
	Expect.that(unavailable_service.last_error()).to_equal(Error.FAILED)
	unavailable_service.shutdown()


func test_structured_logs_are_enabled_by_default_and_preserve_shape() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {"build": 42},
		))).to_equal(Error.OK)
	Expect.that(service.capture_log(
			"player {id} missed",
			ObservabilityLevel.WARN,
			&"combat",
			1234,
			{"id": 7},
		)).to_equal("memory:1")

	var event: ObservabilityEvent = provider.events()[0]
	Expect.that(event.kind()).to_equal(&"log")
	Expect.that(event.level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(event.message()).to_equal("player {id} missed")
	Expect.that(event.source()).to_equal(&"combat")
	Expect.that(event.timestamp_msec()).to_equal(1234)
	Expect.that(event.attributes()).to_equal({"id": 7})
	service.shutdown()


func test_structured_logs_honor_disabled_and_minimum_level_configuration() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_logs_enabled = false,
			p_log_minimum_level = ObservabilityLevel.ERROR,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_log("disabled")).to_equal("")
	Expect.that(provider.events()).to_have_size(0)

	config.logs_enabled = true
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_log("filtered", ObservabilityLevel.WARN)).to_equal("")
	Expect.that(service.capture_log("kept", ObservabilityLevel.ERROR)).to_equal("memory:1")
	Expect.that(provider.events()).to_have_size(1)
	service.shutdown()


func test_structured_logs_apply_deterministic_per_second_rate_limit() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		))).to_equal(Error.OK)
	Expect.that(service.capture_log("first", ObservabilityLevel.INFO, &"game", 1000)).to_equal("memory:1")
	Expect.that(service.capture_log("dropped", ObservabilityLevel.INFO, &"game", 1500)).to_equal("")
	Expect.that(service.capture_log("next window", ObservabilityLevel.INFO, &"game", 2000)).to_equal("memory:2")
	Expect.that(provider.events()).to_have_size(2)
	service.shutdown()


func test_disabled_structured_logs_do_not_consume_rate_limit() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_enabled = true,
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	config.enabled = false
	Expect.that(service.capture_log("suppressed", ObservabilityLevel.INFO, &"game", 1000)).to_equal("")
	config.enabled = true
	Expect.that(service.capture_log("accepted", ObservabilityLevel.INFO, &"game", 1000)).to_equal("memory:1")
	service.shutdown()


func test_direct_structured_log_events_apply_enabled_gate_before_rate_limit() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_enabled = true,
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		)
	var event := ObservabilityEvent.new(
			p_kind = &"log",
			p_level = ObservabilityLevel.INFO,
			p_message = "direct",
			p_source = &"game",
			p_timestamp_msec = 1000,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	config.enabled = false
	Expect.that(service.capture_event(event)).to_equal("")
	config.enabled = true
	Expect.that(service.capture_event(event)).to_equal("memory:1")
	service.shutdown()


func test_disabled_capture_and_flush_are_forwarded() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var disabled: ObservabilityConfig = ObservabilityConfig.new(p_enabled = false)

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

	Expect.that(service.configure(provider, ObservabilityConfig.new(p_enabled = true))).to_equal(Error.OK)
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
	var disabled: ObservabilityConfig = ObservabilityConfig.new(p_enabled = false)
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


func _repeated(value: String, count: int) -> String:
	var result := ""
	for _index in range(count):
		result += value
	return result


func _keep_combat_metric(metric: ObservabilityMetric) -> bool:
	return metric.name().begins_with("combat.")
