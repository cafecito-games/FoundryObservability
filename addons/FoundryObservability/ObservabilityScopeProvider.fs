namespace foundry.observability

## Optional provider capability for global session scope.
trait_name ObservabilityScopeProvider

abstract func set_tag(key: String, value: String) -> bool
abstract func remove_tag(key: String) -> bool
abstract func clear_tags() -> bool
abstract func set_context(name: String, value: Dictionary) -> bool
abstract func remove_context(name: String) -> bool
abstract func clear_contexts() -> bool
abstract func set_user(user: ObservabilityUser) -> bool
abstract func remove_user() -> bool
