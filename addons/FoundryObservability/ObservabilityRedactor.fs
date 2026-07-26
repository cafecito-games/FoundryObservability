namespace foundry.observability

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


func redact_event(event: ObservabilityEvent, p_signal: StringName) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if event == null or (p_signal != &"event" and p_signal != &"log"):
		return _failure(-1)
	var root_name: String = String(p_signal)
	var redacted: Dictionary = _redact_root(root_name, {
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
	if not redacted["valid"]:
		return redacted
	if not (redacted["value"] is Dictionary):
		return _typed_failure(redacted)
	var redacted_root: Dictionary = redacted["value"]
	if not redacted_root.has(root_name) or not (redacted_root[root_name] is Dictionary):
		return _typed_failure(redacted)
	var data: Dictionary = redacted_root[root_name]
	if not _has_type(data, "kind", TYPE_STRING) \
			or not _has_type(data, "level", TYPE_INT) \
			or not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "source", TYPE_STRING) \
			or not _has_type(data, "timestamp_msec", TYPE_INT) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "engine_ticks_msec", TYPE_INT):
		return _typed_failure(redacted)
	var exception_result: Dictionary = _exception_from_value(
			data.get("exception", null),
			redacted,
		)
	if not exception_result["valid"]:
		return exception_result
	var scope_result: Dictionary = _scope_from_value(
			data.get("scope", null),
			redacted,
		)
	if not scope_result["valid"]:
		return scope_result
	@warning_ignore("unsafe_cast")
	var rebuilt_exception: ObservabilityException? = (
			exception_result["value"] as ObservabilityException
		)
	@warning_ignore("unsafe_cast")
	var rebuilt_scope: ObservabilityScope? = (
			scope_result["value"] as ObservabilityScope
		)
	@warning_ignore("unsafe_call_argument", "unsafe_cast")
	return _success(ObservabilityEvent.new(
			StringName(str(data["kind"])),
			int(data["level"]),
			str(data["message"]),
			StringName(str(data["source"])),
			int(data["timestamp_msec"]),
			data["attributes"] as Dictionary,
			rebuilt_exception,
			int(data["engine_ticks_msec"]),
			rebuilt_scope,
		))


func redact_metric(metric: ObservabilityMetric) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if metric == null:
		return _failure(-1)
	var redacted: Dictionary = _redact_root("metric", {
		"type": metric.type(),
		"name": metric.name(),
		"value": metric.value(),
		"unit": metric.unit(),
		"attributes": metric.attributes(),
	})
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "metric")
	if not data_result["valid"]:
		return data_result
	var data: Dictionary = data_result["value"]
	if not _has_type(data, "type", TYPE_INT) \
			or not _has_type(data, "name", TYPE_STRING) \
			or not _has_type(data, "value", TYPE_FLOAT) \
			or not _has_type(data, "unit", TYPE_STRING) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY):
		return _typed_failure(redacted)
	@warning_ignore("unsafe_call_argument", "unsafe_cast")
	return _success(ObservabilityMetric.new(
			int(data["type"]),
			str(data["name"]),
			float(data["value"]),
			str(data["unit"]),
			data["attributes"] as Dictionary,
		))


func redact_contexts(contexts: Dictionary) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	var redacted: Dictionary = _redact_root("contexts", contexts)
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "contexts")
	if not data_result["valid"]:
		return data_result
	var normalized_scope: ObservabilityScope = ObservabilityScope.new()
	var data: Dictionary = data_result["value"]
	for name: Variant in data:
		if not (name is String) and not (name is StringName):
			return _typed_failure(redacted)
		@warning_ignore("unsafe_call_argument", "unsafe_cast")
		if not (data[name] is Dictionary) \
				or not normalized_scope.set_context(
						str(name),
						data[name] as Dictionary,
					):
			return _typed_failure(redacted)
	return _success(normalized_scope.contexts())


func redact_user(user: ObservabilityUser) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if user == null:
		return _failure(-1)
	var redacted: Dictionary = _redact_root("user", {
		"application_user_id": user.application_user_id(),
		"display_name": user.display_name(),
		"contact_email": user.contact_email(),
	})
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "user")
	if not data_result["valid"]:
		return data_result
	var data: Dictionary = data_result["value"]
	if not _has_type(data, "application_user_id", TYPE_STRING) \
			or not _has_type(data, "display_name", TYPE_STRING) \
			or not _has_type(data, "contact_email", TYPE_STRING):
		return _typed_failure(redacted)
	var rebuilt: ObservabilityUser = ObservabilityUser.new(
			str(data["application_user_id"]),
			str(data["display_name"]),
			str(data["contact_email"]),
		)
	if not rebuilt.is_valid():
		return _typed_failure(redacted)
	return _success(rebuilt)


func redact_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if breadcrumb == null:
		return _failure(-1)
	var redacted: Dictionary = _redact_root("breadcrumbs", {
		"message": breadcrumb.message(),
		"level": breadcrumb.level(),
		"category": String(breadcrumb.category()),
		"timestamp_msec": breadcrumb.timestamp_msec(),
		"attributes": breadcrumb.attributes(),
		"type": String(breadcrumb.type()),
	})
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "breadcrumbs")
	if not data_result["valid"]:
		return data_result
	var data: Dictionary = data_result["value"]
	if not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "level", TYPE_INT) \
			or not _has_type(data, "category", TYPE_STRING) \
			or not _has_type(data, "timestamp_msec", TYPE_INT) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "type", TYPE_STRING):
		return _typed_failure(redacted)
	@warning_ignore("unsafe_call_argument", "unsafe_cast")
	return _success(ObservabilityBreadcrumb.new(
			str(data["message"]),
			int(data["level"]),
			StringName(str(data["category"])),
			int(data["timestamp_msec"]),
			data["attributes"] as Dictionary,
			StringName(str(data["type"])),
		))


func redact_attachment(attachment: ObservabilityAttachment) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if attachment == null or not attachment.is_valid():
		return _failure(-1)
	var redacted: Dictionary = _redact_root("attachments", {
		"filename": attachment.effective_filename(),
		"content_type": attachment.content_type(),
		"category": String(attachment.category()),
	})
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "attachments")
	if not data_result["valid"]:
		return data_result
	var data: Dictionary = data_result["value"]
	if not _valid_attachment_metadata(data):
		return _typed_failure(redacted)
	var rebuilt: ObservabilityAttachment = ObservabilityAttachment.new(
			attachment.path(),
			attachment.bytes(),
			str(data["filename"]),
			str(data["content_type"]),
			StringName(str(data["category"])),
		)
	if not rebuilt.is_valid():
		return _typed_failure(redacted)
	return _success(rebuilt)


func redact_attachment_payload(payload: Dictionary) -> Dictionary:
	var invalid_rule_index: int = _first_invalid_rule_index()
	if invalid_rule_index >= 0:
		return _failure(invalid_rule_index)
	if not _valid_attachment_payload_source(payload):
		return _failure(-1)
	var metadata: Dictionary = {
		"filename": payload["filename"],
		"category": payload["category"],
	}
	if payload.has("content_type"):
		metadata["content_type"] = payload["content_type"]
	var redacted: Dictionary = _redact_root("attachments", metadata)
	if not redacted["valid"]:
		return redacted
	var data_result: Dictionary = _root_dictionary(redacted, "attachments")
	if not data_result["valid"]:
		return data_result
	var data: Dictionary = data_result["value"]
	if not _valid_attachment_payload_metadata(data):
		return _typed_failure(redacted)
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
	return _success(rebuilt)


func _redact_root(root_name: String, value: Variant) -> Dictionary:
	var source_root: Dictionary = {root_name: value}
	var validation: Dictionary = {
		"remaining": MAX_VISITED_ITEMS,
		"active_containers": [],
	}
	if not _validate_source_tree(source_root, validation, 0):
		return _failure(-1)
	var current: Variant = source_root
	var applied_rule_index: int = -1
	var removed_rule_index: int = -1
	var pass_count: int = maxi(1, _rules.size())
	for pass_index: int in range(pass_count):
		var rule_index: int = -1 if _rules.is_empty() else pass_index
		var traversal: Dictionary = {
			"remaining": MAX_VISITED_ITEMS,
			"active_containers": [],
		}
		var pass_result: Dictionary = _redact_value(
				current,
				PackedStringArray(),
				false,
				traversal,
				0,
				rule_index,
			)
		if not pass_result["valid"]:
			return pass_result
		current = pass_result["value"]
		@warning_ignore("unsafe_call_argument")
		applied_rule_index = maxi(
				applied_rule_index,
				int(pass_result.get("rule_index", -1)),
			)
		@warning_ignore("unsafe_call_argument")
		removed_rule_index = maxi(
				removed_rule_index,
				int(pass_result.get("removed_rule_index", -1)),
			)
	return _traversal_success(
			current,
			applied_rule_index,
			removed_rule_index,
		)


func _validate_source_tree(
		value: Variant,
		traversal: Dictionary,
		container_depth: int,
) -> bool:
	if not _consume_traversal_item(traversal):
		return false
	if not (value is Dictionary) and not (value is Array):
		return true
	if container_depth > MAX_CONTAINER_DEPTH:
		return false
	@warning_ignore("unsafe_cast")
	var active_containers: Array = traversal["active_containers"] as Array
	if not _enter_active_container(value, active_containers):
		return false
	if value is Dictionary:
		for key: Variant in value:
			if not _validate_source_tree(
					value[key],
					traversal,
					container_depth + 1,
				):
				active_containers.pop_back()
				return false
	else:
		for child: Variant in value:
			if not _validate_source_tree(
					child,
					traversal,
					container_depth + 1,
				):
				active_containers.pop_back()
				return false
	active_containers.pop_back()
	return true


func _redact_value(
		source_value: Variant,
		path: PackedStringArray,
		parent_is_dictionary: bool,
		traversal: Dictionary,
		container_depth: int,
		rule_index: int,
) -> Dictionary:
	if not _consume_traversal_item(traversal):
		return _failure(-1)
	@warning_ignore("unsafe_cast")
	var active_containers: Array = traversal["active_containers"] as Array
	var value: Variant = source_value
	var applied_rule_index: int = -1
	if rule_index >= 0:
		var rule: ObservabilityRedactionRule = _rules[rule_index]
		if rule == null:
			return _failure(rule_index)
		if _path_matches(_rule_paths[rule_index], path):
			if rule.action() == ObservabilityRedactionRule.REMOVE_FIELD:
				if not parent_is_dictionary:
					return _failure(rule_index)
				return {
					"valid": true,
					"removed": true,
					"rule_index": rule_index,
				}
			if rule.action() == ObservabilityRedactionRule.REPLACE_VALUE:
				var replacement: Variant = rule.replacement()
				if not _runtime_types_are_compatible(value, replacement):
					return _failure(rule_index)
				if value != replacement:
					value = replacement
					applied_rule_index = rule_index
			elif value is String or value is StringName:
				var replacement_text: String = str(rule.replacement())
				var redacted_text: String
				if rule.pattern().is_empty():
					redacted_text = replacement_text
				else:
					redacted_text = _compiled_patterns[rule_index].sub(
							str(value),
							replacement_text,
							true,
						)
				if redacted_text != str(value):
					value = redacted_text
					applied_rule_index = rule_index

	if value is Dictionary:
		if container_depth > MAX_CONTAINER_DEPTH \
				or not _enter_active_container(
						value,
						active_containers,
					):
			return _failure(-1)
		var rebuilt_dictionary: Dictionary = {}
		var removed_rule_index: int = -1
		for key: Variant in value:
			var child_path: PackedStringArray = path.duplicate()
			child_path.append(str(key))
			var child_result: Dictionary = _redact_value(
					value[key],
					child_path,
					true,
					traversal,
					container_depth + 1,
					rule_index,
				)
			if not child_result["valid"]:
				active_containers.pop_back()
				return child_result
			if child_result.get("removed", false) == true:
				@warning_ignore("unsafe_call_argument")
				removed_rule_index = maxi(
						removed_rule_index,
						int(child_result.get("rule_index", -1)),
					)
				continue
			rebuilt_dictionary[_copy_dictionary_key(key)] = child_result["value"]
			if child_result.get("rule_index", -1) >= 0:
				@warning_ignore("unsafe_call_argument")
				applied_rule_index = maxi(
						applied_rule_index,
						int(child_result["rule_index"]),
					)
			if child_result.get("removed_rule_index", -1) >= 0:
				@warning_ignore("unsafe_call_argument")
				removed_rule_index = maxi(
						removed_rule_index,
						int(child_result.get("removed_rule_index", -1)),
					)
		active_containers.pop_back()
		return _traversal_success(
				rebuilt_dictionary,
				applied_rule_index,
				removed_rule_index,
			)
	if value is Array:
		if container_depth > MAX_CONTAINER_DEPTH \
				or not _enter_active_container(
						value,
						active_containers,
					):
			return _failure(-1)
		var rebuilt_array: Array = []
		var removed_rule_index: int = -1
		for index: int in range(value.size()):
			var child_path: PackedStringArray = path.duplicate()
			child_path.append(str(index))
			var child_result: Dictionary = _redact_value(
					value[index],
					child_path,
					false,
					traversal,
					container_depth + 1,
					rule_index,
				)
			if not child_result["valid"]:
				active_containers.pop_back()
				return child_result
			if child_result.get("removed", false) == true:
				active_containers.pop_back()
				@warning_ignore("unsafe_call_argument")
				return _failure(int(child_result.get("rule_index", -1)))
			rebuilt_array.append(child_result["value"])
			if child_result.get("rule_index", -1) >= 0:
				@warning_ignore("unsafe_call_argument")
				applied_rule_index = maxi(
						applied_rule_index,
						int(child_result["rule_index"]),
					)
			if child_result.get("removed_rule_index", -1) >= 0:
				@warning_ignore("unsafe_call_argument")
				removed_rule_index = maxi(
						removed_rule_index,
						int(child_result.get("removed_rule_index", -1)),
					)
		active_containers.pop_back()
		return _traversal_success(
				rebuilt_array,
				applied_rule_index,
				removed_rule_index,
			)
	return _success_with_rule(_copy_leaf(value), applied_rule_index)


func _path_matches(
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


func _exception_from_value(value: Variant, redacted: Dictionary) -> Dictionary:
	if value == null:
		return _success(null)
	if not (value is Dictionary):
		return _typed_failure(redacted)
	var data: Dictionary = value
	if not _has_type(data, "type_name", TYPE_STRING) \
			or not _has_type(data, "message", TYPE_STRING) \
			or not _has_type(data, "stack_trace", TYPE_STRING) \
			or not _has_type(data, "attributes", TYPE_DICTIONARY) \
			or not _has_type(data, "frames", TYPE_ARRAY):
		return _typed_failure(redacted)
	var rebuilt_frames: Array[ObservabilityStackFrame] = []
	for frame_value: Variant in data["frames"]:
		if not (frame_value is Dictionary):
			return _typed_failure(redacted)
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
			return _typed_failure(redacted)
		@warning_ignore("unsafe_call_argument", "unsafe_cast")
		rebuilt_frames.append(ObservabilityStackFrame.new(
				str(frame["file"]),
				str(frame["function"]),
				int(frame["line"]),
				str(frame["language"]),
				bool(frame["in_app"]),
				str(frame["context_line"]),
				PackedStringArray(frame["pre_context"] as Array),
				PackedStringArray(frame["post_context"] as Array),
				frame["variables"] as Dictionary,
			))
	@warning_ignore("unsafe_call_argument", "unsafe_cast")
	return _success(ObservabilityException.new(
			str(data["type_name"]),
			str(data["message"]),
			str(data["stack_trace"]),
			data["attributes"] as Dictionary,
			rebuilt_frames,
		))


func _scope_to_dictionary(scope: ObservabilityScope?) -> Variant:
	if scope == null:
		return null
	return {
		"tags": scope.tags(),
		"contexts": scope.contexts(),
	}


func _scope_from_value(value: Variant, redacted: Dictionary) -> Dictionary:
	if value == null:
		return _success(null)
	if not (value is Dictionary):
		return _typed_failure(redacted)
	var data: Dictionary = value
	if not _has_type(data, "tags", TYPE_DICTIONARY) \
			or not _has_type(data, "contexts", TYPE_DICTIONARY):
		return _typed_failure(redacted)
	var rebuilt: ObservabilityScope = ObservabilityScope.new()
	var tags: Dictionary = data["tags"]
	for key: Variant in tags:
		@warning_ignore("unsafe_call_argument")
		if (not (key is String) and not (key is StringName)) \
				or not (tags[key] is String) \
				or not rebuilt.set_tag(str(key), str(tags[key])):
			return _typed_failure(redacted)
	var contexts: Dictionary = data["contexts"]
	for name: Variant in contexts:
		@warning_ignore("unsafe_call_argument", "unsafe_cast")
		if (not (name is String) and not (name is StringName)) \
				or not (contexts[name] is Dictionary) \
				or not rebuilt.set_context(
						str(name),
						contexts[name] as Dictionary,
					):
			return _typed_failure(redacted)
	return _success(rebuilt)


func _root_dictionary(redacted: Dictionary, root_name: String) -> Dictionary:
	if not (redacted["value"] is Dictionary):
		return _typed_failure(redacted)
	var root: Dictionary = redacted["value"]
	if not root.has(root_name) or not (root[root_name] is Dictionary):
		return _typed_failure(redacted)
	return _success(root[root_name])


func _first_invalid_rule_index() -> int:
	return _invalid_rule_index


func _runtime_types_are_compatible(current: Variant, replacement: Variant) -> bool:
	return typeof(current) == typeof(replacement)


func _consume_traversal_item(traversal: Dictionary) -> bool:
	var remaining: int = traversal["remaining"]
	if remaining <= 0:
		return false
	traversal["remaining"] = remaining - 1
	return true


func _enter_active_container(container: Variant, active_containers: Array) -> bool:
	for active_container: Variant in active_containers:
		if is_same(container, active_container):
			return false
	active_containers.append(container)
	return true


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


func _copy_dictionary_key(key: Variant) -> Variant:
	if key is PackedByteArray:
		return key.duplicate()
	if key is PackedInt32Array:
		return key.duplicate()
	if key is PackedInt64Array:
		return key.duplicate()
	if key is PackedFloat32Array:
		return key.duplicate()
	if key is PackedFloat64Array:
		return key.duplicate()
	if key is PackedStringArray:
		return key.duplicate()
	if key is PackedVector2Array:
		return key.duplicate()
	if key is PackedVector3Array:
		return key.duplicate()
	if key is PackedVector4Array:
		return key.duplicate()
	if key is PackedColorArray:
		return key.duplicate()
	return key


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


func _typed_failure(redacted: Dictionary) -> Dictionary:
	@warning_ignore("unsafe_call_argument")
	var applied_rule_index: int = int(redacted.get("rule_index", -1))
	@warning_ignore("unsafe_call_argument")
	var removed_rule_index: int = int(redacted.get("removed_rule_index", -1))
	return _failure(maxi(applied_rule_index, removed_rule_index))


func _failure(rule_index: int) -> Dictionary:
	return {
		"valid": false,
		"rule_index": rule_index,
	}


func _success(value: Variant) -> Dictionary:
	return {
		"valid": true,
		"value": value,
	}


func _success_with_rule(value: Variant, rule_index: int) -> Dictionary:
	var result: Dictionary = _success(value)
	if rule_index >= 0:
		result["rule_index"] = rule_index
	return result


func _traversal_success(
		value: Variant,
		rule_index: int,
		removed_rule_index: int,
) -> Dictionary:
	var result: Dictionary = _success_with_rule(value, rule_index)
	if removed_rule_index >= 0:
		result["removed_rule_index"] = removed_rule_index
	return result
