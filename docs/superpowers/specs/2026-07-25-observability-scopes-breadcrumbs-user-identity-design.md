# Observability Scopes, Breadcrumbs, and User Identity Design

## Goal

Add provider-neutral session scope support for tags, named structured contexts,
explicit application user identity, and a bounded breadcrumb trail. Global
scope must affect later manual captures, automatic Godot captures, and native
Sentry events on macOS, iOS, and Android. Event-local scope must apply to one
capture without mutating or leaking into the session scope.

The lifecycle follows Sentry Godot: a successful configuration,
reconfiguration, provider replacement, or shutdown starts the next session
with an empty custom scope. A failed reconfiguration preserves the active
session scope.

## Chosen architecture

Scope is provider-owned session state exposed through an optional
`ObservabilityScopeProvider` capability. This matches the native Sentry scope
model and lets global scope reach native crash, hang, and ANR events that do
not originate in the FoundryScript event pipeline.

The alternatives were rejected:

- A core-owned scope copied only into captured events would not naturally
  enrich native SDK events and would require a second synchronization model.
- Encoding tags, contexts, and identity in generic event attributes would lose
  their backend semantics, weaken validation, and make privacy behavior
  unclear.

`FoundryObservability` remains the provider-neutral public entry point. It
validates requests, checks the optional capability, records an observable
error for unsupported or rejected operations, and delegates accepted mutations
to the active provider. `MemoryObservabilityProvider` and
`SentryObservabilityProvider` implement the capability.

## Public value types

### ObservabilityUser

Add a provider-neutral identity DTO with:

- application user ID;
- display name;
- contact email.

All fields are optional strings, but at least one must be nonempty when the
identity is set. Construction and access do not infer or enrich identity.
Constructor-owned backing fields are `final`, and accessors expose only their
values.

Only these explicitly supplied fields may be transmitted:

| Foundry field | Sentry field |
| --- | --- |
| application user ID | user ID |
| display name | username |
| contact email | email |

No IP address, device identifier, locale, timezone, or platform account is
collected as user identity.

### ObservabilityScope

Add an event-local scope value with:

- string-to-string tags;
- string-named context dictionaries.

It supports set, remove, and clear operations for both collections. Tag and
context names must be nonempty, trimmed strings without control characters.
Context values may recursively contain dictionaries with string or
`StringName` keys, arrays, null, booleans, integers, finite floats, strings,
and `StringName` values. Cycles, non-finite floats, unsupported objects, and
unsupported keys are rejected.

The DTO deep-copies input and output containers. Backing dictionary references
that never need reassignment are declared `final`; intentionally mutable state
is changed only through the scope methods. A `duplicate()` or equivalent
snapshot operation produces an isolated scope for event construction and
provider capture.

Removing or clearing an event-local value affects only that local scope. At
capture time, local tags override global tags with the same key. A local
context replaces the complete global context with the same name; nested
contexts are not deep-merged.

### Existing DTOs

`ObservabilityEvent` gains an optional event-local `ObservabilityScope`.
Convenience message, exception, and log capture methods append an optional
scope argument so existing positional calls remain compatible. The event
constructor takes a defensive scope snapshot, and its backing scope reference
is `final`.

`ObservabilityBreadcrumb` gains a `type` field while retaining `attributes` as
provider-neutral structured breadcrumb data. Existing constructor arguments
remain in their current positions and the new argument is appended. Its
constructor-owned backing fields become `final` because breadcrumbs are
read-only payload DTOs.

The implementation must use `final` for every new or touched DTO backing field
that is constructor-owned and not intended to be reassigned. Mutable service
or provider state must remain ordinary private state.

## Public service API

Add these global session operations to `FoundryObservabilityApi` and
`FoundryObservability`:

```text
set_tag(key, value) -> bool
remove_tag(key) -> bool
clear_tags() -> bool

set_context(name, value) -> bool
remove_context(name) -> bool
clear_contexts() -> bool

set_user(user) -> bool
remove_user() -> bool

clear_breadcrumbs() -> bool
```

The existing `capture_breadcrumb()` remains the operation that adds a
breadcrumb to the session trail.

The optional `ObservabilityScopeProvider` capability mirrors the tag,
context, and user methods. The existing breadcrumb capability gains
`clear_breadcrumbs()`. Providers without the relevant optional capability
remain usable for unrelated events, logs, feedback, and metrics.

An event with a nonempty event-local scope requires
`ObservabilityScopeProvider`. If the active provider lacks it, capture returns
an empty ID and stores `Error.ERR_UNAVAILABLE`, rather than silently dropping
scope data.

## Validation and errors

The core validates tag names and values, context names and nested values, user
identity, and breadcrumb input before calling a provider.

- Invalid input returns `false` and stores `Error.ERR_INVALID_PARAMETER`.
- A missing optional provider capability returns `false` and stores
  `Error.ERR_UNAVAILABLE`.
- A provider or native bridge rejection returns `false` and stores
  `Error.FAILED`.
- Disabled capture remains a safe no-op and does not create scope state,
  matching Sentry Godot calls made before initialization.
- Successful explicit scope operations store `Error.OK`.

Provider calls use the existing recursion guard. A failed global mutation must
leave the provider's prior scope snapshot intact.

## Scope lifecycle

Each supporting provider owns its active global scope.

1. A successful enabled configuration starts with empty custom tags, contexts,
   user identity, and breadcrumbs.
2. Successful reconfiguration of the active provider resets that state even
   when the provider instance is unchanged.
3. Successful provider replacement shuts down and clears the old provider;
   the new provider starts empty.
4. Failed reconfiguration or failed replacement preserves the previously
   active provider and its scope.
5. Disabled configuration and shutdown clear scope and remain idempotent.

The Sentry provider keeps an in-process scope snapshot. It constructs a
candidate copy for each mutation, asks the bridge to apply the complete
candidate scope, and commits the candidate only after bridge success. This
provides deterministic state and makes remove and clear operations testable.

## Event data flow

For ordinary and automatic Foundry events:

1. `FoundryObservability` validates the optional event-local scope.
2. The provider captures the event while its global native scope is active.
3. The provider sends the event-local scope in the bridge payload.
4. The native bridge applies event-local tags and contexts in a capture-local
   scope callback.
5. The callback ends after capture, leaving global scope unchanged.

Global scope is not copied into the event-local payload. The native SDK merges
its active global scope with the capture-local scope, so native automatic
events and Foundry-originated events use the same session state.

For `MemoryObservabilityProvider`, captured records retain deterministic
snapshots of the effective tags, contexts, and user state so tests can inspect
the same merge behavior without a native SDK.

## Native Sentry bridge

`SentryObservabilityProvider` adds the optional scope capability and forwards
complete candidate global scope payloads through a new native bridge method.
The payload contains:

```text
{
  tags: Dictionary,
  contexts: Dictionary,
  user: {
    id: String,
    display_name: String,
    contact_email: String
  }
}
```

The user member is omitted when identity is removed.

Apple uses Sentry Cocoa scope configuration. Android uses the Sentry Android
scope callback. Each bridge tracks only the tag and context keys installed by
Foundry. Applying a replacement scope removes stale Foundry-owned keys and
sets the candidate values without clearing Sentry's built-in app, device, OS,
runtime, or integration context.

For event-local scope, Apple uses the capture API's local scope closure and
Android extends its existing capture-local callback, which already installs
refreshed runtime contexts. Event-local contexts replace same-named global
custom contexts inside that callback. Both platforms use the same recursive
normalization rules and preserve nested dictionaries and arrays.

Global user mutations map to the process scope so native events receive the
explicit identity. Replacing a user replaces all three fields; removal clears
the Foundry-supplied user. Event-local user override is outside this issue.

## Breadcrumbs

Add `max_breadcrumbs` to `ObservabilityConfig`, defaulting to 100 and
normalizing negative values to zero. The value is forwarded to both native
Sentry SDK configurations and used by the memory provider.

- Zero disables breadcrumb storage.
- Accepted breadcrumbs preserve insertion order.
- When the limit is exceeded, the oldest breadcrumb is evicted first.
- Reducing the limit through successful reconfiguration starts a fresh scope
  rather than trimming the previous session.
- `clear_breadcrumbs()` removes the complete current trail.

Breadcrumb mapping preserves message, category, type, severity, timestamp,
and recursively normalized structured data. Existing automatic breadcrumbs
continue to use the same path and therefore share the configured bound.

## Unsupported capabilities

Null and third-party providers that do not implement scope or breadcrumb
capabilities remain safe:

- unrelated event capture continues to work;
- a global scope request reports `Error.ERR_UNAVAILABLE`;
- a scoped event is rejected instead of being captured without its requested
  diagnostic context;
- automatic breadcrumbs continue to skip an absent optional breadcrumb
  capability without overwriting an unrelated event error.

This preserves the existing distinction between explicit unsupported requests
and optional automatic enrichment.

## Testing strategy

### FoundryScript core

Add deterministic tests for:

- `final` DTO construction behavior and defensive container copies;
- tag and context validation, mutation, removal, and clearing;
- nested context arrays and dictionaries;
- user set, replacement, removal, and invalid empty identity;
- explicit privacy behavior with no inferred identity;
- successful reset and failed-reconfiguration preservation;
- global scope on manual and automatic events;
- event-local precedence and non-leakage;
- unsupported provider behavior and provider rejection;
- breadcrumb type, ordering, configured bounds, oldest-first eviction, zero
  capacity, and clearing;
- legacy constructor and positional capture compatibility.

The memory provider exposes only read-only snapshots needed by tests.

### Sentry FoundryScript adapter

Fake-bridge tests cover:

- configuration forwarding of `max_breadcrumbs`;
- global candidate scope application and rollback after rejection;
- session reset and failed-reconfiguration preservation;
- event-local scope payloads;
- breadcrumb type forwarding and clear behavior;
- missing native scope methods without breaking unrelated capture.

### Apple

Swift XCTest covers:

- global tag, context, and user replacement/removal;
- stale Foundry-owned key removal without deleting built-in contexts;
- event-local tag precedence and non-leakage;
- deeply nested context conversion;
- explicit user field mapping;
- breadcrumb type, data, timestamp, bounds configuration, and clearing.

### Android

JUnit covers the same behavior through the Java bridge and mapper, including:

- nested map/list conversion;
- capture-local scope application;
- global scope replacement and clear operations;
- user replacement/removal;
- configured breadcrumb limit and FIFO behavior contract.

### Repository contracts and documentation

Update resource, UID, package, API documentation, README status, and native
bridge naming contracts as required. The complete `task test` gate must pass
from the issue worktree before completion.

## Acceptance criteria

- Global scope changes affect subsequent manual, automatic Godot, and native
  Sentry events.
- Per-event tags and contexts affect one event only and do not leak.
- Tags and named contexts support set, remove, and clear behavior.
- Nested context values map consistently on Apple and Android.
- User identity can be set, replaced, and removed, with only explicit ID,
  display name, and contact email transmitted.
- Breadcrumbs preserve order, contain all required fields, and evict the
  oldest item when the configured bound is exceeded.
- Successful session transitions clear custom scope; failed transitions
  preserve it.
- Unsupported capabilities are safe and observable.
- New immutable DTO state uses `final` fields and defensive copies.

## References

- [FoundryObservability issue #10](https://github.com/cafecito-games/FoundryObservability/issues/10)
- [Sentry Godot SDK scope API](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/sentry_sdk.cpp)
- [Sentry Godot Apple backend](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/cocoa/cocoa_sdk.mm)
- [Sentry Godot Android backend](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/android/android_sdk.cpp)
