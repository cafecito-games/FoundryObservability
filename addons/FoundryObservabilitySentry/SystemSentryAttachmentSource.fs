namespace foundry.observability.sentry

## Reads engine-owned attachment data from Godot runtime services.
final class_name SystemSentryAttachmentSource
extends RefCounted
uses SentryAttachmentSource


func is_main_thread() -> bool:
	return OS.get_thread_caller_id() == OS.get_main_thread_id()


func is_headless() -> bool:
	return DisplayServer.get_name().to_lower() == "headless"


func frames_drawn() -> int:
	return Engine.get_frames_drawn()


func scene_root() -> Node?:
	var tree: SceneTree? = Engine.get_main_loop() as SceneTree
	if tree == null:
		return null
	return tree.root


func screenshot_png() -> PackedByteArray:
	var root: Node? = scene_root()
	if root == null or not (root is Viewport):
		return PackedByteArray()
	var viewport: Viewport = root
	var texture: ViewportTexture = viewport.get_texture()
	if texture == null:
		return PackedByteArray()
	var image: Image = texture.get_image()
	if image == null or image.is_empty():
		return PackedByteArray()
	return image.save_png_to_buffer()


func game_log_path() -> String:
	if ProjectSettings.get_setting(
			"debug/file_logging/enable_file_logging",
			false,
		) != true:
		return ""
	return str(ProjectSettings.get_setting(
			"debug/file_logging/log_path",
			"user://logs/godot.log",
		))
