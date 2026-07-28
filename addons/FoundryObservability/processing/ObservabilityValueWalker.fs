namespace foundry.observability.processing

## Rebuilds structured values with bounded, cycle-safe, policy-driven traversal.
final class_name ObservabilityValueWalker extends RefCounted

final var _max_depth: int
final var _max_items: int


class WalkState extends RefCounted:
	var remaining: int
	var active_containers: Array = []

	func _init(p_remaining: int) -> void:
		remaining = p_remaining


class WalkStep extends RefCounted:
	final var valid: bool
	final var included: bool
	final var value: Variant
	final var exhausted: bool
	final var failed_rule_index: int

	func _init(
			p_valid: bool,
			p_included: bool,
			p_value: Variant,
			p_exhausted: bool,
			p_failed_rule_index: int = -1,
	) -> void:
		valid = p_valid
		included = p_included
		value = p_value
		exhausted = p_exhausted
		failed_rule_index = p_failed_rule_index


func _init(max_depth: int, max_items: int) -> void:
	_max_depth = maxi(0, max_depth)
	_max_items = maxi(0, max_items)


func walk(
		value: Variant,
		policy: ObservabilityValuePolicy,
) -> ObservabilityRedactionResult[Variant]:
	if policy == null:
		return ObservabilityRedactionResult[Variant].failure()
	var state: WalkState = WalkState.new(_max_items)
	var step: WalkStep = _walk_value(
		value,
		PackedStringArray(),
		0,
		policy,
		state,
		false,
	)
	if not step.valid or not step.included:
		return ObservabilityRedactionResult[Variant].failure(
				step.failed_rule_index,
			)
	return ObservabilityRedactionResult[Variant].success(step.value)


func _walk_value(
		value: Variant,
		path: PackedStringArray,
		depth: int,
		policy: ObservabilityValuePolicy,
		state: WalkState,
		parent_is_dictionary: bool,
) -> WalkStep:
	if state.remaining <= 0:
		if policy.item_limit_is_failure():
			return WalkStep.new(false, false, null, true)
		return WalkStep.new(true, false, null, true)
	state.remaining -= 1

	var decision: ObservabilityValueVisitDecision = policy.visit(path, value)
	if decision == null:
		return WalkStep.new(false, false, null, false)
	match decision.action():
		ObservabilityValueVisitDecision.Action.KEEP:
			if decision.value() is Dictionary or decision.value() is Array:
				return WalkStep.new(false, false, null, false)
			return WalkStep.new(true, true, _copy_leaf(decision.value()), false)
		ObservabilityValueVisitDecision.Action.REJECT:
			var rejection_is_failure: bool = (
				policy.value_rejection_is_failure(
					path,
					value,
					parent_is_dictionary,
				)
			)
			return WalkStep.new(
				not rejection_is_failure,
				false,
				null,
				false,
				(
					maxi(
						-1,
						policy.value_rejection_rule_index(
							path,
							value,
							parent_is_dictionary,
						),
					)
					if rejection_is_failure
					else -1
				),
			)
		ObservabilityValueVisitDecision.Action.DESCEND:
			var descended_value: Variant = (
				decision.value()
				if decision.value() != null
				else value
			)
			return _descend(
					descended_value,
					path,
					depth,
					policy,
					state,
				)
		_:
			return WalkStep.new(false, false, null, false)


func _descend(
		value: Variant,
		path: PackedStringArray,
		depth: int,
		policy: ObservabilityValuePolicy,
		state: WalkState,
) -> WalkStep:
	if not value is Dictionary and not value is Array:
		return WalkStep.new(false, false, null, false)
	if depth > _max_depth or _contains_identity(state.active_containers, value):
		return WalkStep.new(
			not policy.invalid_container_is_failure(),
			false,
			null,
			false,
		)
	state.active_containers.append(value)
	var result: WalkStep
	if value is Dictionary:
		result = _walk_dictionary(value, path, depth, policy, state)
	else:
		var array_value: Array = value
		result = _walk_array(array_value, path, depth, policy, state)
	state.active_containers.pop_back()
	return result


func _walk_dictionary(
		source: Dictionary,
		path: PackedStringArray,
		depth: int,
		policy: ObservabilityValuePolicy,
		state: WalkState,
) -> WalkStep:
	var rebuilt: Dictionary = {}
	var exhausted: bool = false
	for source_key: Variant in source:
		var child_path: PackedStringArray = path.duplicate()
		child_path.append(str(source_key))
		var key_decision: ObservabilityValueVisitDecision = policy.visit_dictionary_key(child_path, source_key)
		if key_decision == null:
			return WalkStep.new(false, false, null, false)
		if key_decision.action() != ObservabilityValueVisitDecision.Action.KEEP:
			## Rejected keys still consume their dictionary-entry budget, matching
			## traversal of a rejected value and preserving truncating policies.
			if state.remaining <= 0:
				if policy.item_limit_is_failure():
					return WalkStep.new(false, false, null, true)
				exhausted = true
				break
			state.remaining -= 1
			if key_decision.action() \
					!= ObservabilityValueVisitDecision.Action.REJECT \
					or policy.reject_is_failure():
				return WalkStep.new(false, false, null, false)
			continue
		if key_decision.value() is Dictionary or key_decision.value() is Array:
			return WalkStep.new(false, false, null, false)
		var child: WalkStep = _walk_value(
			source[source_key],
			child_path,
			depth + 1,
			policy,
			state,
			true,
		)
		if not child.valid:
			return child
		if child.included:
			rebuilt[_copy_leaf(key_decision.value())] = child.value
		if child.exhausted:
			exhausted = true
			break
	return WalkStep.new(true, true, rebuilt, exhausted)


func _walk_array(
		source: Array,
		path: PackedStringArray,
		depth: int,
		policy: ObservabilityValuePolicy,
		state: WalkState,
) -> WalkStep:
	var rebuilt: Array = []
	var exhausted: bool = false
	for index: int in range(source.size()):
		var child_path: PackedStringArray = path.duplicate()
		child_path.append(str(index))
		var child: WalkStep = _walk_value(
			source[index],
			child_path,
			depth + 1,
			policy,
			state,
			false,
		)
		if not child.valid:
			return child
		if child.included:
			rebuilt.append(child.value)
		if child.exhausted:
			exhausted = true
			break
	return WalkStep.new(true, true, rebuilt, exhausted)


func _contains_identity(active_containers: Array, value: Variant) -> bool:
	for active_container: Variant in active_containers:
		if is_same(active_container, value):
			return true
	return false


func _copy_leaf(value: Variant) -> Variant:
	if value is PackedByteArray:
		return value.duplicate()
	if value is PackedInt32Array:
		return value.duplicate()
	if value is PackedInt64Array:
		return value.duplicate()
	if value is PackedFloat32Array:
		return value.duplicate()
	if value is PackedFloat64Array:
		return value.duplicate()
	if value is PackedStringArray:
		return value.duplicate()
	if value is PackedVector2Array:
		return value.duplicate()
	if value is PackedVector3Array:
		return value.duplicate()
	if value is PackedVector4Array:
		return value.duplicate()
	if value is PackedColorArray:
		return value.duplicate()
	return value
