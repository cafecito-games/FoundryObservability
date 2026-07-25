namespace foundry.observability.sentry.tests

import foundry.observability
import foundry.observability.sentry
import foundry.testlib

class_name ObservabilitySentryTests
extends RefCounted
uses Test


func test_provider_name_is_sentry() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = FakeSentryBridge.new())
	Expect.that(provider.provider_name()).to_equal(&"sentry")


func test_enabled_configuration_requires_native_bridge_and_dsn() -> void:
	var missing_dsn := SentryObservabilityProvider.new(p_bridge = FakeSentryBridge.new())
	Expect.that(missing_dsn.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
		))).to_equal(Error.FAILED)

	var missing_bridge := SentryObservabilityProvider.new()
	Expect.that(missing_bridge.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.FAILED)


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


func test_forwards_config_event_and_flush_to_native_bridge() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(p_bridge = bridge)
	var config := ObservabilityConfig.new(
			p_environment = "production",
			p_release = "1.2.3",
			p_dist = "ios",
			p_global_attributes = {"build": 42},
			p_provider_options = {"dsn": "https://public@example/1", "debug": true},
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
	Expect.that(bridge.captured_payloads[0]["kind"]).to_equal("exception")
	Expect.that(bridge.captured_payloads[0]["timestamp_msec"]).to_equal(1721865600123)
	Expect.that(bridge.captured_payloads[0]["engine_ticks_msec"]).to_equal(4567)
	Expect.that(bridge.captured_payloads[0]["exception"]["type_name"]).to_equal("InvalidState")
	Expect.that(provider.flush(321)).to_equal(Error.OK)
	Expect.that(bridge.flush_timeouts).to_equal([321])


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
