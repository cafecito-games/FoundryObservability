# Sentry iOS Observability Provider Design

**Date:** 2026-07-23
**Status:** Approved for implementation

## Goal

Add the first production observability backend for Foundry: an optional iOS
Sentry provider that implements the existing provider-neutral
`foundry.observability` contract through a Foundry-Swift native extension.

The first slice will support explicit messages, structured logs, exceptions,
flush, lifecycle management, and Sentry Cocoa's normal iOS initialization
behavior. It will not expand the core API or add Android, macOS, performance,
breadcrumbs, user identity, attachments, or custom crash APIs.

## Context and constraints

The existing repository contains the provider-neutral FoundryScript addon at
`addons/FoundryObservability`. Its contract is deliberately independent of a
backend SDK and already defines configuration, normalized events, exception
payloads, severity levels, provider replacement, flush, and idempotent
shutdown.

Provider-specific native work belongs behind an explicit addon boundary. The
AuthenticationKit Foundry migration is the reference for this boundary: a
Foundry-Swift entry point, an iOS `.xcframework`, an export plugin, XcodeGen
project metadata, and a `.foundryextension` descriptor. The shared FoundrySwift
runtime is installed as a sibling addon and is embedded by `FoundrySwiftEmbed`;
the Sentry addon must not embed a second copy.

The Sentry Cocoa SDK is the native iOS backend. Its SwiftPM package is pinned
to an exact release, and the FoundrySwift binary package is pinned to the
existing `0.1.0-alpha.2` release. The local
`~/CafecitoGames/Foundry-Swift` checkout remains the API and toolchain reference
for development; the committed addon build is reproducible without depending
on that machine-local path.

The native build consumes the prebuilt artifacts published by
`Foundry-Swift-Binary`: `FoundrySwift.xcframework` and
`FoundrySwiftMacros.artifactbundle`. The build helper stages and checksum-
verifies those release assets in Xcode derived data before resolving the native
project. Foundry-Swift source is not compiled as part of this addon build.

## Architecture

### Addon boundary

Create a sibling runtime addon named `FoundryObservabilitySentry` rather than
adding Sentry SDK files to the core addon. The sibling addon contains the
FoundryScript provider, the native extension descriptor, the iOS export plugin,
Swift sources, and native build metadata. The core addon remains safe to install
and run on platforms without Sentry or FoundrySwift.

The provider namespace is `foundry.observability.sentry`. The public provider
class is `SentryObservabilityProvider`; the native Foundry class is
`SentryObservabilityBridge`. The distinct names prevent a script provider and a
native bridge from being confused in diagnostics or class lookup.

Expected runtime layout:

```text
addons/FoundryObservabilitySentry/
├── FoundryObservabilitySentry.foundryextension
├── SentryObservabilityProvider.fs
├── SentryObservabilityProvider.fs.uid
├── export_plugin.fs
├── export_plugin.fs.uid
├── plugin.cfg
├── bin/ios/FoundryObservabilitySentry.xcframework
└── FoundryObservabilitySentry/
    ├── Package.swift
    ├── project.yml
    ├── FoundryObservabilitySentry.xcodeproj/
    ├── Sources/FoundryObservabilitySentry/
    └── Tests/FoundryObservabilitySentryTests/
```

The checked-in source package and project metadata are build inputs. Generated
frameworks and build directories are ignored like the AuthenticationKit
migration outputs; distributable archives contain the runtime addon and native
framework only, not SwiftPM caches or tests.

### FoundryScript provider

`SentryObservabilityProvider` implements the existing
`ObservabilityProvider` trait. It does not import or depend on Sentry directly.
At runtime it resolves `SentryObservabilityBridge` through Foundry's class
database and forwards provider operations through typed native methods.

The provider must behave safely when the native extension is absent:

- `provider_name()` returns `&"sentry"`.
- `is_available()` returns false until native initialization succeeds.
- `configure()` returns `Error.FAILED` when an enabled configuration requires a
  missing bridge or a missing DSN.
- A disabled configuration returns `Error.OK` and remains unavailable.
- `capture()` returns an empty string when disabled, unavailable, or rejected by
  the bridge.
- `flush()` returns the bridge result, or `Error.OK` when no native backend is
  active.
- `shutdown()` is safe to call repeatedly.

The provider reads the DSN from `provider_options()["dsn"]`. The remaining
provider options are opaque to the core and are forwarded to Swift. The first
supported options are `debug` and Sentry initialization options that have a
direct Sentry Cocoa equivalent; unsupported values are ignored rather than
causing core configuration to fail.

The provider forwards these normalized fields to Swift:

```text
configure:
  enabled, dsn, environment, release, dist, global_attributes, provider_options

capture:
  kind, level, message, source, timestamp_msec, attributes, exception
```

The bridge payload is represented with Foundry dictionaries so nested attribute
values remain possible. The provider owns the conversion from
`ObservabilityConfig` and `ObservabilityEvent` to that payload and makes no
changes to the core value types.

For deterministic FoundryScript tests, the provider constructor accepts an
optional bridge object. Production callers omit it and the provider resolves
`SentryObservabilityBridge` through `ClassDB`; test callers provide a small
recording object implementing the same method names. This seam is internal to
the provider and does not alter the `ObservabilityProvider` trait.

### Swift bridge

The Swift target uses `FoundrySwift` and Sentry Cocoa. Its entry point is
generated with:

```swift
#initFoundryExtension(
    cdecl: "foundry_observability_sentry_entry_point",
    types: [SentryObservabilityBridge.self]
)
```

`SentryObservabilityBridge` is a Foundry `RefCounted` class with callable
methods for configuration, availability, capture, flush, and shutdown. It
owns no game-facing policy beyond translating the normalized provider payload
to Sentry's event model.

Configuration calls Sentry's startup API with the DSN and deployment metadata.
Disabled configuration does not start or capture through Sentry. Repeated
configuration is safe and replaces the bridge's active configuration without
leaking an old client. Shutdown is idempotent and releases the active client
state according to Sentry Cocoa's lifecycle API.

### Event mapping

Severity mapping:

| Foundry level | Sentry level |
| --- | --- |
| TRACE | debug |
| DEBUG | debug |
| INFO | info |
| WARN | warning |
| ERROR | error |
| FATAL | fatal |
| Unknown | error |

Message and log events become Sentry events with the normalized message,
mapped level, and source logger. Exception events become Sentry exception
events with the normalized exception type and message. The original stack text
is retained in event extras in this first slice rather than attempting to
construct a native Sentry frame list from an engine string.

Global attributes and per-event attributes are merged into Sentry extras, with
per-event keys taking precedence. Provider metadata is stored under reserved
`foundry.*` keys:

- `foundry.kind`
- `foundry.source`
- `foundry.timestamp_msec`
- `foundry.exception_type` when an exception is present
- `foundry.stack_trace` when a stack trace is present

The engine timestamp is uptime-relative, not a Unix timestamp. It is therefore
preserved as `foundry.timestamp_msec`; Sentry assigns its normal wall-clock
event timestamp at capture time.

The bridge returns Sentry's generated event ID as a string. An empty Sentry ID
is translated to an empty provider ID so the existing core records a capture
failure consistently.

### Flush and errors

Foundry timeout values are integer milliseconds. Swift converts them to the
Sentry SDK's timeout unit before flushing. A configured bridge returns
`Error.OK` after a successful flush request and `Error.FAILED` when no active
client can perform the request. Provider failures are returned through the
provider contract only; they are never sent to the FoundryLib logging sink,
which preserves the core's non-recursive failure rule.

## Packaging and export

The native package produces `FoundryObservabilitySentry.xcframework` with iOS
device and iOS simulator slices. The `.foundryextension` descriptor references
that same xcframework for `ios.debug`, `ios.release`,
`ios.simulator.debug`, and `ios.simulator.release`.

The descriptor does not declare FoundrySwift as a dependency. FoundrySwiftEmbed
owns the single shared FoundrySwift framework embedding operation, matching the
AuthenticationKit migration contract. The Sentry addon export plugin contributes
only the Sentry xcframework to iOS exports.

The Swift package uses:

- `Foundry-Swift-Binary` exact `0.1.0-alpha.2` for the shared Foundry bindings.
- `getsentry/sentry-cocoa` exact `9.13.0` for the Sentry SDK.
- Swift language mode 6.
- iOS deployment target 17.0, matching the FoundrySwift binary and migration
  reference.

The build task performs the following deterministic steps:

1. Resolve the Swift packages and generate the Xcode project.
2. Build a Release iOS device framework.
3. Build a Release iOS simulator framework with code signing disabled.
4. Combine both frameworks into the xcframework.
5. Place the xcframework under `addons/FoundryObservabilitySentry/bin/ios`.

No generated framework, SwiftPM checkout, Xcode derived data, or credentials
are committed.

## Testing strategy

### FoundryScript tests

Add provider tests to the consumer test project for:

- stable provider name;
- safe unavailable behavior when the native bridge is not loaded;
- missing DSN rejection for enabled configuration;
- disabled configuration returning `Error.OK` without capture;
- forwarding of message, log, exception, attributes, and timeout values when a
  test bridge is available;
- idempotent shutdown.

The tests must not require a live iOS device, Sentry DSN, or network access.

### Swift tests

Add XCTest coverage for pure translation helpers:

- Foundry severity to Sentry severity;
- merging global and event extras with event precedence;
- reserved metadata fields;
- exception type, message, and stack preservation;
- safe conversion of supported Foundry dictionary values;
- milliseconds-to-seconds flush conversion;
- invalid or empty DSN configuration.

The tests inspect constructed Sentry events and helper results. They do not send
events to Sentry.

### Build and package contract tests

Add shell contracts modelled after AuthenticationKit's migration checks. They
will verify:

- the Swift entry symbol and `#initFoundryExtension` declaration;
- the generated project contains an Info.plist and the expected iOS schemes;
- the build creates the device and simulator framework slices;
- the descriptor uses an xcframework for every iOS variant;
- the descriptor does not declare FoundrySwift;
- the export plugin references only the Sentry xcframework;
- the distributable archive contains required `.fs` UID companions and no
  generated build state.

The full repository gate remains `task test`. Native compilation is exposed as
an explicit iOS task so the FoundryScript-only gate remains runnable on a host
without Xcode or iOS SDK artifacts.

## Out of scope

This design does not add or change the core observability contract. It does not
implement Android or macOS, a Sentry user API, breadcrumbs, user identity,
performance transactions, attachments, feedback UI, offline persistence beyond
the SDK default, custom transport, release automation, or a second crash
handler abstraction.
