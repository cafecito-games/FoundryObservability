namespace foundry.observability.processing

import foundry.observability

## Immutable accepted or rejected attempt to pin one provider generation.
## Invalid public construction is reported and canonicalized to a collaborator-free rejection.
final class_name ObservabilityProviderCall extends RefCounted

final var _accepted: bool
final var _snapshot: ObservabilityProviderSnapshot?
final var _error: int


func _init(
		p_accepted: bool,
		p_snapshot: ObservabilityProviderSnapshot?,
		p_error: int,
) -> void:
	if not is_valid_state(p_accepted, p_snapshot, p_error):
		push_error("ObservabilityProviderCall requires a valid call state.")
		_accepted = false
		_snapshot = null
		_error = Error.ERR_INVALID_DATA
		return
	_accepted = p_accepted
	_snapshot = p_snapshot
	_error = p_error


@warning_ignore("shadowed_variable")
static func begin(
		snapshot: ObservabilityProviderSnapshot,
) -> ObservabilityProviderCall:
	return ObservabilityProviderCall.new(true, snapshot, Error.OK)


@warning_ignore("shadowed_variable")
static func rejected(error: int) -> ObservabilityProviderCall:
	return ObservabilityProviderCall.new(false, null, error)


## Returns whether fields form one of the two closed provider-call shapes.
static func is_valid_state(
		p_accepted: bool,
		p_snapshot: ObservabilityProviderSnapshot?,
		p_error: int,
) -> bool:
	if p_accepted:
		return p_snapshot != null and p_error == Error.OK
	return p_snapshot == null and p_error != Error.OK


func accepted() -> bool:
	return _accepted


func snapshot() -> ObservabilityProviderSnapshot?:
	return _snapshot


func error() -> int:
	return _error


func provider() -> ObservabilityProvider:
	return _required_snapshot().provider()


func config() -> ObservabilityConfig:
	return _required_snapshot().config()


func pipeline() -> ObservabilityProcessingPipeline:
	return _required_snapshot().pipeline()


func generation() -> int:
	return _required_snapshot().generation()


func _required_snapshot() -> ObservabilityProviderSnapshot:
	assert(
			_accepted and _snapshot != null,
			"Rejected provider calls do not expose collaborators.",
		)
	return _snapshot
