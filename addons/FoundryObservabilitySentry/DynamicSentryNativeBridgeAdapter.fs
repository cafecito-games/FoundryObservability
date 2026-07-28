namespace foundry.observability.sentry

## The sole dynamic-call boundary: validates native methods and results, then
## exposes the optional extension through the typed SentryNativeBridge trait.
final class_name DynamicSentryNativeBridgeAdapter
extends RefCounted
uses SentryNativeBridge

var _native_bridge: Object? = null
var _contract_valid: bool = false
var _validity_mutex: Mutex = Mutex.new()


func _init(p_native_bridge: Object?) -> void:
	_native_bridge = p_native_bridge
	_contract_valid = p_native_bridge != null


func contract_valid() -> bool:
	_validity_mutex.lock()
	var valid: bool = _contract_valid
	_validity_mutex.unlock()
	return valid


func supports_core() -> bool:
	var bridge: Object? = _native_bridge
	if not contract_valid() or bridge == null:
		return false
	return bridge.has_method("lifecycleVersion") \
			and bridge.has_method("configure") \
			and bridge.has_method("isAvailable") \
			and bridge.has_method("capture") \
			and bridge.has_method("flush") \
			and bridge.has_method("shutdown")


func lifecycle_version() -> int:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("lifecycleVersion"):
		_invalidate()
		return -1
	var result: Variant = bridge.call("lifecycleVersion")
	if typeof(result) != TYPE_INT:
		_invalidate()
		return -1
	return result


func configure(payload: Dictionary) -> int:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("configure"):
		_invalidate()
		return Error.ERR_UNAVAILABLE
	var result: Variant = bridge.call("configure", payload)
	if typeof(result) != TYPE_INT:
		_invalidate()
		return Error.ERR_UNAVAILABLE
	return result


func is_available(owner: String) -> bool:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("isAvailable"):
		_invalidate()
		return false
	var result: Variant = bridge.call("isAvailable", owner)
	if typeof(result) != TYPE_BOOL:
		_invalidate()
		return false
	return result


func capture(payload: Dictionary) -> String:
	return _call_string("capture", payload)


func supports_logs() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("captureLog")


func capture_log(payload: Dictionary) -> String:
	return _call_string("captureLog", payload)


func supports_scope() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("applyScope")


func apply_scope(payload: Dictionary) -> bool:
	return _call_bool("applyScope", payload)


func supports_breadcrumbs() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("captureBreadcrumb") \
			and bridge.has_method("clearBreadcrumbs")


func capture_breadcrumb(payload: Dictionary) -> bool:
	return _call_bool("captureBreadcrumb", payload)


func clear_breadcrumbs() -> bool:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("clearBreadcrumbs"):
		_invalidate()
		return false
	var result: Variant = bridge.call("clearBreadcrumbs")
	if typeof(result) != TYPE_BOOL:
		_invalidate()
		return false
	return result


func supports_feedback() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("captureFeedback")


func capture_feedback(payload: Dictionary) -> String:
	return _call_string("captureFeedback", payload)


func supports_metrics() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("captureMetric")


func capture_metric(payload: Dictionary) -> bool:
	return _call_bool("captureMetric", payload)


func supports_attachments() -> bool:
	var bridge: Object? = _native_bridge
	return contract_valid() \
			and bridge != null \
			and bridge.has_method("replaceAttachments") \
			and bridge.has_method("captureWithAttachments")


func replace_attachments(payloads: Array[Dictionary]) -> bool:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("replaceAttachments"):
		_invalidate()
		return false
	var result: Variant = bridge.call("replaceAttachments", payloads)
	if typeof(result) != TYPE_BOOL:
		_invalidate()
		return false
	return result


func capture_with_attachments(payload: Dictionary) -> String:
	return _call_string("captureWithAttachments", payload)


func flush(owner: String, timeout_msec: int) -> int:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method("flush"):
		_invalidate()
		return Error.ERR_UNAVAILABLE
	var result: Variant = bridge.call("flush", owner, timeout_msec)
	if typeof(result) != TYPE_INT:
		_invalidate()
		return Error.ERR_UNAVAILABLE
	return result


func shutdown(owner: String) -> void:
	var bridge: Object? = _native_bridge
	if bridge != null and bridge.has_method("shutdown"):
		bridge.call("shutdown", owner)
	else:
		_invalidate()


func _call_bool(method: String, payload: Dictionary) -> bool:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method(method):
		_invalidate()
		return false
	var result: Variant = bridge.call(method, payload)
	if typeof(result) != TYPE_BOOL:
		_invalidate()
		return false
	return result


func _call_string(method: String, payload: Dictionary) -> String:
	var bridge: Object? = _native_bridge
	if not contract_valid() \
			or bridge == null \
			or not bridge.has_method(method):
		_invalidate()
		return ""
	var result: Variant = bridge.call(method, payload)
	if typeof(result) != TYPE_STRING:
		_invalidate()
		return ""
	return result


func _invalidate() -> void:
	_validity_mutex.lock()
	_contract_valid = false
	_validity_mutex.unlock()
