namespace foundry.observability.sentry

## Narrow engine seam used by the built-in diagnostic attachment collector.
class_name SentryAttachmentRuntimeProbe
extends RefCounted


func is_main_thread() -> bool:
	return OS.get_thread_caller_id() == OS.get_main_thread_id()


func is_headless() -> bool:
	return DisplayServer.get_name().to_lower() == "headless"


func frames_drawn() -> int:
	return Engine.get_frames_drawn()


func main_scene_tree() -> SceneTree?:
	return Engine.get_main_loop() as SceneTree


func screenshot_png() -> PackedByteArray:
	var tree: SceneTree? = main_scene_tree()
	if tree == null or tree.root == null:
		return PackedByteArray()
	var texture: ViewportTexture = tree.root.get_texture()
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
