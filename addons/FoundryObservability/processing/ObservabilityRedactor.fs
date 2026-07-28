namespace foundry.observability.processing

import foundry.observability

## Applies a committed provider-neutral redaction policy.
class_name ObservabilityRedactor
extends RefCounted

## Maximum nested Dictionary and Array depth examined in one payload tree.
const MAX_CONTAINER_DEPTH: int = 64
## Maximum total values examined in one public redaction call.
const MAX_VISITED_ITEMS: int = 10_000
## Maximum canonical path segments accepted by one committed rule.
const MAX_RULE_PATH_SEGMENTS: int = 256

final var _policy: ObservabilityRedactionPolicy
final var _rules: Array[ObservabilityRedactionRule]
final var _rule_paths: Array[PackedStringArray]
final var _compiled_patterns: Array[RegEx] = []
final var _invalid_rule_index: int


class RedactionSourcePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	func visit(
			_path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		return ObservabilityValueVisitDecision.keep(value)


class RedactionRulePolicy extends RefCounted:
	uses ObservabilityValuePolicy

	final var _rule: ObservabilityRedactionRule
	final var _path: PackedStringArray
	final var _compiled_pattern: RegEx
	final var _rule_index: int
	var _applied: bool = false
	var _removed: bool = false

	func _init(
			p_rule: ObservabilityRedactionRule,
			p_path: PackedStringArray,
			p_compiled_pattern: RegEx,
			p_rule_index: int,
	) -> void:
		_rule = p_rule
		_path = p_path.duplicate()
		_compiled_pattern = p_compiled_pattern
		_rule_index = p_rule_index

	func visit(
			path: PackedStringArray,
			value: Variant,
	) -> ObservabilityValueVisitDecision:
		if not ObservabilityRedactor._path_matches(_path, path):
			return _preserve(value)
		if _rule.action() == ObservabilityRedactionRule.REMOVE_FIELD:
			_removed = true
			return ObservabilityValueVisitDecision.reject()
		if _rule.action() == ObservabilityRedactionRule.REPLACE_VALUE:
			var replacement: Variant = _rule.replacement()
			if typeof(value) != typeof(replacement):
				return ObservabilityValueVisitDecision.reject()
			if value != replacement:
				_applied = true
			if replacement is Dictionary or replacement is Array:
				return ObservabilityValueVisitDecision.descend(replacement)
			return ObservabilityValueVisitDecision.keep(replacement)
		if value is String or value is StringName:
			var replacement_text: String = str(_rule.replacement())
			var redacted_text: String = replacement_text
			if not _rule.pattern().is_empty():
				redacted_text = _compiled_pattern.sub(
						str(value),
						replacement_text,
						true,
					)
			if redacted_text != str(value):
				_applied = true
				return ObservabilityValueVisitDecision.keep(redacted_text)
			return ObservabilityValueVisitDecision.keep(value)
		return _preserve(value)

	func value_rejection_is_failure(
			path: PackedStringArray,
			_value: Variant,
			parent_is_dictionary: bool,
	) -> bool:
		if _rule.action() == ObservabilityRedactionRule.REMOVE_FIELD \
				and ObservabilityRedactor._path_matches(_path, path):
			return not parent_is_dictionary
		return true

	func value_rejection_rule_index(
			_visited_path: PackedStringArray,
			_value: Variant,
			_parent_is_dictionary: bool,
	) -> int:
		return _rule_index

	func applied_rule_index() -> int:
		return _rule_index if _applied else -1

	func removed_rule_index() -> int:
		return _rule_index if _removed else -1

	func _preserve(value: Variant) -> ObservabilityValueVisitDecision:
		if value is Dictionary or value is Array:
			return ObservabilityValueVisitDecision.descend()
		return ObservabilityValueVisitDecision.keep(value)


class TraversalResult extends RefCounted:
	final var _valid: bool
	final var _value: Variant
	final var _rule_index: int
	final var _removed_rule_index: int

	func _init(
			p_valid: bool,
			p_value: Variant,
			p_rule_index: int,
			p_removed_rule_index: int,
	) -> void:
		_valid = p_valid
		_value = p_value
		_rule_index = p_rule_index
		_removed_rule_index = p_removed_rule_index

	static func success(
			p_value: Variant,
			p_rule_index: int = -1,
			p_removed_rule_index: int = -1,
	) -> TraversalResult:
		return TraversalResult.new(
				true,
				p_value,
				p_rule_index,
				p_removed_rule_index,
			)

	static func failure(p_rule_index: int = -1) -> TraversalResult:
		return TraversalResult.new(false, null, p_rule_index, -1)

	func valid() -> bool:
		return _valid

	func value() -> Variant:
		return _value

	func rule_index() -> int:
		return _rule_index

	func removed_rule_index() -> int:
		return _removed_rule_index

	func failed_rule_index() -> int:
		return maxi(_rule_index, _removed_rule_index)


func _init(policy: ObservabilityRedactionPolicy? = null) -> void:
	_policy = (
			policy.duplicate()
			if policy != null
			else ObservabilityRedactionPolicy.new()
		)
	_rules = _policy.rules()
	_rule_paths = []
	var invalid_rule_index: int = -1
	for rule_index: int in range(_rules.size()):
		var rule: ObservabilityRedactionRule = _rules[rule_index]
		var compiled: RegEx = RegEx.new()
		var path: PackedStringArray = PackedStringArray()
		if rule == null:
			if invalid_rule_index < 0:
				invalid_rule_index = rule_index
		else:
			path = rule.path()
			if (not rule.is_valid() or path.size() > MAX_RULE_PATH_SEGMENTS) \
					and invalid_rule_index < 0:
				invalid_rule_index = rule_index
			if rule.action() == ObservabilityRedactionRule.REPLACE_TEXT \
					and not rule.pattern().is_empty():
				compiled.compile(rule.pattern())
		_rule_paths.append(path)
		_compiled_patterns.append(compiled)
	_invalid_rule_index = invalid_rule_index


## Returns whether the committed policy and its compiled rules are structurally valid.
func is_valid() -> bool:
	return _invalid_rule_index < 0


func redact_event(
		event: ObservabilityEvent,
		signal_type: ObservabilitySignal,
) -> ObservabilityRedactionResult[ObservabilityEvent]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				invalid_rule_index,
			)
	if event == null \
			or (
				signal_type != ObservabilitySignal.EVENT
				and signal_type != ObservabilitySignal.LOG
			):
		return ObservabilityRedactionResult[ObservabilityEvent].failure()
	var root_name: String = (
			"event" if signal_type == ObservabilitySignal.EVENT else "log"
		)
	var redacted: TraversalResult = _redact_root(root_name, {
			"kind": String(event.kind()),
			"level": event.level(),
			"message": event.message(),
			"source": String(event.source()),
			"timestamp_msec": event.timestamp_msec(),
			"attributes": event.attributes(),
			"exception": _exception_to_dictionary(event.exception()),
			"engine_ticks_msec": event.engine_ticks_msec(),
			"scope": _scope_to_dictionary(event.scope()),
	})
	if not redacted.valid():
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(redacted),
			)
	if not (redacted.value() is Dictionary):
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(redacted),
			)
	@warning_ignore("unsafe_cast")
	var redacted_root: Dictionary = redacted.value() as Dictionary
	if not redacted_root.has(root_name) or not (redacted_root[root_name] is Dictionary):
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(redacted),
			)
	var data: Dictionary = redacted_root[root_name]
	if not _has_type(data, "kind", TYPE_STRING) \
			or not _has_type(data, "level", TYPE_INT) \
			or not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "source", TYPE_STRING) \
			or not _has_type(data, "timestamp_msec", TYPE_INT) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "engine_ticks_msec", TYPE_INT):
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(redacted),
			)
	var exception_result: TraversalResult = _exception_from_value(
			data.get("exception", null),
			redacted,
		)
	if not exception_result.valid():
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(exception_result),
			)
	var scope_result: TraversalResult = _scope_from_value(
			data.get("scope", null),
			redacted,
		)
	if not scope_result.valid():
		return ObservabilityRedactionResult[ObservabilityEvent].failure(
				_result_rule_index(scope_result),
			)
	@warning_ignore("unsafe_cast")
	var rebuilt_exception: ObservabilityException? = (
			exception_result.value() as ObservabilityException
		)
	@warning_ignore("unsafe_cast")
	var rebuilt_scope: ObservabilityScope? = (
			scope_result.value() as ObservabilityScope
		)
	var level: int = data["level"]
	var timestamp_msec: int = data["timestamp_msec"]
	var engine_ticks_msec: int = data["engine_ticks_msec"]
	@warning_ignore("unsafe_cast")
	return ObservabilityRedactionResult[ObservabilityEvent].success(
			ObservabilityEvent.new(
				StringName(str(data["kind"])),
				level,
				str(data["message"]),
				StringName(str(data["source"])),
				timestamp_msec,
				data["attributes"] as Dictionary,
				rebuilt_exception,
				engine_ticks_msec,
				rebuilt_scope,
			),
		)


func redact_metric(
		metric: ObservabilityMetric,
) -> ObservabilityRedactionResult[ObservabilityMetric]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[ObservabilityMetric].failure(
				invalid_rule_index,
			)
	if metric == null:
		return ObservabilityRedactionResult[ObservabilityMetric].failure()
	var redacted: TraversalResult = _redact_root("metric", {
		"type": metric.type(),
		"name": metric.name(),
		"value": metric.value(),
		"unit": metric.unit(),
		"attributes": metric.attributes(),
	})
	if not redacted.valid():
		return ObservabilityRedactionResult[ObservabilityMetric].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "metric")
	if not data_result.valid():
		return ObservabilityRedactionResult[ObservabilityMetric].failure(
				_result_rule_index(data_result),
			)
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	if not _has_type(data, "type", TYPE_INT) \
			or not _has_type(data, "name", TYPE_STRING) \
			or not _has_type(data, "value", TYPE_FLOAT) \
			or not _has_type(data, "unit", TYPE_STRING) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY):
		return ObservabilityRedactionResult[ObservabilityMetric].failure(
				_result_rule_index(redacted),
			)
	var metric_type: int = data["type"]
	var metric_value: float = data["value"]
	@warning_ignore("unsafe_cast")
	return ObservabilityRedactionResult[ObservabilityMetric].success(
			ObservabilityMetric.new(
				metric_type,
				str(data["name"]),
				metric_value,
				str(data["unit"]),
				data["attributes"] as Dictionary,
			),
		)


func redact_contexts(
		contexts: Dictionary,
) -> ObservabilityRedactionResult[Dictionary]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[Dictionary].failure(invalid_rule_index)
	var redacted: TraversalResult = _redact_root("contexts", contexts)
	if not redacted.valid():
		return ObservabilityRedactionResult[Dictionary].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "contexts")
	if not data_result.valid():
		return ObservabilityRedactionResult[Dictionary].failure(
				_result_rule_index(data_result),
			)
	var normalized_scope: ObservabilityScope = ObservabilityScope.new()
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	for name: Variant in data:
		if not (name is String) and not (name is StringName):
			return ObservabilityRedactionResult[Dictionary].failure(
					_result_rule_index(redacted),
				)
		@warning_ignore("unsafe_cast")
		if not (data[name] is Dictionary) \
				or not normalized_scope.set_context(
						str(name),
						data[name] as Dictionary,
					):
			return ObservabilityRedactionResult[Dictionary].failure(
					_result_rule_index(redacted),
				)
	return ObservabilityRedactionResult[Dictionary].success(normalized_scope.contexts())


func redact_user(
		user: ObservabilityUser,
) -> ObservabilityRedactionResult[ObservabilityUser]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[ObservabilityUser].failure(
				invalid_rule_index,
			)
	if user == null:
		return ObservabilityRedactionResult[ObservabilityUser].failure()
	var redacted: TraversalResult = _redact_root("user", {
		"application_user_id": user.application_user_id(),
		"display_name": user.display_name(),
		"contact_email": user.contact_email(),
	})
	if not redacted.valid():
		return ObservabilityRedactionResult[ObservabilityUser].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "user")
	if not data_result.valid():
		return ObservabilityRedactionResult[ObservabilityUser].failure(
				_result_rule_index(data_result),
			)
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	if not _has_type(data, "application_user_id", TYPE_STRING) \
			or not _has_type(data, "display_name", TYPE_STRING) \
			or not _has_type(data, "contact_email", TYPE_STRING):
		return ObservabilityRedactionResult[ObservabilityUser].failure(
				_result_rule_index(redacted),
			)
	var rebuilt: ObservabilityUser = ObservabilityUser.new(
			str(data["application_user_id"]),
			str(data["display_name"]),
			str(data["contact_email"]),
		)
	if not rebuilt.is_valid():
		return ObservabilityRedactionResult[ObservabilityUser].failure(
				_result_rule_index(redacted),
			)
	return ObservabilityRedactionResult[ObservabilityUser].success(rebuilt)


func redact_breadcrumb(
		breadcrumb: ObservabilityBreadcrumb,
) -> ObservabilityRedactionResult[ObservabilityBreadcrumb]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[ObservabilityBreadcrumb].failure(
				invalid_rule_index,
			)
	if breadcrumb == null:
		return ObservabilityRedactionResult[ObservabilityBreadcrumb].failure()
	var redacted: TraversalResult = _redact_root("breadcrumbs", {
		"message": breadcrumb.message(),
		"level": breadcrumb.level(),
		"category": String(breadcrumb.category()),
		"timestamp_msec": breadcrumb.timestamp_msec(),
		"attributes": breadcrumb.attributes(),
		"type": String(breadcrumb.type()),
	})
	if not redacted.valid():
		return ObservabilityRedactionResult[ObservabilityBreadcrumb].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "breadcrumbs")
	if not data_result.valid():
		return ObservabilityRedactionResult[ObservabilityBreadcrumb].failure(
				_result_rule_index(data_result),
			)
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	if not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "level", TYPE_INT) \
			or not _has_type(data, "category", TYPE_STRING) \
			or not _has_type(data, "timestamp_msec", TYPE_INT) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "type", TYPE_STRING):
		return ObservabilityRedactionResult[ObservabilityBreadcrumb].failure(
				_result_rule_index(redacted),
			)
	var level: int = data["level"]
	var timestamp_msec: int = data["timestamp_msec"]
	@warning_ignore("unsafe_cast")
	return ObservabilityRedactionResult[ObservabilityBreadcrumb].success(
			ObservabilityBreadcrumb.new(
				str(data["message"]),
				level,
				StringName(str(data["category"])),
				timestamp_msec,
				data["attributes"] as Dictionary,
				StringName(str(data["type"])),
			),
		)


func redact_attachment(
		attachment: ObservabilityAttachment,
) -> ObservabilityRedactionResult[ObservabilityAttachment]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[ObservabilityAttachment].failure(
				invalid_rule_index,
			)
	if attachment == null or not attachment.is_valid():
		return ObservabilityRedactionResult[ObservabilityAttachment].failure()
	var redacted: TraversalResult = _redact_root("attachments", {
		"filename": attachment.effective_filename(),
		"content_type": attachment.content_type(),
		"category": String(attachment.category()),
	})
	if not redacted.valid():
		return ObservabilityRedactionResult[ObservabilityAttachment].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "attachments")
	if not data_result.valid():
		return ObservabilityRedactionResult[ObservabilityAttachment].failure(
				_result_rule_index(data_result),
			)
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	if not _valid_attachment_metadata(data):
		return ObservabilityRedactionResult[ObservabilityAttachment].failure(
				_result_rule_index(redacted),
			)
	var rebuilt: ObservabilityAttachment = ObservabilityAttachment.new(
			attachment.path(),
			attachment.bytes(),
			str(data["filename"]),
			str(data["content_type"]),
			StringName(str(data["category"])),
		)
	if not rebuilt.is_valid():
		return ObservabilityRedactionResult[ObservabilityAttachment].failure(
				_result_rule_index(redacted),
			)
	return ObservabilityRedactionResult[ObservabilityAttachment].success(rebuilt)


func redact_attachment_payload(
		payload: Dictionary,
) -> ObservabilityRedactionResult[Dictionary]:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return ObservabilityRedactionResult[Dictionary].failure(invalid_rule_index)
	if not _valid_attachment_payload_source(payload):
		return ObservabilityRedactionResult[Dictionary].failure()
	var metadata: Dictionary = {
		"filename": payload["filename"],
		"category": payload["category"],
	}
	if payload.has("content_type"):
		metadata["content_type"] = payload["content_type"]
	var redacted: TraversalResult = _redact_root("attachments", metadata)
	if not redacted.valid():
		return ObservabilityRedactionResult[Dictionary].failure(
				_result_rule_index(redacted),
			)
	var data_result: TraversalResult = _root_dictionary(redacted, "attachments")
	if not data_result.valid():
		return ObservabilityRedactionResult[Dictionary].failure(
				_result_rule_index(data_result),
			)
	@warning_ignore("unsafe_cast")
	var data: Dictionary = data_result.value() as Dictionary
	if not _valid_attachment_payload_metadata(data):
		return ObservabilityRedactionResult[Dictionary].failure(
				_result_rule_index(redacted),
			)
	var rebuilt: Dictionary = payload.duplicate(true)
	rebuilt["filename"] = data["filename"]
	rebuilt["category"] = data["category"]
	if data.has("content_type"):
		rebuilt["content_type"] = data["content_type"]
	else:
		rebuilt.erase("content_type")
	if rebuilt.has("bytes"):
		var source_bytes: PackedByteArray = payload["bytes"]
		rebuilt["bytes"] = source_bytes.duplicate()
	return ObservabilityRedactionResult[Dictionary].success(rebuilt)


func _redact_root(root_name: String, value: Variant) -> TraversalResult:
	var source_root: Dictionary = {root_name: value}
	var walker: ObservabilityValueWalker = ObservabilityValueWalker.new(
			MAX_CONTAINER_DEPTH,
			MAX_VISITED_ITEMS,
		)
	var source_copy: ObservabilityRedactionResult[Variant] = (
			walker.walk(source_root, RedactionSourcePolicy.new())
		)
	if not source_copy.valid() or not (source_copy.value() is Dictionary):
		return TraversalResult.failure()
	var current: Variant = source_copy.value()
	var applied_rule_index: int = -1
	var removed_rule_index: int = -1
	for rule_index: int in range(_rules.size()):
		var rule_policy: RedactionRulePolicy = RedactionRulePolicy.new(
				_rules[rule_index],
				_rule_paths[rule_index],
				_compiled_patterns[rule_index],
				rule_index,
			)
		var pass_result: ObservabilityRedactionResult[Variant] = walker.walk(
				current,
				rule_policy,
			)
		if not pass_result.valid():
			return TraversalResult.failure(pass_result.failed_rule_index())
		current = pass_result.value()
		applied_rule_index = maxi(
				applied_rule_index,
				rule_policy.applied_rule_index(),
			)
		removed_rule_index = maxi(
				removed_rule_index,
				rule_policy.removed_rule_index(),
			)
	return TraversalResult.success(
			current,
			applied_rule_index,
			removed_rule_index,
		)


static func _path_matches(
		pattern: PackedStringArray,
		path: PackedStringArray,
) -> bool:
	var pattern_index: int = 0
	var path_index: int = 0
	var wildcard_index: int = -1
	var wildcard_path_index: int = -1
	while path_index < path.size():
		if pattern_index < pattern.size() and pattern[pattern_index] != "**" \
				and (
					pattern[pattern_index] == "*"
					or pattern[pattern_index].to_lower() \
							== path[path_index].to_lower()
				):
			pattern_index += 1
			path_index += 1
		elif pattern_index < pattern.size() and pattern[pattern_index] == "**":
			wildcard_index = pattern_index
			wildcard_path_index = path_index
			pattern_index += 1
		elif wildcard_index >= 0:
			wildcard_path_index += 1
			path_index = wildcard_path_index
			pattern_index = wildcard_index + 1
		else:
			return false
	while pattern_index < pattern.size() and pattern[pattern_index] == "**":
		pattern_index += 1
	return pattern_index == pattern.size()


func _exception_to_dictionary(exception: ObservabilityException?) -> Variant:
	if exception == null:
		return null
	var frame_values: Array = []
	for frame: ObservabilityStackFrame in exception.frames():
		if frame == null:
			frame_values.append(null)
			continue
		frame_values.append({
			"file": frame.file(),
			"function": frame.function(),
			"line": frame.line(),
			"language": frame.language(),
			"in_app": frame.in_app(),
			"context_line": frame.context_line(),
			"pre_context": Array(frame.pre_context()),
			"post_context": Array(frame.post_context()),
			"variables": frame.variables(),
		})
	return {
		"type_name": exception.type_name(),
		"message": exception.message(),
		"stack_trace": exception.stack_trace(),
		"attributes": exception.attributes(),
		"frames": frame_values,
	}


func _exception_from_value(
		value: Variant,
		redacted: TraversalResult,
) -> TraversalResult:
	if value == null:
		return TraversalResult.success(null)
	if not (value is Dictionary):
		return TraversalResult.failure(redacted.failed_rule_index())
	var data: Dictionary = value
	if not _has_type(data, "type_name", TYPE_STRING) \
			or not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "stack_trace", TYPE_STRING) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "frames", TYPE_ARRAY):
		return TraversalResult.failure(redacted.failed_rule_index())
	var rebuilt_frames: Array[ObservabilityStackFrame] = []
	for frame_value: Variant in data["frames"]:
		if not (frame_value is Dictionary):
			return TraversalResult.failure(redacted.failed_rule_index())
		var frame: Dictionary = frame_value
		if not _has_type(frame, "file", TYPE_STRING) \
				or not _has_type(frame, "function", TYPE_STRING) \
				or not _has_type(frame, "line", TYPE_INT) \
				or not _has_type(frame, "language", TYPE_STRING) \
				or not _has_type(frame, "in_app", TYPE_BOOL) \
				or not _has_type(frame, "context_line", TYPE_STRING) \
				or not _is_string_array(frame, "pre_context") \
				or not _is_string_array(frame, "post_context") \
				or not _has_type(frame, "variables", TYPE_DICTIONARY):
			return TraversalResult.failure(redacted.failed_rule_index())
		var line: int = frame["line"]
		var in_app: bool = frame["in_app"]
		@warning_ignore("unsafe_cast")
		rebuilt_frames.append(
				ObservabilityStackFrame.new(
					str(frame["file"]),
					str(frame["function"]),
					line,
					str(frame["language"]),
					in_app,
					str(frame["context_line"]),
					PackedStringArray(frame["pre_context"] as Array),
					PackedStringArray(frame["post_context"] as Array),
					frame["variables"] as Dictionary,
				),
			)
	@warning_ignore("unsafe_cast")
	return TraversalResult.success(
			ObservabilityException.new(
				str(data["type_name"]),
				str(data["message"]),
				str(data["stack_trace"]),
				data["attributes"] as Dictionary,
				rebuilt_frames,
			),
		)


func _scope_to_dictionary(scope: ObservabilityScope?) -> Variant:
	if scope == null:
		return null
	return {
		"tags": scope.tags(),
		"contexts": scope.contexts(),
	}


func _scope_from_value(
		value: Variant,
		redacted: TraversalResult,
) -> TraversalResult:
	if value == null:
		return TraversalResult.success(null)
	if not (value is Dictionary):
		return TraversalResult.failure(redacted.failed_rule_index())
	var data: Dictionary = value
	if not _has_type(data, "tags", TYPE_DICTIONARY) \
			or not _has_type(data, "contexts", TYPE_DICTIONARY):
		return TraversalResult.failure(redacted.failed_rule_index())
	var rebuilt: ObservabilityScope = ObservabilityScope.new()
	var tags: Dictionary = data["tags"]
	for key: Variant in tags:
		if (not (key is String) and not (key is StringName)) \
				or not (tags[key] is String) \
				or not rebuilt.set_tag(str(key), str(tags[key])):
			return TraversalResult.failure(redacted.failed_rule_index())
	var contexts: Dictionary = data["contexts"]
	for name: Variant in contexts:
		@warning_ignore("unsafe_cast")
		if (not (name is String) and not (name is StringName)) \
				or not (contexts[name] is Dictionary) \
				or not rebuilt.set_context(
						str(name),
						contexts[name] as Dictionary,
					):
			return TraversalResult.failure(redacted.failed_rule_index())
	return TraversalResult.success(rebuilt)


func _root_dictionary(
		redacted: TraversalResult,
		root_name: String,
) -> TraversalResult:
	if not (redacted.value() is Dictionary):
		return TraversalResult.failure(redacted.failed_rule_index())
	@warning_ignore("unsafe_cast")
	var root: Dictionary = redacted.value() as Dictionary
	if not root.has(root_name) or not (root[root_name] is Dictionary):
		return TraversalResult.failure(redacted.failed_rule_index())
	return TraversalResult.success(root[root_name])


func _first_invalid_rule_index() -> int:
	return _invalid_rule_index


func _has_type(data: Dictionary, key: String, expected_type: int) -> bool:
	return data.has(key) and typeof(data[key]) == expected_type


func _is_string_array(data: Dictionary, key: String) -> bool:
	if not _has_type(data, key, TYPE_ARRAY):
		return false
	for value: Variant in data[key]:
		if not (value is String):
			return false
	return true


func _valid_attachment_metadata(data: Dictionary) -> bool:
	return _has_type(data, "filename", TYPE_STRING) \
			and _has_type(data, "content_type", TYPE_STRING) \
			and _has_type(data, "category", TYPE_STRING)


func _valid_attachment_payload_source(payload: Dictionary) -> bool:
	if not _valid_attachment_payload_metadata(payload):
		return false
	var has_path: bool = payload.has("path")
	var has_bytes: bool = payload.has("bytes")
	if has_path == has_bytes:
		return false
	if has_path:
		return payload["path"] is String \
				and not str(payload["path"]).is_empty() \
				and str(payload["path"]).begins_with("/")
	return payload["bytes"] is PackedByteArray


func _valid_attachment_payload_metadata(payload: Dictionary) -> bool:
	if not _has_type(payload, "filename", TYPE_STRING) \
			or str(payload["filename"]).is_empty() \
			or not _has_type(payload, "category", TYPE_STRING):
		return false
	var category: String = payload["category"]
	if category != "event.attachment" and category != "event.view_hierarchy":
		return false
	return not payload.has("content_type") \
			or payload["content_type"] is String


func _result_rule_index(redacted: TraversalResult) -> int:
	return redacted.failed_rule_index()
