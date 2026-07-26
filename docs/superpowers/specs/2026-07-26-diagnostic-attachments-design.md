# Diagnostic Attachments Design

## Goal

Add provider-neutral diagnostic attachments that applications and automatic
processors can keep in the active observability session and deliver with later
applicable events. Attachments may reference a file or own in-memory bytes,
preserve filename, content type, and category metadata, and support Godot
virtual paths such as `user://`.

The feature must remain safe when a provider does not support attachments and
when an individual attachment is missing, unreadable, oversized, or unavailable
on the current platform. An attachment failure must not prevent the event from
being captured.

The lifecycle follows Sentry Godot: user attachments persist across applicable
future events until explicitly removed or cleared. Successful configuration,
provider replacement, disabled configuration, and shutdown start the next
session with no user attachments. A failed reconfiguration preserves the
active attachment session.

## Chosen architecture

Attachment state is provider-owned and exposed through an optional
`ObservabilityAttachmentsProvider` capability. This matches the existing
provider-owned scope and breadcrumb model and allows a Sentry provider to mirror
user attachments into native Apple and Android SDK scope, where they can also
accompany applicable native SDK events.

`FoundryObservability` remains the provider-neutral public entry point. It
validates attachment values, checks the optional capability, records errors for
unsupported or rejected mutations, and delegates accepted operations. The
memory provider implements the capability for deterministic tests.

The alternatives were rejected:

- Core-owned attachment state would be easy to inspect but would only reach
  events that pass through `FoundryObservability.capture_event()`. Native crash,
  hang, and ANR events would bypass it.
- Storing attachments directly on each `ObservabilityEvent` would provide
  explicit one-event ownership but would not satisfy the approved persistent
  add, remove, and clear lifecycle.

Built-in game-log, screenshot, and scene-tree attachments are configuration,
not user attachment state. User remove and clear operations never disable or
remove them. The Sentry provider creates or refreshes built-in payloads at safe
capture points and the native bridge combines them with the current user
attachment snapshot.

## Provider-neutral value types

### ObservabilityAttachment

Add an immutable `ObservabilityAttachment` DTO with two named constructors:

```text
ObservabilityAttachment.from_path(
    path,
    filename = "",
    content_type = "",
    category = &"event.attachment",
)

ObservabilityAttachment.from_bytes(
    bytes,
    filename,
    content_type = "",
    category = &"event.attachment",
)
```

Exactly one source is present. A path attachment stores the original virtual or
absolute path and is read lazily for each applicable event. A byte attachment
stores a defensive copy of `PackedByteArray`; accessors return copies.
Mutating source bytes or attachment variables after `add_attachment()` cannot
alter the provider snapshot.

Path and byte attachments use these rules:

- A path and a byte attachment filename must be nonempty and contain no control
  characters. Path attachments may omit `filename`; the final path component is
  then used.
- `content_type` may be empty, meaning `application/octet-stream`, or a trimmed
  nonempty MIME-style value without control characters.
- `category` defaults to `event.attachment`; an explicitly supplied value must
  be nonempty and contain no control characters.
- Relative filesystem paths are rejected. Absolute paths, `user://`, and
  `res://` are accepted. Providers globalize `user://`; packaged `res://`
  resources are read through Godot `FileAccess` and forwarded as bytes because
  they are not guaranteed to exist as ordinary operating-system files.
- An in-memory byte payload may be empty. It still requires a filename so the
  backend receives valid attachment metadata.

The DTO exposes source classification and defensive accessors, but does not
expose provider handles or backend-specific SDK types.

### ObservabilityAttachmentFailure

Add an immutable partial-failure DTO containing:

- opaque attachment handle, empty for a built-in attachment;
- effective filename;
- stable reason;
- engine `Error` value.

Stable reasons are:

- `missing_file`;
- `unreadable_file`;
- `oversized`;
- `platform_unavailable`;
- `provider_rejected`.

This structure reports attachment-specific loss without overloading the event
ID or changing a successful event capture into a failure.

## Public API and optional capability

Extend `FoundryObservabilityApi` and `FoundryObservability` with:

```text
add_attachment(attachment: ObservabilityAttachment) -> String
remove_attachment(handle: String) -> bool
clear_attachments() -> bool
last_attachment_failures() -> Array
```

`add_attachment()` returns an opaque, provider-generated handle. An empty
handle means the attachment was not accepted. Handles belong to the active
provider session and become invalid after a successful session boundary.
Callers use a returned handle for precise removal; filenames do not have to be
unique.

`remove_attachment()` removes one accepted user attachment. Removing an unknown
or stale handle returns false with `Error.ERR_DOES_NOT_EXIST`.
`clear_attachments()` removes all user attachments and succeeds when the
current user list is already empty. Neither operation affects built-ins.

`last_attachment_failures()` returns isolated copies of failures from the most
recent applicable event capture. The list is replaced at the beginning of each
applicable capture, including a capture with no failures. Calls that do not
produce an event envelope—structured logs, metrics, feedback, flush, and scope
mutations—do not replace the list.

The optional provider contract mirrors those four methods. Providers generate
handles because they own attachment state and session lifecycle.

The service follows existing error conventions:

- invalid attachment or handle input uses `Error.ERR_INVALID_PARAMETER`;
- a missing optional capability uses `Error.ERR_UNAVAILABLE`;
- an unknown handle uses `Error.ERR_DOES_NOT_EXIST`;
- provider rejection or a malformed provider result uses `Error.FAILED`;
- successful add, remove, and clear operations store `Error.OK`;
- disabled operations are safe no-ops and do not create attachment state.

Partial delivery failures are returned only through
`last_attachment_failures()`. If the event itself is accepted,
`last_error()` remains `Error.OK`.

## Configuration and limits

Add these provider-neutral `ObservabilityConfig` fields:

```text
max_attachment_bytes = 20 * 1024 * 1024
attach_game_log = false
attach_screenshot = false
attach_scene_tree = false
```

The maximum is per attachment and includes user and built-in payloads. Negative
values normalize to zero. Zero disables attachment delivery while leaving event
capture operational. Add and remove operations may still maintain the session
snapshot while delivery is disabled, so a later successful reconfiguration
starts from a fresh session rather than reviving old attachments.

All built-ins are independently disabled by default because screenshot and
scene-tree capture can affect frame time and the game log may contain sensitive
data. Enabling one does not enable either of the others.

The Sentry provider forwards the maximum into both native SDK configurations
and also performs deterministic preflight checks for Foundry-originated events.
Backend limits may be stricter; backend rejection is reported when it is
synchronously observable.

## Scope and lifecycle

Each supporting provider owns its attachment snapshot.

1. A successful enabled configuration starts with no user attachments.
2. A successful reconfiguration resets user attachments even when the same
   provider instance remains active.
3. Successful provider replacement clears and shuts down the old provider; the
   replacement starts empty.
4. Failed reconfiguration or failed replacement preserves the previous active
   provider and attachment snapshot.
5. Disabled configuration and shutdown clear attachment state and are
   idempotent.
6. Built-ins are reconstructed from the successful active configuration and
   are not part of the user snapshot.

The memory provider stores defensive attachment snapshots by handle and
materializes them during event capture. It records the delivered attachment
payloads beside captured events so deterministic tests can inspect filenames,
content types, categories, and bytes.

The Sentry provider keeps the same ordered handle-to-attachment snapshot in
FoundryScript. Every mutation builds a candidate snapshot, asks the native
bridge to replace the complete Foundry-owned user attachment set, and commits
the candidate only after bridge success. Individual removal is therefore
supported even though the pinned Sentry SDK scopes expose add and clear rather
than remove-by-ID.

The bridge clears only Foundry-owned attachment scope state and then restores
the accepted candidate plus configured built-ins. Failed replacement restores
the previous complete snapshot or fails the provider closed when restoration is
not possible, matching the existing atomic scope-reconfiguration behavior.

## Lazy loading and event data flow

Path attachments retain a path rather than bytes. For each
Foundry-originated applicable event:

1. The provider starts a fresh partial-failure list.
2. `user://` paths are globalized for the current platform. `res://` paths are
   opened through Godot `FileAccess`.
3. Each path is checked for existence, readability, and current size. The file
   contents are not cached by the provider.
4. Missing, unreadable, or oversized attachments are omitted and recorded.
5. In-memory bytes are checked against the same limit.
6. Built-in payloads are refreshed when enabled and safe.
7. The provider asks the native bridge to capture the event with the accepted
   attachment snapshot.
8. The event ID is returned independently from attachment failures.

The native SDK remains responsible for the final lazy read when given an
absolute or globalized `user://` path. A `res://` attachment is materialized as
bytes for each Foundry-originated event and is not installed as a native-scope
file attachment, because an exported package may not expose it as an operating-
system file. If an ordinary file changes or disappears between preflight and
SDK envelope assembly, the SDK omits it according to its native behavior and
emits its SDK diagnostic. Such an asynchronous race cannot be guaranteed in
`last_attachment_failures()`.

Absolute-path, `user://`, and byte attachments mirrored into native scope may
accompany applicable native crash, hang, and ANR events. Packaged `res://`
attachments only accompany Foundry-originated events. Native events do not run
the synchronous Foundry preflight, so native SDK diagnostics are the only
failure signal for a path that becomes invalid at that time.

Attachments apply to event envelopes. They do not accompany structured log,
metric, or feedback APIs unless a backend independently associates those
records with an event.

## Built-in attachments

### Game log

When enabled, resolve Godot's configured file-logging path and register it as a
lazy `text/plain` attachment. If file logging is disabled or the path is absent,
record `missing_file` for Foundry-originated events and continue capture.
The built-in filename is the configured path's final component.

### Screenshot

When enabled, capture the current rendered game viewport as PNG immediately
before an applicable Foundry-originated event is handed to the bridge. Capture
is skipped with `platform_unavailable` when invoked off the main thread, before
the rendering lifecycle is ready, in headless mode, or when the platform cannot
read the viewport safely.

At most one screenshot is produced per rendered frame and reused for additional
events in that frame. The payload uses `screenshot.png`, `image/png`, and
`event.attachment`. A payload exceeding the configured limit is omitted.

### Scene tree

When enabled, serialize a bounded structural snapshot of the current Godot
scene tree immediately before an applicable Foundry-originated event. Capture
requires the main thread and an initialized `SceneTree`; otherwise it records
`platform_unavailable`.

The snapshot contains node name, class/type, visibility where available, and
ordered children. It does not introspect arbitrary node properties, script
fields, text contents, resource paths, or object IDs. The serializer rejects
cycles, bounds depth and node count, and emits UTF-8 JSON named
`view-hierarchy.json` with content type `application/json` and category
`event.view_hierarchy`.

Screenshot and scene-tree collection never run for a crash recovered from a
previous process. Native crashes cannot synchronously access current engine
rendering or scene objects.

## Apple and Android bridges

Apple maps user and built-in payloads to Sentry Cocoa `Attachment` objects and
uses the SDK scope's add and clear operations. It sets `maxAttachmentSize` from
the normalized provider configuration. File paths are already absolute before
they cross the Swift bridge; byte payloads become `Data`.

Android maps payloads to `io.sentry.Attachment` and replaces attachments on the
global Sentry scope. It sets the matching Android SDK attachment-size option
where available and otherwise enforces the provider-neutral preflight limit.
Byte arrays are copied across the Foundry Java bridge. The bridge never assumes
that a `user://` URI is meaningful to Java.

Both bridges:

- accept only complete, validated payload dictionaries;
- preserve filename, content type, and category;
- return explicit booleans for attachment replacement;
- keep mutation atomic from the Foundry provider's perspective;
- clear Foundry attachment state during successful session reset and shutdown;
- do not expose Sentry types in the core addon.

## Testing

FoundryScript core tests cover:

- path and byte construction, metadata defaults, validation, and defensive
  copies;
- capability dispatch and observable unsupported/rejected behavior;
- opaque handles, exact removal, unknown handles, and idempotent clearing;
- persistent delivery across events and lifecycle reset/preservation;
- virtual-path globalization and lazy file-content changes;
- missing, unreadable, empty, and oversized payloads;
- event success with partial failures and failure-list replacement;
- independent built-in toggles and preservation across user clearing;
- safe screenshot and scene-tree skips outside supported capture contexts.

Sentry provider tests cover candidate/commit attachment replacement, bridge
capability checks, failed-mutation rollback, configuration reset, payload
mapping, built-in isolation, and capture-time failure reporting.

Swift XCTest and Android JUnit cover native file and byte mappings, metadata,
complete replacement, clearing, lifecycle ownership, maximum-size
configuration, malformed payload rejection, and defensive byte copying.

Contract tests verify the new core resources and UIDs, native bridge method
names, configuration keys, package contents, and Apple/Android build metadata.
Public documentation and changelog examples show both source forms, handle
removal, persistent lifetime, independent built-ins, limits, and partial
failure inspection.

## Non-goals

This issue does not add:

- a persistent core upload queue or retry database;
- per-event caller-supplied attachment lists;
- attachment encryption or redaction of caller-supplied files;
- automatic capture of arbitrary node properties or user interface text;
- attachment delivery for logs, metrics, or feedback;
- synchronous delivery guarantees for native crashes or SDK envelope assembly.
