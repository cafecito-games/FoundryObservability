namespace foundry.observability.sentry

## Provides stable and capture-time runtime snapshots for Sentry contexts.
trait_name SentryRuntimeContextSource

abstract func stable_snapshot() -> SentryRuntimeSnapshot

abstract func volatile_snapshot() -> SentryRuntimeSnapshot

abstract func privacy_snapshot() -> SentryRuntimeSnapshot.Privacy
