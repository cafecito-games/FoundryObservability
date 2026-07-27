namespace foundry.observability.sentry.tests

## Native-name proxy used only to exercise runtime extension resolution.
class_name FakeSentryNativeObject
extends RefCounted

var bridge: FakeSentryNativeBridge


func _init(p_bridge: FakeSentryNativeBridge) -> void:
	bridge = p_bridge


func lifecycleVersion() -> int:
	return bridge.lifecycle_version()


func configure(payload: Dictionary) -> int:
	return bridge.configure(payload)


func isAvailable(owner: String) -> bool:
	return bridge.is_available(owner)


func capture(payload: Dictionary) -> String:
	return bridge.capture(payload)


func captureLog(payload: Dictionary) -> String:
	return bridge.capture_log(payload)


func applyScope(payload: Dictionary) -> bool:
	return bridge.apply_scope(payload)


func captureBreadcrumb(payload: Dictionary) -> bool:
	return bridge.capture_breadcrumb(payload)


func clearBreadcrumbs() -> bool:
	return bridge.clear_breadcrumbs()


func captureFeedback(payload: Dictionary) -> String:
	return bridge.capture_feedback(payload)


func captureMetric(payload: Dictionary) -> bool:
	return bridge.capture_metric(payload)


func replaceAttachments(payloads: Array[Dictionary]) -> bool:
	return bridge.replace_attachments(payloads)


func captureWithAttachments(payload: Dictionary) -> String:
	return bridge.capture_with_attachments(payload)


func flush(owner: String, timeout_msec: int) -> int:
	return bridge.flush(owner, timeout_msec)


func shutdown(owner: String) -> void:
	bridge.shutdown(owner)
