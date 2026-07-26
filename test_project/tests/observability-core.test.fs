namespace foundry.observability.tests

import foundry.testlib
import foundry.observability
import foundry.observability.sentry.tests

class_name ObservabilityCoreTests
extends RefCounted
uses Test

var _processing_order: Array[String] = []
var _processing_filter_values: Array[String] = []
var _processing_clock_calls: int = 0
var _processing_frame_calls: int = 0
var _recursive_pipeline: ObservabilityProcessingPipeline
var _recursive_state_result: Dictionary = {}
var _lifecycle_pipeline: ObservabilityProcessingPipeline
var _processing_owner_id: int = 1
var _lifecycle_nested_result: Dictionary = {}
var _overlap_nested_result: Dictionary = {}
var _overlap_recursive_result: Dictionary = {}
var _overlap_stage: int = 0
var _service_event_processor_calls: int = 0
var _service_processing_clock_msec: int = 0
var _service_processing_frame_index: int = 0
var _service_processing_clock_calls: int = 0
var _service_processing_frame_calls: int = 0
var _service_lifecycle_service: FoundryObservability
var _service_lifecycle_provider: ObservabilityProvider
var _service_lifecycle_config: ObservabilityConfig
var _service_lifecycle_configure_result: int = Error.OK
var _state_reentry_service: FoundryObservability
var _state_reentry_result: bool = true
var _state_reentry_error: int = Error.OK
var _state_reentry_signal: StringName = &""
var _state_reentry_reason: StringName = &""
var _state_reentry_diagnostic_error: int = Error.OK
var _automatic_nested_service: FoundryObservability
var _automatic_nested_capture_result: String = ""
var _automatic_nested_reason: StringName = &""

class VariableCaptureProbeFrame extends "res://addons/FoundryObservability/ObservabilityStackFrame.fs":
	var public_variables_calls: int = 0

	func _init(p_file: String, p_variables: Dictionary) -> void:
		super(
				p_file,
				"",
				-1,
				"",
				true,
				"",
				PackedStringArray(),
				PackedStringArray(),
				p_variables,
		)

	func variables() -> Dictionary:
		public_variables_calls += 1
		return {"public accessor": true}


class MutableProcessingCallbacks extends Node:
	func process_event(event: ObservabilityEvent) -> ObservabilityEvent:
		return event

	func filter_metric(_metric: ObservabilityMetric) -> bool:
		return true


class ConfigureCountingMemoryProvider extends \
		"res://addons/FoundryObservability/MemoryObservabilityProvider.fs":
	var configure_calls: int = 0

	func configure(config: ObservabilityConfig) -> int:
		configure_calls += 1
		return super.configure(config)


class ReconfiguringCaptureMemoryProvider extends \
		"res://addons/FoundryObservability/MemoryObservabilityProvider.fs":
	var service: FoundryObservability
	var replacement_provider: ObservabilityProvider
	var replacement_config: ObservabilityConfig
	var reconfigure_result: int = Error.OK
	var reconfigure_on_capture: bool = true

	func capture(event: ObservabilityEvent) -> String:
		if reconfigure_on_capture:
			reconfigure_on_capture = false
			reconfigure_result = service.configure(
					replacement_provider,
					replacement_config,
				)
		return super.capture(event)


class ReentrantConfigureMemoryProvider extends \
		"res://addons/FoundryObservability/MemoryObservabilityProvider.fs":
	var service: FoundryObservability
	var nested_flush_result: int = Error.OK
	var request_shutdown: bool = false

	func configure(config: ObservabilityConfig) -> int:
		var result: int = super.configure(config)
		if service != null:
			if request_shutdown:
				service.shutdown()
			else:
				nested_flush_result = service.flush()
		return result


class ShutdownRequestingMemoryProvider extends \
		"res://addons/FoundryObservability/MemoryObservabilityProvider.fs":
	var service: FoundryObservability
	var request_count: int = 0

	func capture(event: ObservabilityEvent) -> String:
		request_count += 1
		service.shutdown()
		return super.capture(event)


class ReconfiguringReservationService extends \
		"res://addons/FoundryObservability/FoundryObservability.fs":
	var replacement_config: ObservabilityConfig
	var reconfigure_result: int = Error.OK
	var armed: bool = false

	func _try_begin_pinned_provider_call(
			provider: ObservabilityProvider,
			pipeline: ObservabilityProcessingPipeline,
			generation: int,
	) -> bool:
		_reconfigure_before_reservation(provider)
		return super._try_begin_pinned_provider_call(provider, pipeline, generation)

	func _reconfigure_before_reservation(provider: ObservabilityProvider) -> void:
		if not armed:
			return
		armed = false
		reconfigure_result = configure(provider, replacement_config)


class ShutdownDuringCandidatePreparationService extends \
		"res://addons/FoundryObservability/FoundryObservability.fs":
	var shutdown_during_preparation: bool = false

	func _configure_candidate_pipeline(
			pipeline: ObservabilityProcessingPipeline,
			config: ObservabilityConfig,
	) -> int:
		if shutdown_during_preparation:
			shutdown_during_preparation = false
			shutdown()
		return pipeline.configure(config)


class RecordingScopeProvider extends RefCounted:
	uses ObservabilityProvider, ObservabilityScopeProvider, ObservabilityBreadcrumbsProvider

	var operation_result: bool = true
	var calls: Array[StringName] = []
	var captured_events: Array[ObservabilityEvent] = []
	var _enabled: bool = false
	var _shutdown: bool = false

	func provider_name() -> StringName:
		return &"recording-scope"

	func is_available() -> bool:
		return _enabled and not _shutdown

	func configure(config: ObservabilityConfig) -> int:
		_enabled = config.enabled
		_shutdown = false
		return Error.OK

	func capture(event: ObservabilityEvent) -> String:
		if not is_available():
			return ""
		calls.append(&"capture")
		captured_events.append(event)
		return "recording-scope:%s" % captured_events.size()

	func capture_feedback(_feedback: ObservabilityFeedback) -> String:
		return ""

	func capture_breadcrumb(_breadcrumb: ObservabilityBreadcrumb) -> bool:
		return true

	func set_tag(_key: String, _value: String) -> bool:
		calls.append(&"set_tag")
		return operation_result

	func remove_tag(_key: String) -> bool:
		calls.append(&"remove_tag")
		return operation_result

	func clear_tags() -> bool:
		calls.append(&"clear_tags")
		return operation_result

	func set_context(_name: String, _value: Dictionary) -> bool:
		calls.append(&"set_context")
		return operation_result

	func remove_context(_name: String) -> bool:
		calls.append(&"remove_context")
		return operation_result

	func clear_contexts() -> bool:
		calls.append(&"clear_contexts")
		return operation_result

	func set_user(_user: ObservabilityUser) -> bool:
		calls.append(&"set_user")
		return operation_result

	func remove_user() -> bool:
		calls.append(&"remove_user")
		return operation_result

	func clear_breadcrumbs() -> bool:
		calls.append(&"clear_breadcrumbs")
		return operation_result

	func flush(_timeout_msec: int = 2000) -> int:
		return Error.OK

	func shutdown() -> void:
		_shutdown = true
		_enabled = false


class NonBooleanScopeProvider extends "res://tests/support/scopeless_observability_provider.notest.fs":
	func set_tag(_key: String, _value: String) -> String:
		return "not a bool"

	func remove_tag(_key: String) -> String:
		return "not a bool"

	func clear_tags() -> String:
		return "not a bool"

	func set_context(_name: String, _value: Dictionary) -> String:
		return "not a bool"

	func remove_context(_name: String) -> String:
		return "not a bool"

	func clear_contexts() -> String:
		return "not a bool"

	func set_user(_user: ObservabilityUser) -> String:
		return "not a bool"

	func remove_user() -> String:
		return "not a bool"

	func clear_breadcrumbs() -> String:
		return "not a bool"


class MalformedAttachmentsProvider extends "res://tests/support/attachmentless_observability_provider.notest.fs":
	var add_result: Variant = 7
	var remove_result: Variant = "not an error"
	var clear_result: Variant = "not a bool"
	var failures_result: Variant = ["not a failure"]

	func add_attachment(_attachment: ObservabilityAttachment) -> Variant:
		return add_result

	func remove_attachment(_handle: String) -> Variant:
		return remove_result

	func clear_attachments() -> Variant:
		return clear_result

	func last_attachment_failures() -> Variant:
		return failures_result


func test_levels_are_ordered_and_named() -> void:
	Expect.that(ObservabilityLevel.TRACE).to_be_less_than(ObservabilityLevel.DEBUG)
	Expect.that(ObservabilityLevel.DEBUG).to_be_less_than(ObservabilityLevel.INFO)
	Expect.that(ObservabilityLevel.INFO).to_be_less_than(ObservabilityLevel.WARN)
	Expect.that(ObservabilityLevel.WARN).to_be_less_than(ObservabilityLevel.ERROR)
	Expect.that(ObservabilityLevel.ERROR).to_be_less_than(ObservabilityLevel.FATAL)
	Expect.that(ObservabilityLevel.name(ObservabilityLevel.ERROR)).to_equal("ERROR")


func test_observability_user_exposes_explicit_valid_identity() -> void:
	var user := ObservabilityUser.new("player-7", "Mina", "mina@example.com")

	Expect.that(user.application_user_id()).to_equal("player-7")
	Expect.that(user.display_name()).to_equal("Mina")
	Expect.that(user.contact_email()).to_equal("mina@example.com")
	Expect.that(user.is_valid()).to_be_true()


func test_observability_user_rejects_empty_padded_and_control_character_identity() -> void:
	Expect.that(ObservabilityUser.new().is_valid()).to_be_false()
	Expect.that(ObservabilityUser.new(" player-7").is_valid()).to_be_false()
	Expect.that(ObservabilityUser.new("", "Mina ").is_valid()).to_be_false()
	Expect.that(ObservabilityUser.new("", "", "\tmina@example.com").is_valid()).to_be_false()
	Expect.that(ObservabilityUser.new("player\n7").is_valid()).to_be_false()


func test_observability_scope_mutates_and_defensively_copies_tags_and_contexts() -> void:
	var source_context: Dictionary = {
		"round": 3,
		"active": true,
		"ratio": 2.5,
		"winner": null,
		"players": [&"Mina", "Bo"],
		&"rules": {&"mode": &"ranked"},
	}
	var scope := ObservabilityScope.new()

	Expect.that(scope.is_empty()).to_be_true()
	Expect.that(scope.set_tag("region", "iad")).to_be_true()
	Expect.that(scope.set_context("game", source_context)).to_be_true()
	source_context["round"] = 99
	source_context["players"][0] = "Changed"

	var exposed_tags: Dictionary = scope.tags()
	var exposed_contexts: Dictionary = scope.contexts()
	exposed_tags["region"] = "fra"
	exposed_contexts["game"]["round"] = 100
	exposed_contexts["game"]["players"][0] = "Changed again"

	Expect.that(scope.tags()).to_equal({"region": "iad"})
	Expect.that(scope.contexts()).to_equal({
		"game": {
			"round": 3,
			"active": true,
			"ratio": 2.5,
			"winner": null,
			"players": ["Mina", "Bo"],
			"rules": {"mode": "ranked"},
		},
	})
	Expect.that(scope.is_empty()).to_be_false()

	var copied_scope: ObservabilityScope = scope.duplicate()
	Expect.that(copied_scope.remove_tag("region")).to_be_true()
	Expect.that(copied_scope.remove_context("game")).to_be_true()
	Expect.that(copied_scope.remove_tag("missing")).to_be_false()
	Expect.that(copied_scope.remove_context("missing")).to_be_false()
	Expect.that(copied_scope.is_empty()).to_be_true()
	Expect.that(scope.tags()).to_equal({"region": "iad"})
	Expect.that(scope.contexts().has("game")).to_be_true()

	scope.clear_tags()
	scope.clear_contexts()
	Expect.that(scope.is_empty()).to_be_true()


func test_observability_scope_rejects_invalid_names_and_values_atomically() -> void:
	var scope := ObservabilityScope.new()
	Expect.that(scope.set_tag("stable", "kept")).to_be_true()
	Expect.that(scope.set_context("stable", {"value": 7})).to_be_true()
	var original_contexts: Dictionary = scope.contexts()

	Expect.that(scope.set_tag("", "value")).to_be_false()
	Expect.that(scope.set_tag(" padded", "value")).to_be_false()
	Expect.that(scope.set_tag("padded ", "value")).to_be_false()
	Expect.that(scope.set_tag("bad\nname", "value")).to_be_false()
	Expect.that(scope.remove_tag(" bad")).to_be_false()
	Expect.that(scope.set_context("", {})).to_be_false()
	Expect.that(scope.set_context(" padded", {})).to_be_false()
	Expect.that(scope.set_context("bad\u007fname", {})).to_be_false()
	Expect.that(scope.remove_context("bad\tname")).to_be_false()

	Expect.that(scope.set_context("nan", {"value": NAN})).to_be_false()
	Expect.that(scope.set_context("infinity", {"value": INF})).to_be_false()
	Expect.that(scope.set_context("object", {"value": RefCounted.new()})).to_be_false()
	Expect.that(scope.set_context("unsupported key", {7: "value"})).to_be_false()

	var cyclic_array: Array = []
	cyclic_array.append(cyclic_array)
	Expect.that(scope.set_context("array cycle", {"value": cyclic_array})).to_be_false()
	var cyclic_dictionary: Dictionary = {}
	cyclic_dictionary["self"] = cyclic_dictionary
	Expect.that(scope.set_context("dictionary cycle", cyclic_dictionary)).to_be_false()

	Expect.that(scope.tags()).to_equal({"stable": "kept"})
	Expect.that(scope.contexts()).to_equal(original_contexts)


func test_observability_scope_enforces_depth_and_item_limits_atomically() -> void:
	var scope := ObservabilityScope.new()
	Expect.that(scope.set_context("stable", {"value": 7})).to_be_true()

	var accepted_depth: Array = []
	var accepted_cursor: Array = accepted_depth
	for _index: int in range(ObservabilityScope.MAX_CONTAINER_DEPTH - 1):
		var child: Array = []
		accepted_cursor.append(child)
		accepted_cursor = child
	Expect.that(scope.set_context("accepted depth", {"value": accepted_depth})).to_be_true()

	var excessive_depth: Array = []
	var excessive_cursor: Array = excessive_depth
	for _index: int in range(ObservabilityScope.MAX_CONTAINER_DEPTH):
		var child: Array = []
		excessive_cursor.append(child)
		excessive_cursor = child
	Expect.that(scope.set_context("excessive depth", {"value": excessive_depth})).to_be_false()

	var accepted_items: Dictionary = {}
	for index: int in range(ObservabilityScope.MAX_CONTAINER_ITEMS):
		accepted_items["item-%s" % index] = index
	Expect.that(scope.set_context("accepted items", accepted_items)).to_be_true()

	var excessive_items: Dictionary = accepted_items.duplicate()
	excessive_items["overflow"] = true
	Expect.that(scope.set_context("excessive items", excessive_items)).to_be_false()
	Expect.that(scope.contexts().has("excessive depth")).to_be_false()
	Expect.that(scope.contexts().has("excessive items")).to_be_false()
	Expect.that(scope.contexts()["stable"]).to_equal({"value": 7})


func test_observability_scope_preserves_repeated_noncyclic_containers() -> void:
	var shared: Dictionary = {"players": [&"Mina", "Bo"]}
	var scope := ObservabilityScope.new()

	Expect.that(scope.set_context("match", {
		"first": shared,
		"second": shared,
	})).to_be_true()
	shared["players"][0] = "Changed source"

	var exposed: Dictionary = scope.contexts()
	exposed["match"]["first"]["players"][0] = "Changed first copy"
	Expect.that(exposed["match"]["second"]["players"]).to_equal(["Mina", "Bo"])
	Expect.that(scope.contexts()["match"]).to_equal({
		"first": {"players": ["Mina", "Bo"]},
		"second": {"players": ["Mina", "Bo"]},
	})


func test_observability_event_snapshots_scope_and_returns_isolated_copies() -> void:
	var source_scope := ObservabilityScope.new()
	Expect.that(source_scope.set_tag("region", "iad")).to_be_true()
	Expect.that(source_scope.set_context("game", {"round": 3})).to_be_true()
	var event := ObservabilityEvent.new(
			p_attributes = {},
			p_scope = source_scope,
	)
	source_scope.set_tag("region", "fra")
	source_scope.set_context("game", {"round": 99})

	var exposed_scope: ObservabilityScope = event.scope()
	exposed_scope.set_tag("region", "syd")
	exposed_scope.set_context("game", {"round": 100})

	Expect.that(event.scope().tags()).to_equal({"region": "iad"})
	Expect.that(event.scope().contexts()).to_equal({"game": {"round": 3}})
	Expect.that(ObservabilityEvent.new().scope()).to_be_null()


func test_observability_breadcrumb_appends_type_without_changing_existing_positions() -> void:
	var legacy := ObservabilityBreadcrumb.new(
			"door opened",
			ObservabilityLevel.INFO,
			&"navigation",
			1234,
			{"door": "north"},
	)
	var typed := ObservabilityBreadcrumb.new(
			p_message = "request sent",
			p_attributes = {},
			p_type = &"http",
	)

	Expect.that(legacy.message()).to_equal("door opened")
	Expect.that(legacy.attributes()).to_equal({"door": "north"})
	Expect.that(legacy.type()).to_equal(&"default")
	Expect.that(typed.type()).to_equal(&"http")


func test_global_scope_operations_delegate_and_clear_prior_errors() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.set_tag("", "invalid")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.set_tag("region", "iad")).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_tag("region")).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.clear_tags()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.set_context("match", {
		"id": "m-1",
		"teams": [{"name": "red"}, {"name": "blue"}],
	})).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_context("match")).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.clear_contexts()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.set_user(
			ObservabilityUser.new("player-7", "Mina", "mina@example.com"),
		)).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_user()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(provider.calls).to_equal([
		&"set_tag",
		&"remove_tag",
		&"clear_tags",
		&"set_context",
		&"remove_context",
		&"clear_contexts",
		&"set_user",
		&"remove_user",
	])
	service.shutdown()


func test_global_scope_operations_validate_before_calling_provider() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.set_tag(" padded", "iad")).to_be_false()
	Expect.that(service.remove_tag("bad\nname")).to_be_false()
	Expect.that(service.set_context("match", {"bad": NAN})).to_be_false()
	Expect.that(service.set_context(" padded", {})).to_be_false()
	Expect.that(service.remove_context("bad\tname")).to_be_false()
	Expect.that(service.set_user(null)).to_be_false()
	Expect.that(service.set_user(ObservabilityUser.new())).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(provider.calls).to_have_size(0)
	service.shutdown()


func test_global_scope_requires_complete_capability_without_blocking_events() -> void:
	var service: FoundryObservability = _service()
	var provider := ScopelessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.set_tag(" invalid", "iad")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.set_tag("region", "iad")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("still works")).to_equal("scopeless:1")
	Expect.that(provider.capture_count).to_equal(1)
	service.shutdown()


func test_partial_scope_capability_is_unavailable_without_blocking_unscoped_events() -> void:
	var service: FoundryObservability = _service()
	var provider := PartialScopeObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.capture_message("unscoped")).to_equal("partial-scope:1")
	Expect.that(provider.capture_count).to_equal(1)

	Expect.that(service.set_tag("region", "iad")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(provider.scope_call_count).to_equal(0)

	var local_scope := ObservabilityScope.new()
	Expect.that(local_scope.set_tag("region", "fra")).to_be_true()
	Expect.that(service.capture_message(
			"scoped",
			ObservabilityLevel.INFO,
			{},
			local_scope,
		)).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(provider.capture_count).to_equal(1)
	Expect.that(provider.scope_call_count).to_equal(0)
	service.shutdown()


func test_global_scope_provider_false_and_non_boolean_results_fail() -> void:
	var service: FoundryObservability = _service()
	var rejecting := RecordingScopeProvider.new()
	Expect.that(service.configure(rejecting, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	rejecting.operation_result = false
	Expect.that(service.set_tag("region", "iad")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()

	var non_boolean_service: FoundryObservability = _service()
	var non_boolean := NonBooleanScopeProvider.new()
	Expect.that(non_boolean_service.configure(non_boolean, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	Expect.that(non_boolean_service.set_tag("region", "iad")).to_be_false()
	Expect.that(non_boolean_service.last_error()).to_equal(Error.FAILED)
	non_boolean_service.shutdown()


func test_disabled_scope_operations_do_not_call_provider() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_enabled = false,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.set_tag("region", "iad")).to_be_false()
	Expect.that(service.remove_tag("region")).to_be_false()
	Expect.that(service.clear_tags()).to_be_false()
	Expect.that(service.set_context("match", {"id": "m-1"})).to_be_false()
	Expect.that(service.remove_context("match")).to_be_false()
	Expect.that(service.clear_contexts()).to_be_false()
	Expect.that(service.set_user(ObservabilityUser.new("player-7"))).to_be_false()
	Expect.that(service.remove_user()).to_be_false()
	Expect.that(service.clear_breadcrumbs()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(provider.calls).to_have_size(0)
	service.shutdown()


func test_scopeless_provider_rejects_nonempty_event_scope_but_accepts_empty_scope() -> void:
	var service: FoundryObservability = _service()
	var provider := ScopelessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var nonempty_scope := ObservabilityScope.new()
	Expect.that(nonempty_scope.set_tag("region", "iad")).to_be_true()

	Expect.that(service.capture_event(ObservabilityEvent.new(
			p_message = "scoped",
			p_attributes = {},
			p_scope = nonempty_scope,
		))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(provider.capture_count).to_equal(0)

	Expect.that(service.capture_event(ObservabilityEvent.new(
			p_message = "empty scope",
			p_attributes = {},
			p_scope = ObservabilityScope.new(),
		))).to_equal("scopeless:1")
	Expect.that(provider.capture_count).to_equal(1)
	service.shutdown()


func test_convenience_capture_methods_append_and_preserve_event_scope() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var scope := ObservabilityScope.new()
	Expect.that(scope.set_tag("region", "iad")).to_be_true()

	Expect.that(service.capture_message(
			"message", ObservabilityLevel.INFO, {"kind": "message"}, scope,
		)).to_equal("recording-scope:1")
	Expect.that(service.capture_exception(
			ObservabilityException.new("Failure", "exception", "stack", {}),
			{"kind": "exception"},
			scope,
		)).to_equal("recording-scope:2")
	Expect.that(service.capture_log(
			"log", ObservabilityLevel.WARN, &"game", -1, {"kind": "log"}, 1234, scope,
		)).to_equal("recording-scope:3")

	for event: ObservabilityEvent in provider.captured_events:
		Expect.that(event.scope().tags()).to_equal({"region": "iad"})
	service.shutdown()


func test_timestamp_and_exception_normalization_preserve_scope_snapshots() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var scope := ObservabilityScope.new()
	Expect.that(scope.set_context("match", {"id": "m-1"})).to_be_true()

	Expect.that(service.capture_event(ObservabilityEvent.new(
			p_kind = &"exception",
			p_message = "failure",
			p_exception = ObservabilityException.new(
				p_type_name = "Failure",
				p_message = "failure",
				p_attributes = {},
				p_frames = [ObservabilityStackFrame.new(p_function = "run")],
			),
			p_attributes = {},
			p_scope = scope,
		))).to_equal("recording-scope:1")
	scope.set_context("match", {"id": "changed"})

	var captured: ObservabilityEvent = provider.captured_events[0]
	Expect.that(captured.timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(captured.exception().frames()).to_have_size(1)
	Expect.that(captured.scope().contexts()).to_equal({"match": {"id": "m-1"}})
	service.shutdown()


func test_clear_breadcrumbs_reports_capability_results() -> void:
	var service: FoundryObservability = _service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.clear_breadcrumbs()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	provider.operation_result = false
	Expect.that(service.clear_breadcrumbs()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()

	var missing_service: FoundryObservability = _service()
	Expect.that(missing_service.configure(
			ScopelessObservabilityProvider.new(),
			ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {},
				p_automatic_capture_enabled = false,
			),
		)).to_equal(Error.OK)
	Expect.that(missing_service.clear_breadcrumbs()).to_be_false()
	Expect.that(missing_service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	missing_service.shutdown()

	var non_boolean_service: FoundryObservability = _service()
	Expect.that(non_boolean_service.configure(
			NonBooleanScopeProvider.new(),
			ObservabilityConfig.new(
				p_global_attributes = {},
				p_provider_options = {},
				p_automatic_capture_enabled = false,
			),
		)).to_equal(Error.OK)
	Expect.that(non_boolean_service.clear_breadcrumbs()).to_be_false()
	Expect.that(non_boolean_service.last_error()).to_equal(Error.FAILED)
	non_boolean_service.shutdown()


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


func test_stack_frame_and_exception_defensively_copy_structured_data() -> void:
	var source_pre_context := PackedStringArray(["if target.is_alive():"])
	var source_post_context := PackedStringArray(["return damage"])
	var source_variables := {"combat": {"damage": 10}}
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_function = "attack",
			p_line = 42,
			p_language = "foundryscript",
			p_in_app = true,
			p_context_line = "deal_damage()",
			p_pre_context = source_pre_context,
			p_post_context = source_post_context,
			p_variables = source_variables,
		)
	var source_frames: Array[ObservabilityStackFrame] = [frame]
	var exception := ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = source_frames,
		)

	source_pre_context.append("changed before")
	source_post_context.append("changed after")
	source_variables["combat"]["damage"] = 99
	source_frames.clear()
	var exposed_pre_context := frame.pre_context()
	var exposed_post_context := frame.post_context()
	var exposed_variables := frame.variables()
	var exposed_frames := exception.frames()
	exposed_pre_context.append("changed accessor")
	exposed_post_context.append("changed accessor")
	exposed_variables["combat"]["damage"] = 100
	exposed_frames.clear()

	Expect.that(frame.file()).to_equal("res://player.fs")
	Expect.that(frame.function()).to_equal("attack")
	Expect.that(frame.line()).to_equal(42)
	Expect.that(frame.language()).to_equal("foundryscript")
	Expect.that(frame.in_app()).to_be_true()
	Expect.that(frame.context_line()).to_equal("deal_damage()")
	Expect.that(frame.pre_context()).to_equal(PackedStringArray(["if target.is_alive():"]))
	Expect.that(frame.post_context()).to_equal(PackedStringArray(["return damage"]))
	Expect.that(frame.variables()).to_equal({"combat": {"damage": 10}})
	Expect.that(exception.stack_trace()).to_equal("formatted fallback")
	Expect.that(exception.frames()).to_equal([frame])


func test_stack_frame_defaults_and_legacy_exception_positional_arguments() -> void:
	var frame := ObservabilityStackFrame.new()
	var exception := ObservabilityException.new("Legacy", "message", "stack", {})

	Expect.that(frame.file()).to_equal("")
	Expect.that(frame.function()).to_equal("")
	Expect.that(frame.line()).to_equal(-1)
	Expect.that(frame.language()).to_equal("")
	Expect.that(frame.in_app()).to_be_true()
	Expect.that(frame.context_line()).to_equal("")
	Expect.that(frame.pre_context()).to_equal(PackedStringArray())
	Expect.that(frame.post_context()).to_equal(PackedStringArray())
	Expect.that(frame.variables()).to_equal({})
	Expect.that(exception.type_name()).to_equal("Legacy")
	Expect.that(exception.message()).to_equal("message")
	Expect.that(exception.stack_trace()).to_equal("stack")
	Expect.that(exception.attributes()).to_equal({})
	Expect.that(exception.frames()).to_equal([])


func test_stack_frame_capture_defaults_keep_context_and_remove_variables() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new()
	var frame := ObservabilityStackFrame.new(
			p_file = "res://player.fs",
			p_function = "attack",
			p_line = 42,
			p_language = "foundryscript",
			p_context_line = "deal_damage()",
			p_pre_context = PackedStringArray([
				"discard", "keep 1", "keep 2", "keep 3", "keep 4", "keep 5",
			]),
			p_post_context = PackedStringArray([
				"keep 1", "keep 2", "keep 3", "keep 4", "keep 5", "discard",
			]),
			p_variables = {"secret": "do not capture"},
		)

	Expect.that(config.stack_trace_source_context_enabled).to_be_true()
	Expect.that(config.stack_trace_variables_enabled).to_be_false()
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_event(ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = "attack failed",
			p_timestamp_msec = 1234,
			p_attributes = {},
			p_exception = ObservabilityException.new(
					p_type_name = "CombatError",
					p_message = "attack failed",
					p_stack_trace = "formatted fallback",
					p_attributes = {},
					p_frames = [frame],
			),
	))).to_equal("memory:1")

	var captured_frame: ObservabilityStackFrame = provider.events()[0].exception().frames()[0]
	Expect.that(captured_frame.context_line()).to_equal("deal_damage()")
	Expect.that(captured_frame.pre_context()).to_equal(PackedStringArray([
			"keep 1", "keep 2", "keep 3", "keep 4", "keep 5",
	]))
	Expect.that(captured_frame.post_context()).to_equal(PackedStringArray([
			"keep 1", "keep 2", "keep 3", "keep 4", "keep 5",
	]))
	Expect.that(captured_frame.variables()).to_equal({})
	service.shutdown()


func test_stack_frame_capture_can_disable_context_and_enable_variables() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_source_context_enabled = false,
			p_stack_trace_variables_enabled = true,
	)
	var frame := ObservabilityStackFrame.new(
			p_function = "attack",
			p_line = 0,
			p_context_line = "deal_damage()",
			p_pre_context = PackedStringArray(["before"]),
			p_post_context = PackedStringArray(["after"]),
			p_variables = {
				"flag": true,
				"count": 5,
				"ratio": 2.5,
				"label": &"sword",
				"nested": {
					"name": &"Knight",
					"values": [false, 9, "safe", NAN, Vector2(1.0, 2.0)],
					"bad vector": Vector2(3.0, 4.0),
					7: "bad key",
				},
			},
		)

	Expect.that(config.stack_trace_source_context_enabled).to_be_false()
	Expect.that(config.stack_trace_variables_enabled).to_be_true()
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	var captured_frame: ObservabilityStackFrame = provider.events()[0].exception().frames()[0]
	Expect.that(captured_frame.line()).to_equal(-1)
	Expect.that(captured_frame.context_line()).to_equal("")
	Expect.that(captured_frame.pre_context()).to_equal(PackedStringArray())
	Expect.that(captured_frame.post_context()).to_equal(PackedStringArray())
	Expect.that(captured_frame.variables()).to_equal({
			"flag": true,
			"count": 5,
			"ratio": 2.5,
			"label": "sword",
			"nested": {"name": "Knight", "values": [false, 9, "safe"]},
	})
	service.shutdown()


func test_stack_frame_capture_drops_empty_frames_and_preserves_partial_fallback() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var frames: Array[ObservabilityStackFrame] = [
		null,
		ObservabilityStackFrame.new(),
		ObservabilityStackFrame.new(p_language = "foundryscript"),
	]

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = frames,
	))).to_equal("memory:1")

	var captured_exception: ObservabilityException = provider.events()[0].exception()
	Expect.that(captured_exception.stack_trace()).to_equal("formatted fallback")
	Expect.that(captured_exception.frames()).to_have_size(1)
	Expect.that(captured_exception.frames()[0].language()).to_equal("foundryscript")
	service.shutdown()


func test_stack_frame_capture_drops_non_identity_frames_and_keeps_partial_identity() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var exception := ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [
				ObservabilityStackFrame.new(),
				ObservabilityStackFrame.new(p_in_app = true),
				ObservabilityStackFrame.new(
						p_in_app = false,
						p_context_line = "context only",
				),
				ObservabilityStackFrame.new(
						p_in_app = false,
						p_pre_context = PackedStringArray(["pre-context only"]),
				),
				ObservabilityStackFrame.new(
						p_in_app = false,
						p_pre_context = PackedStringArray(),
						p_post_context = PackedStringArray(["post-context only"]),
				),
				ObservabilityStackFrame.new(
						p_in_app = false,
						p_pre_context = PackedStringArray(),
						p_post_context = PackedStringArray(),
						p_variables = {"variables only": 1},
				),
				ObservabilityStackFrame.new(
						p_line = 0,
						p_in_app = false,
				),
				ObservabilityStackFrame.new(
						p_line = -42,
						p_in_app = false,
				),
				ObservabilityStackFrame.new(
						p_file = "res://identity_only.fs",
						p_in_app = false,
				),
				ObservabilityStackFrame.new(
						p_language = "foundryscript",
						p_in_app = false,
				),
				ObservabilityStackFrame.new(
						p_function = "attack",
						p_in_app = false,
				),
				ObservabilityStackFrame.new(
						p_line = 42,
						p_in_app = false,
				),
			],
	)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_source_context_enabled = true,
			p_stack_trace_variables_enabled = true,
	)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_exception(exception)).to_equal("memory:1")
	var captured_frames: Array[ObservabilityStackFrame] = provider.events()[0].exception().frames()
	Expect.that(captured_frames).to_have_size(4)
	Expect.that(captured_frames[0].file()).to_equal("res://identity_only.fs")
	Expect.that(captured_frames[1].language()).to_equal("foundryscript")
	Expect.that(captured_frames[2].function()).to_equal("attack")
	Expect.that(captured_frames[3].line()).to_equal(42)
	service.shutdown()


func test_stack_frame_capture_uses_bounded_internal_variables_and_skips_dropped_frames() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var useful_frame := VariableCaptureProbeFrame.new("res://capture.fs", {"stored": true})
	var dropped_frame := VariableCaptureProbeFrame.new("", {"stored": true})

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [useful_frame, dropped_frame],
	))).to_equal("memory:1")

	Expect.that(useful_frame.public_variables_calls).to_equal(0)
	Expect.that(dropped_frame.public_variables_calls).to_equal(0)
	var captured_frames: Array[ObservabilityStackFrame] = provider.events()[0].exception().frames()
	Expect.that(captured_frames).to_have_size(1)
	Expect.that(captured_frames[0].variables()).to_equal({"stored": true})
	service.shutdown()


func test_stack_frame_capture_clears_nearby_context_without_a_current_line() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var frame := ObservabilityStackFrame.new(
			p_function = "attack",
			p_in_app = false,
			p_pre_context = PackedStringArray([
				"discard", "keep 1", "keep 2", "keep 3", "keep 4", "keep 5",
			]),
			p_post_context = PackedStringArray([
				"keep 1", "keep 2", "keep 3", "keep 4", "keep 5", "discard",
			]),
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	var captured_frame: ObservabilityStackFrame = provider.events()[0].exception().frames()[0]
	Expect.that(captured_frame.context_line()).to_equal("")
	Expect.that(captured_frame.pre_context()).to_equal(PackedStringArray())
	Expect.that(captured_frame.post_context()).to_equal(PackedStringArray())
	service.shutdown()


func test_stack_frame_capture_normalizes_string_name_variable_keys() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var frame := ObservabilityStackFrame.new(
			p_file = "res://variables.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = {
				&"top": &"value",
				"nested": {&"child": &"nested value"},
			},
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	Expect.that(provider.events()[0].exception().frames()[0].variables()).to_equal({
			"top": "value",
			"nested": {"child": "nested value"},
	})
	service.shutdown()


func test_stack_frame_capture_bounds_variable_container_depth() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var at_limit: Array = ["leaf"]
	for _index: int in range(7):
		at_limit = [at_limit]
	var beyond_limit: Array = ["leaf"]
	for _index: int in range(8):
		beyond_limit = [beyond_limit]
	var expected_beyond_limit: Array = []
	for _index: int in range(7):
		expected_beyond_limit = [expected_beyond_limit]
	var frame := ObservabilityStackFrame.new(
			p_file = "res://depth.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = {
				"at limit": at_limit,
				"beyond limit": beyond_limit,
			},
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	var variables: Dictionary = provider.events()[0].exception().frames()[0].variables()
	Expect.that(variables["at limit"]).to_equal(at_limit)
	Expect.that(variables["beyond limit"]).to_equal(expected_beyond_limit)
	service.shutdown()


func test_stack_frame_capture_bounds_total_variable_items() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var first_branch: Array = []
	var second_branch: Array = []
	var expected_second_branch: Array = []
	for index: int in range(200):
		first_branch.append(index)
		second_branch.append(index)
		if index < 53:
			expected_second_branch.append(index)
	var frame := ObservabilityStackFrame.new(
			p_file = "res://budget.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = {"branches": [first_branch, second_branch]},
		)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	Expect.that(provider.events()[0].exception().frames()[0].variables()).to_equal({
			"branches": [first_branch, expected_second_branch],
	})
	service.shutdown()


func test_stack_frame_construction_omits_cyclic_array_references() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var cyclic: Array = []
	cyclic.append(cyclic)
	var source_variables: Dictionary = {"cycle": cyclic, "finite": 7}
	var frame := ObservabilityStackFrame.new(
			p_file = "res://cycle.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = source_variables,
		)
	cyclic.append("mutated")
	source_variables["finite"] = 99

	Expect.that(frame.variables()).to_equal({"cycle": [], "finite": 7})

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	var variables: Dictionary = provider.events()[0].exception().frames()[0].variables()
	Expect.that(variables).to_equal({"cycle": [], "finite": 7})
	service.shutdown()


func test_stack_frame_construction_omits_cyclic_dictionary_references() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var cyclic: Dictionary = {"kept": "cycle value"}
	cyclic["self"] = cyclic
	var source_variables: Dictionary = {"cycle": cyclic, "finite": 7}
	var frame := ObservabilityStackFrame.new(
			p_file = "res://dictionary-cycle.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = source_variables,
	)
	cyclic["kept"] = "mutated"
	source_variables["finite"] = 99

	Expect.that(frame.variables()).to_equal({
			"cycle": {"kept": "cycle value"},
			"finite": 7,
	})

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	Expect.that(provider.events()[0].exception().frames()[0].variables()).to_equal({
			"cycle": {"kept": "cycle value"},
			"finite": 7,
	})
	service.shutdown()


func test_stack_frame_construction_preserves_repeated_containers_as_isolated_copies() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var shared: Array = ["shared value"]
	var source_variables: Dictionary = {
		"first": shared,
		"second": shared,
		"finite": 7,
	}
	var frame := ObservabilityStackFrame.new(
			p_file = "res://repeated.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = source_variables,
	)
	shared[0] = "mutated"
	source_variables["finite"] = 99

	var exposed_variables: Dictionary = frame.variables()
	var exposed_first: Array = exposed_variables["first"]
	var exposed_second: Array = exposed_variables.get("second", [])
	exposed_first[0] = "mutated first copy"
	Expect.that(exposed_second).to_equal(["shared value"])
	Expect.that(frame.variables()).to_equal({
			"first": ["shared value"],
			"second": ["shared value"],
			"finite": 7,
	})

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	Expect.that(provider.events()[0].exception().frames()[0].variables()).to_equal({
			"first": ["shared value"],
			"second": ["shared value"],
			"finite": 7,
	})
	service.shutdown()


func test_stack_frame_capture_preserves_valid_repeat_after_unsupported_key() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var shared: Array = ["shared value"]
	var frame := ObservabilityStackFrame.new(
			p_file = "res://invalid-repeat.fs",
			p_in_app = false,
			p_pre_context = PackedStringArray(),
			p_post_context = PackedStringArray(),
			p_variables = {7: shared, "valid": shared},
	)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_stack_trace_variables_enabled = true,
	))).to_equal(Error.OK)
	Expect.that(service.capture_exception(ObservabilityException.new(
			p_type_name = "CombatError",
			p_message = "attack failed",
			p_stack_trace = "formatted fallback",
			p_attributes = {},
			p_frames = [frame],
	))).to_equal("memory:1")

	Expect.that(provider.events()[0].exception().frames()[0].variables()).to_equal({
			"valid": ["shared value"],
	})
	service.shutdown()


func test_stack_frame_internal_sanitizer_omits_mutual_cycle_back_edge() -> void:
	var cyclic_array: Array = []
	var cyclic_dictionary: Dictionary = {"kept": "cycle value"}
	cyclic_dictionary["array"] = cyclic_array
	cyclic_array.append(cyclic_dictionary)
	var frame := ObservabilityStackFrame.new()

	var sanitized: Dictionary = frame._bounded_sanitized_variable_source(
			{"cycle": cyclic_array, "finite": 7},
			ObservabilityStackFrame.MAX_VARIABLE_CONTAINER_DEPTH,
			ObservabilityStackFrame.MAX_VARIABLE_ITEMS,
	)

	Expect.that(sanitized).to_equal({
			"cycle": [{"kept": "cycle value"}],
			"finite": 7,
	})
	sanitized["cycle"][0]["kept"] = "mutated sanitized copy"
	Expect.that(cyclic_dictionary["kept"]).to_equal("cycle value")


func test_event_separates_wall_clock_timestamp_and_engine_ticks() -> void:
	var event := ObservabilityEvent.new(
			p_timestamp_msec = 1721865600123,
			p_attributes = {},
			p_exception = null,
			p_engine_ticks_msec = 4567,
		)
	var epoch := ObservabilityEvent.new(p_timestamp_msec = 0)
	var missing := ObservabilityEvent.new()

	Expect.that(event.timestamp_msec()).to_equal(1721865600123)
	Expect.that(event.engine_ticks_msec()).to_equal(4567)
	Expect.that(epoch.timestamp_msec()).to_equal(0)
	Expect.that(ObservabilityEvent.UNASSIGNED_TIMESTAMP).to_equal(-1)
	Expect.that(missing.timestamp_msec()).to_equal(ObservabilityEvent.UNASSIGNED_TIMESTAMP)
	Expect.that(missing.engine_ticks_msec()).to_equal(-1)


func test_converts_engine_ticks_to_unix_epoch_milliseconds() -> void:
	Expect.that(FoundryObservability._unix_msec_from_engine_ticks(
			4000, 5000, 1721865600000,
		)).to_equal(1721865599000)
	Expect.that(FoundryObservability._unix_msec_from_engine_ticks(
			6000, 5000, 1721865600000,
		)).to_equal(1721865601000)


func test_capture_preserves_custom_wall_time_and_resolves_missing_time() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)

	var explicit := ObservabilityEvent.new(p_message = "explicit", p_timestamp_msec = 0)
	Expect.that(service.capture_event(explicit)).to_equal("memory:1")
	var before_unix_msec: int = floori(Time.get_unix_time_from_system() * 1000.0)
	Expect.that(service.capture_event(ObservabilityEvent.new(
			p_message = "fallback",
		))).to_equal("memory:2")
	var after_unix_msec: int = floori(Time.get_unix_time_from_system() * 1000.0)

	Expect.that(provider.events()[0].timestamp_msec()).to_equal(0)
	var fallback: ObservabilityEvent = provider.events()[1]
	Expect.that(fallback.timestamp_msec()).not_().to_be_less_than(before_unix_msec)
	Expect.that(fallback.timestamp_msec()).not_().to_be_greater_than(after_unix_msec)
	Expect.that(fallback.engine_ticks_msec()).not_().to_be_less_than(0)
	service.shutdown()


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


func test_processing_signal_limits_normalize_negative_values() -> void:
	var limits := ObservabilitySignalLimits.new(-1, -2, -3, -4)
	var copied: ObservabilitySignalLimits = limits.duplicate()

	Expect.that(limits.per_frame()).to_equal(0)
	Expect.that(limits.repeated_window_msec()).to_equal(0)
	Expect.that(limits.window_count()).to_equal(0)
	Expect.that(limits.window_msec()).to_equal(0)
	Expect.that(copied).to_not_equal(limits)
	Expect.that(copied.per_frame()).to_equal(0)


func test_signal_limiter_samples_before_consuming_frame_capacity() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			0.25, ObservabilitySignalLimits.new(1, 0, 0, 0))

	for index: int in range(3):
		Expect.that(limiter.admit("sample-%s" % index, index, 1)).to_equal({
				"accepted": false, "reason": &"sampled", "limit_kind": &"",
		})
	Expect.that(limiter.admit("sample-3", 3, 1)).to_equal({
			"accepted": true, "reason": &"", "limit_kind": &"",
	})
	Expect.that(limiter.admit("sample-4", 4, 1)).to_equal({
			"accepted": false, "reason": &"sampled", "limit_kind": &"",
	})


func test_signal_limiter_stably_samples_decimal_rates() -> void:
	var tenth := ObservabilitySignalLimiter.new(0.1)
	for index: int in range(9):
		Expect.that(tenth.admit("tenth-%s" % index, index, index)["accepted"]).to_be_false()
	Expect.that(tenth.admit("tenth-9", 9, 9)["accepted"]).to_be_true()

	var fixed_run := ObservabilitySignalLimiter.new(0.1)
	var accepted: int = 0
	for index: int in range(1000):
		if fixed_run.admit("fixed-%s" % index, index, index)["accepted"]:
			accepted += 1
	Expect.that(accepted).to_equal(100)


func test_signal_limiter_skips_identity_state_when_repetition_is_disabled() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(0, 0, 0, 0))
	for index: int in range(1025):
		Expect.that(limiter.admit("untracked-%s" % index, index, index)["accepted"]).to_be_true()

	Expect.that(limiter._identity_records).to_equal({})
	Expect.that(limiter._identity_sequence).to_equal(0)


func test_signal_limiter_combines_limits_at_expiry_boundaries() -> void:
	var limiter := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(2, 100, 2, 1000))

	Expect.that(limiter.admit("a", 0, 1)["accepted"]).to_be_true()
	Expect.that(limiter.admit("a", 1, 1)).to_equal({
			"accepted": false, "reason": &"rate_limited", "limit_kind": &"repeated",
	})
	Expect.that(limiter.admit("b", 2, 1)["accepted"]).to_be_true()
	Expect.that(limiter.admit("c", 3, 1)).to_equal({
			"accepted": false, "reason": &"rate_limited", "limit_kind": &"per_frame",
	})
	Expect.that(limiter.admit("c", 4, 2)).to_equal({
			"accepted": false, "reason": &"rate_limited", "limit_kind": &"window",
	})
	Expect.that(limiter.admit("c", 1001, 2)["accepted"]).to_be_true()


func test_signal_limiter_bounds_hashed_identities_and_resets_every_mode() -> void:
	var limits := ObservabilitySignalLimits.new(0, 100000, 0, 0)
	var limiter := ObservabilitySignalLimiter.new(1.0, limits)
	for index: int in range(1025):
		Expect.that(limiter.admit("identity-%s" % index, index, index)["accepted"]).to_be_true()
	Expect.that(limiter.admit("identity-0", 2000, 2000)["accepted"]).to_be_true()
	Expect.that(limiter.admit("identity-1024", 2001, 2001)).to_equal({
			"accepted": false, "reason": &"rate_limited", "limit_kind": &"repeated",
	})
	limiter.reset()
	Expect.that(limiter.admit("identity-1024", 2002, 2002)["accepted"]).to_be_true()
	var resettable := ObservabilitySignalLimiter.new(
			0.5, ObservabilitySignalLimits.new(1, 1000, 1, 1000), 1)
	Expect.that(resettable.admit("reset", 0, 1)["reason"]).to_equal(&"sampled")
	Expect.that(resettable.admit("reset", 0, 1)["accepted"]).to_be_true()
	resettable.reset()
	Expect.that(resettable._sample_compensation).to_equal(0.0)
	Expect.that(resettable.admit("reset", 0, 1)["reason"]).to_equal(&"sampled")
	Expect.that(resettable.admit("reset", 0, 1)["accepted"]).to_be_true()


func test_signal_limiter_defensively_copies_committed_limits() -> void:
	var source := ObservabilitySignalLimits.new(7, 8, 9, 10)
	var limiter := ObservabilitySignalLimiter.new(1.0, source)

	Expect.that(limiter._limits).to_not_equal(source)
	Expect.that(limiter._limits.per_frame()).to_equal(7)
	Expect.that(limiter._limits.repeated_window_msec()).to_equal(8)
	Expect.that(limiter._limits.window_count()).to_equal(9)
	Expect.that(limiter._limits.window_msec()).to_equal(10)


func test_signal_limiter_rejections_do_not_reserve_any_other_limit() -> void:
	var legacy := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(1, 0, 0, 0), 1)
	Expect.that(legacy.admit("first", 0, 1)["accepted"]).to_be_true()
	Expect.that(legacy.admit("legacy-drop", 500, 2)).to_equal({
			"accepted": false, "reason": &"rate_limited", "limit_kind": &"legacy_log_window",
	})
	Expect.that(legacy.admit("next-second", 1000, 2)["accepted"]).to_be_true()

	var repeated := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(1, 1000, 0, 0))
	Expect.that(repeated.admit("a", 0, 1)["accepted"]).to_be_true()
	Expect.that(repeated.admit("a", 1, 2)["limit_kind"]).to_equal(&"repeated")
	Expect.that(repeated.admit("b", 2, 2)["accepted"]).to_be_true()

	var window := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(1, 0, 1, 1000))
	Expect.that(window.admit("a", 0, 1)["accepted"]).to_be_true()
	Expect.that(window.admit("b", 1, 2)["limit_kind"]).to_equal(&"window")
	Expect.that(window.admit("c", 2, 2)["limit_kind"]).to_equal(&"window")
	Expect.that(window.admit("d", 1000, 2)["accepted"]).to_be_true()
	var repeated_boundary := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(0, 100, 0, 0))
	Expect.that(repeated_boundary.admit("boundary", 0, 1)["accepted"]).to_be_true()
	Expect.that(repeated_boundary.admit("boundary", 99, 2)["limit_kind"]).to_equal(&"repeated")
	Expect.that(repeated_boundary.admit("boundary", 100, 3)["accepted"]).to_be_true()


func test_signal_limiter_normalizes_samples_and_handles_backward_inputs() -> void:
	var disabled := ObservabilitySignalLimiter.new(0.0)
	Expect.that(disabled.admit("never", 0, 0)).to_equal({
			"accepted": false, "reason": &"sampled", "limit_kind": &"",
	})
	var clamped := ObservabilitySignalLimiter.new(2.0)
	Expect.that(clamped.admit("always", 0, 0)["accepted"]).to_be_true()
	var negative := ObservabilitySignalLimiter.new(-1.0)
	Expect.that(negative.admit("never", 0, 0)["accepted"]).to_be_false()
	var legacy_disabled := ObservabilitySignalLimiter.new(1.0, null, -1)
	Expect.that(legacy_disabled.admit("first", 0, 0)["accepted"]).to_be_true()
	Expect.that(legacy_disabled.admit("second", 1, 1)["accepted"]).to_be_true()
	var backward := ObservabilitySignalLimiter.new(
			1.0, ObservabilitySignalLimits.new(1, 100, 1, 100))
	Expect.that(backward.admit("first", 100, 4)["accepted"]).to_be_true()
	Expect.that(backward.admit("next", 50, 3)["limit_kind"]).to_equal(&"window")
	Expect.that(backward.admit("next", 51, 3)["limit_kind"]).to_equal(&"window")


func test_redaction_rules_copy_paths_and_structured_replacements() -> void:
	var path := PackedStringArray(["request", "password"])
	var replacement := {"status": "redacted", "nested": {"keep": false}}
	var rule := ObservabilityRedactionRule.replace_value(path, replacement)
	path[1] = "changed"
	replacement["status"] = "changed"
	var exposed_replacement: Dictionary = rule.replacement()
	exposed_replacement["status"] = "exposed change"

	Expect.that(rule.path()).to_equal(PackedStringArray(["request", "password"]))
	Expect.that(rule.action()).to_equal(ObservabilityRedactionRule.REPLACE_VALUE)
	Expect.that(rule.replacement()).to_equal({
			"status": "redacted", "nested": {"keep": false},
	})
	Expect.that(rule.is_valid()).to_be_true()
	Expect.that(ObservabilityRedactionRule.remove_field(
			PackedStringArray()).is_valid()).to_be_false()
	Expect.that(ObservabilityRedactionRule.new(
			PackedStringArray(["message"]), 99).is_valid()).to_be_false()
	Expect.that(ObservabilityRedactionRule.new(
			PackedStringArray(["message"]),
			ObservabilityRedactionRule.REPLACE_TEXT,
			"[",
			"replacement",
	).is_valid()).to_be_false()
	Expect.that(ObservabilityRedactionRule.new(
			PackedStringArray(["message"]),
			ObservabilityRedactionRule.REPLACE_TEXT,
			"",
			42,
	).is_valid()).to_be_false()
	Expect.that(ObservabilityRedactionRule.sensitive_key("token").path()).to_equal(
			PackedStringArray(["**", "token"]))


func test_redaction_rules_copy_packed_replacements() -> void:
	var path := PackedStringArray(["payload"])
	var source := PackedStringArray(["secret"])
	var rule := ObservabilityRedactionRule.replace_value(path, source)
	source[0] = "changed"
	var exposed: PackedStringArray = rule.replacement()
	exposed[0] = "exposed change"

	Expect.that(rule.replacement()).to_equal(PackedStringArray(["secret"]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedByteArray([1, 2])).replacement()).to_equal(PackedByteArray([1, 2]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedInt32Array([3, 4])).replacement()).to_equal(PackedInt32Array([3, 4]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedInt64Array([5, 6])).replacement()).to_equal(PackedInt64Array([5, 6]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedFloat32Array([7.0, 8.0])).replacement()).to_equal(
			PackedFloat32Array([7.0, 8.0]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedFloat64Array([9.0, 10.0])).replacement()).to_equal(
			PackedFloat64Array([9.0, 10.0]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedVector2Array([Vector2(1.0, 2.0)])).replacement()).to_equal(
			PackedVector2Array([Vector2(1.0, 2.0)]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedVector3Array([Vector3(3.0, 4.0, 5.0)])).replacement()).to_equal(
			PackedVector3Array([Vector3(3.0, 4.0, 5.0)]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedVector4Array([Vector4(6.0, 7.0, 8.0, 9.0)])).replacement()).to_equal(
			PackedVector4Array([Vector4(6.0, 7.0, 8.0, 9.0)]))
	Expect.that(ObservabilityRedactionRule.replace_value(
			path, PackedColorArray([Color(0.1, 0.2, 0.3, 0.4)])).replacement()).to_equal(
			PackedColorArray([Color(0.1, 0.2, 0.3, 0.4)]))


func test_redaction_rules_copy_nested_packed_replacements() -> void:
	var path := PackedStringArray(["payload"])
	var dictionary_source := {"packed": PackedStringArray(["dictionary secret"])}
	var dictionary_rule := ObservabilityRedactionRule.replace_value(path, dictionary_source)
	var dictionary_input_packed: PackedStringArray = dictionary_source["packed"]
	dictionary_input_packed[0] = "changed input"
	var dictionary_exposed: Dictionary = dictionary_rule.replacement()
	var dictionary_exposed_packed: PackedStringArray = dictionary_exposed["packed"]
	dictionary_exposed_packed[0] = "changed output"

	var array_source: Array = [PackedStringArray(["array secret"])]
	var array_rule := ObservabilityRedactionRule.replace_value(path, array_source)
	var array_input_packed: PackedStringArray = array_source[0]
	array_input_packed[0] = "changed input"
	var array_exposed: Array = array_rule.replacement()
	var array_exposed_packed: PackedStringArray = array_exposed[0]
	array_exposed_packed[0] = "changed output"

	Expect.that(dictionary_rule.replacement()).to_equal({
			"packed": PackedStringArray(["dictionary secret"]),
	})
	Expect.that(array_rule.replacement()).to_equal([
			PackedStringArray(["array secret"]),
	])


func test_redaction_rules_reject_cyclic_replacements_without_retaining_payloads() -> void:
	var path := PackedStringArray(["payload"])
	var self_cycle: Dictionary = {}
	self_cycle["self"] = self_cycle
	var self_rule := ObservabilityRedactionRule.replace_value(path, self_cycle)
	self_cycle["later"] = "source mutation"

	var first: Array = []
	var second: Dictionary = {"first": first}
	first.append(second)
	var mutual_rule := ObservabilityRedactionRule.replace_value(path, first)
	second["later"] = "source mutation"

	Expect.that(self_rule.is_valid()).to_be_false()
	Expect.that(self_rule.replacement()).to_be_null()
	Expect.that(self_rule.duplicate().is_valid()).to_be_false()
	Expect.that(self_rule.duplicate().replacement()).to_be_null()
	Expect.that(mutual_rule.is_valid()).to_be_false()
	Expect.that(mutual_rule.replacement()).to_be_null()
	Expect.that(mutual_rule.duplicate().is_valid()).to_be_false()
	Expect.that(mutual_rule.duplicate().replacement()).to_be_null()


func test_redaction_rules_allow_repeated_acyclic_replacement_containers() -> void:
	var path := PackedStringArray(["payload"])
	var shared := {"packed": PackedStringArray(["secret"])}
	var source: Array = [shared, shared]
	var rule := ObservabilityRedactionRule.replace_value(path, source)
	var source_packed: PackedStringArray = shared["packed"]
	source_packed[0] = "changed"

	Expect.that(rule.is_valid()).to_be_true()
	Expect.that(rule.replacement()).to_equal([
			{"packed": PackedStringArray(["secret"])},
			{"packed": PackedStringArray(["secret"])},
	])


func test_redaction_policy_copies_ordered_rules() -> void:
	var rules: Array[ObservabilityRedactionRule] = [
			ObservabilityRedactionRule.remove_field(PackedStringArray(["secret"])),
	]
	var policy := ObservabilityRedactionPolicy.new(rules)
	rules.clear()
	var exposed: Array[ObservabilityRedactionRule] = policy.rules()
	exposed.clear()
	var copied: ObservabilityRedactionPolicy = policy.duplicate()

	Expect.that(policy.rules()).to_have_size(1)
	Expect.that(policy.rules()[0].path()).to_equal(PackedStringArray(["secret"]))
	Expect.that(policy.is_valid()).to_be_true()
	Expect.that(copied).to_not_equal(policy)
	Expect.that(copied.rules()).to_have_size(1)


func test_redaction_policy_keeps_null_rules_invalid_without_crashing() -> void:
	var malformed: Array[ObservabilityRedactionRule] = [null]
	var policy := ObservabilityRedactionPolicy.new(malformed)
	var copied: ObservabilityRedactionPolicy = policy.duplicate()

	Expect.that(policy.rules()).to_have_size(1)
	Expect.that(policy.rules()[0]).to_be_null()
	Expect.that(policy.is_valid()).to_be_false()
	Expect.that(copied.rules()).to_have_size(1)
	Expect.that(copied.rules()[0]).to_be_null()
	Expect.that(copied.is_valid()).to_be_false()


func test_redactor_applies_recursive_keys_paths_and_text_patterns() -> void:
	var source_attributes: Dictionary = {
		"password": "secret",
		"nested": {"PASSWORD": "second"},
		"delete_me": "gone",
	}
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.sensitive_key("password"),
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["event", "attributes", "delete_me"]),
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["event", "**"]),
				"[0-9]{3}-[0-9]{2}-[0-9]{4}",
				"[ssn]",
			),
	])
	var redactor := ObservabilityRedactor.new(policy)
	var source := ObservabilityEvent.new(
			p_message = "customer 123-45-6789 and 987-65-4321",
			p_attributes = source_attributes,
		)
	var result: Dictionary = redactor.redact_event(source, &"event")

	Expect.that(result["valid"]).to_be_true()
	var event: ObservabilityEvent = result["value"]
	Expect.that(event).to_not_equal(source)
	Expect.that(event.message()).to_equal("customer [ssn] and [ssn]")
	Expect.that(event.attributes()["password"]).to_equal("[REDACTED]")
	Expect.that(event.attributes()["nested"]["PASSWORD"]).to_equal("[REDACTED]")
	Expect.that(event.attributes().has("delete_me")).to_be_false()
	Expect.that(source.message()).to_equal("customer 123-45-6789 and 987-65-4321")
	Expect.that(source.attributes()).to_equal(source_attributes)


func test_redactor_rebuilds_every_provider_owned_value_type() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["**"]),
				"secret",
				"safe",
			),
	])
	var redactor := ObservabilityRedactor.new(policy)
	var frame := ObservabilityStackFrame.new(
			"secret.fs",
			"secret_function",
			7,
			"secret-language",
			true,
			"secret context",
			PackedStringArray(["secret pre"]),
			PackedStringArray(["secret post"]),
			{"secret_variable": "secret"},
		)
	var exception := ObservabilityException.new(
			"secret-type",
			"secret exception",
			"secret stack",
			{"detail": "secret"},
			[frame],
		)
	var scope := ObservabilityScope.new()
	Expect.that(scope.set_tag("secret-tag", "secret")).to_be_true()
	Expect.that(scope.set_context("secret-context", {"detail": "secret"})).to_be_true()
	var source_event := ObservabilityEvent.new(
			&"secret-kind",
			ObservabilityLevel.ERROR,
			"secret event",
			&"secret-source",
			10,
			{"token": "secret"},
			exception,
			11,
			scope,
		)
	var event_result: Dictionary = redactor.redact_event(source_event, &"log")
	Expect.that(event_result["valid"]).to_be_true()
	var event: ObservabilityEvent = event_result["value"]
	Expect.that(event.kind()).to_equal(&"safe-kind")
	Expect.that(event.message()).to_equal("safe event")
	Expect.that(event.source()).to_equal(&"safe-source")
	Expect.that(event.attributes()["token"]).to_equal("safe")
	Expect.that(event.exception().type_name()).to_equal("safe-type")
	Expect.that(event.exception().message()).to_equal("safe exception")
	Expect.that(event.exception().stack_trace()).to_equal("safe stack")
	Expect.that(event.exception().attributes()["detail"]).to_equal("safe")
	var rebuilt_frame: ObservabilityStackFrame = event.exception().frames()[0]
	Expect.that(rebuilt_frame.file()).to_equal("safe.fs")
	Expect.that(rebuilt_frame.function()).to_equal("safe_function")
	Expect.that(rebuilt_frame.language()).to_equal("safe-language")
	Expect.that(rebuilt_frame.context_line()).to_equal("safe context")
	Expect.that(rebuilt_frame.pre_context()).to_equal(PackedStringArray(["safe pre"]))
	Expect.that(rebuilt_frame.post_context()).to_equal(PackedStringArray(["safe post"]))
	Expect.that(rebuilt_frame.variables()["secret_variable"]).to_equal("safe")
	Expect.that(event.scope().tags()).to_equal({"secret-tag": "safe"})
	Expect.that(event.scope().contexts()).to_equal({
			"secret-context": {"detail": "safe"},
		})
	Expect.that(source_event.message()).to_equal("secret event")
	Expect.that(frame.file()).to_equal("secret.fs")
	Expect.that(scope.tags()).to_equal({"secret-tag": "secret"})

	var metric_result: Dictionary = redactor.redact_metric(ObservabilityMetric.new(
			ObservabilityMetricType.GAUGE,
			"secret.metric",
			1.0,
			"secret-unit",
			{"token": "secret"},
	))
	Expect.that(metric_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var metric: ObservabilityMetric = metric_result["value"] as ObservabilityMetric
	Expect.that(metric.name()).to_equal("safe.metric")
	Expect.that(metric.unit()).to_equal("safe-unit")
	Expect.that(metric.attributes()["token"]).to_equal("safe")

	var contexts_source := {"secret-context": {"token": "secret"}}
	var contexts_result: Dictionary = redactor.redact_contexts(contexts_source)
	Expect.that(contexts_result["valid"]).to_be_true()
	Expect.that(contexts_result["value"]).to_equal({
			"secret-context": {"token": "safe"},
		})
	Expect.that(contexts_source).to_equal({"secret-context": {"token": "secret"}})

	var user_result: Dictionary = redactor.redact_user(ObservabilityUser.new(
			"secret-id",
			"secret-name",
			"secret@example.invalid",
	))
	Expect.that(user_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var user: ObservabilityUser = user_result["value"] as ObservabilityUser
	Expect.that(user.application_user_id()).to_equal("safe-id")
	Expect.that(user.display_name()).to_equal("safe-name")
	Expect.that(user.contact_email()).to_equal("safe@example.invalid")

	var breadcrumb_result: Dictionary = redactor.redact_breadcrumb(
			ObservabilityBreadcrumb.new(
					"secret message",
					ObservabilityLevel.INFO,
					&"secret.category",
					1,
					{"token": "secret"},
					&"secret-type",
				),
		)
	Expect.that(breadcrumb_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var breadcrumb: ObservabilityBreadcrumb = (
			breadcrumb_result["value"] as ObservabilityBreadcrumb
		)
	Expect.that(breadcrumb.message()).to_equal("safe message")
	Expect.that(breadcrumb.category()).to_equal(&"safe.category")
	Expect.that(breadcrumb.type()).to_equal(&"safe-type")
	Expect.that(breadcrumb.attributes()["token"]).to_equal("safe")

	var path_attachment: ObservabilityAttachment = ObservabilityAttachment.from_path(
			"user://private.log",
			"secret.log",
			"secret/plain",
		)
	var attachment_result: Dictionary = redactor.redact_attachment(path_attachment)
	Expect.that(attachment_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var rebuilt_path_attachment: ObservabilityAttachment = (
			attachment_result["value"] as ObservabilityAttachment
		)
	Expect.that(rebuilt_path_attachment.path()).to_equal("user://private.log")
	Expect.that(rebuilt_path_attachment.filename()).to_equal("safe.log")
	Expect.that(rebuilt_path_attachment.content_type()).to_equal("safe/plain")
	Expect.that(path_attachment.filename()).to_equal("secret.log")

	var byte_source := PackedByteArray([1, 2, 3])
	var byte_attachment: ObservabilityAttachment = ObservabilityAttachment.from_bytes(
			byte_source,
			"secret.bin",
			"secret/binary",
		)
	var byte_result: Dictionary = redactor.redact_attachment(byte_attachment)
	byte_source[0] = 9
	Expect.that(byte_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var rebuilt_byte_attachment: ObservabilityAttachment = (
			byte_result["value"] as ObservabilityAttachment
		)
	Expect.that(rebuilt_byte_attachment.bytes()).to_equal(PackedByteArray([1, 2, 3]))
	Expect.that(rebuilt_byte_attachment.filename()).to_equal("safe.bin")

	var payload_source: Dictionary = {
		"path": "/tmp/secret-source.log",
		"filename": "secret.log",
		"content_type": "secret/plain",
		"category": "event.attachment",
		"persistent": true,
	}
	var payload_result: Dictionary = redactor.redact_attachment_payload(payload_source)
	Expect.that(payload_result["valid"]).to_be_true()
	Expect.that(payload_result["value"]["path"]).to_equal("/tmp/secret-source.log")
	Expect.that(payload_result["value"]["filename"]).to_equal("safe.log")
	Expect.that(payload_result["value"]["content_type"]).to_equal("safe/plain")
	Expect.that(payload_result["value"]["category"]).to_equal("event.attachment")
	Expect.that(payload_result["value"]["persistent"]).to_be_true()
	Expect.that(payload_source["filename"]).to_equal("secret.log")

	var payload_bytes := PackedByteArray([4, 5, 6])
	var byte_payload_result: Dictionary = redactor.redact_attachment_payload({
		"bytes": payload_bytes,
		"filename": "secret.data",
		"content_type": "secret/binary",
		"category": "event.view_hierarchy",
	})
	payload_bytes[0] = 9
	Expect.that(byte_payload_result["valid"]).to_be_true()
	Expect.that(byte_payload_result["value"]["bytes"]).to_equal(
			PackedByteArray([4, 5, 6]))
	Expect.that(byte_payload_result["value"]["filename"]).to_equal("safe.data")


func test_redactor_wildcards_are_case_insensitive_and_preserve_input() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["MeTrIc", "*"]),
				"secret",
				"first",
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["metric", "**"]),
				"first|secret",
				"safe",
			),
	])
	var source := ObservabilityMetric.new(
			ObservabilityMetricType.COUNTER,
			"secret",
			3.0,
			"secret",
			{"nested": ["secret", {"token": "secret"}]},
		)
	var result: Dictionary = ObservabilityRedactor.new(policy).redact_metric(source)

	Expect.that(result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var rebuilt: ObservabilityMetric = result["value"] as ObservabilityMetric
	Expect.that(rebuilt.name()).to_equal("safe")
	Expect.that(rebuilt.unit()).to_equal("safe")
	Expect.that(rebuilt.attributes()).to_equal({
			"nested": ["safe", {"token": "safe"}],
		})
	Expect.that(source.name()).to_equal("secret")
	Expect.that(source.attributes()).to_equal({
			"nested": ["secret", {"token": "secret"}],
		})


func test_redactor_fails_closed_without_returning_sensitive_payloads() -> void:
	var incompatible := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_value(
				PackedStringArray(["event", "level"]),
				"not-an-integer",
			),
	])
	var source := ObservabilityEvent.new(
			p_message = "do-not-leak-this-message",
			p_attributes = {"token": "do-not-leak-this-token"},
		)
	var result: Dictionary = ObservabilityRedactor.new(incompatible).redact_event(
			source,
			&"event",
		)
	Expect.that(result).to_equal({"valid": false, "rule_index": 0})
	Expect.that(source.message()).to_equal("do-not-leak-this-message")

	var malformed_rules: Array[ObservabilityRedactionRule] = [null]
	var malformed := ObservabilityRedactor.new(
			ObservabilityRedactionPolicy.new(malformed_rules),
		).redact_event(source, &"event")
	Expect.that(malformed).to_equal({"valid": false, "rule_index": 0})

	var no_policy: Dictionary = ObservabilityRedactor.new(null).redact_event(
			source,
			&"event",
		)
	Expect.that(no_policy["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var isolated: ObservabilityEvent = no_policy["value"] as ObservabilityEvent
	Expect.that(isolated).to_not_equal(source)
	Expect.that(isolated.message()).to_equal("do-not-leak-this-message")


func test_redactor_remove_field_only_removes_dictionary_children() -> void:
	var attribute_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["event", "attributes", "delete_me"]),
			),
	])
	var attribute_result: Dictionary = ObservabilityRedactor.new(
			attribute_policy,
	).redact_event(ObservabilityEvent.new(
			p_attributes = {"delete_me": "secret", "keep": "safe"},
		), &"event")
	Expect.that(attribute_result["valid"]).to_be_true()
	@warning_ignore("unsafe_cast")
	var rebuilt: ObservabilityEvent = (
			attribute_result["value"] as ObservabilityEvent
		)
	Expect.that(rebuilt.attributes()).to_equal({"keep": "safe"})

	for path: PackedStringArray in [
		PackedStringArray(["event"]),
		PackedStringArray(["event", "message"]),
		PackedStringArray(["event", "attributes", "items", "*"]),
	]:
		var policy := ObservabilityRedactionPolicy.new([
			ObservabilityRedactionRule.remove_field(path),
		])
		var result: Dictionary = ObservabilityRedactor.new(policy).redact_event(
				ObservabilityEvent.new(
						p_message = "sensitive-message",
						p_attributes = {"items": ["sensitive-item"]},
					),
				&"event",
			)
		Expect.that(result).to_equal({"valid": false, "rule_index": 0})

	var ordered_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["event", "kind"]),
				"message",
				"renamed",
			),
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["event", "message"]),
			),
	])
	var ordered_result: Dictionary = ObservabilityRedactor.new(
			ordered_policy,
		).redact_event(ObservabilityEvent.new(
			p_message = "sensitive-message",
		), &"event")
	Expect.that(ordered_result).to_equal({"valid": false, "rule_index": 1})


func test_redactor_rejects_cyclic_contexts_without_retaining_payloads() -> void:
	var self_cycle: Dictionary = {}
	self_cycle["self"] = self_cycle
	var self_result: Dictionary = ObservabilityRedactor.new().redact_contexts({
		"cycle": self_cycle,
	})
	Expect.that(self_result).to_equal({"valid": false, "rule_index": -1})
	var replacement_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_value(
				PackedStringArray(["contexts", "cycle"]),
				{"replacement": "safe"},
			),
	])
	var concealed_result: Dictionary = ObservabilityRedactor.new(
			replacement_policy,
		).redact_contexts({"cycle": self_cycle})
	Expect.that(concealed_result).to_equal({"valid": false, "rule_index": -1})

	var mutual_array: Array = []
	var mutual_dictionary: Dictionary = {"array": mutual_array}
	mutual_array.append(mutual_dictionary)
	var mutual_result: Dictionary = ObservabilityRedactor.new().redact_contexts({
		"cycle": mutual_dictionary,
	})
	Expect.that(mutual_result).to_equal({"valid": false, "rule_index": -1})


func test_redactor_bounds_container_depth_and_visited_items_per_call() -> void:
	var deep: Dictionary = {"value": "kept"}
	for _index: int in range(70):
		deep = {"child": deep}
	var deep_result: Dictionary = ObservabilityRedactor.new().redact_event(
			ObservabilityEvent.new(p_attributes = deep),
			&"event",
		)
	Expect.that(deep_result).to_equal({"valid": false, "rule_index": -1})

	var flooded: Array = []
	for index: int in range(10_010):
		flooded.append(index)
	var redactor := ObservabilityRedactor.new()
	var flooded_result: Dictionary = redactor.redact_event(
			ObservabilityEvent.new(p_attributes = {"items": flooded}),
			&"event",
		)
	Expect.that(flooded_result).to_equal({"valid": false, "rule_index": -1})

	var recovery_result: Dictionary = redactor.redact_event(
			ObservabilityEvent.new(p_attributes = {"value": "kept"}),
			&"event",
		)
	Expect.that(recovery_result["valid"]).to_be_true()


func test_redactor_allows_repeated_acyclic_context_containers() -> void:
	var shared: Dictionary = {"value": "secret"}
	var source: Dictionary = {"first": shared, "second": shared}
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["contexts", "**"]),
				"secret",
				"safe",
			),
	])
	var result: Dictionary = ObservabilityRedactor.new(policy).redact_contexts(source)

	Expect.that(result["valid"]).to_be_true()
	Expect.that(result["value"]).to_equal({
		"first": {"value": "safe"},
		"second": {"value": "safe"},
	})
	Expect.that(source).to_equal({
		"first": {"value": "secret"},
		"second": {"value": "secret"},
	})


func test_redactor_attachment_payload_matches_native_mapper_contract() -> void:
	var redactor := ObservabilityRedactor.new()
	var invalid_payloads: Array[Dictionary] = [
		{},
		{"filename": "a", "category": "event.attachment"},
		{
			"filename": "a",
			"category": "event.attachment",
			"path": "/tmp/a",
			"bytes": PackedByteArray([1]),
		},
		{"filename": "a", "category": "event.attachment", "path": ""},
		{"filename": "a", "category": "event.attachment", "path": "relative/a"},
		{"filename": "a", "category": "event.attachment", "bytes": [1, 2]},
		{"filename": "", "category": "event.attachment", "path": "/tmp/a"},
		{"filename": "a", "category": "", "path": "/tmp/a"},
		{"filename": "a", "category": "other", "path": "/tmp/a"},
		{
			"filename": "a",
			"category": "event.attachment",
			"path": "/tmp/a",
			"content_type": 12,
		},
	]
	for payload: Dictionary in invalid_payloads:
		Expect.that(redactor.redact_attachment_payload(payload)).to_equal({
			"valid": false,
			"rule_index": -1,
		})

	var path_result: Dictionary = redactor.redact_attachment_payload({
		"filename": "path.log",
		"category": "event.attachment",
		"path": "/tmp/path.log",
	})
	Expect.that(path_result["valid"]).to_be_true()
	Expect.that(path_result["value"]).to_equal({
		"filename": "path.log",
		"category": "event.attachment",
		"path": "/tmp/path.log",
	})

	var empty_bytes_result: Dictionary = redactor.redact_attachment_payload({
		"filename": "empty.bin",
		"category": "event.attachment",
		"bytes": PackedByteArray(),
	})
	Expect.that(empty_bytes_result).to_equal({
		"valid": true,
		"value": {
			"filename": "empty.bin",
			"category": "event.attachment",
			"bytes": PackedByteArray(),
		},
	})

	var source_bytes := PackedByteArray([1, 2, 3])
	var bytes_result: Dictionary = redactor.redact_attachment_payload({
		"filename": "view.json",
		"content_type": "",
		"category": "event.view_hierarchy",
		"bytes": source_bytes,
	})
	source_bytes[0] = 9
	Expect.that(bytes_result["valid"]).to_be_true()
	Expect.that(bytes_result["value"]["bytes"]).to_equal(PackedByteArray([1, 2, 3]))
	Expect.that(bytes_result["value"]["content_type"]).to_equal("")


func test_redactor_reports_latest_effective_rule_for_invalid_metadata() -> void:
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["attachments", "filename"]),
				"does-not-match",
				"unused",
			),
		ObservabilityRedactionRule.replace_value(
				PackedStringArray(["attachments", "category"]),
				"invalid",
			),
	])
	var source: Dictionary = {
		"filename": "safe.log",
		"content_type": "text/plain",
		"category": "event.attachment",
		"path": "/tmp/safe.log",
	}
	var result: Dictionary = ObservabilityRedactor.new(
			policy,
		).redact_attachment_payload(source)

	Expect.that(result).to_equal({"valid": false, "rule_index": 1})
	Expect.that(source["category"]).to_equal("event.attachment")

	var no_op_result: Dictionary = ObservabilityRedactor.new(
			ObservabilityRedactionPolicy.new([policy.rules()[0]]),
		).redact_attachment_payload(source)
	Expect.that(no_op_result["valid"]).to_be_true()
	Expect.that(no_op_result["value"]["filename"]).to_equal("safe.log")

	var mixed_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["attachments", "content_type"]),
			),
		ObservabilityRedactionRule.replace_value(
				PackedStringArray(["attachments", "category"]),
				"invalid",
			),
	])
	var mixed_result: Dictionary = ObservabilityRedactor.new(
			mixed_policy,
		).redact_attachment_payload(source)
	Expect.that(mixed_result).to_equal({"valid": false, "rule_index": 1})

	var removal_only_policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.remove_field(
				PackedStringArray(["attachments", "filename"]),
			),
	])
	var removal_only_result: Dictionary = ObservabilityRedactor.new(
			removal_only_policy,
		).redact_attachment_payload(source)
	Expect.that(removal_only_result).to_equal({"valid": false, "rule_index": 0})


func test_redactor_fails_closed_on_adversarial_rule_paths() -> void:
	var adversarial_path := PackedStringArray()
	for _index: int in range(257):
		adversarial_path.append("**")
	adversarial_path.append("filename")
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				adversarial_path,
				"safe",
				"changed",
			),
	])
	var result: Dictionary = ObservabilityRedactor.new(
			policy,
		).redact_attachment_payload({
			"filename": "safe.log",
			"category": "event.attachment",
			"path": "/tmp/safe.log",
		})

	Expect.that(result).to_equal({"valid": false, "rule_index": 0})


func test_processing_diagnostic_preserves_payload_free_fields() -> void:
	var diagnostic := ObservabilityProcessingDiagnostic.new(
			7,
			ObservabilityProcessingDiagnostic.EVENT,
			ObservabilityProcessingDiagnostic.DROPPED,
			ObservabilityProcessingDiagnostic.RATE_LIMITED,
			3,
			2,
			ObservabilityProcessingDiagnostic.WINDOW,
			Error.ERR_BUSY,
	)
	var copied: ObservabilityProcessingDiagnostic = diagnostic.duplicate()

	Expect.that(diagnostic.sequence()).to_equal(7)
	Expect.that(diagnostic.processing_signal()).to_equal(
			ObservabilityProcessingDiagnostic.EVENT)
	Expect.that(diagnostic.outcome()).to_equal(ObservabilityProcessingDiagnostic.DROPPED)
	Expect.that(diagnostic.reason()).to_equal(ObservabilityProcessingDiagnostic.RATE_LIMITED)
	Expect.that(diagnostic.processor_index()).to_equal(3)
	Expect.that(diagnostic.rule_index()).to_equal(2)
	Expect.that(diagnostic.limit_kind()).to_equal(ObservabilityProcessingDiagnostic.WINDOW)
	Expect.that(diagnostic.error()).to_equal(Error.ERR_BUSY)
	Expect.that(copied).to_not_equal(diagnostic)
	Expect.that(copied.sequence()).to_equal(7)


func test_processing_config_defaults_and_defensively_copies_inputs() -> void:
	var processors: Array[Callable] = [Callable()]
	var limits := ObservabilitySignalLimits.new(8, 9, 10, 11)
	var log_limits := ObservabilitySignalLimits.new(12, 13, 14, 15)
	var metric_limits := ObservabilitySignalLimits.new(16, 17, 18, 19)
	var policy := ObservabilityRedactionPolicy.new([
			ObservabilityRedactionRule.sensitive_key("authorization"),
	])
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_sample_rate = -0.5,
			p_log_sample_rate = 2.0,
			p_event_processors = processors,
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = limits,
			p_log_limits = log_limits,
			p_metric_limits = metric_limits,
			p_redaction_policy = policy,
	)
	processors.clear()
	var exposed_processors: Array[Callable] = config.event_processors()
	exposed_processors.clear()
	var exposed_policy: ObservabilityRedactionPolicy = config.redaction_policy()
	var exposed_rules: Array[ObservabilityRedactionRule] = exposed_policy.rules()
	exposed_rules.clear()

	Expect.that(config.event_sample_rate).to_be_close_to(-0.5)
	Expect.that(config.log_sample_rate).to_be_close_to(2.0)
	Expect.that(config.metric_sample_rate).to_be_close_to(1.0)
	Expect.that(config.event_processors()).to_have_size(1)
	Expect.that(config.log_processors()).to_have_size(0)
	Expect.that(config.metric_processors()).to_have_size(0)
	Expect.that(config.event_limits().per_frame()).to_equal(8)
	Expect.that(config.log_limits()).to_not_equal(log_limits)
	Expect.that(config.log_limits().per_frame()).to_equal(12)
	Expect.that(config.metric_limits()).to_not_equal(metric_limits)
	Expect.that(config.metric_limits().window_msec()).to_equal(19)
	Expect.that(config.redaction_policy().rules()).to_have_size(1)


func test_processing_config_default_and_legacy_event_limits() -> void:
	var defaults := ObservabilityConfig.new()
	var legacy := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_automatic_events_per_frame = -1,
			p_automatic_repeated_error_window_msec = 2000,
			p_automatic_event_throttle_count = 30,
			p_automatic_event_throttle_window_msec = 4000,
	)

	Expect.that(defaults.event_sample_rate).to_be_close_to(1.0)
	Expect.that(defaults.log_sample_rate).to_be_close_to(1.0)
	Expect.that(defaults.event_processors()).to_have_size(0)
	Expect.that(defaults.log_processors()).to_have_size(0)
	Expect.that(defaults.metric_processors()).to_have_size(0)
	Expect.that(defaults.event_limits().per_frame()).to_equal(5)
	Expect.that(defaults.event_limits().repeated_window_msec()).to_equal(1000)
	Expect.that(defaults.event_limits().window_count()).to_equal(20)
	Expect.that(defaults.event_limits().window_msec()).to_equal(10000)
	Expect.that(defaults.log_limits()).to_not_equal(defaults.log_limits())
	Expect.that(defaults.metric_limits()).to_not_equal(defaults.metric_limits())
	Expect.that(defaults.log_limits().per_frame()).to_equal(0)
	Expect.that(defaults.log_limits().repeated_window_msec()).to_equal(0)
	Expect.that(defaults.log_limits().window_count()).to_equal(0)
	Expect.that(defaults.log_limits().window_msec()).to_equal(0)
	Expect.that(defaults.metric_limits().per_frame()).to_equal(0)
	Expect.that(defaults.metric_limits().repeated_window_msec()).to_equal(0)
	Expect.that(defaults.metric_limits().window_count()).to_equal(0)
	Expect.that(defaults.metric_limits().window_msec()).to_equal(0)
	Expect.that(defaults.redaction_policy().rules()).to_have_size(0)
	Expect.that(legacy.event_limits().per_frame()).to_equal(0)
	Expect.that(legacy.event_limits().repeated_window_msec()).to_equal(2000)
	Expect.that(legacy.event_limits().window_count()).to_equal(30)
	Expect.that(legacy.event_limits().window_msec()).to_equal(4000)


func test_processing_config_derives_implicit_event_limits_from_current_legacy_fields() -> void:
	var config := ObservabilityConfig.new()
	config.automatic_events_per_frame = 11
	config.automatic_repeated_error_window_msec = 12
	config.automatic_event_throttle_count = 13
	config.automatic_event_throttle_window_msec = 14

	Expect.that(config.event_limits().per_frame()).to_equal(11)
	Expect.that(config.event_limits().repeated_window_msec()).to_equal(12)
	Expect.that(config.event_limits().window_count()).to_equal(13)
	Expect.that(config.event_limits().window_msec()).to_equal(14)


func test_processing_config_keeps_explicit_event_limits_after_legacy_mutation() -> void:
	var limits := ObservabilitySignalLimits.new(21, 22, 23, 24)
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = limits,
	)
	config.automatic_events_per_frame = 31
	config.automatic_repeated_error_window_msec = 32
	config.automatic_event_throttle_count = 33
	config.automatic_event_throttle_window_msec = 34

	Expect.that(config.event_limits()).to_not_equal(limits)
	Expect.that(config.event_limits().per_frame()).to_equal(21)
	Expect.that(config.event_limits().repeated_window_msec()).to_equal(22)
	Expect.that(config.event_limits().window_count()).to_equal(23)
	Expect.that(config.event_limits().window_msec()).to_equal(24)


func test_byte_attachment_defensively_copies_data_and_preserves_metadata() -> void:
	var source_bytes := PackedByteArray([1, 2, 3])
	var attachment := ObservabilityAttachment.from_bytes(
			source_bytes,
			"diagnostics.bin",
			"application/x-diagnostics",
			ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY,
		)
	source_bytes[0] = 9

	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return
	Expect.that(attachment.path()).to_equal("")
	Expect.that(attachment.bytes()).to_equal(PackedByteArray([1, 2, 3]))
	Expect.that(attachment.filename()).to_equal("diagnostics.bin")
	Expect.that(attachment.effective_filename()).to_equal("diagnostics.bin")
	Expect.that(attachment.content_type()).to_equal("application/x-diagnostics")
	Expect.that(attachment.category()).to_equal(
			ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY)
	Expect.that(attachment.is_bytes()).to_be_true()
	Expect.that(attachment.is_path()).to_be_false()
	Expect.that(attachment.is_valid()).to_be_true()

	var exposed_bytes: PackedByteArray = attachment.bytes()
	exposed_bytes[1] = 8
	Expect.that(attachment.bytes()).to_equal(PackedByteArray([1, 2, 3]))


func test_path_attachment_preserves_path_and_derives_effective_filename() -> void:
	var attachment := ObservabilityAttachment.from_path(
			"user://diagnostics/session/game.log",
		)

	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return
	Expect.that(attachment.path()).to_equal("user://diagnostics/session/game.log")
	Expect.that(attachment.bytes()).to_equal(PackedByteArray())
	Expect.that(attachment.filename()).to_equal("")
	Expect.that(attachment.effective_filename()).to_equal("game.log")
	Expect.that(attachment.content_type()).to_equal(
			ObservabilityAttachment.DEFAULT_CONTENT_TYPE)
	Expect.that(attachment.category()).to_equal(
			ObservabilityAttachment.DEFAULT_CATEGORY)
	Expect.that(attachment.is_path()).to_be_true()
	Expect.that(attachment.is_bytes()).to_be_false()
	Expect.that(attachment.is_valid()).to_be_true()


func test_attachment_factories_reject_invalid_inputs() -> void:
	Expect.that(ObservabilityAttachment.from_path("")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("diagnostics/game.log")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("user://")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("res://")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("/")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path(
			"user://diagnostics/",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_path(
			"user://game.log",
			"bad\nname.log",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(),
			"",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(),
			"game.log",
			" text/plain",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(),
			"game.log",
			"",
			&"",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(),
			"game.log",
			"",
			&"event.minidump",
		)).to_be_null()
	Expect.that(ObservabilityAttachment.from_path(
			"user://diagnostics/",
			"diagnostics.log",
		)).not_().to_be_null()


func test_attachment_failure_preserves_diagnostic_fields() -> void:
	var failure := ObservabilityAttachmentFailure.new(
			"attachment:7",
			"game.log",
			ObservabilityAttachmentFailure.MISSING_FILE,
			Error.ERR_FILE_NOT_FOUND,
		)
	var copied_failure: ObservabilityAttachmentFailure = failure.duplicate()

	Expect.that(failure.handle()).to_equal("attachment:7")
	Expect.that(failure.filename()).to_equal("game.log")
	Expect.that(failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE)
	Expect.that(failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)
	Expect.that(copied_failure).to_not_equal(failure)
	Expect.that(copied_failure.handle()).to_equal("attachment:7")
	Expect.that(copied_failure.filename()).to_equal("game.log")
	Expect.that(copied_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE)
	Expect.that(copied_failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)


func test_automatic_capture_masks_and_config_defaults() -> void:
	var config := ObservabilityConfig.new()

	Expect.that(config.max_breadcrumbs).to_equal(100)
	Expect.that(config.max_attachment_bytes).to_equal(20 * 1024 * 1024)
	Expect.that(config.attach_game_log).to_be_false()
	Expect.that(config.attach_screenshot).to_be_false()
	Expect.that(config.attach_scene_tree).to_be_false()
	Expect.that(config.automatic_capture_enabled).to_be_true()
	Expect.that(config.automatic_event_mask).to_equal(
			ObservabilityCaptureMask.ERROR
			| ObservabilityCaptureMask.SCRIPT
			| ObservabilityCaptureMask.SHADER)
	Expect.that(config.automatic_breadcrumb_mask).to_equal(ObservabilityCaptureMask.ALL)
	Expect.that(config.automatic_log_mask).to_equal(ObservabilityCaptureMask.NONE)
	Expect.that(config.automatic_events_per_frame).to_equal(5)
	Expect.that(config.automatic_repeated_error_window_msec).to_equal(1000)
	Expect.that(config.automatic_event_throttle_count).to_equal(20)
	Expect.that(config.automatic_event_throttle_window_msec).to_equal(10000)


func test_breadcrumb_config_appends_capacity_and_normalizes_negative_values() -> void:
	var positional := ObservabilityConfig.new(
			true,
			"production",
			"1.2.3",
			"arm64",
			{},
			{},
			true,
			ObservabilityLevel.TRACE,
			0,
			true,
			1.0,
			Callable(),
			true,
			ObservabilityCaptureMask.DEFAULT_EVENTS,
			ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
			ObservabilityCaptureMask.NONE,
			5,
			1000,
			20,
			10000,
			true,
			false,
			PackedStringArray(["FoundryObservability: "]),
			true,
			5000,
			true,
			5000,
			false,
			7,
		)
	var negative := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = -1,
		)

	Expect.that(positional.max_breadcrumbs).to_equal(7)
	Expect.that(negative.max_breadcrumbs).to_equal(0)


func test_attachment_config_appends_limits_and_normalizes_negative_values() -> void:
	var explicit := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_attachment_bytes = -1,
			p_attach_game_log = true,
			p_attach_screenshot = true,
			p_attach_scene_tree = true,
		)

	Expect.that(explicit.max_attachment_bytes).to_equal(0)
	Expect.that(explicit.attach_game_log).to_be_true()
	Expect.that(explicit.attach_screenshot).to_be_true()
	Expect.that(explicit.attach_scene_tree).to_be_true()


func test_mobile_diagnostic_config_defaults_match_native_integrations() -> void:
	var config := ObservabilityConfig.new()

	Expect.that(config.application_hang_detection_enabled).to_be_true()
	Expect.that(config.application_hang_timeout_msec).to_equal(5000)
	Expect.that(config.android_anr_detection_enabled).to_be_true()
	Expect.that(config.android_anr_timeout_msec).to_equal(5000)
	Expect.that(config.android_anr_attach_thread_dump).to_be_false()


func test_mobile_diagnostic_timeouts_have_a_safe_minimum() -> void:
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(["FoundryObservability: "]),
			p_application_hang_timeout_msec = 25,
			p_android_anr_timeout_msec = -1,
		)

	Expect.that(config.application_hang_timeout_msec).to_equal(1000)
	Expect.that(config.android_anr_timeout_msec).to_equal(1000)


func test_config_legacy_positional_prefix_argument_is_preserved() -> void:
	var prefixes := PackedStringArray(["Legacy: "])
	var config := ObservabilityConfig.new(
			true,
			"production",
			"1.2.3",
			"arm64",
			{},
			{},
			true,
			ObservabilityLevel.TRACE,
			0,
			true,
			1.0,
			Callable(),
			true,
			ObservabilityCaptureMask.DEFAULT_EVENTS,
			ObservabilityCaptureMask.DEFAULT_BREADCRUMBS,
			ObservabilityCaptureMask.NONE,
			5,
			1000,
			20,
			10000,
			true,
			false,
			prefixes,
		)

	Expect.that(config.automatic_message_filter_prefixes()).to_equal(prefixes)


func test_mobile_diagnostic_config_preserves_explicit_values() -> void:
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(["FoundryObservability: "]),
			p_application_hang_detection_enabled = false,
			p_application_hang_timeout_msec = 3200,
			p_android_anr_detection_enabled = false,
			p_android_anr_timeout_msec = 6400,
			p_android_anr_attach_thread_dump = true,
		)

	Expect.that(config.application_hang_detection_enabled).to_be_false()
	Expect.that(config.application_hang_timeout_msec).to_equal(3200)
	Expect.that(config.android_anr_detection_enabled).to_be_false()
	Expect.that(config.android_anr_timeout_msec).to_equal(6400)
	Expect.that(config.android_anr_attach_thread_dump).to_be_true()


func test_automatic_capture_config_and_breadcrumb_copy_inputs() -> void:
	var prefixes := PackedStringArray(["Internal: "])
	var attributes := {"file": "res://player.fs"}
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_events_per_frame = -1,
			p_automatic_repeated_error_window_msec = -1,
			p_automatic_event_throttle_count = -1,
			p_automatic_event_throttle_window_msec = -1,
			p_automatic_message_filter_prefixes = prefixes,
		)
	var breadcrumb := ObservabilityBreadcrumb.new(
			p_message = "warning",
			p_level = ObservabilityLevel.WARN,
			p_category = &"error",
			p_timestamp_msec = 1234,
			p_attributes = attributes,
		)
	prefixes[0] = "changed"
	attributes["file"] = "changed"

	Expect.that(config.automatic_events_per_frame).to_equal(0)
	Expect.that(config.automatic_repeated_error_window_msec).to_equal(0)
	Expect.that(config.automatic_event_throttle_count).to_equal(0)
	Expect.that(config.automatic_event_throttle_window_msec).to_equal(0)
	Expect.that(config.automatic_message_filter_prefixes()).to_equal(
			PackedStringArray(["Internal: "]))
	Expect.that(breadcrumb.message()).to_equal("warning")
	Expect.that(breadcrumb.level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(breadcrumb.category()).to_equal(&"error")
	Expect.that(breadcrumb.timestamp_msec()).to_equal(1234)
	Expect.that(breadcrumb.attributes()).to_equal({"file": "res://player.fs"})


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


func test_metrics_support_sampling_boundaries_and_memory_clearing() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_sample_rate = 0.0,
		))).to_equal(Error.OK)
	Expect.that(service.capture_counter("sampled.metric")).to_be_false()
	Expect.that(provider.metrics()).to_have_size(0)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_sample_rate = 1.0,
		))).to_equal(Error.OK)
	Expect.that(service.capture_counter("sampled.metric")).to_be_true()
	Expect.that(provider.metrics()).to_have_size(1)
	provider.clear_metrics()
	Expect.that(provider.metrics()).to_have_size(0)
	service.shutdown()


func test_metrics_reject_non_boolean_filter_results() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_metric_filter = Callable(self, "_invalid_metric_filter"),
		))).to_equal(Error.OK)

	Expect.that(service.capture_counter("combat.hit")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_DATA)
	Expect.that(provider.metrics()).to_have_size(0)
	service.shutdown()


func test_metrics_isolate_provider_rejection_and_shutdown() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	provider.metric_capture_result = false

	Expect.that(service.capture_counter("provider.rejected")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.capture_message("events still work")).to_equal("memory:1")

	provider.metric_capture_result = true
	Expect.that(service.capture_counter("provider.accepted")).to_be_true()
	service.shutdown()
	Expect.that(service.capture_counter("after.shutdown")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.OK)


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
	Expect.that(provider.events()[0].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(provider.events()[0].engine_ticks_msec()).not_().to_be_less_than(0)
	Expect.that(provider.events()[1].kind()).to_equal(&"exception")
	Expect.that(provider.events()[1].message()).to_equal("boom")
	Expect.that(provider.events()[1].exception()).to_not_be_null()
	Expect.that(provider.events()[1].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(provider.events()[1].engine_ticks_msec()).not_().to_be_less_than(0)
	service.shutdown()


func test_memory_provider_rejects_null_event_without_changing_capture_history() -> void:
	var provider := MemoryObservabilityProvider.new()
	Expect.that(provider.configure(ObservabilityConfig.new())).to_equal(Error.OK)

	Expect.that(provider.capture(null)).to_equal("")
	Expect.that(provider.events()).to_have_size(0)
	Expect.that(provider.captured_scopes()).to_have_size(0)
	provider.shutdown()


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


func test_memory_provider_captures_and_clears_breadcrumbs() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)
	var breadcrumb := ObservabilityBreadcrumb.new(p_message = "trail")

	Expect.that(service.capture_breadcrumb(breadcrumb)).to_be_true()
	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("trail")
	Expect.that(provider.breadcrumbs()[0]).not_().to_equal(breadcrumb)
	provider.clear_breadcrumbs()
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	service.shutdown()


func test_service_redacts_rebuilt_provider_owned_state_without_retaining_inputs() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var policy := ObservabilityRedactionPolicy.new([
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["contexts", "**", "token"]),
				"secret",
				"safe",
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["user", "contact_email"]),
				"secret@example.com",
				"safe@example.invalid",
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["breadcrumbs", "message"]),
				"secret",
				"safe",
			),
		ObservabilityRedactionRule.replace_text(
				PackedStringArray(["attachments", "filename"]),
				"secret",
				"safe",
			),
	])
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = policy,
	))).to_equal(Error.OK)

	var context_source: Dictionary = {
		"token": "secret",
		"nested": {"token": "secret"},
	}
	var manual_breadcrumb := ObservabilityBreadcrumb.new(p_message = "secret manual")
	var automatic_breadcrumb := ObservabilityBreadcrumb.new(
			p_message = "secret automatic",
		)
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([1, 2, 3]),
			"secret.log",
			"text/plain",
		)
	Expect.that(service.set_context("account", context_source)).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new(
			"player-7",
			"Mina",
			"secret@example.com",
		))).to_be_true()
	Expect.that(service.capture_breadcrumb(manual_breadcrumb)).to_be_true()
	Expect.that(service._capture_automatic_breadcrumb(automatic_breadcrumb)).to_be_true()
	Expect.that(service.add_attachment(attachment).is_empty()).to_be_false()

	context_source["token"] = "mutated"
	context_source["nested"]["token"] = "mutated"
	Expect.that(service.capture_message("state snapshot")).to_equal("memory:1")
	Expect.that(provider.captured_scopes()[0]["contexts"]["account"]).to_equal({
		"token": "safe",
		"nested": {"token": "safe"},
	})
	Expect.that(provider.captured_scopes()[0]["user"]["contact_email"]).to_equal(
			"safe@example.invalid",
		)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("safe manual")
	Expect.that(provider.breadcrumbs()[1].message()).to_equal("safe automatic")
	Expect.that(provider.breadcrumbs()[0]).not_().to_equal(manual_breadcrumb)
	Expect.that(provider.breadcrumbs()[1]).not_().to_equal(automatic_breadcrumb)
	Expect.that(provider.captured_attachments()[0][0]["filename"]).to_equal("safe.log")
	Expect.that(manual_breadcrumb.message()).to_equal("secret manual")
	Expect.that(automatic_breadcrumb.message()).to_equal("secret automatic")
	Expect.that(attachment.filename()).to_equal("secret.log")
	service.shutdown()


func test_service_state_redaction_failures_skip_provider_mutations_and_report_state() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["contexts", "account"]),
						7,
					),
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["user", "contact_email"]),
						7,
					),
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["breadcrumbs", "level"]),
						"invalid",
					),
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["attachments", "filename"]),
						7,
					),
			]),
	))).to_equal(Error.OK)

	Expect.that(service.set_context("account", {"token": "secret"})).to_be_false()
	_expect_state_redaction_failure(service)
	Expect.that(service.set_user(ObservabilityUser.new(
			"player-7",
			"Mina",
			"secret@example.com",
		))).to_be_false()
	_expect_state_redaction_failure(service)
	Expect.that(service.capture_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "secret",
		))).to_be_false()
	_expect_state_redaction_failure(service)
	Expect.that(service._capture_automatic_breadcrumb(ObservabilityBreadcrumb.new(
			p_message = "automatic secret",
		))).to_be_false()
	_expect_state_redaction_failure(service)
	Expect.that(service.add_attachment(ObservabilityAttachment.from_bytes(
			PackedByteArray([1]),
			"secret.log",
		)).is_empty()).to_be_true()
	_expect_state_redaction_failure(service)

	Expect.that(provider.breadcrumbs()).to_have_size(0)
	Expect.that(service.capture_message("provider state unchanged")).to_equal("memory:1")
	Expect.that(provider.captured_scopes()[0]).to_equal({
		"tags": {},
		"contexts": {},
		"user": null,
	})
	Expect.that(provider.captured_attachments()[0]).to_have_size(0)
	service.shutdown()


func test_service_rejects_recursive_state_mutation_with_operation_local_error() -> void:
	_state_reentry_service = _processing_service()
	_state_reentry_result = true
	_state_reentry_error = Error.OK
	_state_reentry_signal = &""
	_state_reentry_reason = &""
	_state_reentry_diagnostic_error = Error.OK
	var provider := MemoryObservabilityProvider.new()
	Expect.that(_state_reentry_service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [
				Callable(self, "_service_set_context_during_processing"),
			],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)

	Expect.that(_state_reentry_service.capture_message("outer")).to_equal("memory:1")
	Expect.that(_state_reentry_result).to_be_false()
	Expect.that(_state_reentry_error).to_equal(Error.ERR_BUSY)
	Expect.that(_state_reentry_service.last_error()).to_equal(Error.ERR_BUSY)
	Expect.that(provider.captured_scopes()[0]["contexts"]).to_equal({})
	Expect.that(_state_reentry_signal).to_equal(
			ObservabilityProcessingDiagnostic.STATE,
		)
	Expect.that(_state_reentry_reason).to_equal(
			ObservabilityProcessingDiagnostic.RECURSIVE,
		)
	Expect.that(_state_reentry_diagnostic_error).to_equal(Error.ERR_BUSY)
	_shutdown_processing_service(_state_reentry_service)
	_state_reentry_service = null


func test_memory_scope_merges_local_overrides_and_defensively_snapshots_user() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.set_tag("region", "iad")).to_be_true()
	Expect.that(service.set_tag("build", "42")).to_be_true()
	Expect.that(service.set_tag("temporary", "removed")).to_be_true()
	Expect.that(service.remove_tag("temporary")).to_be_true()
	Expect.that(service.set_context("game", {
		"round": 1,
		"nested": {"global": true},
	})).to_be_true()
	Expect.that(service.set_context("runtime", {"branch": "main"})).to_be_true()
	Expect.that(service.set_context("temporary", {"removed": true})).to_be_true()
	Expect.that(service.remove_context("temporary")).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new(
			"player-7",
			"Mina",
			"mina@example.com",
		))).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("", "Bo", ""))).to_be_true()

	Expect.that(provider.set_tag(" invalid", "rejected")).to_be_false()
	Expect.that(provider.set_context("invalid", {"value": NAN})).to_be_false()

	var local := ObservabilityScope.new()
	Expect.that(local.set_tag("region", "fra")).to_be_true()
	Expect.that(local.set_context("game", {"local": true})).to_be_true()
	Expect.that(service.capture_message(
			"local",
			ObservabilityLevel.INFO,
			{},
			local,
		)).to_equal("memory:1")
	local.set_tag("region", "changed")
	local.set_context("game", {"changed": true})

	var first_exposed: Dictionary = provider.captured_scopes()[0]
	Expect.that(first_exposed).to_equal({
		"tags": {"region": "fra", "build": "42"},
		"contexts": {
			"game": {"local": true},
			"runtime": {"branch": "main"},
		},
		"user": {
			"id": "",
			"display_name": "Bo",
			"contact_email": "",
		},
	})
	first_exposed["tags"]["region"] = "mutated"
	first_exposed["contexts"]["game"]["local"] = false
	first_exposed["user"]["display_name"] = "mutated"
	Expect.that(provider.captured_scopes()[0]).to_equal({
		"tags": {"region": "fra", "build": "42"},
		"contexts": {
			"game": {"local": true},
			"runtime": {"branch": "main"},
		},
		"user": {
			"id": "",
			"display_name": "Bo",
			"contact_email": "",
		},
	})

	Expect.that(service.remove_tag("build")).to_be_true()
	Expect.that(service.remove_context("runtime")).to_be_true()
	Expect.that(service.capture_message("later")).to_equal("memory:2")
	Expect.that(provider.captured_scopes()[1]).to_equal({
		"tags": {"region": "iad"},
		"contexts": {
			"game": {
				"round": 1,
				"nested": {"global": true},
			},
		},
		"user": {
			"id": "",
			"display_name": "Bo",
			"contact_email": "",
		},
	})

	Expect.that(service.clear_tags()).to_be_true()
	Expect.that(service.clear_contexts()).to_be_true()
	Expect.that(service.remove_user()).to_be_true()
	Expect.that(service.capture_message("empty")).to_equal("memory:3")
	Expect.that(provider.captured_scopes()[2]).to_equal({
		"tags": {},
		"contexts": {},
		"user": null,
	})
	Expect.that(provider.events()).to_have_size(3)
	Expect.that(provider.captured_scopes()).to_have_size(3)

	provider.clear()
	Expect.that(provider.events()).to_have_size(0)
	Expect.that(provider.captured_scopes()).to_have_size(0)
	service.shutdown()


func test_memory_global_scope_reaches_automatic_godot_events() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_log_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.set_tag("capture", "automatic")).to_be_true()
	Expect.that(service.set_context("engine", {"frame": 7})).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("player-7"))).to_be_true()
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1234, func() -> int: return 7)

	logger._log_error(
			"tick",
			"res://loop.fs",
			9,
			"boom",
			"",
			false,
			Logger.ERROR_TYPE_ERROR,
			[],
		)

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.captured_scopes()).to_equal([{
		"tags": {"capture": "automatic"},
		"contexts": {"engine": {"frame": 7}},
		"user": {
			"id": "player-7",
			"display_name": "",
			"contact_email": "",
		},
	}])
	service.shutdown()


func test_memory_successful_reconfigure_resets_session_scope_and_updates_bound() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var initial := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 3,
		)
	Expect.that(service.configure(provider, initial)).to_equal(Error.OK)
	Expect.that(service.set_tag("region", "iad")).to_be_true()
	Expect.that(service.set_context("game", {"round": 1})).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("player-7"))).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "old"))).to_be_true()
	Expect.that(service.capture_message("retained history")).to_equal("memory:1")

	Expect.that(service.configure(provider, initial)).to_equal(Error.OK)
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	Expect.that(provider.captured_scopes()[0]).to_equal({
		"tags": {"region": "iad"},
		"contexts": {"game": {"round": 1}},
		"user": {
			"id": "player-7",
			"display_name": "",
			"contact_email": "",
		},
	})
	Expect.that(service.capture_message("equivalent reset")).to_equal("memory:2")
	Expect.that(provider.captured_scopes()[1]).to_equal({
		"tags": {},
		"contexts": {},
		"user": null,
	})
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "before reduction"))).to_be_true()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 1,
		))).to_equal(Error.OK)
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "one"))).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "two"))).to_be_true()
	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("two")
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.captured_scopes()).to_have_size(2)
	Expect.that(provider.captured_scopes()[0]).to_equal({
		"tags": {"region": "iad"},
		"contexts": {"game": {"round": 1}},
		"user": {
			"id": "player-7",
			"display_name": "",
			"contact_email": "",
		},
	})
	service.shutdown()


func test_memory_failed_same_provider_reconfigure_preserves_active_session() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 2,
		))).to_equal(Error.OK)
	Expect.that(service.set_tag("region", "iad")).to_be_true()
	Expect.that(service.set_context("game", {"round": 1})).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("player-7"))).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "one"))).to_be_true()

	provider.configure_result = Error.FAILED
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_enabled = false,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 0,
		))).to_equal(Error.FAILED)
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "two"))).to_be_true()
	Expect.that(service.capture_message("preserved")).to_equal("memory:1")
	Expect.that(provider.breadcrumbs()).to_have_size(2)
	Expect.that(provider.captured_scopes()[0]).to_equal({
		"tags": {"region": "iad"},
		"contexts": {"game": {"round": 1}},
		"user": {
			"id": "player-7",
			"display_name": "",
			"contact_email": "",
		},
	})
	service.shutdown()


func test_memory_provider_replacement_and_shutdown_clear_only_live_session_state() -> void:
	var service: FoundryObservability = _service()
	var first := MemoryObservabilityProvider.new()
	var second := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		)
	Expect.that(service.configure(first, config)).to_equal(Error.OK)
	Expect.that(service.set_tag("provider", "first")).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("player-7"))).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "first"))).to_be_true()
	Expect.that(service.capture_message("first history")).to_equal("memory:1")

	Expect.that(service.configure(second, config)).to_equal(Error.OK)
	Expect.that(first.shutdown_count).to_equal(1)
	Expect.that(first.breadcrumbs()).to_have_size(0)
	Expect.that(first.set_tag("after", "shutdown")).to_be_false()
	Expect.that(first.clear_breadcrumbs()).to_be_false()
	Expect.that(first.events()).to_have_size(1)
	Expect.that(first.captured_scopes()).to_have_size(1)
	Expect.that(first.captured_scopes()[0]).to_equal({
		"tags": {"provider": "first"},
		"contexts": {},
		"user": {
			"id": "player-7",
			"display_name": "",
			"contact_email": "",
		},
	})
	Expect.that(service.capture_message("second empty")).to_equal("memory:1")
	Expect.that(second.captured_scopes()[0]).to_equal({
		"tags": {},
		"contexts": {},
		"user": null,
	})
	Expect.that(service.set_tag("provider", "second")).to_be_true()
	Expect.that(service.set_context("game", {"round": 2})).to_be_true()
	Expect.that(service.set_user(ObservabilityUser.new("player-8"))).to_be_true()
	Expect.that(service.capture_message("second history")).to_equal("memory:2")
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "second"))).to_be_true()

	service.shutdown()
	service.shutdown()
	Expect.that(second.shutdown_count).to_equal(1)
	Expect.that(second.breadcrumbs()).to_have_size(0)
	Expect.that(second.events()).to_have_size(2)
	Expect.that(second.captured_scopes()).to_have_size(2)
	Expect.that(second.captured_scopes()[1]).to_equal({
		"tags": {"provider": "second"},
		"contexts": {"game": {"round": 2}},
		"user": {
			"id": "player-8",
			"display_name": "",
			"contact_email": "",
		},
	})


func test_memory_breadcrumb_bound_zero_and_service_clear_results() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 2,
		))).to_equal(Error.OK)
	var one := ObservabilityBreadcrumb.new(p_message = "one")
	var two := ObservabilityBreadcrumb.new(p_message = "two")
	var three := ObservabilityBreadcrumb.new(p_message = "three")

	Expect.that(service.capture_breadcrumb(one)).to_be_true()
	Expect.that(service.capture_breadcrumb(two)).to_be_true()
	Expect.that(service.capture_breadcrumb(three)).to_be_true()
	Expect.that(provider.breadcrumbs()).to_have_size(2)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("two")
	Expect.that(provider.breadcrumbs()[1].message()).to_equal("three")
	Expect.that(service.clear_breadcrumbs()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(provider.breadcrumbs()).to_have_size(0)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 0,
		))).to_equal(Error.OK)
	Expect.that(service.capture_breadcrumb(one)).to_be_false()
	Expect.that(provider.breadcrumbs()).to_have_size(0)

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_enabled = false,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_breadcrumbs = 2,
		))).to_equal(Error.OK)
	Expect.that(service.clear_breadcrumbs()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.OK)
	service.shutdown()
	Expect.that(service.clear_breadcrumbs()).to_be_false()


func test_missing_breadcrumb_capability_is_observable_and_isolated() -> void:
	var service: FoundryObservability = _service()
	var provider := BreadcrumblessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		))).to_equal(Error.OK)

	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "unsupported"))).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("still works")).to_equal("event:1")
	service.shutdown()


func test_automatic_logger_routes_error_metadata_by_independent_masks() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1234, func() -> int: return 7)
	var backtraces: Array[ScriptBacktrace] = Engine.capture_script_backtraces(false)

	logger._log_error(
			"attack", "res://player.fs", 42, "ERR_INVALID_DATA", "bad hit",
			false, Logger.ERROR_TYPE_ERROR, backtraces)

	Expect.that(provider.events()).to_have_size(2)
	var exception_event: ObservabilityEvent = provider.events()[0]
	Expect.that(exception_event.kind()).to_equal(&"exception")
	Expect.that(exception_event.source()).to_equal(&"foundry.engine")
	Expect.that(exception_event.timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(exception_event.engine_ticks_msec()).to_equal(1234)
	Expect.that(exception_event.exception().type_name()).to_equal("ERROR")
	Expect.that(exception_event.exception().stack_trace()).to_contain("observability-core.test.fs")
	Expect.that(exception_event.attributes()["error.function"]).to_equal("attack")
	Expect.that(exception_event.attributes()["error.file"]).to_equal("res://player.fs")
	Expect.that(exception_event.attributes()["error.line"]).to_equal(42)
	Expect.that(exception_event.attributes()["error.code"]).to_equal("ERR_INVALID_DATA")
	Expect.that(exception_event.attributes()["error.rationale"]).to_equal("bad hit")
	var serialized_backtraces: Array = exception_event.attributes()["error.script_backtraces"]
	Expect.that(serialized_backtraces.size()).to_be_greater_than(0)
	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.breadcrumbs()[0].timestamp_msec()).to_equal(1234)
	Expect.that(provider.events()[1].kind()).to_equal(&"log")
	Expect.that(provider.events()[1].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(provider.events()[1].engine_ticks_msec()).to_equal(1234)
	service.shutdown()


func test_automatic_logger_maps_error_categories_and_levels() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ALL_ERRORS,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1, func() -> int: return 1)

	logger._log_error("run", "res://case.fs", 1, "warning", "", false,
			Logger.ERROR_TYPE_WARNING, [])
	logger._log_error("run", "res://case.fs", 2, "script", "", false,
			Logger.ERROR_TYPE_SCRIPT, [])
	logger._log_error("run", "res://case.fs", 3, "shader", "", false,
			Logger.ERROR_TYPE_SHADER, [])
	logger._log_error("run", "res://case.fs", 4, "fatal", "", false,
			Logger.ERROR_TYPE_FATAL, [])

	Expect.that(provider.events()[0].level()).to_equal(ObservabilityLevel.WARN)
	Expect.that(provider.events()[0].exception().type_name()).to_equal("WARNING")
	Expect.that(provider.events()[1].level()).to_equal(ObservabilityLevel.ERROR)
	Expect.that(provider.events()[1].exception().type_name()).to_equal("SCRIPT ERROR")
	Expect.that(provider.events()[2].exception().type_name()).to_equal("SHADER ERROR")
	Expect.that(provider.events()[3].level()).to_equal(ObservabilityLevel.FATAL)
	Expect.that(provider.events()[3].exception().type_name()).to_equal("FATAL")
	service.shutdown()


func test_automatic_logger_filters_and_routes_messages_without_events() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ALL_ERRORS,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.MESSAGE,
			p_automatic_log_mask = ObservabilityCaptureMask.MESSAGE,
			p_automatic_message_filter_prefixes = PackedStringArray(["Internal: "]),
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1234, func() -> int: return 1)

	logger._log_message("\u001b[31mhello\u001b[0m\n", false)
	logger._log_message("Internal: ignored", true)

	Expect.that(provider.breadcrumbs()).to_have_size(1)
	Expect.that(provider.breadcrumbs()[0].message()).to_equal("hello")
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].kind()).to_equal(&"log")
	Expect.that(provider.events()[0].level()).to_equal(ObservabilityLevel.INFO)
	Expect.that(provider.events()[0].timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(provider.events()[0].engine_ticks_msec()).to_equal(1234)
	service.shutdown()


func test_automatic_errors_share_event_limits_without_suppressing_other_destinations() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 1000,
			p_automatic_events_per_frame = 1,
			p_automatic_event_throttle_count = 2,
			p_automatic_event_throttle_window_msec = 10000,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_log_limits = ObservabilitySignalLimits.new(),
	)
	var capture_time := AutomaticCaptureTime.new(1000, 1)
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, capture_time.now, capture_time.frame)

	for _index: int in range(3):
		logger._capture_error(
				"tick", "res://loop.fs", 9, "boom", "", false,
				Logger.ERROR_TYPE_ERROR, [],
			)

	var non_log_count: int = 0
	var log_count: int = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() != &"log":
			non_log_count += 1
		else:
			log_count += 1
	Expect.that(non_log_count).to_equal(1)
	Expect.that(provider.breadcrumbs()).to_have_size(3)
	Expect.that(log_count).to_equal(3)
	Expect.that(provider.events()[0].engine_ticks_msec()).to_equal(1000)
	Expect.that(provider.breadcrumbs()[0].timestamp_msec()).to_equal(1000)

	capture_time.frame_index = 2
	capture_time.now_msec = 1500
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	logger._capture_error(
			"tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [],
		)
	non_log_count = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() != &"log":
			non_log_count += 1
	Expect.that(non_log_count).to_equal(1)

	capture_time.frame_index = 3
	capture_time.now_msec = 2000
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	logger._capture_error(
			"tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [],
		)
	non_log_count = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() != &"log":
			non_log_count += 1
	Expect.that(non_log_count).to_equal(2)

	capture_time.frame_index = 4
	capture_time.now_msec = 2001
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	logger._capture_error(
			"tick", "res://loop.fs", 10, "different", "", false,
			Logger.ERROR_TYPE_ERROR, [],
		)
	non_log_count = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() != &"log":
			non_log_count += 1
	Expect.that(non_log_count).to_equal(2)

	capture_time.frame_index = 5
	capture_time.now_msec = 12001
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	Expect.that(service.capture_message("manual capacity")).not_().to_equal("")
	logger._capture_error(
			"tick", "res://loop.fs", 11, "automatic after manual", "", false,
			Logger.ERROR_TYPE_ERROR, [],
		)
	non_log_count = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() != &"log":
			non_log_count += 1
	Expect.that(non_log_count).to_equal(3)
	Expect.that(provider.breadcrumbs()).to_have_size(7)
	log_count = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() == &"log":
			log_count += 1
	Expect.that(log_count).to_equal(7)
	_shutdown_processing_service(service)


func test_automatic_logger_does_not_suppress_after_all_destinations_reject() -> void:
	var service := _processing_service()
	var provider := RejectingObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 1000,
			p_automatic_events_per_frame = 1,
			p_automatic_event_throttle_count = 1,
			p_automatic_event_throttle_window_msec = 1000,
		)
	_service_processing_clock_msec = 1000
	_service_processing_frame_index = 1
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)

	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])

	Expect.that(provider.capture_count).to_equal(3)
	Expect.that(provider.breadcrumb_count).to_equal(2)
	_shutdown_processing_service(service)


func test_successful_automatic_breadcrumb_does_not_clear_event_failure() -> void:
	var service: FoundryObservability = _service()
	var provider := RejectingObservabilityProvider.new()
	provider.breadcrumb_capture_result = true
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)

	Expect.that(service.capture_message("rejected event")).to_equal("")
	Expect.that(service._capture_automatic_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "accepted breadcrumb"))).to_be_true()

	Expect.that(provider.capture_count).to_equal(1)
	Expect.that(provider.breadcrumb_count).to_equal(1)
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func test_rejected_automatic_breadcrumb_reports_failure_after_accepted_event() -> void:
	var service: FoundryObservability = _service()
	var provider := RejectingObservabilityProvider.new()
	provider.event_capture_result = true
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)

	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])

	Expect.that(provider.capture_count).to_equal(1)
	Expect.that(provider.breadcrumb_count).to_equal(1)
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	service.shutdown()


func test_automatic_event_limits_do_not_suppress_breadcrumbs_or_logs() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1, 0, 0, 0),
			p_log_limits = ObservabilitySignalLimits.new(2, 0, 0, 0),
	)
	var capture_time := AutomaticCaptureTime.new(1000, 1)
	_service_processing_clock_msec = capture_time.now_msec
	_service_processing_frame_index = capture_time.frame_index
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, capture_time.now, capture_time.frame)

	for index: int in range(3):
		logger._capture_error(
				"tick", "res://loop.fs", 9 + index, str(index), "", false,
				Logger.ERROR_TYPE_ERROR, [],
			)

	var exception_count: int = 0
	var log_count: int = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() == &"exception":
			exception_count += 1
		elif event.kind() == &"log":
			log_count += 1
	Expect.that(exception_count).to_equal(1)
	Expect.that(log_count).to_equal(2)
	Expect.that(provider.breadcrumbs()).to_have_size(3)
	_shutdown_processing_service(service)


func test_automatic_processor_recursion_drops_nested_message_without_payload_delivery() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	_automatic_nested_service = service
	_automatic_nested_capture_result = "not called"
	_automatic_nested_reason = &""
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_log_mask = ObservabilityCaptureMask.NONE,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [
				Callable(self, "_automatic_capture_nested_message"),
			],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	)
	_service_processing_clock_msec = 1000
	_service_processing_frame_index = 1
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)

	logger._capture_error(
			"tick", "res://loop.fs", 9, "outer", "", false,
			Logger.ERROR_TYPE_ERROR, [],
		)

	Expect.that(_automatic_nested_capture_result).to_equal("")
	Expect.that(_automatic_nested_reason).to_equal(
			ObservabilityProcessingDiagnostic.RECURSIVE,
		)
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("outer")
	Expect.that(provider.events()[0].message()).not_().to_equal("nested")
	_shutdown_processing_service(service)


func test_automatic_capture_reservation_is_atomic() -> void:
	var service := _processing_service()

	Expect.that(service.try_begin_automatic_capture()).to_be_true()
	Expect.that(service.try_begin_automatic_capture()).to_be_false()
	service.end_automatic_capture()
	Expect.that(service.try_begin_automatic_capture()).to_be_true()
	service.end_automatic_capture()
	_shutdown_processing_service(service)


func test_automatic_capture_skips_missing_optional_breadcrumb_capability() -> void:
	TestContext.current().stop_diagnostics()
	var service: FoundryObservability = _service()
	var provider := BreadcrumblessObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	push_error("event-only automatic capture")
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.capture_message("next event")).to_equal("event:2")
	service.shutdown()


func test_automatic_capture_installs_only_after_successful_enabled_configuration() -> void:
	TestContext.current().stop_diagnostics()
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var disabled := ObservabilityConfig.new(
			p_enabled = true,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
		)

	Expect.that(service.configure(provider, disabled)).to_equal(Error.OK)
	push_error("automatic disabled")
	Expect.that(provider.events()).to_have_size(0)

	var failing: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	failing.configure_result = Error.FAILED
	Expect.that(service.configure(failing, ObservabilityConfig.new())).to_equal(Error.FAILED)
	push_error("failed candidate")
	Expect.that(provider.events()).to_have_size(0)

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	push_error("automatic enabled")
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].message()).to_equal("automatic enabled")

	failing.configure_result = Error.FAILED
	Expect.that(service.configure(failing, ObservabilityConfig.new())).to_equal(Error.FAILED)
	push_error("failed active replacement")
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.events()[1].message()).to_equal("failed active replacement")
	service.shutdown()


func test_automatic_capture_reconfigures_without_duplicate_logger_registration() -> void:
	TestContext.current().stop_diagnostics()
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	_push_test_error("same diagnostic")
	Expect.that(provider.events()).to_have_size(1)

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	_push_test_error("same diagnostic")
	Expect.that(provider.events()).to_have_size(2)
	_push_test_error("new diagnostic")
	Expect.that(provider.events()).to_have_size(3)
	service.shutdown()


func test_automatic_capture_moves_to_replacement_provider_and_is_removed_on_shutdown() -> void:
	TestContext.current().stop_diagnostics()
	var service: FoundryObservability = _service()
	var first: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	var second: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(first, ObservabilityConfig.new())).to_equal(Error.OK)
	push_error("first provider")
	Expect.that(first.events()).to_have_size(1)

	Expect.that(service.configure(second, ObservabilityConfig.new())).to_equal(Error.OK)
	push_error("second provider")
	Expect.that(first.events()).to_have_size(1)
	Expect.that(second.events()).to_have_size(1)

	service.shutdown()
	push_error("after shutdown")
	Expect.that(second.events()).to_have_size(1)


func test_automatic_capture_blocks_provider_generated_diagnostic_recursion() -> void:
	TestContext.current().stop_diagnostics()
	var service: FoundryObservability = _service()
	var provider: ReentrantObservabilityProvider = ReentrantObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
		)

	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	Expect.that(service.capture_message("outer diagnostic")).to_equal("reentrant:1")
	Expect.that(provider.capture_count).to_equal(1)
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
	var service := _processing_service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	_service_processing_frame_index = 1

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		))).to_equal(Error.OK)
	_service_processing_clock_msec = 1000
	Expect.that(service.capture_log(
			"first", ObservabilityLevel.INFO, &"game", -1, {}, 1000,
		)).to_equal("memory:1")
	_service_processing_clock_msec = 1500
	Expect.that(service.capture_log(
			"dropped", ObservabilityLevel.INFO, &"game", -1, {}, 1500,
		)).to_equal("")
	_service_processing_clock_msec = 2000
	Expect.that(service.capture_log(
			"next window", ObservabilityLevel.INFO, &"game", -1, {}, 2000,
		)).to_equal("memory:2")
	Expect.that(provider.events()).to_have_size(2)
	_shutdown_processing_service(service)


func test_service_processes_replacement_event_before_memory_provider_delivery() -> void:
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()
	_service_event_processor_calls = 0
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_service_replace_event")],
	))).to_equal(Error.OK)

	Expect.that(service.capture_message("original")).to_equal("memory:1")
	Expect.that(_service_event_processor_calls).to_equal(1)
	Expect.that(provider.events()[0].message()).to_equal("processed")
	var diagnostic: ObservabilityProcessingDiagnostic = service.last_processing_diagnostic()
	Expect.that(diagnostic.processing_signal()).to_equal(ObservabilityProcessingDiagnostic.EVENT)
	Expect.that(diagnostic.outcome()).to_equal(ObservabilityProcessingDiagnostic.ACCEPTED)
	Expect.that(service.last_error()).to_equal(Error.OK)
	service.shutdown()


func test_service_processor_drop_preserves_ok_error_and_publishes_diagnostic() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_service_drop_event")],
	))).to_equal(Error.OK)

	Expect.that(service.capture_message("dropped")).to_equal("")
	Expect.that(provider.events()).to_have_size(0)
	Expect.that(service.last_error()).to_equal(Error.OK)
	var diagnostic: ObservabilityProcessingDiagnostic = service.last_processing_diagnostic()
	Expect.that(diagnostic.outcome()).to_equal(ObservabilityProcessingDiagnostic.DROPPED)
	Expect.that(diagnostic.reason()).to_equal(ObservabilityProcessingDiagnostic.PROCESSOR)
	Expect.that(diagnostic.processor_index()).to_equal(0)
	Expect.that(diagnostic.error()).to_equal(Error.OK)
	service.shutdown()


func test_service_processing_capacity_is_independent_per_event_log_and_metric() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	_service_processing_clock_msec = 1000
	_service_processing_frame_index = 1
	var one_per_frame := ObservabilitySignalLimits.new(1, 0, 0, 0)
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = one_per_frame,
			p_log_limits = one_per_frame,
			p_metric_limits = one_per_frame,
	))).to_equal(Error.OK)

	Expect.that(service.capture_message("event first")).to_equal("memory:1")
	Expect.that(service.capture_log("log first")).to_equal("memory:2")
	Expect.that(service.capture_counter("metric.first")).to_be_true()
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.metrics()).to_have_size(1)

	Expect.that(service.capture_message("event second")).to_equal("")
	_expect_service_per_frame_diagnostic(service, ObservabilityProcessingDiagnostic.EVENT)
	Expect.that(service.capture_log("log second")).to_equal("")
	_expect_service_per_frame_diagnostic(service, ObservabilityProcessingDiagnostic.LOG)
	Expect.that(service.capture_counter("metric.second")).to_be_false()
	_expect_service_per_frame_diagnostic(service, ObservabilityProcessingDiagnostic.METRIC)
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.metrics()).to_have_size(1)
	_shutdown_processing_service(service)


func test_service_failed_reconfiguration_preserves_processing_diagnostic_and_admission() -> void:
	var service := _processing_service()
	var active := ConfigureCountingMemoryProvider.new()
	_service_processing_clock_msec = 1000
	_service_processing_frame_index = 1
	var active_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1, 0, 0, 0),
		)
	Expect.that(service.configure(active, active_config)).to_equal(Error.OK)
	Expect.that(service.capture_message("accepted")).to_equal("memory:1")
	Expect.that(service.capture_message("limited")).to_equal("")
	var prior: ObservabilityProcessingDiagnostic = service.last_processing_diagnostic()

	var invalid_candidate := ConfigureCountingMemoryProvider.new()
	Expect.that(service.configure(invalid_candidate, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable()],
		))).to_equal(Error.ERR_INVALID_DATA)
	Expect.that(invalid_candidate.configure_calls).to_equal(0)
	_expect_service_diagnostic_unchanged(service, prior)
	Expect.that(service.capture_message("still limited after invalid")).to_equal("")

	var failing_candidate := ConfigureCountingMemoryProvider.new()
	failing_candidate.configure_result = Error.FAILED
	prior = service.last_processing_diagnostic()
	Expect.that(service.configure(failing_candidate, active_config)).to_equal(Error.FAILED)
	Expect.that(failing_candidate.configure_calls).to_equal(1)
	_expect_service_diagnostic_unchanged(service, prior)
	Expect.that(service.capture_message("still limited after candidate failure")).to_equal("")

	active.configure_result = Error.FAILED
	prior = service.last_processing_diagnostic()
	Expect.that(service.configure(active, active_config)).to_equal(Error.FAILED)
	_expect_service_diagnostic_unchanged(service, prior)
	Expect.that(service.capture_message("still limited after same provider failure")).to_equal("")
	_shutdown_processing_service(service)


func test_service_constructor_preserves_positional_arguments_and_uses_processing_seams() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	_service_processing_clock_msec = 0
	_service_processing_frame_index = 1
	_service_processing_clock_calls = 0
	_service_processing_frame_calls = 0
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1, 0, 0, 0),
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)

	Expect.that(service.capture_message("first")).to_equal("memory:1")
	Expect.that(service.capture_message("limited")).to_equal("")
	Expect.that(service.last_processing_diagnostic().limit_kind()).to_equal(
			ObservabilityProcessingDiagnostic.PER_FRAME)
	_service_processing_frame_index = 2
	Expect.that(service.capture_message("next frame")).to_equal("memory:2")
	Expect.that(_service_processing_clock_calls).to_be_greater_than(0)
	Expect.that(_service_processing_frame_calls).to_be_greater_than(0)
	_shutdown_processing_service(service)


func test_service_event_capture_does_not_cross_reconfigured_generation() -> void:
	var service := _processing_service()
	var original := MemoryObservabilityProvider.new()
	var replacement := MemoryObservabilityProvider.new()
	var replacement_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
		)
	_service_lifecycle_service = service
	_service_lifecycle_provider = replacement
	_service_lifecycle_config = replacement_config
	_service_lifecycle_configure_result = Error.FAILED
	Expect.that(service.configure(original, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [
				Callable(self, "_service_reconfigure_from_event_processor"),
			],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)

	Expect.that(service.capture_message("old generation")).to_equal("")
	Expect.that(_service_lifecycle_configure_result).to_equal(Error.OK)
	Expect.that(original.events()).to_have_size(0)
	Expect.that(replacement.events()).to_have_size(0)
	Expect.that(service.last_processing_diagnostic()).to_be_null()

	Expect.that(service.capture_message("new generation")).to_equal("memory:1")
	Expect.that(replacement.events()).to_have_size(1)
	Expect.that(replacement.events()[0].message()).to_equal("new generation")
	_shutdown_processing_service(service)


func test_service_metric_capture_does_not_cross_reconfigured_generation() -> void:
	var service := _processing_service()
	var original := MemoryObservabilityProvider.new()
	var replacement := MemoryObservabilityProvider.new()
	var replacement_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_metric_limits = ObservabilitySignalLimits.new(),
		)
	_service_lifecycle_service = service
	_service_lifecycle_provider = replacement
	_service_lifecycle_config = replacement_config
	_service_lifecycle_configure_result = Error.FAILED
	Expect.that(service.configure(original, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [
				Callable(self, "_service_reconfigure_from_metric_processor"),
			],
			p_metric_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)

	Expect.that(service.capture_counter("old.metric")).to_be_false()
	Expect.that(_service_lifecycle_configure_result).to_equal(Error.OK)
	Expect.that(original.metrics()).to_have_size(0)
	Expect.that(replacement.metrics()).to_have_size(0)
	Expect.that(service.last_processing_diagnostic()).to_be_null()

	Expect.that(service.capture_counter("new.metric")).to_be_true()
	Expect.that(replacement.metrics()).to_have_size(1)
	Expect.that(replacement.metrics()[0].name()).to_equal("new.metric")
	_shutdown_processing_service(service)


func test_service_rejects_reconfiguration_during_provider_capture() -> void:
	var service := _processing_service()
	var active := ReconfiguringCaptureMemoryProvider.new()
	var replacement := ConfigureCountingMemoryProvider.new()
	var replacement_config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
		)
	active.service = service
	active.replacement_provider = replacement
	active.replacement_config = replacement_config
	Expect.that(service.configure(active, replacement_config)).to_equal(Error.OK)

	Expect.that(service.capture_message("active generation")).to_equal("memory:1")
	Expect.that(active.reconfigure_result).to_equal(Error.ERR_BUSY)
	Expect.that(active.shutdown_count).to_equal(0)
	Expect.that(replacement.configure_calls).to_equal(0)
	Expect.that(active.events()).to_have_size(1)
	Expect.that(service.provider_name()).to_equal(&"memory")

	Expect.that(service.capture_message("still active")).to_equal("memory:2")
	Expect.that(active.events()).to_have_size(2)
	_shutdown_processing_service(service)


func test_service_blocks_other_provider_calls_during_reconfiguration() -> void:
	var service := _processing_service()
	var active := MemoryObservabilityProvider.new()
	var replacement := ReentrantConfigureMemoryProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
		)
	Expect.that(service.configure(active, config)).to_equal(Error.OK)
	replacement.service = service

	Expect.that(service.configure(replacement, config)).to_equal(Error.OK)
	Expect.that(replacement.nested_flush_result).to_equal(Error.ERR_BUSY)
	Expect.that(active.flush_count).to_equal(0)
	Expect.that(service.capture_message("replacement active")).to_equal("memory:1")
	_shutdown_processing_service(service)


func test_scope_operation_does_not_cross_same_provider_generation() -> void:
	var service := _reconfiguring_reservation_service()
	var provider := RecordingScopeProvider.new()
	Expect.that(service.configure(
			provider,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	service.replacement_config = _ordinary_generation_config(false)
	service.armed = true

	Expect.that(service.set_tag("region", "iad")).to_be_false()
	Expect.that(service.reconfigure_result).to_equal(Error.OK)
	Expect.that(provider.calls).to_have_size(0)
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.OK)
	_shutdown_processing_service(service)


func test_attachment_does_not_repopulate_same_provider_after_session_reset() -> void:
	var service := _reconfiguring_reservation_service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(
			provider,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	service.replacement_config = _ordinary_generation_config(true)
	service.armed = true
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([1, 2, 3]),
			"stale.bin",
		)

	Expect.that(service.add_attachment(attachment)).to_equal("")
	Expect.that(service.reconfigure_result).to_equal(Error.OK)
	Expect.that(service.capture_message("new session")).to_equal("memory:1")
	Expect.that(provider.captured_attachments()[0]).to_have_size(0)
	Expect.that(service.last_error()).to_equal(Error.OK)
	_shutdown_processing_service(service)


func test_breadcrumb_does_not_cross_same_provider_generation() -> void:
	var service := _reconfiguring_reservation_service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(
			provider,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	service.replacement_config = _ordinary_generation_config(true)
	service.armed = true

	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "stale"),
		)).to_be_false()
	Expect.that(service.reconfigure_result).to_equal(Error.OK)
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	Expect.that(service.last_error()).to_equal(Error.OK)
	_shutdown_processing_service(service)


func test_feedback_does_not_cross_same_provider_generation() -> void:
	var service := _reconfiguring_reservation_service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(
			provider,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	service.replacement_config = _ordinary_generation_config(true)
	service.armed = true

	Expect.that(service.capture_feedback(
			ObservabilityFeedback.new(p_message = "stale"),
		)).to_equal("")
	Expect.that(service.reconfigure_result).to_equal(Error.OK)
	Expect.that(provider.feedback()).to_have_size(0)
	Expect.that(service.last_error()).to_equal(Error.OK)
	_shutdown_processing_service(service)


func test_shutdown_requested_inside_provider_capture_completes_after_call() -> void:
	var service := _processing_service()
	var provider := ShutdownRequestingMemoryProvider.new()
	provider.service = service
	Expect.that(service.configure(
			provider,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)

	Expect.that(service.capture_message("final event")).to_equal("memory:1")
	Expect.that(provider.request_count).to_equal(1)
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(provider.shutdown_count).to_equal(1)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(service.capture_message("after shutdown")).to_equal("")

	service.shutdown()
	Expect.that(provider.flush_count).to_equal(1)
	Expect.that(provider.shutdown_count).to_equal(1)
	service.free()


func test_shutdown_requested_during_configure_cleans_committed_candidate() -> void:
	var service := _processing_service()
	var active := MemoryObservabilityProvider.new()
	var replacement := ReentrantConfigureMemoryProvider.new()
	Expect.that(service.configure(
			active,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	replacement.service = service
	replacement.request_shutdown = true

	Expect.that(service.configure(
			replacement,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	Expect.that(active.shutdown_count).to_equal(1)
	Expect.that(replacement.flush_count).to_equal(1)
	Expect.that(replacement.shutdown_count).to_equal(1)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()

	service.shutdown()
	Expect.that(replacement.shutdown_count).to_equal(1)
	service.free()


func test_shutdown_during_candidate_preparation_cannot_resurrect_configuration() -> void:
	var service := ShutdownDuringCandidatePreparationService.new(
			ObservabilityStartupSettings.new(),
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs",
			Callable(self, "_service_processing_clock"),
			Callable(self, "_service_processing_frame"),
		)
	var active := MemoryObservabilityProvider.new()
	var replacement := ConfigureCountingMemoryProvider.new()
	Expect.that(service.configure(
			active,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	service.shutdown_during_preparation = true

	Expect.that(service.configure(
			replacement,
			_ordinary_generation_config(true),
		)).to_equal(Error.ERR_BUSY)
	Expect.that(service.shutdown_during_preparation).to_be_false()
	Expect.that(replacement.configure_calls).to_equal(0)
	Expect.that(replacement.shutdown_count).to_equal(0)
	Expect.that(active.flush_count).to_equal(1)
	Expect.that(active.shutdown_count).to_equal(1)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()

	service.shutdown()
	Expect.that(active.shutdown_count).to_equal(1)
	service.free()


func test_failed_replacement_requesting_shutdown_cleans_both_providers_once() -> void:
	var service := _processing_service()
	var active := MemoryObservabilityProvider.new()
	var replacement := ReentrantConfigureMemoryProvider.new()
	Expect.that(service.configure(
			active,
			_ordinary_generation_config(true),
		)).to_equal(Error.OK)
	replacement.service = service
	replacement.request_shutdown = true
	replacement.configure_result = Error.FAILED

	Expect.that(service.configure(
			replacement,
			_ordinary_generation_config(true),
		)).to_equal(Error.FAILED)
	Expect.that(replacement.flush_count).to_equal(0)
	Expect.that(replacement.shutdown_count).to_equal(1)
	Expect.that(active.flush_count).to_equal(1)
	Expect.that(active.shutdown_count).to_equal(1)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()

	service.shutdown()
	Expect.that(replacement.shutdown_count).to_equal(1)
	Expect.that(active.shutdown_count).to_equal(1)
	service.free()


func test_automatic_capture_rejects_completed_shutdown() -> void:
	var service: FoundryObservability = _service()
	service.shutdown()

	var reserved: bool = service.try_begin_automatic_capture()
	Expect.that(reserved).to_be_false()
	if reserved:
		service.end_automatic_capture()


func test_service_normalizes_final_processor_replacement_before_delivery() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_stack_trace_source_context_enabled = false,
			p_stack_trace_variables_enabled = false,
			p_event_processors = [
				Callable(self, "_service_replace_with_unnormalized_exception"),
			],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)

	Expect.that(service.capture_message("input")).to_equal("memory:1")
	var captured: ObservabilityEvent = provider.events()[0]
	Expect.that(captured.timestamp_msec()).to_be_greater_than(1_000_000_000_000)
	Expect.that(captured.engine_ticks_msec()).to_be_greater_than(-1)
	Expect.that(captured.kind()).to_equal(&"exception")
	Expect.that(captured.exception().frames()).to_have_size(1)
	var frame: ObservabilityStackFrame = captured.exception().frames()[0]
	Expect.that(frame.line()).to_equal(-1)
	Expect.that(frame.context_line()).to_equal("")
	Expect.that(frame.pre_context()).to_equal(PackedStringArray())
	Expect.that(frame.post_context()).to_equal(PackedStringArray())
	Expect.that(frame.variables()).to_equal({})
	_shutdown_processing_service(service)


func test_automatic_logger_preserves_event_failure_after_successful_log() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_wrong_type")],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
			p_log_limits = ObservabilitySignalLimits.new(),
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service,
			config,
			func() -> int: return 1000,
			func() -> int: return 1,
		)

	logger._log_error(
			"run",
			"res://case.fs",
			1,
			"failure",
			"",
			false,
			Logger.ERROR_TYPE_ERROR,
			[],
		)

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].kind()).to_equal(&"log")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_DATA)
	_shutdown_processing_service(service)


func test_automatic_logger_preserves_breadcrumb_redaction_failure_after_log() -> void:
	var service := _processing_service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_event_mask = ObservabilityCaptureMask.NONE,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_log_limits = ObservabilitySignalLimits.new(),
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["breadcrumbs", "level"]),
						"invalid",
					),
			]),
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service,
			config,
			func() -> int: return 1000,
			func() -> int: return 1,
		)

	logger._log_error(
			"run",
			"res://case.fs",
			1,
			"failure",
			"",
			false,
			Logger.ERROR_TYPE_ERROR,
			[],
		)

	Expect.that(provider.breadcrumbs()).to_have_size(0)
	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.events()[0].kind()).to_equal(&"log")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_DATA)
	_shutdown_processing_service(service)


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
	Expect.that(service.capture_log(
			"suppressed", ObservabilityLevel.INFO, &"game", -1, {}, 1000,
		)).to_equal("")
	config.enabled = true
	Expect.that(service.capture_log(
			"accepted", ObservabilityLevel.INFO, &"game", -1, {}, 1000,
		)).to_equal("memory:1")
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
			p_timestamp_msec = 1721865600123,
			p_attributes = {},
			p_exception = null,
			p_engine_ticks_msec = 1000,
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

	Expect.that(explicit_config.provider_options().get("dsn")).to_equal(
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
	Expect.that(environment_config.provider_options().get("dsn")).to_equal(
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


func test_startup_settings_reject_declared_project_setting_type_mismatches() -> void:
	for invalid_values: Dictionary in _malformed_startup_project_values():
		var settings := ObservabilityStartupSettings.from_sources(invalid_values)
		Expect.that(settings.validation_error()).to_equal(
				Error.ERR_INVALID_PARAMETER,
			)
		Expect.that(settings.skip_status()).to_equal(
				ObservabilityStartupStatus.NOT_STARTED,
			)

	var numeric_dsn := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: 7,
	})
	Expect.that(numeric_dsn.has_dsn()).to_be_false()

	var array_environment := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.ENVIRONMENT: ["production"],
	})
	Expect.that(array_environment.observability_config().environment).to_equal(
			"export_release",
		)

	var numeric_release := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.RELEASE: 123,
	})
	Expect.that(numeric_release.observability_config().release).to_equal(
			"Unknown Foundry project@noversion",
		)

	var array_dist := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DIST: ["ios"],
	})
	Expect.that(array_dist.observability_config().dist).to_equal("")

	var provider_options: Dictionary = {"kept": true}
	var mixed_values := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: 7,
		ObservabilityStartupSettings.ENVIRONMENT: "production",
		ObservabilityStartupSettings.RELEASE: "1.2.3",
		ObservabilityStartupSettings.DIST: "ios",
		ObservabilityStartupSettings.PROVIDER_OPTIONS: provider_options,
	})
	provider_options["kept"] = false
	var mixed_config: ObservabilityConfig = mixed_values.observability_config()
	Expect.that(mixed_values.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)
	Expect.that(mixed_config.environment).to_equal("production")
	Expect.that(mixed_config.release).to_equal("1.2.3")
	Expect.that(mixed_config.dist).to_equal("ios")
	Expect.that(mixed_config.provider_options().get("kept")).to_be_true()


func test_startup_settings_expands_release_tokens_in_a_single_pass() -> void:
	var settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.RELEASE:
						"{app_name}|{app_version}",
			},
			{},
			{
				"app_name": "Oakhaven-{app_version}",
				"app_version": "1.2.3-{app_name}",
			},
		)

	Expect.that(settings.observability_config().release).to_equal(
			"Oakhaven-{app_version}|1.2.3-{app_name}",
		)


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

	var editor := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.SKIP_EDITOR_PLAY: true,
				ObservabilityStartupSettings.SKIP_DEBUG_EXPORTS: true,
			},
			{},
			{
				"editor_hint": true,
				"editor_feature": true,
				"debug_build": true,
			},
		)
	Expect.that(editor.observability_config().environment).to_equal("editor_dev")
	Expect.that(editor.skip_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_EDITOR,
		)

	var editor_play := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.SKIP_EDITOR_PLAY: true,
				ObservabilityStartupSettings.SKIP_DEBUG_EXPORTS: true,
			},
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

	var not_started := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{"debug_build": true},
		)
	Expect.that(not_started.skip_status()).to_equal(
			ObservabilityStartupStatus.NOT_STARTED,
		)

	var disabled := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.AUTO_INIT: false},
			{},
			{"editor_hint": true},
		)
	Expect.that(disabled.skip_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(disabled.capture_enabled()).to_be_true()

	var capture_disabled := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.ENABLED: false},
		)
	Expect.that(capture_disabled.skip_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(capture_disabled.capture_enabled()).to_be_false()


func test_startup_settings_validate_and_merge_provider_options() -> void:
	var nested_options := {"sample_rate": 0.5}
	var options := {
		"dsn": "wrong",
		"debug": false,
		"send_default_pii": true,
		"nested": nested_options,
	}
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
	nested_options["sample_rate"] = 0.1
	var resolved: Dictionary = settings.observability_config().provider_options()

	Expect.that(settings.validation_error()).to_equal(Error.OK)
	Expect.that(settings.has_dsn()).to_be_true()
	Expect.that(settings.debug_enabled()).to_be_true()
	Expect.that(resolved.get("dsn")).to_equal("https://public@example/1")
	Expect.that(resolved.get("debug")).to_be_true()
	Expect.that(resolved.get("send_default_pii")).to_be_true()
	Expect.that(resolved).to_equal({
			"dsn": "https://public@example/1",
			"debug": true,
			"send_default_pii": true,
			"nested": {"sample_rate": 0.5},
		})

	var auto_debug := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{"debug_build": true},
		)
	Expect.that(auto_debug.debug_enabled()).to_be_true()
	Expect.that(
			auto_debug.observability_config().provider_options().get("debug"),
		).to_be_true()

	var auto_release := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{"debug_build": false},
		)
	Expect.that(auto_release.debug_enabled()).to_be_false()
	Expect.that(
			auto_release.observability_config().provider_options().get("debug"),
		).to_be_false()

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


func test_startup_settings_provider_option_depth_counts_containers() -> void:
	var max_depth := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						_provider_options_with_container_depth(8),
			},
		)
	Expect.that(max_depth.validation_error()).to_equal(Error.OK)

	var too_deep := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						_provider_options_with_container_depth(9),
			},
		)
	Expect.that(too_deep.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)


func test_startup_settings_provider_options_reject_cycles_cleanly() -> void:
	var self_cycle: Dictionary = {}
	self_cycle["self"] = self_cycle
	var self_cycle_settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS: self_cycle,
			},
		)
	Expect.that(self_cycle_settings.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)

	var cyclic_array: Array = []
	var cyclic_dictionary: Dictionary = {"array": cyclic_array}
	cyclic_array.append(cyclic_dictionary)
	var mutual_cycle_settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						cyclic_dictionary,
			},
		)
	Expect.that(mutual_cycle_settings.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)


func test_startup_settings_provider_options_enforce_item_budget() -> void:
	var at_limit := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						_wide_provider_options(256),
			},
		)
	Expect.that(at_limit.validation_error()).to_equal(Error.OK)

	var over_limit := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						_wide_provider_options(257),
			},
		)
	Expect.that(over_limit.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)

	var shared: Dictionary = {"finite": 0.5, "label": &"shared"}
	var repeated := {"left": shared, "right": shared}
	var repeated_settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS: repeated,
			},
		)
	shared["finite"] = 0.25
	Expect.that(repeated_settings.validation_error()).to_equal(Error.OK)
	Expect.that(
			repeated_settings.observability_config().provider_options(),
		).to_equal({
			"left": {"finite": 0.5, "label": &"shared"},
			"right": {"finite": 0.5, "label": &"shared"},
			"dsn": "",
			"debug": false,
		})


func test_startup_settings_provider_options_charge_repeated_shared_subgraphs() -> void:
	var shared: Dictionary = _wide_provider_options(64)
	var repeated: Dictionary = {}
	for index: int in range(4):
		repeated["shared_%d" % index] = shared

	var settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS: repeated,
			},
		)

	Expect.that(settings.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)


func test_startup_settings_provider_options_validate_data_shapes() -> void:
	var nested_array: Array = [
		null,
		true,
		7,
		0.5,
		"text",
		&"name",
		[{"nested": "value"}],
	]
	var valid := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						{"values": nested_array},
			},
		)
	nested_array[6][0]["nested"] = "mutated"
	Expect.that(valid.validation_error()).to_equal(Error.OK)
	Expect.that(
			valid.observability_config().provider_options().get("values"),
		).to_equal([
			null,
			true,
			7,
			0.5,
			"text",
			&"name",
			[{"nested": "value"}],
		])

	var nonfinite := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS:
						{"nan": NAN, "infinity": INF},
			},
		)
	Expect.that(nonfinite.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)

	var invalid_key: Dictionary = {}
	invalid_key[7] = "not a provider option key"
	var invalid_key_settings := ObservabilityStartupSettings.from_sources(
			{
				ObservabilityStartupSettings.PROVIDER_OPTIONS: invalid_key,
			},
		)
	Expect.that(invalid_key_settings.validation_error()).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)


func test_startup_settings_register_project_defaults_idempotently() -> void:
	var had_auto_init: bool = ProjectSettings.has_setting(
			ObservabilityStartupSettings.AUTO_INIT)
	var previous_auto_init: Variant = ProjectSettings.get_setting(
			ObservabilityStartupSettings.AUTO_INIT, null)
	var had_debug_diagnostics: bool = ProjectSettings.has_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)
	var previous_debug_diagnostics: Variant = ProjectSettings.get_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS, null)

	ProjectSettings.set_setting(ObservabilityStartupSettings.AUTO_INIT, false)
	ProjectSettings.set_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS,
			ObservabilityStartupSettings.DEBUG_OFF,
		)
	ObservabilityStartupSettings.register_project_settings()
	ObservabilityStartupSettings.register_project_settings()
	var defaults: Dictionary = ObservabilityStartupSettings.project_setting_defaults()

	for setting_name: String in defaults:
		Expect.that(ProjectSettings.has_setting(setting_name)).to_be_true()
		var property_info: Dictionary = _project_setting_property(setting_name)
		var usage: int = property_info.get("usage", 0)
		Expect.that(
				usage & PROPERTY_USAGE_EDITOR_BASIC_SETTING,
			).to_equal(PROPERTY_USAGE_EDITOR_BASIC_SETTING)
		Expect.that(
				usage & PROPERTY_USAGE_RESTART_IF_CHANGED,
			).to_equal(0)
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.AUTO_INIT)).to_be_false()
	Expect.that(ProjectSettings.get_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)).to_equal(
					ObservabilityStartupSettings.DEBUG_OFF,
				)
	var debug_info: Dictionary = _project_setting_property(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)
	Expect.that(debug_info.get("type")).to_equal(TYPE_INT)
	Expect.that(debug_info.get("hint")).to_equal(PROPERTY_HINT_ENUM)
	Expect.that(debug_info.get("hint_string")).to_equal("Off,On,Auto")

	_restore_project_setting(
			ObservabilityStartupSettings.AUTO_INIT,
			had_auto_init,
			previous_auto_init,
		)
	_restore_project_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS,
			had_debug_diagnostics,
			previous_debug_diagnostics,
		)
	Expect.that(ProjectSettings.has_setting(
			ObservabilityStartupSettings.AUTO_INIT)).to_equal(had_auto_init)
	if had_auto_init:
		Expect.that(ProjectSettings.get_setting(
				ObservabilityStartupSettings.AUTO_INIT)).to_equal(
						previous_auto_init,
					)
	Expect.that(ProjectSettings.has_setting(
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)).to_equal(
					had_debug_diagnostics,
				)
	if had_debug_diagnostics:
		Expect.that(ProjectSettings.get_setting(
				ObservabilityStartupSettings.DEBUG_DIAGNOSTICS)).to_equal(
						previous_debug_diagnostics,
					)


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
	var service: FoundryObservability = _startup_service(settings)

	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.INITIALIZED,
		)
	Expect.that(service.startup_message()).to_contain("initialized")
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.capture_message("startup event")).to_equal("sentry:1")
	Expect.that(bridge.captured_payloads).to_have_size(1)
	if not bridge.configured_payload.is_empty():
		Expect.that(bridge.configured_payload["environment"]).to_equal(
				"production",
			)
		Expect.that(bridge.configured_payload["release"]).to_equal("1.2.3")
		Expect.that(
				bridge.configured_payload["provider_options"]["send_default_pii"],
			).to_be_true()

	service.shutdown()
	service.free()
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


func test_startup_reports_safe_disabled_missing_and_invalid_states() -> void:
	var disabled: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.ENABLED: false,
				ObservabilityStartupSettings.PROVIDER_OPTIONS: {
					"invalid": Vector2.ONE,
				},
			}),
		)
	Expect.that(disabled.startup_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(disabled.last_error()).to_equal(Error.OK)
	Expect.that(disabled.provider_name()).to_equal(&"null")
	disabled.shutdown()
	disabled.free()

	var auto_init_disabled: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.AUTO_INIT: false,
				ObservabilityStartupSettings.PROVIDER_OPTIONS: {
					"invalid": Vector2.ONE,
				},
			}),
		)
	Expect.that(auto_init_disabled.startup_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(auto_init_disabled.last_error()).to_equal(Error.OK)
	Expect.that(auto_init_disabled.provider_name()).to_equal(&"null")
	auto_init_disabled.shutdown()
	auto_init_disabled.free()

	var missing_dsn: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources(),
		)
	Expect.that(missing_dsn.startup_status()).to_equal(
			ObservabilityStartupStatus.MISSING_DSN,
		)
	Expect.that(missing_dsn.last_error()).to_equal(Error.ERR_UNCONFIGURED)
	Expect.that(missing_dsn.provider_name()).to_equal(&"null")
	missing_dsn.shutdown()
	missing_dsn.free()

	var invalid: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
				ObservabilityStartupSettings.PROVIDER_OPTIONS: {
					"invalid": Vector2.ONE,
				},
			}),
		)
	Expect.that(invalid.startup_status()).to_equal(
			ObservabilityStartupStatus.CONFIGURATION_FAILED,
		)
	Expect.that(invalid.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(invalid.startup_message()).to_contain("invalid")
	Expect.that(invalid.provider_name()).to_equal(&"null")
	invalid.shutdown()
	invalid.free()

	var null_settings: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.AUTO_INIT: false,
			}),
		)
	Expect.that(null_settings._initialize_startup(null)).to_equal(
			Error.ERR_INVALID_PARAMETER,
		)
	Expect.that(null_settings.startup_status()).to_equal(
			ObservabilityStartupStatus.CONFIGURATION_FAILED,
		)
	Expect.that(null_settings.startup_message()).to_contain("invalid")
	null_settings.shutdown()
	null_settings.free()


func test_startup_rejects_malformed_project_values_before_skip_resolution() -> void:
	for invalid_values: Dictionary in _malformed_startup_project_values():
		var settings := ObservabilityStartupSettings.from_sources(
				invalid_values,
				{},
				{"editor_feature": true, "debug_build": true},
			)
		var service: FoundryObservability = _startup_service(
				settings,
				"res://addons/FoundryObservability/ObservabilityProvider.fs",
			)

		Expect.that(service.startup_status()).to_equal(
				ObservabilityStartupStatus.CONFIGURATION_FAILED,
			)
		Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
		Expect.that(service.provider_name()).to_equal(&"null")

		service.shutdown()
		service.free()


func test_startup_reports_missing_noninstantiable_and_wrong_provider_scripts() -> void:
	var settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
	})
	for provider_path: String in [
		"res://addons/FoundryObservability/MissingProvider.fs",
		"res://addons/FoundryObservability/ObservabilityProvider.fs",
		"res://addons/FoundryObservability/ObservabilityConfig.fs",
	]:
		var service: FoundryObservability = _startup_service(
				settings,
				provider_path,
			)
		Expect.that(service.startup_status()).to_equal(
				ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
			)
		Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
		Expect.that(service.startup_message()).to_contain("unavailable")
		Expect.that(service.provider_name()).to_equal(&"null")
		service.shutdown()
		service.free()


func test_startup_configures_provider_before_other_provider_behavior() -> void:
	var service: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
			(
				"res://tests/support/"
				+ "startup_order_observability_provider.notest.fs"
			),
		)
	var candidate: Variant = service.get("_startup_provider")

	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.INITIALIZED,
		)
	Expect.that(candidate is StartupOrderObservabilityProvider).to_be_true()
	if not (candidate is StartupOrderObservabilityProvider):
		service.shutdown()
		service.free()
		return
	@warning_ignore("unsafe_cast")
	var provider: StartupOrderObservabilityProvider = (
		candidate as StartupOrderObservabilityProvider
	)
	Expect.that(provider.call_order).to_equal([&"configure"])
	Expect.that(service.provider_name()).to_equal(&"startup_order")
	Expect.that(provider.call_order).to_equal([
		&"configure",
		&"provider_name",
	])

	service.shutdown()
	service.free()


func test_startup_maps_provider_unavailable_configuration_result() -> void:
	var bridge := FakeSentryBridge.new()
	bridge.configure_result = Error.ERR_UNAVAILABLE
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var service: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
		)

	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.PROVIDER_UNAVAILABLE,
		)
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.startup_message()).to_contain("failed")
	Expect.that(service.provider_name()).to_equal(&"null")

	service.shutdown()
	service.free()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_startup_reuses_provider_for_reconfiguration_and_restart() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var first_settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
		ObservabilityStartupSettings.ENVIRONMENT: "production",
	})
	var service: FoundryObservability = _startup_service(first_settings)
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
	if not bridge.configured_payload.is_empty():
		Expect.that(bridge.configured_payload["environment"]).to_equal("staging")

	service.shutdown()
	Expect.that(service._initialize_startup(changed_settings)).to_equal(Error.OK)
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_available()).to_be_true()

	service.shutdown()
	service.free()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_startup_capture_disable_tears_down_once_and_can_restart() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var enabled_settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
	})
	var service: FoundryObservability = _startup_service(enabled_settings)
	var first_owner: String = bridge.active_owner

	Expect.that(service.capture_message("before disable")).to_equal("sentry:1")
	Expect.that(service.get("_automatic_logger")).to_not_be_null()
	bridge.flush_result = Error.FAILED
	var disabled_settings := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.ENABLED: false,
	})

	Expect.that(service._initialize_startup(disabled_settings)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)
	Expect.that(service.startup_message()).to_equal(
			"Automatic startup is disabled.",
		)
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.provider_name()).to_equal(&"null")
	Expect.that(service.is_enabled()).to_be_false()
	Expect.that(service.is_available()).to_be_false()
	Expect.that(service.get("_automatic_logger")).to_be_null()
	Expect.that(bridge.active_owner).to_equal("")
	Expect.that(bridge.flush_owners).to_equal([first_owner])
	Expect.that(bridge.shutdown_owners).to_equal([first_owner])
	Expect.that(bridge.shutdown_count).to_equal(1)
	Expect.that(service.capture_message("while disabled")).to_equal("")
	Expect.that(bridge.captured_payloads).to_have_size(1)

	Expect.that(service._initialize_startup(disabled_settings)).to_equal(Error.OK)
	Expect.that(bridge.flush_owners).to_have_size(1)
	Expect.that(bridge.shutdown_owners).to_have_size(1)
	Expect.that(bridge.shutdown_count).to_equal(1)

	bridge.flush_result = Error.OK
	Expect.that(service._initialize_startup(enabled_settings)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.INITIALIZED,
		)
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(service.is_available()).to_be_true()
	Expect.that(service.get("_automatic_logger")).to_not_be_null()
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(bridge.configured_payloads).to_have_size(2)
	Expect.that(service.capture_message("after restart")).to_equal("sentry:2")
	Expect.that(bridge.captured_payloads).to_have_size(2)

	service.shutdown()
	service.free()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_startup_only_skips_preserve_an_active_provider() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var service: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
		)
	var first_owner: String = bridge.active_owner
	var automatic_logger: Variant = service.get("_automatic_logger")

	var auto_init_disabled := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.AUTO_INIT: false,
	})
	Expect.that(service._initialize_startup(auto_init_disabled)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.DISABLED,
		)

	var editor := ObservabilityStartupSettings.from_sources(
			{},
			{},
			{"editor_hint": true},
		)
	Expect.that(service._initialize_startup(editor)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_EDITOR,
		)

	var editor_play := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.SKIP_EDITOR_PLAY: true},
			{},
			{"editor_feature": true},
		)
	Expect.that(service._initialize_startup(editor_play)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_EDITOR_PLAY,
		)

	var debug_export := ObservabilityStartupSettings.from_sources(
			{ObservabilityStartupSettings.SKIP_DEBUG_EXPORTS: true},
			{},
			{"debug_build": true},
		)
	Expect.that(service._initialize_startup(debug_export)).to_equal(Error.OK)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.SKIPPED_DEBUG,
		)

	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_enabled()).to_be_true()
	Expect.that(service.is_available()).to_be_true()
	Expect.that(service.get("_automatic_logger")).to_equal(automatic_logger)
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(bridge.flush_owners).to_have_size(0)
	Expect.that(bridge.shutdown_owners).to_have_size(0)
	Expect.that(bridge.shutdown_count).to_equal(0)
	Expect.that(service.capture_message("after startup skips")).to_equal("sentry:1")

	service.shutdown()
	service.free()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_startup_failure_preserves_working_provider_and_diagnostics() -> void:
	var bridge := FakeSentryBridge.new()
	Engine.register_singleton("SentryObservabilityBridge", bridge)
	var service: FoundryObservability = _startup_service(
			ObservabilityStartupSettings.from_sources({
				ObservabilityStartupSettings.DSN: "https://public@example/1",
			}),
		)
	var first_owner: String = bridge.active_owner
	bridge.configure_result = Error.FAILED
	var failed := ObservabilityStartupSettings.from_sources({
		ObservabilityStartupSettings.DSN: "https://public@example/1",
	})

	Expect.that(service._initialize_startup(failed)).to_equal(Error.FAILED)
	Expect.that(service.startup_status()).to_equal(
			ObservabilityStartupStatus.CONFIGURATION_FAILED,
		)
	Expect.that(service.startup_message()).to_contain("failed")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(bridge.active_owner).to_equal(first_owner)
	Expect.that(service.provider_name()).to_equal(&"sentry")
	Expect.that(service.is_available()).to_be_true()

	service.shutdown()
	service.free()
	Engine.unregister_singleton("SentryObservabilityBridge")


func test_attachment_service_validates_and_maps_provider_results() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
	))).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([1, 2, 3]),
			"diagnostics.bin",
	)
	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return

	Expect.that(service.add_attachment(null)).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	Expect.that(service.add_attachment(ObservabilityAttachment.new())).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
	for invalid_handle: String in ["", " padded", "padded ", "bad\nhandle"]:
		Expect.that(service.remove_attachment(invalid_handle)).to_be_false()
		Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)

	var handle: String = service.add_attachment(attachment)
	Expect.that(handle.begins_with("memory-attachment:")).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_attachment("memory-attachment:missing")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_DOES_NOT_EXIST)
	Expect.that(service.remove_attachment(handle)).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_attachment(handle)).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_DOES_NOT_EXIST)
	Expect.that(service.clear_attachments()).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	service.shutdown()


func test_attachment_capability_is_optional_and_malformed_results_fail_safely() -> void:
	var service: FoundryObservability = _service()
	var attachmentless := AttachmentlessObservabilityProvider.new()
	Expect.that(service.configure(attachmentless, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
	))).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([7]),
			"optional.bin",
	)
	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return

	Expect.that(service.add_attachment(attachment)).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.remove_attachment("valid-handle")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.clear_attachments()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("events still work")).to_equal("attachmentless:1")

	var malformed := MalformedAttachmentsProvider.new()
	Expect.that(service.configure(malformed, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
	))).to_equal(Error.OK)
	Expect.that(service.add_attachment(attachment)).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.remove_attachment("valid-handle")).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	Expect.that(service.clear_attachments()).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.FAILED)
	var prior_error: int = service.last_error()
	Expect.that(service.last_attachment_failures()).to_have_size(0)
	Expect.that(service.last_error()).to_equal(prior_error)
	Expect.that(service.capture_message("malformed attachments do not block events")).to_equal(
			"attachmentless:1",
	)
	service.shutdown()


func test_memory_byte_attachments_persist_snapshot_and_isolate_mutation() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
	))).to_equal(Error.OK)
	var source: PackedByteArray = PackedByteArray([1, 2, 3])
	var attachment := ObservabilityAttachment.from_bytes(
			source,
			"diagnostics.bin",
			"application/x-diagnostics",
			ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY,
	)
	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return
	var handle: String = service.add_attachment(attachment)
	source[0] = 99

	Expect.that(service.capture_message("first")).to_equal("memory:1")
	Expect.that(service.capture_message("second")).to_equal("memory:2")
	var snapshots: Array[Array] = provider.captured_attachments()
	Expect.that(snapshots).to_have_size(2)
	for snapshot: Array in snapshots:
		Expect.that(snapshot).to_equal([{
			"bytes": PackedByteArray([1, 2, 3]),
			"filename": "diagnostics.bin",
			"content_type": "application/x-diagnostics",
			"category": ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY,
			"path": "",
		}])

	snapshots[0][0]["bytes"][0] = 88
	snapshots[0][0]["filename"] = "mutated.bin"
	Expect.that(provider.captured_attachments()[0][0]).to_equal({
		"bytes": PackedByteArray([1, 2, 3]),
		"filename": "diagnostics.bin",
		"content_type": "application/x-diagnostics",
		"category": ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY,
		"path": "",
	})

	Expect.that(service.remove_attachment(handle)).to_be_true()
	Expect.that(service.capture_message("removed")).to_equal("memory:3")
	Expect.that(provider.captured_attachments()[2]).to_have_size(0)
	var second := ObservabilityAttachment.from_bytes(PackedByteArray([4]), "second.bin")
	Expect.that(second).not_().to_be_null()
	if second == null:
		return
	Expect.that(service.add_attachment(second)).not_().to_equal("")
	Expect.that(service.clear_attachments()).to_be_true()
	Expect.that(service.capture_message("cleared")).to_equal("memory:4")
	Expect.that(provider.captured_attachments()[3]).to_have_size(0)
	service.shutdown()


func test_memory_path_attachments_are_lazy_and_report_isolated_event_failures() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var path: String = "user://foundry-observability-attachment-test.bin"
	var global_path: String = ProjectSettings.globalize_path(path)
	DirAccess.remove_absolute(global_path)
	var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
	Expect.that(file).not_().to_be_null()
	if file == null:
		return
	file.store_buffer(PackedByteArray([1, 2]))
	file.close()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_attachment_bytes = 2,
	))).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_path(
			path,
			"lazy.bin",
			"application/x-lazy",
	)
	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return
	var handle: String = service.add_attachment(attachment)

	Expect.that(service.capture_message("accepted")).to_equal("memory:1")
	Expect.that(provider.captured_attachments()[0][0]["bytes"]).to_equal(
			PackedByteArray([1, 2]),
	)
	Expect.that(provider.captured_attachments()[0][0]["path"]).to_equal(path)
	Expect.that(service.last_attachment_failures()).to_have_size(0)

	file = FileAccess.open(path, FileAccess.WRITE)
	Expect.that(file).not_().to_be_null()
	if file == null:
		return
	file.store_buffer(PackedByteArray([3, 4, 5]))
	file.close()
	Expect.that(service.capture_message("oversized")).to_equal("memory:2")
	Expect.that(provider.captured_attachments()[1]).to_have_size(0)
	var oversized_failures: Array = service.last_attachment_failures()
	@warning_ignore("unsafe_cast")
	var oversized_failure: ObservabilityAttachmentFailure = (
			oversized_failures[0] as ObservabilityAttachmentFailure
	)
	Expect.that(oversized_failures).to_have_size(1)
	Expect.that(oversized_failure.handle()).to_equal(handle)
	Expect.that(oversized_failure.filename()).to_equal("lazy.bin")
	Expect.that(oversized_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.OVERSIZED,
	)
	Expect.that(oversized_failure.error()).to_equal(Error.FAILED)
	var prior_error: int = service.last_error()

	var temporary := ObservabilityAttachment.from_bytes(
			PackedByteArray([9]),
			"temporary.bin",
	)
	Expect.that(temporary).not_().to_be_null()
	if temporary == null:
		return
	var temporary_handle: String = service.add_attachment(temporary)
	Expect.that(temporary_handle).not_().to_equal("")
	Expect.that(service.last_attachment_failures()).to_have_size(1)
	Expect.that(service.remove_attachment(temporary_handle)).to_be_true()
	Expect.that(service.last_attachment_failures()).to_have_size(1)
	Expect.that(service.clear_attachments()).to_be_true()
	Expect.that(service.last_attachment_failures()).to_have_size(1)
	handle = service.add_attachment(attachment)
	Expect.that(handle).not_().to_equal("")
	Expect.that(service.last_attachment_failures()).to_have_size(1)

	Expect.that(service.capture_feedback(
			ObservabilityFeedback.new(p_message = "feedback"),
	)).not_().to_equal("")
	Expect.that(service.capture_counter("attachment.stability")).to_be_true()
	Expect.that(service.capture_breadcrumb(
			ObservabilityBreadcrumb.new(p_message = "breadcrumb"),
	)).to_be_true()
	DirAccess.remove_absolute(global_path)
	Expect.that(service.capture_log("log")).not_().to_equal("")
	Expect.that(provider.captured_attachments()[2]).to_have_size(0)
	Expect.that(service.flush()).to_equal(Error.OK)
	var stable_failures: Array = service.last_attachment_failures()
	@warning_ignore("unsafe_cast")
	var stable_failure: ObservabilityAttachmentFailure = (
			stable_failures[0] as ObservabilityAttachmentFailure
	)
	Expect.that(stable_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.OVERSIZED,
	)
	Expect.that(service.last_error()).to_equal(Error.OK)

	oversized_failures[0] = null
	Expect.that(service.last_attachment_failures()[0]).not_().to_be_null()
	Expect.that(service.capture_message("missing")).to_equal("memory:4")
	var missing_failures: Array = service.last_attachment_failures()
	@warning_ignore("unsafe_cast")
	var missing_failure: ObservabilityAttachmentFailure = (
			missing_failures[0] as ObservabilityAttachmentFailure
	)
	Expect.that(missing_failures).to_have_size(1)
	Expect.that(missing_failure.reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE,
	)
	Expect.that(missing_failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(prior_error).to_equal(Error.OK)
	service.shutdown()


func test_memory_attachment_session_boundaries_are_atomic() -> void:
	var service: FoundryObservability = _service()
	var first := MemoryObservabilityProvider.new()
	var second := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
	)
	Expect.that(service.configure(first, config)).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_bytes(
			PackedByteArray([1]),
			"session.bin",
	)
	Expect.that(attachment).not_().to_be_null()
	if attachment == null:
		return
	var original_handle: String = service.add_attachment(attachment)

	first.configure_result = Error.FAILED
	Expect.that(service.configure(first, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_attachment_bytes = 0,
	))).to_equal(Error.FAILED)
	Expect.that(service.capture_message("failed reconfigure preserves")).to_equal("memory:1")
	Expect.that(first.captured_attachments()[0]).to_have_size(1)
	Expect.that(service.remove_attachment(original_handle)).to_be_true()
	var replacement_handle: String = service.add_attachment(attachment)

	first.configure_result = Error.OK
	Expect.that(service.configure(first, config)).to_equal(Error.OK)
	Expect.that(service.remove_attachment(replacement_handle)).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_DOES_NOT_EXIST)
	var current_handle: String = service.add_attachment(attachment)
	config.enabled = false
	Expect.that(service.add_attachment(attachment)).to_equal("")
	Expect.that(service.remove_attachment(current_handle)).to_be_false()
	Expect.that(service.clear_attachments()).to_be_false()
	config.enabled = true
	Expect.that(service.capture_message("disabled operations preserved")).to_equal("memory:2")
	Expect.that(first.captured_attachments()[1]).to_have_size(1)

	Expect.that(service.configure(second, config)).to_equal(Error.OK)
	Expect.that(first.remove_attachment(current_handle)).to_equal(Error.FAILED)
	Expect.that(first.clear_attachments()).to_be_false()
	Expect.that(service.capture_message("replacement starts empty")).to_equal("memory:1")
	Expect.that(second.captured_attachments()[0]).to_have_size(0)
	service.shutdown()


func test_memory_attachment_zero_limit_clear_history_and_shutdown_state() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(
					["FoundryObservability: "]),
			p_max_attachment_bytes = 0,
	))).to_equal(Error.OK)
	var empty := ObservabilityAttachment.from_bytes(PackedByteArray(), "empty.bin")
	var positive := ObservabilityAttachment.from_bytes(PackedByteArray([1]), "positive.bin")
	var empty_file: FileAccess = FileAccess.open(
			"user://memory-zero-empty.log",
			FileAccess.WRITE,
		)
	empty_file.close()
	var empty_path := ObservabilityAttachment.from_path(
			"user://memory-zero-empty.log",
			"empty.log",
		)
	Expect.that(empty).not_().to_be_null()
	Expect.that(positive).not_().to_be_null()
	Expect.that(empty_path).not_().to_be_null()
	if empty == null or positive == null or empty_path == null:
		return
	Expect.that(service.add_attachment(empty)).not_().to_equal("")
	Expect.that(service.add_attachment(positive)).not_().to_equal("")
	Expect.that(service.add_attachment(empty_path)).not_().to_equal("")

	Expect.that(service.capture_message("zero limit")).to_equal("memory:1")
	Expect.that(provider.captured_attachments()[0]).to_have_size(0)
	var zero_limit_failures: Array = service.last_attachment_failures()
	Expect.that(zero_limit_failures).to_have_size(3)
	for zero_limit_failure: ObservabilityAttachmentFailure in zero_limit_failures:
		Expect.that(zero_limit_failure.reason()).to_equal(
				ObservabilityAttachmentFailure.OVERSIZED,
			)
	provider.clear()
	Expect.that(provider.captured_attachments()).to_have_size(0)
	Expect.that(service.capture_message("live attachments remain")).to_equal("memory:2")
	Expect.that(provider.captured_attachments()[0]).to_have_size(0)

	service.shutdown()
	Expect.that(provider.remove_attachment("memory-attachment:1")).to_equal(Error.FAILED)
	Expect.that(provider.clear_attachments()).to_be_false()
	Expect.that(provider.last_attachment_failures()).to_have_size(0)


func test_processing_pipeline_redacts_before_processors_and_again_after_replacement() -> void:
	_processing_order.clear()
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 2,
	)
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [
				Callable(self, "_processing_replace_first"),
				Callable(self, "_processing_reintroduce_secret"),
				Callable(self, "_processing_replace_second"),
			],
			p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_text(
						PackedStringArray(["event", "message"]), "secret", "safe"),
			]),
	))).to_equal(Error.OK)
	var result: Dictionary = pipeline.process_event(ObservabilityEvent.new(
			p_message = "secret",
	))
	Expect.that(result["accepted"]).to_be_true()
	var value: ObservabilityEvent = result["value"]
	Expect.that(value.message()).to_equal("safe-first-safe-second")
	Expect.that(_processing_order).to_equal(["first", "reintroduce", "second"])


func test_processing_pipeline_separates_logs_and_rejects_signal_crossing_processors() -> void:
	_processing_order.clear()
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_event_marker")],
			p_log_processors = [Callable(self, "_processing_log_marker")],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
			p_log_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	var log_result: Dictionary = pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"log", p_message = "line",
	))
	Expect.that(log_result["accepted"]).to_be_true()
	@warning_ignore("unsafe_cast")
	Expect.that((log_result["value"] as ObservabilityEvent).message()).to_equal("line-log")
	Expect.that(_processing_order).to_equal(["log"])

	var crossing := ObservabilityProcessingPipeline.new()
	Expect.that(crossing.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_log_processors = [Callable(self, "_processing_log_to_event")],
			p_event_processors = [], p_metric_processors = [],
			p_log_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(crossing.process_event(ObservabilityEvent.new(
			p_kind = &"log",
	))["accepted"]).to_be_false()
	Expect.that(crossing.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)
	Expect.that(crossing.last_diagnostic().processor_index()).to_equal(0)


func test_processing_pipeline_reports_processor_drops_and_wrong_types() -> void:
	var dropped := ObservabilityProcessingPipeline.new()
	Expect.that(dropped.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_drop")],
			p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(dropped.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(dropped.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.PROCESSOR)
	Expect.that(dropped.last_diagnostic().processor_index()).to_equal(0)
	Expect.that(dropped.last_diagnostic().error()).to_equal(Error.OK)

	var invalid := ObservabilityProcessingPipeline.new()
	Expect.that(invalid.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_wrong_type")],
			p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(invalid.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(invalid.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)
	Expect.that(invalid.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)


func test_processing_pipeline_fails_closed_when_processor_target_expires() -> void:
	var callbacks := MutableProcessingCallbacks.new()
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(_processing_config([
			Callable(callbacks, "process_event"),
	]))).to_equal(Error.OK)
	callbacks.free()

	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)


func test_processing_pipeline_fails_closed_when_metric_filter_target_expires() -> void:
	var callbacks := MutableProcessingCallbacks.new()
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_metric_filter = Callable(callbacks, "filter_metric"),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_metric_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	callbacks.free()

	Expect.that(pipeline.process_metric(ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0,
	))["accepted"]).to_be_false()
	Expect.that(pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)


func test_processing_pipeline_metric_filter_is_pre_redacted_and_precedes_processors() -> void:
	_processing_order.clear()
	_processing_filter_values.clear()
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_metric_filter = Callable(self, "_processing_metric_filter"),
			p_metric_processors = [Callable(self, "_processing_metric_marker")],
			p_event_processors = [], p_log_processors = [],
			p_metric_limits = ObservabilitySignalLimits.new(),
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_text(
						PackedStringArray(["metric", "name"]), "secret", "safe"),
			]),
	))).to_equal(Error.OK)
	var result: Dictionary = pipeline.process_metric(ObservabilityMetric.new(
			p_name = "secret.metric", p_value = 1.0,
	))
	Expect.that(result["accepted"]).to_be_true()
	Expect.that(_processing_filter_values).to_equal(["safe.metric"])
	Expect.that(_processing_order).to_equal(["metric"])
	@warning_ignore("unsafe_cast")
	Expect.that((result["value"] as ObservabilityMetric).name()).to_equal("safe.metric-processed")

	var rejected := ObservabilityProcessingPipeline.new()
	Expect.that(rejected.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_metric_filter = Callable(self, "_processing_metric_false"),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_metric_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(rejected.process_metric(ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0,
	))["accepted"]).to_be_false()
	Expect.that(rejected.last_diagnostic().processor_index()).to_equal(-1)
	Expect.that(rejected.last_diagnostic().error()).to_equal(Error.OK)

	var malformed := ObservabilityProcessingPipeline.new()
	Expect.that(malformed.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_metric_filter = Callable(self, "_processing_metric_non_bool"),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_metric_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(malformed.process_metric(ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0,
	))["accepted"]).to_be_false()
	Expect.that(malformed.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)


func test_processing_pipeline_blocks_recursive_entries_before_callbacks_or_limits() -> void:
	_recursive_pipeline = ObservabilityProcessingPipeline.new(
			func() -> int: return 1,
			func() -> int: return 1,
			func() -> int: return 1,
	)
	Expect.that(_recursive_pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_reenter_metric")],
			p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1),
			p_metric_limits = ObservabilitySignalLimits.new(1),
	))).to_equal(Error.OK)
	Expect.that(_recursive_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(_recursive_pipeline.recursive_drop_count()).to_equal(1)
	Expect.that(_recursive_pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.RECURSIVE)
	Expect.that(_recursive_pipeline.process_metric(ObservabilityMetric.new(
			p_name = "after.recursion", p_value = 1.0,
	))["accepted"]).to_be_true()


func test_processing_pipeline_recursive_state_result_is_operation_local() -> void:
	_recursive_state_result = {}
	_recursive_pipeline = ObservabilityProcessingPipeline.new()
	Expect.that(_recursive_pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_reenter_state")],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)

	Expect.that(
			_recursive_pipeline.process_event(ObservabilityEvent.new())["accepted"],
		).to_be_true()
	Expect.that(_recursive_state_result["accepted"]).to_be_false()
	Expect.that(_recursive_state_result["valid"]).to_be_false()
	Expect.that(_recursive_state_result["value"]).to_be_null()
	Expect.that(_recursive_state_result["signal"]).to_equal(
			ObservabilityProcessingDiagnostic.STATE,
		)
	Expect.that(_recursive_state_result["reason"]).to_equal(
			ObservabilityProcessingDiagnostic.RECURSIVE,
		)
	Expect.that(_recursive_state_result["error"]).to_equal(Error.ERR_BUSY)


func test_processing_pipeline_state_result_survives_later_diagnostic_overwrite() -> void:
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_service_drop_event")],
			p_log_processors = [],
			p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_value(
						PackedStringArray(["contexts", "account"]),
						7,
					),
			]),
	))).to_equal(Error.OK)

	var state_result: Dictionary = pipeline.redact_contexts({
		"account": {"token": "secret"},
	})
	Expect.that(state_result["accepted"]).to_be_false()
	Expect.that(state_result["valid"]).to_be_false()
	Expect.that(state_result["signal"]).to_equal(
			ObservabilityProcessingDiagnostic.STATE,
		)
	Expect.that(state_result["reason"]).to_equal(
			ObservabilityProcessingDiagnostic.REDACTION_FAILED,
		)
	Expect.that(state_result["error"]).to_equal(Error.ERR_INVALID_DATA)

	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.PROCESSOR,
		)
	Expect.that(pipeline.last_diagnostic().error()).to_equal(Error.OK)
	Expect.that(state_result["reason"]).to_equal(
			ObservabilityProcessingDiagnostic.REDACTION_FAILED,
		)
	Expect.that(state_result["error"]).to_equal(Error.ERR_INVALID_DATA)


func test_processing_pipeline_reconfigure_reentry_is_recursive_and_outer_result_is_stale() -> void:
	_processing_owner_id = 1
	_lifecycle_nested_result = {}
	_lifecycle_pipeline = ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(_lifecycle_pipeline.configure(_processing_config(
			[Callable(self, "_processing_reconfigure_and_reenter")],
	))).to_equal(Error.OK)

	var outer: Dictionary = _lifecycle_pipeline.process_event(ObservabilityEvent.new())
	Expect.that(_lifecycle_nested_result["accepted"]).to_be_false()
	Expect.that(outer["accepted"]).to_be_false()
	Expect.that(_lifecycle_pipeline.recursive_drop_count()).to_equal(1)
	Expect.that(_lifecycle_pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.RECURSIVE)
	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()


func test_processing_pipeline_suppresses_old_generation_processor_drop_after_reset() -> void:
	_processing_owner_id = 1
	_lifecycle_pipeline = ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(_lifecycle_pipeline.configure(_processing_config(
			[Callable(self, "_processing_reconfigure_and_drop")],
	))).to_equal(Error.OK)

	Expect.that(_lifecycle_pipeline.process_event(
			ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(_lifecycle_pipeline.last_diagnostic()).to_be_null()
	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()


func test_processing_pipeline_allows_overlapping_different_owners() -> void:
	_processing_owner_id = 1
	_overlap_stage = 0
	_overlap_nested_result = {}
	_lifecycle_pipeline = ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(_lifecycle_pipeline.configure(_processing_config(
			[Callable(self, "_processing_overlap_different_owner")],
	))).to_equal(Error.OK)

	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(_overlap_nested_result["accepted"]).to_be_true()
	Expect.that(_lifecycle_pipeline.recursive_drop_count()).to_equal(0)
	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()


func test_processing_pipeline_exact_release_preserves_other_active_owner_reservation() -> void:
	_processing_owner_id = 1
	_overlap_stage = 0
	_overlap_nested_result = {}
	_overlap_recursive_result = {}
	_lifecycle_pipeline = ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(_lifecycle_pipeline.configure(_processing_config(
			[Callable(self, "_processing_overlap_and_probe_original_owner")],
	))).to_equal(Error.OK)

	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(_overlap_nested_result["accepted"]).to_be_true()
	Expect.that(_overlap_recursive_result["accepted"]).to_be_false()
	Expect.that(_lifecycle_pipeline.recursive_drop_count()).to_equal(1)
	Expect.that(_lifecycle_pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()


func test_processing_pipeline_uses_signal_local_limiters_and_publishes_limit_diagnostics() -> void:
	_processing_clock_calls = 0
	_processing_frame_calls = 0
	var pipeline := ObservabilityProcessingPipeline.new(
			Callable(self, "_processing_clock"),
			Callable(self, "_processing_frame"),
	)
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1),
			p_log_limits = ObservabilitySignalLimits.new(1),
			p_metric_limits = ObservabilitySignalLimits.new(1),
	))).to_equal(Error.OK)
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(pipeline.last_diagnostic().limit_kind()).to_equal(
			ObservabilityProcessingDiagnostic.PER_FRAME)
	Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"log",
	))["accepted"]).to_be_true()
	Expect.that(pipeline.process_metric(ObservabilityMetric.new(
			p_name = "limit.metric", p_value = 1.0,
	))["accepted"]).to_be_true()
	Expect.that(_processing_clock_calls).to_equal(4)
	Expect.that(_processing_frame_calls).to_equal(4)


func test_processing_pipeline_configure_is_atomic_and_success_resets_state() -> void:
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
	)
	var active := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1))
	Expect.that(pipeline.configure(active)).to_equal(Error.OK)
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	var prior: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	var invalid := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [], p_event_sample_rate = NAN)
	Expect.that(pipeline.configure(invalid)).to_equal(Error.ERR_INVALID_PARAMETER)
	var infinite := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_log_sample_rate = INF,
	)
	Expect.that(pipeline.configure(infinite)).to_equal(Error.ERR_INVALID_PARAMETER)
	var invalid_processor := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable()], p_log_processors = [], p_metric_processors = [],
	)
	Expect.that(pipeline.configure(invalid_processor)).to_equal(Error.ERR_INVALID_DATA)
	var invalid_rule := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([null]),
	)
	Expect.that(pipeline.configure(invalid_rule)).to_equal(Error.ERR_INVALID_DATA)
	var overlong_path := PackedStringArray()
	for _index: int in range(ObservabilityRedactor.MAX_RULE_PATH_SEGMENTS + 1):
		overlong_path.append("segment")
	var overlong_policy := ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.replace_text(overlong_path, "", "safe"),
			]),
	)
	Expect.that(pipeline.configure(overlong_policy)).to_equal(Error.ERR_INVALID_DATA)
	Expect.that(pipeline.last_diagnostic().sequence()).to_equal(prior.sequence())
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_sample_rate = 1.0,
			p_event_limits = ObservabilitySignalLimits.new(1),
	))).to_equal(Error.OK)
	Expect.that(pipeline.last_diagnostic()).to_be_null()
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()
	Expect.that(pipeline.configure(null)).to_equal(Error.ERR_INVALID_PARAMETER)


func test_processing_pipeline_configures_structural_rules_and_rejects_matching_payloads() -> void:
	var matching := ObservabilityProcessingPipeline.new()
	Expect.that(matching.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.remove_field(
						PackedStringArray(["event", "message"])),
			]),
	))).to_equal(Error.OK)
	Expect.that(matching.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(matching.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.REDACTION_FAILED)
	Expect.that(matching.last_diagnostic().rule_index()).to_equal(0)
	Expect.that(matching.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)

	var nonmatching := ObservabilityProcessingPipeline.new()
	Expect.that(nonmatching.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_redaction_policy = ObservabilityRedactionPolicy.new([
				ObservabilityRedactionRule.remove_field(
						PackedStringArray(["other", "message"])),
			]),
	))).to_equal(Error.OK)
	Expect.that(nonmatching.process_event(ObservabilityEvent.new())["accepted"]).to_be_true()


func test_processing_pipeline_records_provider_results_and_keeps_admission_consumed() -> void:
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
	)
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(1),
	))).to_equal(Error.OK)
	var accepted: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	Expect.that(accepted["accepted"]).to_be_true()
	pipeline.record_provider_result(&"event", false, Error.OK, accepted["operation_token"])
	var rejected: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	Expect.that(rejected.reason()).to_equal(ObservabilityProcessingDiagnostic.PROVIDER_REJECTED)
	Expect.that(rejected.error()).to_equal(Error.FAILED)
	var isolated: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	Expect.that(isolated).to_not_equal(rejected)
	pipeline.record_provider_result(&"event", true, Error.ERR_INVALID_PARAMETER)
	Expect.that(pipeline.last_diagnostic().sequence()).to_equal(rejected.sequence())
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()


func test_processing_pipeline_pairs_provider_results_to_current_pending_tokens() -> void:
	_processing_owner_id = 1
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(pipeline.configure(_processing_config([]))).to_equal(Error.OK)
	var old: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	Expect.that(old["accepted"]).to_be_true()
	Expect.that(old.has("operation_token")).to_be_true()

	Expect.that(pipeline.configure(_processing_config([]))).to_equal(Error.OK)
	pipeline.record_provider_result(&"event", false, Error.FAILED, old["operation_token"])
	Expect.that(pipeline.last_diagnostic()).to_be_null()

	var current: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	pipeline.record_provider_result(&"event", true, Error.ERR_INVALID_PARAMETER,
			current["operation_token"])
	var accepted: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	Expect.that(accepted.outcome()).to_equal(ObservabilityProcessingDiagnostic.ACCEPTED)
	Expect.that(accepted.error()).to_equal(Error.OK)

	pipeline.record_provider_result(&"event", false, Error.FAILED, current["operation_token"])
	pipeline.record_provider_result(&"metric", false, Error.FAILED, current["operation_token"])
	pipeline.record_provider_result(&"invalid", false, Error.FAILED, current["operation_token"])
	pipeline.record_provider_result(&"event", false, Error.FAILED, -999)
	Expect.that(pipeline.last_diagnostic().sequence()).to_equal(accepted.sequence())

	var paired_after_mismatch: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	pipeline.record_provider_result(
			&"metric", false, Error.FAILED, paired_after_mismatch["operation_token"])
	Expect.that(pipeline.last_diagnostic().sequence()).to_equal(accepted.sequence())
	pipeline.record_provider_result(
			&"event", false, Error.ERR_INVALID_PARAMETER,
			paired_after_mismatch["operation_token"])
	var rejected: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	Expect.that(rejected.reason()).to_equal(
			ObservabilityProcessingDiagnostic.PROVIDER_REJECTED)
	Expect.that(rejected.error()).to_equal(Error.ERR_INVALID_PARAMETER)

	var legacy: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	Expect.that(legacy["accepted"]).to_be_true()
	pipeline.record_provider_result(&"event", true, Error.FAILED)
	Expect.that(pipeline.last_diagnostic().outcome()).to_equal(
			ObservabilityProcessingDiagnostic.ACCEPTED)
	Expect.that(pipeline.last_diagnostic().error()).to_equal(Error.OK)


func test_processing_pipeline_keeps_only_latest_pending_result_per_owner_and_signal() -> void:
	_processing_owner_id = 1
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(pipeline.configure(_processing_config([]))).to_equal(Error.OK)
	var first: Dictionary = pipeline.process_event(ObservabilityEvent.new())
	var latest: Dictionary = pipeline.process_event(ObservabilityEvent.new())

	Expect.that(pipeline._pending_provider_results).to_have_size(1)
	Expect.that(pipeline._pending_provider_results.has(latest["operation_token"])).to_be_true()
	Expect.that(pipeline._pending_provider_results.has(first["operation_token"])).to_be_false()
	pipeline.record_provider_result(&"event", false, Error.FAILED, first["operation_token"])
	Expect.that(pipeline.last_diagnostic()).to_be_null()

	pipeline.record_provider_result(&"event", true, Error.FAILED)
	var accepted: ObservabilityProcessingDiagnostic = pipeline.last_diagnostic()
	Expect.that(accepted.outcome()).to_equal(ObservabilityProcessingDiagnostic.ACCEPTED)
	Expect.that(pipeline._pending_provider_results).to_have_size(0)
	pipeline.record_provider_result(&"event", false, Error.FAILED, latest["operation_token"])
	pipeline.record_provider_result(&"event", false, Error.FAILED)
	Expect.that(pipeline.last_diagnostic().sequence()).to_equal(accepted.sequence())


func test_processing_pipeline_bounds_pending_results_and_evicts_oldest_token() -> void:
	_processing_owner_id = 1
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
			Callable(self, "_processing_owner"),
	)
	Expect.that(pipeline.configure(_processing_config([]))).to_equal(Error.OK)
	var oldest: Dictionary = {}
	var newest: Dictionary = {}
	for owner_id: int in range(1, 1026):
		_processing_owner_id = owner_id
		var result: Dictionary = pipeline.process_event(ObservabilityEvent.new())
		Expect.that(result["accepted"]).to_be_true()
		if oldest.is_empty():
			oldest = result
		newest = result

	Expect.that(pipeline._pending_provider_results).to_have_size(1024)
	Expect.that(pipeline._pending_provider_results.has(oldest["operation_token"])).to_be_false()
	Expect.that(pipeline._pending_provider_results.has(newest["operation_token"])).to_be_true()
	Expect.that(pipeline._processing_depth).to_equal(0)
	Expect.that(pipeline._active_operations).to_have_size(0)
	Expect.that(pipeline.recursive_drop_count()).to_equal(0)

	pipeline.record_provider_result(&"event", false, Error.FAILED, oldest["operation_token"])
	Expect.that(pipeline.last_diagnostic()).to_be_null()
	pipeline.record_provider_result(&"event", true, Error.FAILED, newest["operation_token"])
	Expect.that(pipeline.last_diagnostic().outcome()).to_equal(
			ObservabilityProcessingDiagnostic.ACCEPTED)


func test_processing_pipeline_identity_excludes_attributes_but_includes_message() -> void:
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
	)
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {}, p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(0, 1000),
	))).to_equal(Error.OK)
	Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_message = "same", p_attributes = {"secret": "one"},
	))["accepted"]).to_be_true()
	Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_message = "same", p_attributes = {"secret": "two"},
	))["accepted"]).to_be_false()
	Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_message = "different", p_attributes = {"secret": "two"},
	))["accepted"]).to_be_true()


func test_processing_pipeline_rejects_event_to_log_processor_crossing() -> void:
	var pipeline := ObservabilityProcessingPipeline.new()
	Expect.that(pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [Callable(self, "_processing_event_to_log")],
			p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(pipeline.process_event(ObservabilityEvent.new())["accepted"]).to_be_false()
	Expect.that(pipeline.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)
	Expect.that(pipeline.last_diagnostic().processor_index()).to_equal(0)
	Expect.that(pipeline.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)


func test_processing_pipeline_rejects_invalid_metric_processor_replacements() -> void:
	var invalid_replacements: Array[Callable] = [
		Callable(self, "_processing_metric_too_long_name"),
		Callable(self, "_processing_metric_control_name"),
		Callable(self, "_processing_metric_control_unit"),
		Callable(self, "_processing_metric_invalid_unit"),
		Callable(self, "_processing_metric_invalid_attribute_key"),
		Callable(self, "_processing_metric_invalid_attribute_value"),
		Callable(self, "_processing_metric_nonfinite_attribute"),
	]
	for processor: Callable in invalid_replacements:
		var pipeline := ObservabilityProcessingPipeline.new()
		Expect.that(pipeline.configure(ObservabilityConfig.new(
				p_global_attributes = {}, p_provider_options = {},
				p_automatic_message_filter_prefixes = PackedStringArray(),
				p_event_processors = [], p_log_processors = [],
				p_metric_processors = [processor],
				p_metric_limits = ObservabilitySignalLimits.new(),
		))).to_equal(Error.OK)
		Expect.that(pipeline.process_metric(ObservabilityMetric.new(
				p_name = "metric", p_value = 1.0,
		))["accepted"]).to_be_false()
		Expect.that(pipeline.last_diagnostic().reason()).to_equal(
				ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)
		Expect.that(pipeline.last_diagnostic().processor_index()).to_equal(0)
		Expect.that(pipeline.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)


func test_processing_pipeline_reports_metric_processor_drop_and_wrong_type() -> void:
	var dropped := ObservabilityProcessingPipeline.new()
	Expect.that(dropped.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [],
			p_metric_processors = [Callable(self, "_processing_metric_drop")],
			p_metric_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(dropped.process_metric(ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0,
	))["accepted"]).to_be_false()
	Expect.that(dropped.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.PROCESSOR)
	Expect.that(dropped.last_diagnostic().processor_index()).to_equal(0)
	Expect.that(dropped.last_diagnostic().error()).to_equal(Error.OK)

	var wrong_type := ObservabilityProcessingPipeline.new()
	Expect.that(wrong_type.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [],
			p_metric_processors = [Callable(self, "_processing_metric_wrong_type")],
			p_metric_limits = ObservabilitySignalLimits.new(),
	))).to_equal(Error.OK)
	Expect.that(wrong_type.process_metric(ObservabilityMetric.new(
			p_name = "metric", p_value = 1.0,
	))["accepted"]).to_be_false()
	Expect.that(wrong_type.last_diagnostic().reason()).to_equal(
			ObservabilityProcessingDiagnostic.INVALID_PROCESSOR_RESULT)
	Expect.that(wrong_type.last_diagnostic().processor_index()).to_equal(0)
	Expect.that(wrong_type.last_diagnostic().error()).to_equal(Error.ERR_INVALID_DATA)


func test_processing_pipeline_event_identity_matrix_excludes_attributes_and_scope() -> void:
	var excluded := _processing_repeated_pipeline()
	Expect.that(excluded.process_event(ObservabilityEvent.new(
			p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.INFO,
			p_message = "same", p_attributes = {"one": 1}, p_scope = ObservabilityScope.new(),
	))["accepted"]).to_be_true()
	var changed_scope := ObservabilityScope.new()
	changed_scope.set_tag("region", "iad")
	Expect.that(excluded.process_event(ObservabilityEvent.new(
			p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.INFO,
			p_message = "same", p_attributes = {"two": 2}, p_scope = changed_scope,
	))["accepted"]).to_be_false()

	for event: ObservabilityEvent in [
		ObservabilityEvent.new(p_kind = &"exception", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "same"),
		ObservabilityEvent.new(p_kind = &"message", p_source = &"other", p_level = ObservabilityLevel.INFO, p_message = "same"),
		ObservabilityEvent.new(p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.WARN, p_message = "same"),
		ObservabilityEvent.new(p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "other"),
		ObservabilityEvent.new(p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "same", p_attributes = {}, p_exception = ObservabilityException.new("Type", "message", "stack", {})),
	]:
		var pipeline := _processing_repeated_pipeline()
		Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"message", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "same",
		))["accepted"]).to_be_true()
		Expect.that(pipeline.process_event(event)["accepted"]).to_be_true()


func test_processing_pipeline_log_and_metric_identity_matrices() -> void:
	var excluded_log := _processing_repeated_pipeline()
	Expect.that(excluded_log.process_event(ObservabilityEvent.new(
			p_kind = &"log", p_source = &"game", p_level = ObservabilityLevel.INFO,
			p_message = "same", p_attributes = {"one": 1}, p_engine_ticks_msec = 1,
	))["accepted"]).to_be_true()
	Expect.that(excluded_log.process_event(ObservabilityEvent.new(
			p_kind = &"log", p_source = &"game", p_level = ObservabilityLevel.INFO,
			p_message = "same", p_attributes = {"two": 2}, p_engine_ticks_msec = 2,
	))["accepted"]).to_be_false()
	for event: ObservabilityEvent in [
		ObservabilityEvent.new(p_kind = &"log", p_source = &"other", p_level = ObservabilityLevel.INFO, p_message = "same"),
		ObservabilityEvent.new(p_kind = &"log", p_source = &"game", p_level = ObservabilityLevel.WARN, p_message = "same"),
		ObservabilityEvent.new(p_kind = &"log", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "other"),
	]:
		var pipeline := _processing_repeated_pipeline()
		Expect.that(pipeline.process_event(ObservabilityEvent.new(
			p_kind = &"log", p_source = &"game", p_level = ObservabilityLevel.INFO, p_message = "same",
		))["accepted"]).to_be_true()
		Expect.that(pipeline.process_event(event)["accepted"]).to_be_true()

	var excluded_metric := _processing_repeated_pipeline()
	Expect.that(excluded_metric.process_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 1.0,
			p_unit = "count", p_attributes = {"one": 1},
	))["accepted"]).to_be_true()
	Expect.that(excluded_metric.process_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 2.0,
			p_unit = "count", p_attributes = {"two": 2},
	))["accepted"]).to_be_false()
	for metric: ObservabilityMetric in [
		ObservabilityMetric.new(p_type = ObservabilityMetricType.DISTRIBUTION, p_name = "metric", p_value = 1.0, p_unit = "count"),
		ObservabilityMetric.new(p_type = ObservabilityMetricType.GAUGE, p_name = "other", p_value = 1.0, p_unit = "count"),
		ObservabilityMetric.new(p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 1.0, p_unit = "other"),
	]:
		var pipeline := _processing_repeated_pipeline()
		Expect.that(pipeline.process_metric(ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 1.0, p_unit = "count",
		))["accepted"]).to_be_true()
		Expect.that(pipeline.process_metric(metric)["accepted"]).to_be_true()


func _processing_replace_first(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("first")
	return _processing_event_with_message(event, event.message() + "-first")


func _processing_reintroduce_secret(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("reintroduce")
	return _processing_event_with_message(event, event.message() + "-secret")


func _processing_replace_second(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("second")
	return _processing_event_with_message(event, event.message() + "-second")


func _processing_event_marker(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("event")
	return event


func _processing_log_marker(event: ObservabilityEvent) -> ObservabilityEvent:
	_processing_order.append("log")
	return _processing_event_with_message(event, event.message() + "-log")


func _processing_log_to_event(event: ObservabilityEvent) -> ObservabilityEvent:
	return ObservabilityEvent.new(p_kind = &"message", p_message = event.message())


func _processing_event_to_log(event: ObservabilityEvent) -> ObservabilityEvent:
	return ObservabilityEvent.new(p_kind = &"log", p_message = event.message())


func _processing_drop(_event: ObservabilityEvent) -> Variant:
	return null


func _processing_wrong_type(_event: ObservabilityEvent) -> Variant:
	return ObservabilityMetric.new(p_name = "wrong", p_value = 1.0)


func _processing_metric_filter(metric: ObservabilityMetric) -> bool:
	_processing_filter_values.append(metric.name())
	return true


func _processing_clock() -> int:
	_processing_clock_calls += 1
	return 100


func _processing_frame() -> int:
	_processing_frame_calls += 1
	return 1


func _processing_metric_false(_metric: ObservabilityMetric) -> bool:
	return false


func _processing_metric_non_bool(_metric: ObservabilityMetric) -> Variant:
	return "not a bool"


func _processing_metric_marker(metric: ObservabilityMetric) -> ObservabilityMetric:
	_processing_order.append("metric")
	return ObservabilityMetric.new(
			metric.type(), metric.name() + "-processed", metric.value(), metric.unit(), metric.attributes())


func _processing_metric_drop(_metric: ObservabilityMetric) -> Variant:
	return null


func _processing_metric_wrong_type(_metric: ObservabilityMetric) -> Variant:
	return ObservabilityEvent.new()


func _processing_metric_too_long_name(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(p_name = _repeated("n", 201), p_value = 1.0)


func _processing_metric_control_name(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(p_name = "bad\nname", p_value = 1.0)


func _processing_metric_invalid_unit(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 1.0,
			p_unit = "bad\u00a0unit",
	)


func _processing_metric_control_unit(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(
			p_type = ObservabilityMetricType.GAUGE, p_name = "metric", p_value = 1.0,
			p_unit = "bad\nunit",
	)


func _processing_metric_invalid_attribute_key(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(p_name = "metric", p_value = 1.0, p_attributes = {"bad\nkey": 1})


func _processing_metric_invalid_attribute_value(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(p_name = "metric", p_value = 1.0, p_attributes = {"valid": []})


func _processing_metric_nonfinite_attribute(_metric: ObservabilityMetric) -> ObservabilityMetric:
	return ObservabilityMetric.new(p_name = "metric", p_value = 1.0, p_attributes = {"valid": NAN})


func _processing_repeated_pipeline() -> ObservabilityProcessingPipeline:
	var pipeline := ObservabilityProcessingPipeline.new(
			func() -> int: return 10,
			func() -> int: return 1,
	)
	pipeline.configure(ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [], p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(0, 1000),
			p_log_limits = ObservabilitySignalLimits.new(0, 1000),
			p_metric_limits = ObservabilitySignalLimits.new(0, 1000),
	))
	return pipeline


func _processing_owner() -> int:
	return _processing_owner_id


func _processing_config(processors: Array[Callable]) -> ObservabilityConfig:
	return ObservabilityConfig.new(
			p_global_attributes = {}, p_provider_options = {},
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = processors, p_log_processors = [], p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
	)


func _processing_reconfigure_and_reenter(event: ObservabilityEvent) -> ObservabilityEvent:
	_lifecycle_pipeline.configure(_processing_config([]))
	_lifecycle_nested_result = _lifecycle_pipeline.process_event(ObservabilityEvent.new())
	return event


func _processing_reconfigure_and_drop(_event: ObservabilityEvent) -> Variant:
	_lifecycle_pipeline.configure(_processing_config([]))
	return null


func _processing_overlap_different_owner(event: ObservabilityEvent) -> ObservabilityEvent:
	if _overlap_stage == 0:
		_overlap_stage = 1
		_processing_owner_id = 2
		_overlap_nested_result = _lifecycle_pipeline.process_event(ObservabilityEvent.new())
		_processing_owner_id = 1
		_overlap_stage = 2
	return event


func _processing_overlap_and_probe_original_owner(event: ObservabilityEvent) -> ObservabilityEvent:
	if _overlap_stage == 0:
		_overlap_stage = 1
		_processing_owner_id = 2
		_overlap_nested_result = _lifecycle_pipeline.process_event(ObservabilityEvent.new())
		_processing_owner_id = 1
		_overlap_stage = 3
	elif _overlap_stage == 1:
		_overlap_stage = 2
		_processing_owner_id = 1
		_overlap_recursive_result = _lifecycle_pipeline.process_event(ObservabilityEvent.new())
		_processing_owner_id = 2
	return event


func _processing_reenter_metric(event: ObservabilityEvent) -> ObservabilityEvent:
	_recursive_pipeline.process_metric(ObservabilityMetric.new(
			p_name = "nested.metric", p_value = 1.0,
		))
	return event


func _processing_reenter_state(event: ObservabilityEvent) -> ObservabilityEvent:
	_recursive_state_result = _recursive_pipeline.redact_contexts({
		"nested": {"value": "must not be admitted"},
	})
	return event


func _processing_event_with_message(event: ObservabilityEvent, message: String) -> ObservabilityEvent:
	return ObservabilityEvent.new(
			event.kind(), event.level(), message, event.source(), event.timestamp_msec(),
			event.attributes(), event.exception(), event.engine_ticks_msec(), event.scope())


func _service_replace_event(event: ObservabilityEvent) -> ObservabilityEvent:
	_service_event_processor_calls += 1
	return _processing_event_with_message(event, "processed")


func _service_drop_event(_event: ObservabilityEvent) -> Variant:
	return null


func _automatic_capture_nested_message(
		event: ObservabilityEvent,
) -> ObservabilityEvent:
	_automatic_nested_capture_result = _automatic_nested_service.capture_message("nested")
	var diagnostic: ObservabilityProcessingDiagnostic? = (
			_automatic_nested_service.last_processing_diagnostic()
		)
	if diagnostic != null:
		_automatic_nested_reason = diagnostic.reason()
	return event


func _service_set_context_during_processing(
		event: ObservabilityEvent,
) -> ObservabilityEvent:
	_state_reentry_result = _state_reentry_service.set_context(
			"nested",
			{"value": "must not reach provider"},
		)
	_state_reentry_error = _state_reentry_service.last_error()
	var diagnostic: ObservabilityProcessingDiagnostic = (
			_state_reentry_service.last_processing_diagnostic()
		)
	_state_reentry_signal = diagnostic.processing_signal()
	_state_reentry_reason = diagnostic.reason()
	_state_reentry_diagnostic_error = diagnostic.error()
	return event


func _service_reconfigure_from_event_processor(
		event: ObservabilityEvent,
) -> ObservabilityEvent:
	_service_lifecycle_configure_result = _service_lifecycle_service.configure(
			_service_lifecycle_provider,
			_service_lifecycle_config,
		)
	return _processing_event_with_message(event, "stale replacement")


func _service_reconfigure_from_metric_processor(
		metric: ObservabilityMetric,
) -> ObservabilityMetric:
	_service_lifecycle_configure_result = _service_lifecycle_service.configure(
			_service_lifecycle_provider,
			_service_lifecycle_config,
		)
	return ObservabilityMetric.new(
			metric.type(),
			"stale.metric",
			metric.value(),
			metric.unit(),
			metric.attributes(),
		)


func _service_replace_with_unnormalized_exception(
		_event: ObservabilityEvent,
) -> ObservabilityEvent:
	return ObservabilityEvent.new(
			p_kind = &"exception",
			p_level = ObservabilityLevel.ERROR,
			p_message = "replacement",
			p_source = &"processor",
			p_timestamp_msec = ObservabilityEvent.UNASSIGNED_TIMESTAMP,
			p_attributes = {},
			p_exception = ObservabilityException.new(
					p_type_name = "ReplacementFailure",
					p_message = "replacement",
					p_attributes = {},
					p_frames = [
						ObservabilityStackFrame.new(),
						ObservabilityStackFrame.new(
								p_file = "res://replacement.fs",
								p_function = "run",
								p_line = 0,
								p_context_line = "secret context",
								p_pre_context = PackedStringArray(["before"]),
								p_post_context = PackedStringArray(["after"]),
								p_variables = {"secret": "value"},
							),
					],
				),
			p_engine_ticks_msec = ObservabilityEvent.UNASSIGNED_TIMESTAMP,
		)


func _expect_service_per_frame_diagnostic(
		service: FoundryObservability,
		processing_signal: StringName,
) -> void:
	var diagnostic: ObservabilityProcessingDiagnostic = service.last_processing_diagnostic()
	Expect.that(diagnostic.processing_signal()).to_equal(processing_signal)
	Expect.that(diagnostic.reason()).to_equal(ObservabilityProcessingDiagnostic.RATE_LIMITED)
	Expect.that(diagnostic.limit_kind()).to_equal(ObservabilityProcessingDiagnostic.PER_FRAME)


func _expect_service_diagnostic_unchanged(
		service: FoundryObservability,
		prior: ObservabilityProcessingDiagnostic,
) -> void:
	var current: ObservabilityProcessingDiagnostic = service.last_processing_diagnostic()
	Expect.that(current.sequence()).to_equal(prior.sequence())
	Expect.that(current.processing_signal()).to_equal(prior.processing_signal())
	Expect.that(current.outcome()).to_equal(prior.outcome())
	Expect.that(current.reason()).to_equal(prior.reason())
	Expect.that(current.limit_kind()).to_equal(prior.limit_kind())
	Expect.that(current.error()).to_equal(prior.error())


func _processing_service() -> FoundryObservability:
	var service_script: Script = ResourceLoader.load(
			"res://addons/FoundryObservability/FoundryObservability.fs",
		) as Script
	@warning_ignore("unsafe_method_access")
	var candidate: Variant = service_script.new(
			ObservabilityStartupSettings.new(),
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs",
			Callable(self, "_service_processing_clock"),
			Callable(self, "_service_processing_frame"),
	)
	@warning_ignore("unsafe_cast")
	return candidate as FoundryObservability


func _reconfiguring_reservation_service() -> ReconfiguringReservationService:
	return ReconfiguringReservationService.new(
			ObservabilityStartupSettings.new(),
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs",
			Callable(self, "_service_processing_clock"),
			Callable(self, "_service_processing_frame"),
		)


func _ordinary_generation_config(enabled: bool) -> ObservabilityConfig:
	return ObservabilityConfig.new(
			p_enabled = enabled,
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_capture_enabled = false,
			p_automatic_message_filter_prefixes = PackedStringArray(),
			p_event_processors = [],
			p_log_processors = [],
			p_metric_processors = [],
			p_event_limits = ObservabilitySignalLimits.new(),
			p_log_limits = ObservabilitySignalLimits.new(),
			p_metric_limits = ObservabilitySignalLimits.new(),
		)


func _service_processing_clock() -> int:
	_service_processing_clock_calls += 1
	return _service_processing_clock_msec


func _service_processing_frame() -> int:
	_service_processing_frame_calls += 1
	return _service_processing_frame_index


func _shutdown_processing_service(service: FoundryObservability) -> void:
	service.shutdown()
	service.free()


func _expect_state_redaction_failure(service: FoundryObservability) -> void:
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_DATA)
	var diagnostic: ObservabilityProcessingDiagnostic? = (
			service.last_processing_diagnostic()
		)
	Expect.that(diagnostic).to_not_be_null()
	if diagnostic == null:
		return
	Expect.that(diagnostic.processing_signal()).to_equal(
			ObservabilityProcessingDiagnostic.STATE,
		)
	Expect.that(diagnostic.outcome()).to_equal(
			ObservabilityProcessingDiagnostic.DROPPED,
		)
	Expect.that(diagnostic.reason()).to_equal(
			ObservabilityProcessingDiagnostic.REDACTION_FAILED,
		)
	Expect.that(diagnostic.error()).to_equal(Error.ERR_INVALID_DATA)


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability


func _startup_service(
		settings: ObservabilityStartupSettings,
		provider_path: String = (
			"res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs"
		),
) -> FoundryObservability:
	var service_script: Script = ResourceLoader.load(
			"res://addons/FoundryObservability/FoundryObservability.fs",
		) as Script
	@warning_ignore("unsafe_method_access")
	var candidate: Variant = service_script.new(settings, provider_path)
	if not (candidate is FoundryObservability):
		return null
	@warning_ignore("unsafe_cast")
	return candidate as FoundryObservability


func _push_test_error(message: String) -> void:
	push_error(message)


func _repeated(value: String, count: int) -> String:
	var result := ""
	for _index in range(count):
		result += value
	return result


func _provider_options_with_container_depth(depth: int) -> Dictionary:
	var options: Dictionary = {}
	var cursor: Dictionary = options
	for _level: int in range(depth):
		var nested: Dictionary = {}
		cursor["nested"] = nested
		cursor = nested
	cursor["value"] = "leaf"
	return options


func _wide_provider_options(item_count: int) -> Dictionary:
	var options: Dictionary = {}
	for index: int in range(item_count):
		options["item_%d" % index] = index
	return options


func _project_setting_property(setting_name: String) -> Dictionary:
	for property_info: Dictionary in ProjectSettings.get_property_list():
		if property_info.get("name") == setting_name:
			return property_info
	return {}


func _malformed_startup_project_values() -> Array[Dictionary]:
	var dsn: String = "https://public@example/1"
	return [
		{
			ObservabilityStartupSettings.AUTO_INIT: 0,
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.ENABLED: "true",
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.SKIP_EDITOR_PLAY: "true",
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.SKIP_DEBUG_EXPORTS: "true",
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS:
					ObservabilityStartupSettings.DEBUG_OFF,
			ObservabilityStartupSettings.DSN: dsn,
		},
		{ObservabilityStartupSettings.DSN: 7},
		{
			ObservabilityStartupSettings.ENVIRONMENT: ["production"],
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.RELEASE: 123,
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.DIST: ["ios"],
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.RELEASE: &"named-release",
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.DEBUG_DIAGNOSTICS: "Auto",
			ObservabilityStartupSettings.DSN: dsn,
		},
		{
			ObservabilityStartupSettings.ENABLED: false,
			ObservabilityStartupSettings.DSN: 7,
		},
	]


func _restore_project_setting(
		setting_name: String,
		was_present: bool,
		previous_value: Variant,
) -> void:
	if was_present:
		ProjectSettings.set_setting(setting_name, previous_value)
	else:
		ProjectSettings.clear(setting_name)


func _keep_combat_metric(metric: ObservabilityMetric) -> bool:
	return metric.name().begins_with("combat.")


func _invalid_metric_filter(_metric: ObservabilityMetric) -> String:
	return "not a bool"
