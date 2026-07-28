namespace foundry.observability.processing

## Immutable instruction returned by a structured-value traversal policy.
final class_name ObservabilityValueVisitDecision extends RefCounted

enum Action:
	KEEP = 0
	DESCEND = 1
	REJECT = 2

final var _action: Action
final var _value: Variant


func _init(p_action: Action, p_value: Variant = null) -> void:
	_action = p_action
	_value = p_value


static func keep(p_value: Variant) -> ObservabilityValueVisitDecision:
	return ObservabilityValueVisitDecision.new(Action.KEEP, p_value)


static func descend(
		p_value: Variant = null,
) -> ObservabilityValueVisitDecision:
	return ObservabilityValueVisitDecision.new(Action.DESCEND, p_value)


static func reject() -> ObservabilityValueVisitDecision:
	return ObservabilityValueVisitDecision.new(Action.REJECT)


func action() -> Action:
	return _action


func value() -> Variant:
	return _value
