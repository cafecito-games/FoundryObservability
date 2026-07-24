namespace foundry.observability.sentry

import foundry.observability

## FoundryScript adapter for the optional iOS Sentry native bridge.
class_name SentryObservabilityProvider
extends RefCounted
uses ObservabilityProvider

const _NATIVE_CLASS: String = "SentryObservabilityBridge"

var _bridge: Object? = null
var _enabled: bool = false
var _shutdown: bool = false


## Creates a provider with an optional bridge seam used by deterministic tests.
func _init(p_bridge: Object? = null) -> void:
	_bridge = p_bridge


## Returns the stable Sentry provider identifier.
func provider_name() -> StringName:
	return &"sentry"


## Returns whether the configured native bridge can currently accept events.
func is_available() -> bool:
	if not _enabled or _shutdown:
		return false
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not bridge.has_method("isAvailable"):
		return false
	return bridge.call("isAvailable") == true


## Validates and forwards the complete provider configuration.
func configure(config: ObservabilityConfig) -> int:
	var options: Dictionary = config.provider_options()
	var dsn: String = str(options.get("dsn", ""))
	if config.enabled and dsn.is_empty():
		return Error.FAILED

	var bridge: Object? = _resolve_bridge()
	if config.enabled and bridge == null:
		return Error.FAILED

	_enabled = false
	_shutdown = false
	if bridge == null:
		return Error.OK

	var payload: Dictionary = {
			"enabled": config.enabled,
			"dsn": dsn,
			"environment": config.environment,
			"release": config.release,
			"dist": config.dist,
			"global_attributes": config.global_attributes(),
			"provider_options": options,
		}
	var result: Variant = bridge.call("configure", payload)
	if not (result is int):
		return Error.FAILED
	var result_code: int = result
	if result_code == Error.OK:
		_enabled = config.enabled
	return result_code


## Translates one normalized event and returns the native provider ID.
func capture(event: ObservabilityEvent) -> String:
	if event == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available():
		return ""

	var payload: Dictionary = {
			"kind": String(event.kind()),
			"level": event.level(),
			"message": event.message(),
			"source": String(event.source()),
			"timestamp_msec": event.timestamp_msec(),
			"attributes": event.attributes(),
		}
	var exception: ObservabilityException? = event.exception()
	if exception != null:
		payload["exception"] = {
				"type_name": exception.type_name(),
				"message": exception.message(),
				"stack_trace": exception.stack_trace(),
				"attributes": exception.attributes(),
			}
	return str(bridge.call("capture", payload))


## Flushes native Sentry work within the requested timeout.
func flush(timeout_msec: int = 2000) -> int:
	var bridge: Object? = _resolve_bridge()
	if bridge == null or not _enabled or _shutdown:
		return Error.OK
	var result: Variant = bridge.call("flush", timeout_msec)
	if not (result is int):
		return Error.FAILED
	return result


## Shuts down the native bridge once.
func shutdown() -> void:
	if _shutdown:
		return
	_shutdown = true
	_enabled = false
	var bridge: Object? = _resolve_bridge()
	if bridge != null and bridge.has_method("shutdown"):
		bridge.call("shutdown")


func _resolve_bridge() -> Object?:
	if _bridge != null:
		return _bridge
	if not ClassDB.class_exists(_NATIVE_CLASS) or not ClassDB.can_instantiate(_NATIVE_CLASS):
		return null
	_bridge = ClassDB.instantiate(_NATIVE_CLASS)
	return _bridge
