namespace foundry.observability

## Provider-neutral configuration shared by all integrations.
class_name ObservabilityConfig
extends RefCounted

## Enables provider capture after configuration when true.
var enabled: bool = true
## Identifies the deployment environment, such as production or staging.
var environment: String = ""
## Identifies the game release associated with captured events.
var release: String = ""
## Identifies an optional distribution variant of the release.
var dist: String = ""
## Enables structured log capture independently from messages and exceptions.
var logs_enabled: bool = true
## Filters structured logs below this normalized severity.
var log_minimum_level: int = ObservabilityLevel.TRACE
## Limits accepted logs per one-second monotonic engine-tick window; zero delegates to the provider.
var log_rate_limit_per_second: int = 0
## Enables custom metric capture independently from events and logs.
var metrics_enabled: bool = true
## Deterministic fraction of otherwise accepted metrics to retain.
var metric_sample_rate: float = 1.0
## Optional predicate receiving each normalized metric before sampling.
var metric_filter: Callable = Callable()
var _global_attributes: Dictionary = {}
var _provider_options: Dictionary = {}


## Creates configuration with enabled metadata, copied attributes, and opaque provider options.
func _init(
		p_enabled: bool = true,
		p_environment: String = "",
		p_release: String = "",
		p_dist: String = "",
		p_global_attributes: Dictionary = {},
		p_provider_options: Dictionary = {},
		p_logs_enabled: bool = true,
		p_log_minimum_level: int = ObservabilityLevel.TRACE,
		p_log_rate_limit_per_second: int = 0,
		p_metrics_enabled: bool = true,
		p_metric_sample_rate: float = 1.0,
		p_metric_filter: Callable = Callable(),
) -> void:
	enabled = p_enabled
	environment = p_environment
	release = p_release
	dist = p_dist
	logs_enabled = p_logs_enabled
	log_minimum_level = p_log_minimum_level
	log_rate_limit_per_second = maxi(0, p_log_rate_limit_per_second)
	metrics_enabled = p_metrics_enabled
	metric_sample_rate = p_metric_sample_rate
	metric_filter = p_metric_filter
	_global_attributes = p_global_attributes.duplicate(true)
	_provider_options = p_provider_options.duplicate(true)


## Returns a deep copy of attributes applied by provider integrations.
func global_attributes() -> Dictionary:
	return _global_attributes.duplicate(true)


## Returns a deep copy of backend-specific options not interpreted by the core.
func provider_options() -> Dictionary:
	return _provider_options.duplicate(true)
