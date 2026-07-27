namespace foundry.observability

import foundry.observability.processing

## Bounded deterministic admission control for one observability signal.
class_name ObservabilitySignalLimiter
extends RefCounted

const MAX_IDENTITIES: int = 1024

var _sample_rate: float = 1.0
var _limits: ObservabilitySignalLimits
var _legacy_limit_per_second: int = 0
var _sample_accumulator: float = 0.0
var _sample_compensation: float = 0.0
var _has_current_frame: bool = false
var _current_frame: int = 0
var _current_frame_count: int = 0
var _accepted_timepoints: Array[int] = []
var _has_legacy_second: bool = false
var _legacy_second: int = 0
var _legacy_second_count: int = 0
var _identity_records: Dictionary = {}
var _identity_sequence: int = 0
var _has_last_now_msec: bool = false
var _last_now_msec: int = 0


## Samples before evaluating any acceptance limit.
func _init(
		p_sample_rate: float = 1.0,
		p_limits: ObservabilitySignalLimits? = null,
		p_legacy_limit_per_second: int = 0,
) -> void:
	_sample_rate = clampf(p_sample_rate, 0.0, 1.0)
	_limits = p_limits.duplicate() if p_limits != null else ObservabilitySignalLimits.new()
	_legacy_limit_per_second = maxi(0, p_legacy_limit_per_second)


## Returns a stable payload-free admission outcome.
func admit(
		identity: String,
		now_msec: int,
		frame_index: int,
) -> ObservabilityAdmissionDecision:
	var compensated_rate: float = _sample_rate - _sample_compensation
	var sampled_total: float = _sample_accumulator + compensated_rate
	_sample_compensation = (sampled_total - _sample_accumulator) - compensated_rate
	_sample_accumulator = sampled_total
	if _sample_accumulator < 1.0:
		return _dropped(
				ObservabilityProcessingReason.SAMPLED,
				ObservabilityLimitKind.NONE,
			)
	_sample_accumulator -= 1.0

	var candidate_frame_count: int = 1
	if _has_current_frame and frame_index == _current_frame:
		candidate_frame_count = _current_frame_count + 1

	var effective_now_msec: int = now_msec
	## The caller supplies a monotonic clock; retain the last accepted value on regressions.
	if _has_last_now_msec:
		effective_now_msec = maxi(effective_now_msec, _last_now_msec)
	var candidate_timepoints: Array[int] = _prune_window(
			effective_now_msec, _accepted_timepoints)
	var has_repeated_identity: bool = false
	var digest: String = ""
	if _limits.repeated_window_msec() > 0:
		digest = identity.sha256_text()
		if _identity_records.has(digest):
			var prior: Dictionary = _identity_records[digest]
			var prior_time_msec: int = prior["time_msec"]
			var age_msec: int = effective_now_msec - prior_time_msec
			has_repeated_identity = age_msec < _limits.repeated_window_msec()

	var candidate_legacy_second: int = floori(float(effective_now_msec) / 1000.0)
	var candidate_legacy_count: int = 1
	if _has_legacy_second and candidate_legacy_second == _legacy_second:
		candidate_legacy_count = _legacy_second_count + 1

	if _limits.per_frame() > 0 and candidate_frame_count > _limits.per_frame():
		return _dropped(
				ObservabilityProcessingReason.RATE_LIMITED,
				ObservabilityLimitKind.PER_FRAME,
			)
	if has_repeated_identity:
		return _dropped(
				ObservabilityProcessingReason.RATE_LIMITED,
				ObservabilityLimitKind.REPEATED,
			)
	if _limits.window_count() > 0 and _limits.window_msec() > 0 \
			and candidate_timepoints.size() >= _limits.window_count():
		return _dropped(
				ObservabilityProcessingReason.RATE_LIMITED,
				ObservabilityLimitKind.WINDOW,
			)
	if _legacy_limit_per_second > 0 and candidate_legacy_count > _legacy_limit_per_second:
		return _dropped(
				ObservabilityProcessingReason.RATE_LIMITED,
				ObservabilityLimitKind.LEGACY_LOG_WINDOW,
			)

	_has_current_frame = true
	_current_frame = frame_index
	_current_frame_count = candidate_frame_count
	if _limits.window_count() > 0 and _limits.window_msec() > 0:
		candidate_timepoints.append(effective_now_msec)
		_accepted_timepoints = candidate_timepoints
	if _limits.repeated_window_msec() > 0:
		_identity_sequence += 1
		_identity_records[digest] = {
			"time_msec": effective_now_msec,
			"sequence": _identity_sequence,
		}
		_evict_oldest_identity()
	_has_legacy_second = true
	_legacy_second = candidate_legacy_second
	_legacy_second_count = candidate_legacy_count
	_has_last_now_msec = true
	_last_now_msec = effective_now_msec
	return ObservabilityAdmissionDecision.accepted_decision()


## Clears sample, time, identity, frame, and legacy state.
func reset() -> void:
	_sample_accumulator = 0.0
	_sample_compensation = 0.0
	_has_current_frame = false
	_current_frame = 0
	_current_frame_count = 0
	_accepted_timepoints.clear()
	_has_legacy_second = false
	_legacy_second = 0
	_legacy_second_count = 0
	_identity_records.clear()
	_identity_sequence = 0
	_has_last_now_msec = false
	_last_now_msec = 0


## Keeps only timestamps whose age is below the configured sliding window.
func _prune_window(now_msec: int, timepoints: Array[int]) -> Array[int]:
	if _limits.window_count() == 0 or _limits.window_msec() == 0:
		return []
	var retained: Array[int] = []
	for timepoint_msec: int in timepoints:
		if now_msec - timepoint_msec < _limits.window_msec():
			retained.append(timepoint_msec)
	return retained


## Evicts the oldest retained digest only after the bounded capacity is exceeded.
func _evict_oldest_identity() -> void:
	if _identity_records.size() <= MAX_IDENTITIES:
		return
	var oldest_digest: String = ""
	var oldest_sequence: int = _identity_sequence
	for digest: String in _identity_records:
		var record: Dictionary = _identity_records[digest]
		var sequence: int = record["sequence"]
		if oldest_digest.is_empty() or sequence < oldest_sequence:
			oldest_digest = digest
			oldest_sequence = sequence
	_identity_records.erase(oldest_digest)


func _dropped(
		reason: ObservabilityProcessingReason,
		limit_kind: ObservabilityLimitKind,
) -> ObservabilityAdmissionDecision:
	return ObservabilityAdmissionDecision.dropped(reason, limit_kind)
