namespace foundry.observability

## Provider-neutral normalized custom metric value.
class_name ObservabilityMetric
extends RefCounted

final var _type: int
final var _name: String
final var _value: float
final var _unit: String
final var _attributes: Dictionary


## Creates a metric with defensively copied attributes.
func _init(
		p_type: int = ObservabilityMetricType.COUNTER,
		p_name: String = "",
		p_value: float = 0.0,
		p_unit: String = "",
		p_attributes: Dictionary = {},
) -> void:
	_type = p_type
	_name = p_name
	_value = p_value
	_unit = p_unit
	_attributes = p_attributes.duplicate(true)


## Returns the ObservabilityMetricType value.
func type() -> int:
	return _type


## Returns the stable metric name.
func name() -> String:
	return _name


## Returns the recorded numeric value.
func value() -> float:
	return _value


## Returns the optional measurement unit.
func unit() -> String:
	return _unit


## Returns a deep copy of the structured attributes.
func attributes() -> Dictionary:
	return _attributes.duplicate(true)
