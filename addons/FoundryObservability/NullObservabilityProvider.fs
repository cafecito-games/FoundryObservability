namespace foundry.observability

## Safe provider used before configuration and when no backend is available.
class_name NullObservabilityProvider
extends RefCounted
uses ObservabilityProvider


func provider_name() -> StringName:
	return &"null"


func is_available() -> bool:
	return false


func configure(_config: ObservabilityConfig) -> int:
	return Error.OK


func capture(_event: ObservabilityEvent) -> String:
	return ""


func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


func shutdown() -> void:
	pass
