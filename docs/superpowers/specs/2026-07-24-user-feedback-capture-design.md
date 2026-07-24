# User Feedback Capture Design

## Goal

Add a provider-neutral way for games to submit player feedback, optionally
linking it to an event ID returned by the observability service. Feedback must
remain distinct from error events and structured logs, support anonymous use,
fail safely when invalid or unsupported, and leave offline delivery to the
configured provider.

The value shape follows the established Sentry Godot feedback model:
[SentryFeedback](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/sentry_feedback.h)
contains a required message plus optional name, contact email, and associated
event ID.

## Public API

Add `ObservabilityFeedback` in the `foundry.observability` namespace. It is a
`RefCounted` value with defensive storage and accessors for:

- `message: String`
- `name: String`
- `contact_email: String`
- `associated_event_id: String`

The constructor uses the repository's existing `p_` parameter convention.
Optional strings default to empty. The event ID remains opaque so providers
other than Sentry are not forced to use UUIDs.

Extend `FoundryObservabilityApi` with:

```text
abstract func capture_feedback(feedback: ObservabilityFeedback) -> String
```

Extend `ObservabilityProvider` with the same method. A non-empty result means
the provider accepted or queued the feedback; an empty result means no
feedback was accepted. `flush()` remains the explicit best-effort delivery
operation, and `shutdown()` retains its existing flush-and-release behavior.

## Core behavior

`FoundryObservability.capture_feedback()` validates before calling the
provider:

- The message must contain non-whitespace content and be no longer than 4096
  Unicode characters.
- Optional values may be empty, but non-empty values must not contain control
  characters.
- A non-empty contact email must have one `@`, non-empty local and domain
  portions, no whitespace, and no control characters.
- Associated event IDs are not format-validated beyond safe string input.

The original accepted values are forwarded without trimming. Invalid input
returns an empty ID and sets `last_error()` to `Error.ERR_INVALID_PARAMETER`.
Disabled services remain safe no-ops. Provider rejection or unavailability
returns an empty ID and sets `last_error()` to `Error.FAILED`, matching the
existing capture contract. Feedback never consumes structured-log rate-limit
state and never enters `capture_event()`.

`NullObservabilityProvider` accepts the contract as a no-op. The memory
provider stores accepted feedback in a separate list and returns deterministic
IDs, allowing tests to verify feedback/event separation and field preservation.

## Sentry provider and native bridges

The FoundryScript Sentry provider requires an enabled native bridge to expose
`captureFeedback`, in addition to the existing structured-log capability
checks. It sends a dedicated payload containing `message` and only the
explicitly supplied optional values.

The Apple bridge constructs `SentryFeedback` with custom source and calls
`SentrySDK.capture(feedback:)`. The Android bridge constructs
`io.sentry.protocol.Feedback`, sets optional fields only when present, parses an
associated event ID when provided, and calls `Sentry.feedback().capture()`.
Both bridges return a generated `sentry-feedback:` ID with a UUID suffix because
the pinned native feedback APIs enqueue asynchronously without returning an
event ID. Invalid bridge payloads and unavailable SDKs return an empty string.

Native configuration forwards an explicit `send_default_pii` provider option,
defaulting to false. Missing name and email fields are omitted and the bridge
does not read or synthesize user identity. Callers who explicitly opt into
provider PII behavior do so through provider configuration.

## Tests and documentation

Add deterministic FoundryScript tests for model accessors, valid anonymous and
identified feedback, event association, maximum/empty/malformed input,
privacy omission, provider failure, unavailable providers, and feedback/event
separation. Extend Sentry test doubles and tests for bridge capability checks,
payload mapping, and acceptance IDs. Extend Android native tests for feedback
capture, flush, and idempotent shutdown; retain the existing Apple build and
mapper contracts.

Update `docs/API.md`, `README.md`, `CHANGELOG.md`, package/FoundryScript
contract checks, and the new source's UID companion. Document that feedback is
anonymous unless the caller supplies contact fields, that provider SDKs own
offline queues and retries, and that `flush()` is best effort.

## Non-goals

This change does not add a feedback UI, core persistence, core retry queues,
attachments, automatic user identity collection, or a provider-specific API to
the public Foundry observability namespace.
