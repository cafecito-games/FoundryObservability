namespace foundry.observability

## Provider-neutral immutable diagnostic attachment value.
class_name ObservabilityAttachment
extends RefCounted

const DEFAULT_CONTENT_TYPE: String = "application/octet-stream"
const DEFAULT_CATEGORY: StringName = &"event.attachment"
const VIEW_HIERARCHY_CATEGORY: StringName = &"event.view_hierarchy"

final var _path: String
final var _bytes: PackedByteArray
final var _filename: String
final var _content_type: String
final var _category: StringName


func _init(
		p_path: String = "",
		p_bytes: PackedByteArray = PackedByteArray(),
		p_filename: String = "",
		p_content_type: String = "",
		p_category: StringName = DEFAULT_CATEGORY,
) -> void:
	_path = p_path
	_bytes = p_bytes.duplicate()
	_filename = p_filename
	_content_type = p_content_type
	_category = p_category


@warning_ignore("shadowed_variable")
static func from_path(
		path: String,
		filename: String = "",
		content_type: String = "",
		category: StringName = DEFAULT_CATEGORY,
) -> ObservabilityAttachment?:
	var attachment: ObservabilityAttachment = ObservabilityAttachment.new(
			path,
			PackedByteArray(),
			filename,
			content_type,
			category,
		)
	if not attachment.is_valid():
		return null
	return attachment


@warning_ignore("shadowed_variable")
static func from_bytes(
		bytes: PackedByteArray,
		filename: String,
		content_type: String = "",
		category: StringName = DEFAULT_CATEGORY,
) -> ObservabilityAttachment?:
	var attachment: ObservabilityAttachment = ObservabilityAttachment.new(
			"",
			bytes,
			filename,
			content_type,
			category,
		)
	if not attachment.is_valid():
		return null
	return attachment


func path() -> String:
	return _path


func bytes() -> PackedByteArray:
	return _bytes.duplicate()


func filename() -> String:
	return _filename


func effective_filename() -> String:
	if not _filename.is_empty():
		return _filename
	return _path.get_file()


func content_type() -> String:
	if _content_type.is_empty():
		return DEFAULT_CONTENT_TYPE
	return _content_type


func category() -> StringName:
	return _category


func is_path() -> bool:
	return not _path.is_empty()


func is_bytes() -> bool:
	return _path.is_empty()


func duplicate() -> ObservabilityAttachment:
	return ObservabilityAttachment.new(
			_path,
			_bytes,
			_filename,
			_content_type,
			_category,
		)


func is_valid() -> bool:
	if not _is_valid_content_type() or not _is_valid_category():
		return false
	if _path.is_empty():
		return _is_safe_nonempty(_filename)
	if not _bytes.is_empty():
		return false
	if not _is_safe_nonempty(_path) or not _is_supported_path(_path):
		return false
	if _filename.is_empty():
		return _is_safe_nonempty(_path.get_file())
	return _is_safe_nonempty(_filename)


func _is_valid_content_type() -> bool:
	return _content_type.is_empty() or _is_safe_nonempty(_content_type)


func _is_valid_category() -> bool:
	return _category == DEFAULT_CATEGORY or _category == VIEW_HIERARCHY_CATEGORY


func _is_supported_path(value: String) -> bool:
	return value.begins_with("user://") \
			or value.begins_with("res://") \
			or value.is_absolute_path()


func _is_safe_nonempty(value: String) -> bool:
	if value.is_empty():
		return false
	if value.strip_edges() != value or _has_surrounding_whitespace(value):
		return false
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return false
	return true


func _has_surrounding_whitespace(value: String) -> bool:
	return _is_whitespace(value.unicode_at(0)) \
			or _is_whitespace(value.unicode_at(value.length() - 1))


func _is_whitespace(codepoint: int) -> bool:
	return (codepoint >= 9 and codepoint <= 13) \
			or codepoint == 32 \
			or codepoint == 133 \
			or codepoint == 160 \
			or codepoint == 5760 \
			or (codepoint >= 8192 and codepoint <= 8202) \
			or codepoint == 8232 \
			or codepoint == 8233 \
			or codepoint == 8239 \
			or codepoint == 8287 \
			or codepoint == 12288
