namespace foundry.observability

## Immutable ordered collection of redaction rules.
class_name ObservabilityRedactionPolicy
extends RefCounted

final var _rules: Array[ObservabilityRedactionRule]


func _init(p_rules: Array[ObservabilityRedactionRule] = []) -> void:
	_rules = []
	for rule: ObservabilityRedactionRule in p_rules:
		_rules.append(rule.duplicate())


func rules() -> Array[ObservabilityRedactionRule]:
	var copied: Array[ObservabilityRedactionRule] = []
	for rule: ObservabilityRedactionRule in _rules:
		copied.append(rule.duplicate())
	return copied


func duplicate() -> ObservabilityRedactionPolicy:
	return ObservabilityRedactionPolicy.new(_rules)


func is_valid() -> bool:
	for rule: ObservabilityRedactionRule in _rules:
		if not rule.is_valid():
			return false
	return true
