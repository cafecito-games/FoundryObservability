namespace foundry.observability.processing

## Immutable payload-free admission decision; invalid public construction becomes sampled drop.
final class_name ObservabilityAdmissionDecision extends RefCounted

final var _accepted: bool
final var _reason: ObservabilityProcessingReason
final var _limit_kind: ObservabilityLimitKind


func _init(
		p_accepted: bool,
		p_reason: ObservabilityProcessingReason,
		p_limit_kind: ObservabilityLimitKind,
) -> void:
	## Invalid inputs are reported and canonicalized to a sampled drop.
	if not is_valid_state(p_accepted, p_reason, p_limit_kind):
		push_error("ObservabilityAdmissionDecision requires a valid admission state.")
		_accepted = false
		_reason = ObservabilityProcessingReason.SAMPLED
		_limit_kind = ObservabilityLimitKind.NONE
		assert(
				is_valid_state(_accepted, _reason, _limit_kind),
				"ObservabilityAdmissionDecision fallback must remain valid.",
			)
		return
	_accepted = p_accepted
	_reason = p_reason
	_limit_kind = p_limit_kind


static func accepted_decision() -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.new(
			true,
			ObservabilityProcessingReason.NONE,
			ObservabilityLimitKind.NONE,
		)


static func dropped(
		p_reason: ObservabilityProcessingReason,
		p_limit_kind: ObservabilityLimitKind = ObservabilityLimitKind.NONE,
) -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.new(false, p_reason, p_limit_kind)


static func is_valid_state(
		p_accepted: bool,
		p_reason: ObservabilityProcessingReason,
		p_limit_kind: ObservabilityLimitKind,
) -> bool:
	if p_accepted:
		return p_reason == ObservabilityProcessingReason.NONE \
				and p_limit_kind == ObservabilityLimitKind.NONE
	if p_reason == ObservabilityProcessingReason.SAMPLED:
		return p_limit_kind == ObservabilityLimitKind.NONE
	if p_reason != ObservabilityProcessingReason.RATE_LIMITED:
		return false
	return p_limit_kind == ObservabilityLimitKind.PER_FRAME \
			or p_limit_kind == ObservabilityLimitKind.REPEATED \
			or p_limit_kind == ObservabilityLimitKind.WINDOW \
			or p_limit_kind == ObservabilityLimitKind.LEGACY_LOG_WINDOW


func accepted() -> bool:
	return _accepted


func reason() -> ObservabilityProcessingReason:
	return _reason


func limit_kind() -> ObservabilityLimitKind:
	return _limit_kind
