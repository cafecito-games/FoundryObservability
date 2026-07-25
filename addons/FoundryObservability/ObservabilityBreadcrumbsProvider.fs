namespace foundry.observability

## Optional provider capability for accepting normalized breadcrumbs.
trait_name ObservabilityBreadcrumbsProvider

## Captures a breadcrumb and reports whether the provider accepted it.
abstract func capture_breadcrumb(breadcrumb: ObservabilityBreadcrumb) -> bool
