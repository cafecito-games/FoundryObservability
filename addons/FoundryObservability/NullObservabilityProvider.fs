namespace foundry.observability

## Safe provider used before configuration and when no backend is available.
class_name NullObservabilityProvider
extends RefCounted
uses ObservabilityProvider


## Returns the null provider identifier.
func provider_name() -> StringName:
	return &"null"


## Always returns false because no backend is configured.
func is_available() -> bool:
	return false


## Accepts configuration without enabling a backend and returns Error.OK.
func configure(_config: ObservabilityConfig) -> int:
	return Error.OK


## Performs a safe no-op and returns an empty event ID.
func capture(_event: ObservabilityEvent) -> String:
	return ""


## Performs a safe no-op and returns an empty feedback ID.
func capture_feedback(_feedback: ObservabilityFeedback) -> String:
	return ""


## Performs a safe no-op and returns Error.OK.
func flush(_timeout_msec: int = 2000) -> int:
	return Error.OK


## Performs a safe no-op shutdown.
func shutdown() -> void:
	pass
