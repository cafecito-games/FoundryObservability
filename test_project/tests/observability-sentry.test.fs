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
			p_timestamp_msec = 1234,
			p_attributes = {"screen": "title"},
			p_exception = exception,
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.is_available()).to_be_true()
	Expect.that(provider.capture(event)).to_equal("sentry:1")
	Expect.that(bridge.configured_payload["environment"]).to_equal("production")
	Expect.that(bridge.configured_payload["global_attributes"]).to_equal({"build": 42})
	Expect.that(bridge.captured_payloads[0]["kind"]).to_equal("exception")
	Expect.that(bridge.captured_payloads[0]["timestamp_msec"]).to_equal(1234)
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
			p_timestamp_msec = 1234,
			p_attributes = {"logger_name": "combat", "id": 7},
		)

	Expect.that(provider.configure(config)).to_equal(Error.OK)
	Expect.that(provider.capture(event)).to_equal("sentry-log:1")
	Expect.that(bridge.captured_log_payloads[0]["kind"]).to_equal("log")
	Expect.that(bridge.captured_log_payloads[0]["timestamp_msec"]).to_equal(1234)
	Expect.that(bridge.configured_payload["logs_enabled"]).to_be_true()
	provider.shutdown()


func test_structured_log_is_safe_when_bridge_does_not_support_it() -> void:
	var provider := SentryObservabilityProvider.new(p_bridge = EventOnlySentryBridge.new())
	Expect.that(provider.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {"dsn": "https://public@example/1"},
		))).to_equal(Error.OK)
	Expect.that(provider.capture(ObservabilityEvent.new(
			p_kind = &"log",
			p_message = "unsupported",
		))).to_equal("")
	provider.shutdown()


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
