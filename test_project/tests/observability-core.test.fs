namespace foundry.observability.tests

import foundry.testlib
import foundry.observability

class_name ObservabilityCoreTests
extends RefCounted
uses Test

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


func test_automatic_capture_masks_and_config_defaults() -> void:
	var config := ObservabilityConfig.new()

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
	Expect.that(service.last_error()).to_equal(Error.ERR_INVALID_PARAMETER)
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
	Expect.that(provider.breadcrumbs()).to_equal([breadcrumb])
	provider.clear_breadcrumbs()
	Expect.that(provider.breadcrumbs()).to_have_size(0)
	service.shutdown()


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


func test_automatic_logger_suppresses_duplicate_errors_deterministically() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 1000,
			p_automatic_events_per_frame = 0,
			p_automatic_event_throttle_count = 0,
		)
	var capture_time := AutomaticCaptureTime.new(1000, 1)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, capture_time.now, capture_time.frame)

	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	capture_time.now_msec = 1500
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(2)
	Expect.that(provider.breadcrumbs()).to_have_size(1)

	capture_time.now_msec = 2000
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(4)
	Expect.that(provider.breadcrumbs()).to_have_size(2)
	service.shutdown()


func test_automatic_logger_does_not_suppress_after_all_destinations_reject() -> void:
	var service: FoundryObservability = _service()
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
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)

	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	logger._log_error("tick", "res://loop.fs", 9, "boom", "", false,
			Logger.ERROR_TYPE_ERROR, [])

	Expect.that(provider.capture_count).to_equal(4)
	Expect.that(provider.breadcrumb_count).to_equal(2)
	service.shutdown()


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
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_log_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_repeated_error_window_msec = 0,
			p_automatic_events_per_frame = 1,
			p_automatic_event_throttle_count = 2,
			p_automatic_event_throttle_window_msec = 1000,
		)
	var capture_time := AutomaticCaptureTime.new(1000, 1)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, capture_time.now, capture_time.frame)

	logger._log_error("tick", "res://loop.fs", 9, "a", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	logger._log_error("tick", "res://loop.fs", 9, "b", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	capture_time.frame_index = 2
	capture_time.now_msec = 1002
	logger._log_error("tick", "res://loop.fs", 9, "c", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	logger._log_error("tick", "res://loop.fs", 9, "d", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	capture_time.frame_index = 3
	capture_time.now_msec = 1003
	logger._log_error("tick", "res://loop.fs", 9, "e", "", false,
			Logger.ERROR_TYPE_ERROR, [])

	var exception_count: int = 0
	for event: ObservabilityEvent in provider.events():
		if event.kind() == &"exception":
			exception_count += 1
	Expect.that(exception_count).to_equal(2)
	Expect.that(provider.breadcrumbs()).to_have_size(5)
	Expect.that(provider.events()).to_have_size(7)

	capture_time.frame_index = 4
	capture_time.now_msec = 2001
	logger._log_error("tick", "res://loop.fs", 9, "f", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.breadcrumbs()).to_have_size(6)
	Expect.that(provider.events()).to_have_size(9)
	service.shutdown()


func test_automatic_logger_bounds_identity_state_and_resets_limits() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()
	var config := ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_automatic_event_mask = ObservabilityCaptureMask.ERROR,
			p_automatic_breadcrumb_mask = ObservabilityCaptureMask.NONE,
			p_automatic_repeated_error_window_msec = 100000,
			p_automatic_events_per_frame = 0,
			p_automatic_event_throttle_count = 0,
		)
	Expect.that(service.configure(provider, config)).to_equal(Error.OK)
	var logger := AutomaticObservabilityLogger.new(
			service, config, func() -> int: return 1000, func() -> int: return 1)
	for index: int in range(101):
		logger._log_error("tick", "res://loop.fs", index, str(index), "", false,
				Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(101)

	logger._log_error("tick", "res://loop.fs", 0, "0", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(102)
	logger.reset()
	logger._log_error("tick", "res://loop.fs", 0, "0", "", false,
			Logger.ERROR_TYPE_ERROR, [])
	Expect.that(provider.events()).to_have_size(103)
	service.shutdown()


func test_automatic_capture_reservation_is_atomic() -> void:
	var service: FoundryObservability = _service()

	Expect.that(service.try_begin_automatic_capture()).to_be_true()
	Expect.that(service.try_begin_automatic_capture()).to_be_false()
	service.end_automatic_capture()
	Expect.that(service.try_begin_automatic_capture()).to_be_true()
	service.end_automatic_capture()


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
	var service: FoundryObservability = _service()
	var provider: MemoryObservabilityProvider = MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_global_attributes = {},
			p_provider_options = {},
			p_log_rate_limit_per_second = 1,
		))).to_equal(Error.OK)
	Expect.that(service.capture_log(
			"first", ObservabilityLevel.INFO, &"game", -1, {}, 1000,
		)).to_equal("memory:1")
	Expect.that(service.capture_log(
			"dropped", ObservabilityLevel.INFO, &"game", -1, {}, 1500,
		)).to_equal("")
	Expect.that(service.capture_log(
			"next window", ObservabilityLevel.INFO, &"game", -1, {}, 2000,
		)).to_equal("memory:2")
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


func _service() -> FoundryObservability:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.root.get_node("FoundryObservability") as FoundryObservability


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


func _keep_combat_metric(metric: ObservabilityMetric) -> bool:
	return metric.name().begins_with("combat.")


func _invalid_metric_filter(_metric: ObservabilityMetric) -> String:
	return "not a bool"
