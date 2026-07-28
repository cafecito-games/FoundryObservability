namespace foundry.observability

## Immutable Apple app-hang and Android ANR diagnostic configuration.
final class_name ObservabilityMobileDiagnosticsConfig extends RefCounted

final var _application_hang_detection_enabled: bool
final var _application_hang_timeout_msec: int
final var _android_anr_detection_enabled: bool
final var _android_anr_timeout_msec: int
final var _android_anr_attach_thread_dump: bool


func _init(
		p_application_hang_detection_enabled: bool = true,
		p_application_hang_timeout_msec: int = 5000,
		p_android_anr_detection_enabled: bool = true,
		p_android_anr_timeout_msec: int = 5000,
		p_android_anr_attach_thread_dump: bool = false,
) -> void:
	_application_hang_detection_enabled = p_application_hang_detection_enabled
	_application_hang_timeout_msec = maxi(1000, p_application_hang_timeout_msec)
	_android_anr_detection_enabled = p_android_anr_detection_enabled
	_android_anr_timeout_msec = maxi(1000, p_android_anr_timeout_msec)
	_android_anr_attach_thread_dump = p_android_anr_attach_thread_dump


func application_hang_detection_enabled() -> bool:
	return _application_hang_detection_enabled


func application_hang_timeout_msec() -> int:
	return _application_hang_timeout_msec


func android_anr_detection_enabled() -> bool:
	return _android_anr_detection_enabled


func android_anr_timeout_msec() -> int:
	return _android_anr_timeout_msec


func android_anr_attach_thread_dump() -> bool:
	return _android_anr_attach_thread_dump
