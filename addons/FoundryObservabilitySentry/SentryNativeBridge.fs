namespace foundry.observability.sentry

## Typed boundary for the optional platform-native Sentry implementation.
trait_name SentryNativeBridge

abstract func contract_valid() -> bool
abstract func supports_core() -> bool
abstract func lifecycle_version() -> int
abstract func configure(payload: Dictionary) -> int
abstract func is_available(owner: String) -> bool
abstract func capture(payload: Dictionary) -> String
abstract func supports_logs() -> bool
abstract func capture_log(payload: Dictionary) -> String
abstract func supports_scope() -> bool
abstract func apply_scope(payload: Dictionary) -> bool
abstract func supports_breadcrumbs() -> bool
abstract func capture_breadcrumb(payload: Dictionary) -> bool
abstract func clear_breadcrumbs() -> bool
abstract func supports_feedback() -> bool
abstract func capture_feedback(payload: Dictionary) -> String
abstract func supports_metrics() -> bool
abstract func capture_metric(payload: Dictionary) -> bool
abstract func supports_attachments() -> bool
abstract func replace_attachments(payloads: Array[Dictionary]) -> bool
abstract func capture_with_attachments(payload: Dictionary) -> String
abstract func flush(owner: String, timeout_msec: int) -> int
abstract func shutdown(owner: String) -> void
