namespace foundry.observability.sentry

import foundry.observability

## Collects optional engine-owned diagnostics without coupling them to native SDK state.
class_name SentryBuiltInAttachmentCollector
extends RefCounted

const MAX_SCENE_DEPTH: int = 32
const MAX_SCENE_NODES: int = 1024

var _source: SentryAttachmentSource
var _cached_screenshot_frame: int = -1
var _cached_screenshot: PackedByteArray = PackedByteArray()


func _init(p_source: SentryAttachmentSource) -> void:
	_source = p_source


## Returns persistent and capture-local attachment payloads plus isolated failures.
func collect(event: ObservabilityEvent?, config: ObservabilityConfig) -> SentryAttachmentCollection:
	var attachments: Array[Dictionary] = []
	var failures: Array[ObservabilityAttachmentFailure] = []
	if config.attachments().max_bytes() == 0:
		if config.attachments().attach_game_log():
			var game_log_path: String = _source.game_log_path()
			var game_log_filename: String = (
					game_log_path.get_file()
					if not game_log_path.is_empty()
					else "godot.log"
				)
			failures.append(_failure(
					"built-in:game-log",
					game_log_filename,
					ObservabilityAttachmentFailure.OVERSIZED,
					Error.FAILED,
				))
		if event != null and config.attachments().attach_screenshot():
			failures.append(_failure(
					"built-in:screenshot",
					"screenshot.png",
					ObservabilityAttachmentFailure.OVERSIZED,
					Error.FAILED,
				))
		if event != null and config.attachments().attach_scene_tree():
			failures.append(_failure(
					"built-in:scene-tree",
					"view-hierarchy.json",
					ObservabilityAttachmentFailure.OVERSIZED,
					Error.FAILED,
				))
		return SentryAttachmentCollection.new(attachments, failures)
	if config.attachments().attach_game_log():
		_collect_game_log(
				event != null,
				config.attachments().max_bytes(),
				attachments,
				failures,
			)
	if event != null and config.attachments().attach_screenshot():
		_collect_screenshot(config.attachments().max_bytes(), attachments, failures)
	if event != null and config.attachments().attach_scene_tree():
		_collect_scene_tree(config.attachments().max_bytes(), attachments, failures)
	return SentryAttachmentCollection.new(attachments, failures)


func _collect_game_log(
		validate_current_file: bool,
		max_bytes: int,
		attachments: Array[Dictionary],
		failures: Array[ObservabilityAttachmentFailure],
) -> void:
	var path: String = _source.game_log_path()
	if path.is_empty():
		failures.append(_failure(
				"built-in:game-log",
				"godot.log",
				ObservabilityAttachmentFailure.MISSING_FILE,
				Error.ERR_FILE_NOT_FOUND,
			))
		return
	var readable_path: String = path
	if readable_path.begins_with("user://"):
		readable_path = ProjectSettings.globalize_path(readable_path)
	if not validate_current_file:
		attachments.append({
			"path": readable_path,
			"filename": path.get_file(),
			"content_type": "text/plain",
			"category": String(ObservabilityAttachment.DEFAULT_CATEGORY),
			"persistent": true,
		})
		return
	if not FileAccess.file_exists(readable_path):
		failures.append(_failure(
				"built-in:game-log",
				path.get_file(),
				ObservabilityAttachmentFailure.MISSING_FILE,
				Error.ERR_FILE_NOT_FOUND,
			))
		return
	var file: FileAccess = FileAccess.open(readable_path, FileAccess.READ)
	if file == null:
		failures.append(_failure(
				"built-in:game-log",
				path.get_file(),
				ObservabilityAttachmentFailure.UNREADABLE_FILE,
				Error.ERR_FILE_CANT_OPEN,
			))
		return
	var length: int = file.get_length()
	file.close()
	if length > max_bytes:
		failures.append(_failure(
				"built-in:game-log",
				path.get_file(),
				ObservabilityAttachmentFailure.OVERSIZED,
				Error.FAILED,
			))
		return
	attachments.append({
		"path": readable_path,
		"filename": path.get_file(),
		"content_type": "text/plain",
		"category": String(ObservabilityAttachment.DEFAULT_CATEGORY),
		"persistent": true,
	})


func _collect_screenshot(
		max_bytes: int,
		attachments: Array[Dictionary],
		failures: Array[ObservabilityAttachmentFailure],
) -> void:
	if not _source.is_main_thread() or _source.is_headless():
		failures.append(_failure(
				"built-in:screenshot",
				"screenshot.png",
				ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
				Error.ERR_UNAVAILABLE,
			))
		return
	var root: Node? = _source.scene_root()
	if root == null:
		failures.append(_failure(
				"built-in:screenshot",
				"screenshot.png",
				ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
				Error.ERR_UNAVAILABLE,
			))
		return
	var frame: int = _source.frames_drawn()
	if frame != _cached_screenshot_frame:
		_cached_screenshot_frame = frame
		_cached_screenshot = _source.screenshot_png()
	if _cached_screenshot.is_empty():
		failures.append(_failure(
				"built-in:screenshot",
				"screenshot.png",
				ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
				Error.ERR_UNAVAILABLE,
			))
		return
	if _cached_screenshot.size() > max_bytes:
		failures.append(_failure(
				"built-in:screenshot",
				"screenshot.png",
				ObservabilityAttachmentFailure.OVERSIZED,
				Error.FAILED,
			))
		return
	attachments.append({
		"bytes": _cached_screenshot.duplicate(),
		"filename": "screenshot.png",
		"content_type": "image/png",
		"category": String(ObservabilityAttachment.DEFAULT_CATEGORY),
		"persistent": false,
	})


func _collect_scene_tree(
		max_bytes: int,
		attachments: Array[Dictionary],
		failures: Array[ObservabilityAttachmentFailure],
) -> void:
	if not _source.is_main_thread():
		failures.append(_failure(
				"built-in:scene-tree",
				"view-hierarchy.json",
				ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
				Error.ERR_UNAVAILABLE,
			))
		return
	var root: Node? = _source.scene_root()
	if root == null:
		failures.append(_failure(
				"built-in:scene-tree",
				"view-hierarchy.json",
				ObservabilityAttachmentFailure.PLATFORM_UNAVAILABLE,
				Error.ERR_UNAVAILABLE,
			))
		return
	var count: Array[int] = [0]
	var hierarchy: Dictionary = _scene_node(root, 0, count)
	var bytes: PackedByteArray = JSON.stringify(hierarchy).to_utf8_buffer()
	if bytes.size() > max_bytes:
		failures.append(_failure(
				"built-in:scene-tree",
				"view-hierarchy.json",
				ObservabilityAttachmentFailure.OVERSIZED,
				Error.FAILED,
			))
		return
	attachments.append({
		"bytes": bytes,
		"filename": "view-hierarchy.json",
		"content_type": "application/json",
		"category": String(ObservabilityAttachment.VIEW_HIERARCHY_CATEGORY),
		"persistent": false,
	})


func _scene_node(node: Node, depth: int, count: Array[int]) -> Dictionary:
	count[0] += 1
	var result: Dictionary = {
		"type": node.get_class(),
		"name": String(node.name),
	}
	if node is CanvasItem:
		var canvas_item: CanvasItem = node
		result["visible"] = canvas_item.is_visible_in_tree()
	elif node is Node3D:
		var spatial_node: Node3D = node
		result["visible"] = spatial_node.is_visible_in_tree()
	var children: Array = []
	if depth < MAX_SCENE_DEPTH and count[0] < MAX_SCENE_NODES:
		for child: Node in node.get_children():
			if count[0] >= MAX_SCENE_NODES:
				break
			children.append(_scene_node(child, depth + 1, count))
	if not children.is_empty():
		result["children"] = children
	else:
		result["children"] = []
	return result


func _failure(
		handle: String,
		filename: String,
		reason: StringName,
		error: int,
) -> ObservabilityAttachmentFailure:
	return ObservabilityAttachmentFailure.new(handle, filename, reason, error)
