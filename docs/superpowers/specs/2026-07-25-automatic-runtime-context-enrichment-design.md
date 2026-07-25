# Automatic Runtime Context Enrichment Design

## Summary

FoundryObservability will automatically enrich Sentry events with stable
application, Godot engine, device, display, graphics, and runtime context on
macOS, iOS, and Android. Stable values will be collected when an enabled
Sentry provider is configured and attached to the native Sentry scope so they
are available to ordinary events and native crash reports. Volatile values
will be refreshed immediately before ordinary event capture.

The implementation will follow Sentry Godot's split between initialization
context and capture-time context while preserving the existing
provider-neutral Foundry Observability API. Collection will happen in
FoundryScript, where the same Godot APIs and field semantics are available on
all target platforms. The Swift and Java bridges will only sanitize and attach
the resulting dictionaries to native Sentry scopes.

Reference:
[Sentry Godot context collection](https://github.com/getsentry/sentry-godot/blob/main/src/sentry/contexts.cpp).

## Goals

- Include stable application name, application version, process start time,
  engine version, engine build identity, and architecture on every supported
  target.
- Include supported device model/type, processor, memory, storage, display,
  GPU, renderer, and runtime-mode information.
- Refresh free memory, usable memory, free storage, and display orientation
  close to ordinary event capture.
- Avoid Godot memory-information APIs on iOS.
- Omit unavailable, empty, generic, or invalid values rather than publishing
  misleading placeholders.
- Keep personally identifying or stable identifying values disabled by
  default and controlled by the existing Sentry `send_default_pii` provider
  option.
- Keep collection or conversion failures from rejecting an otherwise valid
  event.
- Preserve explicit environment, release, distribution, global attributes,
  and event attributes without collisions from automatic context.
- Add deterministic FoundryScript, Swift, and Java tests and update public
  documentation.

## Non-goals

- Add a provider-neutral context mutation API.
- Add context dictionaries to `ObservabilityEvent` or `ObservabilityConfig`.
- Replace the app, device, or OS context already owned by the native Sentry
  Cocoa and Android SDKs.
- Collect command-line arguments, IP addresses, granted permissions, user
  paths, hostnames, or environment variables.
- Refresh volatile Godot context while handling a native crash. Cocoa and
  Android crash events continue to use the stable scope context and the
  platform SDK's crash-safe device and OS collection.
- Add performance tracing, frame profiling, or continuous telemetry.
- Add platform support beyond macOS, iOS, and Android.

## Public API boundary

The provider-neutral API remains unchanged. Games continue to configure
`SentryObservabilityProvider` through `ObservabilityConfig` and capture the
same immutable `ObservabilityEvent` values.

The Sentry provider uses two private payload members:

```text
configuration payload:
{
  ...
  stable_contexts: Dictionary
}

event payload:
{
  ...
  contexts: Dictionary
}
```

`stable_contexts` becomes native scope context after successful Sentry
initialization. `contexts` contains a complete per-event snapshot formed by
merging the cached stable context with a fresh volatile update. These members
are an internal bridge contract and are not exposed through
`FoundryObservabilityApi`.

Automatic context is never inserted into `global_attributes`,
`event.attributes`, exception attributes, or the explicit top-level release,
environment, and distribution fields. Existing attribute precedence is
unchanged. Custom context keys also avoid the native SDK-owned `app`, `device`,
and `os` names, so native default context is not replaced.

## Components

### Runtime context probe

Add a private FoundryScript probe in the Sentry addon that wraps the Godot
singletons used for collection:

- `ProjectSettings`
- `Engine`
- `OS`
- `Time`
- `DisplayServer`
- `RenderingServer`
- `DirAccess`

The probe exposes small, typed snapshot methods rather than leaking singleton
objects to the collector. `SentryObservabilityProvider` accepts an optional
injected probe after its existing bridge argument. Production uses the real
probe; deterministic tests use a fake probe that returns fixed values and
records unsafe calls.

### Runtime context collector

Add a private FoundryScript collector with two responsibilities:

```text
stable_contexts(environment: String, send_default_pii: bool) -> Dictionary
volatile_contexts() -> Dictionary
```

The collector normalizes field names, classifies runtime mode, filters
unsupported values, and returns deep-copied dictionaries. It holds no native
Sentry dependency.

The provider caches the stable result only after a successful enabled
configuration. A failed replacement leaves the current provider session and
its cached stable context unchanged. Disabled configuration and shutdown clear
the cached snapshot.

Before ordinary message or exception capture, the provider deep-merges a fresh
volatile result over the cached stable result and sends the complete snapshot
as `contexts`. Breadcrumb, feedback, metric, and structured-log APIs retain
their existing payloads; they still inherit stable scope context from the
native SDK.

### Native context conversion

The Swift and Java bridges add context conversion helpers beside their existing
event mappers. The helpers accept dictionaries containing strings, booleans,
finite numbers, arrays, and nested dictionaries. Unsupported values and empty
context dictionaries are omitted.

During native startup, each lifecycle driver installs `stable_contexts` on the
global Sentry scope after successful SDK initialization. During ordinary event
capture, each bridge applies the complete event `contexts` snapshot to the
SDK's capture-local scope callback. Cocoa and Android enrich an event from
scope after mapping the raw event, so attaching the snapshot directly to the
event would allow stale global scope values to overwrite refreshed volatile
values. The capture-local scope keeps the global crash scope stable, preserves
fresh per-event values, and does not leak one capture's snapshot into another.
Context attachment must not change the current `extra`/attribute merge order.

Stable contexts are part of native lifecycle configuration equality. An
equivalent configuration transfers ownership without restarting the SDK;
changed stable context follows the existing close-and-start replacement path.

## Context schema

All automatic contexts use custom names to coexist with native Sentry context.

### `foundry_app`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `name` | `application/config/name` | Empty |
| `version` | `application/config/version` | Empty |
| `start_time` | Unix system time minus monotonic engine uptime, formatted as UTC | Unavailable or invalid |
| `architecture` | Engine architecture name | Empty |

### `godot_engine`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `version` | Engine version string | Empty |
| `version_commit` | Engine version hash | Empty |
| `architecture` | Engine architecture name | Empty |
| `runtime_mode` | Runtime classifier | Never omitted when collection is available |
| `editor` | Editor hint | None |
| `debug_build` | Debug-build flag | None |
| `headless` | Headless display/runtime flag | None |

Runtime-mode classification uses this precedence:

1. `headless` when the display server is headless or the dedicated-server
   feature is present;
2. `editor` when the engine reports an editor hint;
3. `debug_export` when the process is a debug build;
4. `release_export` otherwise.

### `foundry_device`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `model` | OS model name | Empty or `GenericDevice` |
| `type` | Target classification | Unknown |
| `architecture` | Engine architecture name | Empty |
| `processor_name` | OS processor name | Empty |
| `processor_count` | OS processor count | Not positive |
| `memory_size` | Physical memory | Unsupported or not positive |
| `free_memory` | Free memory | Unsupported or negative |
| `usable_memory` | Available memory | Unsupported or negative |
| `free_storage` | Space remaining under `user://` | Unsupported or negative |

`free_memory`, `usable_memory`, and `free_storage` are refreshed before
ordinary event capture. Physical memory remains the configuration-time value.
No memory-information method is called when the probe identifies iOS.

When `send_default_pii` is true, the collector may also include:

| Field | Source | Omission rule |
| --- | --- | --- |
| `unique_identifier` | Godot OS unique identifier | Empty |
| `locale` | OS locale | Empty |
| `timezone` | System time-zone name | Empty |

These three fields are absent when `send_default_pii` is false. The collector
does not derive a replacement identifier when the OS does not provide one.

### `display`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `server` | Display-server name | Empty |
| `screen_count` | Display screen count | Not positive |
| `touchscreen_available` | Display-server capability | Display unavailable |
| `primary_width_pixels` | Primary-screen size | Not positive |
| `primary_height_pixels` | Primary-screen size | Not positive |
| `primary_dpi` | Primary-screen DPI | Not positive |
| `primary_refresh_rate` | Primary-screen refresh rate | Not positive |
| `primary_orientation` | Normalized screen orientation | Unknown |

`primary_orientation` is refreshed before ordinary event capture. When the
display server is headless, unsupported physical display values are omitted;
the `godot_engine.headless` field remains the authoritative mode signal.

### `gpu`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `name` | Video-adapter name | Empty; omit the entire context |
| `vendor_name` | Video-adapter vendor | Empty |
| `api_version` | Video-adapter API version | Empty |
| `device_type` | Video-adapter type mapping | Unknown |
| `driver_name` | First driver-info item | Missing or empty |
| `driver_version` | Second driver-info item | Missing or empty |
| `rendering_method` | Current rendering method | Empty |

An absent GPU context is expected in headless mode and is not an error.

### `foundry_runtime`

Collected at configuration:

| Field | Source | Omission rule |
| --- | --- | --- |
| `environment` | Explicit `ObservabilityConfig.environment` | Empty |
| `mode` | Same classifier as `godot_engine.runtime_mode` | Collection unavailable |
| `sandboxed` | OS sandbox flag | Collection unavailable |
| `userfs_persistent` | OS user-filesystem flag | Collection unavailable |

The explicit Sentry top-level `environment` remains authoritative. The custom
context repeats it only to make the runtime snapshot self-contained.

## Merge and lifecycle behavior

Context dictionaries use a two-level merge:

1. Start with a deep copy of the cached stable contexts.
2. For each volatile context, replace only the volatile fields present in that
   update.

Missing volatile fields do not erase valid stable fields. Unsupported values
are omitted from the update. The merge never mutates cached stable context or
the caller's event.

Native scope context is installed only after successful SDK initialization.
If context conversion omits every field in one context, that context is not
installed. If all automatic context is unavailable, Sentry initialization and
event capture continue with native SDK defaults.

Native crash reports receive stable scope context. They deliberately do not
receive current-session volatile context during next-launch processing,
avoiding stale memory, storage, or orientation data.

## Safety and privacy

- The collector checks platform identity before memory collection and never
  calls `OS.get_memory_info()` on iOS.
- All dictionary lookups validate type and range before publishing a field.
- Empty strings, non-finite numbers, invalid dimensions, negative capacities,
  unknown enum values, and generic model placeholders are omitted.
- Context collection does not write diagnostics through the engine logger,
  preventing automatic-capture recursion.
- Native conversion omits unsupported values instead of throwing or rejecting
  the event.
- Stable identifiers, locale, and timezone require
  `provider_options["send_default_pii"] == true`.
- Command-line arguments, permissions, hostnames, IP addresses, user paths,
  and arbitrary environment variables are never collected.

## Testing

### FoundryScript

- Verify stable application, engine, device, display, GPU, and runtime
  conversion from a fixed fake probe.
- Verify runtime-mode precedence for headless, editor, debug export, and
  release export.
- Verify empty, generic, invalid, and unsupported values are omitted.
- Verify iOS collection never invokes the fake probe's memory method.
- Verify identifying fields are absent by default and present only when
  `send_default_pii` is true.
- Verify volatile memory, storage, and orientation values override the cached
  snapshot without mutating it.
- Verify stable context is forwarded during configuration and complete context
  during event capture.
- Verify failed configuration preserves the active cached context and shutdown
  clears it.
- Verify existing event attributes and reserved Foundry metadata are
  unchanged.

### Swift

- Verify recursive context conversion preserves supported values and omits
  unsupported values.
- Verify empty contexts are not attached.
- Verify capture-local context application does not change event extras.
- Verify lifecycle equality includes stable context.
- Verify stable scope context is applied after successful Sentry startup.

### Java

- Verify recursive context conversion preserves supported values and omits
  unsupported values.
- Verify capture-local context and stable global scope context use separate
  Sentry context storage from event extras.
- Verify lifecycle equality includes stable context.
- Verify invalid or empty context does not fail initialization or capture.

### Repository contracts and documentation

- Extend package checks for new FoundryScript resources and UID companions.
- Document automatic fields, configuration/capture timing, iOS omission,
  privacy behavior, and native crash limitations in `docs/API.md`.
- Run the complete `task test` gate with the Android SDK path configured.

## Acceptance criteria

- Ordinary Sentry events on macOS, iOS, and Android contain stable application
  and engine context plus every supported device, display, GPU, and runtime
  field.
- Stable scope context is available to native crash reports without depending
  on orderly shutdown.
- Free memory, usable memory, free storage, and orientation are refreshed near
  ordinary event capture.
- iOS never calls the unsafe Godot memory-information API.
- Unsupported or invalid fields are absent rather than populated with
  placeholders.
- Automatic context does not replace native app/device/OS context or modify
  explicit release, environment, distribution, global attributes, or event
  attributes.
- Identifying fields remain absent unless `send_default_pii` is enabled.
- Collection and conversion failures do not reject capture.
- Deterministic FoundryScript, Swift, Java, package, and documentation checks
  pass.
