namespace foundry.observability

## Immutable diagnostic attachment configuration.
final class_name ObservabilityAttachmentConfig extends RefCounted

final var _max_bytes: int
final var _attach_game_log: bool
final var _attach_screenshot: bool
final var _attach_scene_tree: bool


func _init(
		p_max_bytes: int = 20 * 1024 * 1024,
		p_attach_game_log: bool = false,
		p_attach_screenshot: bool = false,
		p_attach_scene_tree: bool = false,
) -> void:
	_max_bytes = maxi(0, p_max_bytes)
	_attach_game_log = p_attach_game_log
	_attach_screenshot = p_attach_screenshot
	_attach_scene_tree = p_attach_scene_tree


func max_bytes() -> int:
	return _max_bytes


func attach_game_log() -> bool:
	return _attach_game_log


func attach_screenshot() -> bool:
	return _attach_screenshot


func attach_scene_tree() -> bool:
	return _attach_scene_tree
