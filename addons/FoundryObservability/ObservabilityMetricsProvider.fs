namespace foundry.observability

## Optional provider capability for accepting normalized custom metrics.
trait_name ObservabilityMetricsProvider

## Accepts a normalized metric into the provider's local SDK or store.
abstract func capture_metric(metric: ObservabilityMetric) -> bool
