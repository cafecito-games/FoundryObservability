namespace foundry.observability

## Public contract implemented by the FoundryObservability autoload.
trait_name FoundryObservabilityApi

## Configures a provider and returns Error.OK or the provider's failure code.
abstract func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int
## Rereads project settings and initializes the supported startup provider.
abstract func initialize_from_project_settings() -> int
## Returns the stable status of the latest project-settings startup attempt.
abstract func startup_status() -> StringName
## Returns the human-readable diagnostic for the latest startup attempt.
abstract func startup_message() -> String
## Returns whether the active configuration permits capture.
abstract func is_enabled() -> bool
## Returns whether the active provider is currently available.
abstract func is_available() -> bool
## Returns the active provider's stable identifier.
abstract func provider_name() -> StringName
## Returns the most recent stored provider or capture error.
abstract func last_error() -> int
## Returns an enum-typed, payload-free Diagnostic without changing last_error.
abstract func last_processing_diagnostic() -> ObservabilityProcessingDiagnostic?
## Adds a persistent diagnostic attachment and returns its provider-local handle.
abstract func add_attachment(attachment: ObservabilityAttachment) -> String
## Removes a persistent diagnostic attachment by provider-local handle.
abstract func remove_attachment(handle: String) -> bool
## Clears all persistent diagnostic attachments.
abstract func clear_attachments() -> bool
## Returns isolated failures from the latest attachment-bearing event.
abstract func last_attachment_failures() -> Array
## Captures an event and returns a provider ID, or an empty string on no-op/failure.
abstract func capture_event(event: ObservabilityEvent) -> String
## Creates and captures a game-sourced message event.
abstract func capture_message(
		message: String,
		level: int = ObservabilityLevel.INFO,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String
## Creates and captures a game-sourced ERROR exception event.
abstract func capture_exception(
		exception: ObservabilityException,
		attributes: Dictionary = {},
		scope: ObservabilityScope? = null,
) -> String
## Creates and captures a structured log record.
abstract func capture_log(
		message: String,
		level: int = ObservabilityLevel.INFO,
		source: StringName = &"game",
		timestamp_msec: int = -1,
		attributes: Dictionary = {},
		engine_ticks_msec: int = -1,
		scope: ObservabilityScope? = null,
) -> String
## Sets a global session tag when the active provider supports scope.
abstract func set_tag(key: String, value: String) -> bool
## Removes a global session tag when the active provider supports scope.
abstract func remove_tag(key: String) -> bool
## Removes all global session tags when the active provider supports scope.
abstract func clear_tags() -> bool
## Sets a global structured context when the active provider supports scope.
abstract func set_context(name: String, value: Dictionary) -> bool
## Removes a global structured context when the active provider supports scope.
abstract func remove_context(name: String) -> bool
## Removes all global structured contexts when the active provider supports scope.
abstract func clear_contexts() -> bool
## Sets the explicit global application user when the active provider supports scope.
abstract func set_user(user: ObservabilityUser) -> bool
## Removes the explicit global application user when the active provider supports scope.
abstract func remove_user() -> bool
## Captures a normalized breadcrumb through an optional provider capability.
abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
## Clears the current breadcrumb trail through an optional provider capability.
abstract func clear_breadcrumbs() -> bool
## Captures explicit player feedback and returns a provider ID, or an empty string on no-op/failure.
abstract func capture_feedback(feedback: ObservabilityFeedback) -> String
## Captures a normalized custom metric and reports whether a provider accepted it.
abstract func capture_metric(metric: ObservabilityMetric) -> bool
## Creates and captures a counter metric.
abstract func capture_counter(metric_name: String, value: int = 1, attributes: Dictionary = {}) -> bool
## Creates and captures a gauge metric.
abstract func capture_gauge(metric_name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
## Creates and captures a distribution metric.
abstract func capture_distribution(metric_name: String, value: float, unit: String = "", attributes: Dictionary = {}) -> bool
## Flushes pending work within timeout_msec and returns an Error value.
abstract func flush(timeout_msec: int = 2000) -> int
## Flushes and shuts down the service; repeated calls are safe.
abstract func shutdown() -> void
