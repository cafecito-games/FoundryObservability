# FoundryObservability Cross-Platform Sentry Provider Design

## Goal

Extend the existing `FoundryObservabilitySentry` addon so the same
`SentryObservabilityProvider` transparently uses the Swift Sentry bridge on
iOS and macOS and the Sentry Android bridge on Android.

This change keeps the provider-neutral FoundryScript API stable. A game
project configures one provider and does not inspect the current platform.

## Scope

The addon will support these native runtime artifacts:

- iOS device and simulator:
  `bin/ios/FoundryObservabilitySentry.xcframework`.
- macOS arm64:
  `bin/macos_arm64/FoundryObservabilitySentry.framework`.
- Android debug and release:
  `bin/android/debug/FoundryObservabilitySentry-debug.aar` and
  `bin/android/release/FoundryObservabilitySentry-release.aar`.

The existing Swift bridge remains the Apple implementation. The Android
implementation will be a Java Android library using `FoundryPlugin`, matching
the Foundry Android plugin structure used by AuthenticationKit. The Android
library will depend on `io.sentry:sentry-android:8.50.1`, following the
current official Sentry Godot Android integration's SDK version and deferred
initialization pattern.

This slice does not add public Sentry-specific APIs for users, breadcrumbs,
attachments, user identity, metrics, tracing, or feedback. It implements the
existing normalized event/configuration contract on all three target families.

## Shared bridge contract

Both native implementations expose the same bridge name:

```text
SentryObservabilityBridge
```

The existing Apple bridge continues to expose:

```text
configure(payload: Dictionary) -> int
isAvailable() -> bool
capture(payload: Dictionary) -> String
flush(timeout_msec: int) -> int
shutdown() -> void
```

The Android `FoundryPlugin` will expose the same methods through its
`@UsedByFoundry` annotations and return types.

The configuration payload is:

```text
{
  enabled: bool,
  dsn: String,
  environment: String,
  release: String,
  dist: String,
  global_attributes: Dictionary,
  provider_options: Dictionary
}
```

The event payload is:

```text
{
  kind: String,
  level: int,
  message: String,
  source: String,
  timestamp_msec: int,
  attributes: Dictionary,
  exception: {
    type_name: String,
    message: String,
    stack_trace: String,
    attributes: Dictionary
  }
}
```

The exception member is omitted when no exception is attached.

## FoundryScript platform resolution

`SentryObservabilityProvider` retains its injected bridge constructor seam for
deterministic tests. When no bridge is injected, `_resolve_bridge()` will:

1. Look for the Android singleton named `SentryObservabilityBridge` and use it
   when present.
2. Otherwise instantiate the registered Apple/macOS `ClassDB` class with the
   same name when available.
3. Return `null` on unsupported hosts.

This order prevents a platform-specific native implementation from being
selected by caller code and keeps unsupported platforms safe. Existing
configuration validation remains unchanged: enabled configuration requires a
non-empty DSN and a resolvable native bridge; disabled configuration succeeds
without native code.

## Android native bridge

Create the Android module at
`addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry` with:

- Gradle Android library configuration using Java 17, compile SDK 36, and
  minimum SDK 24.
- `SentryObservabilityBridge.java` extending
  `games.cafecito.foundry.plugin.FoundryPlugin`.
- An Android manifest entry under
  `org.godotengine.plugin.v2.SentryObservabilityBridge`.
- Sentry manifest metadata disabling automatic initialization until the
  provider receives its configuration.
- `addons/FoundryObservabilitySentry/android-dependencies.txt` containing the
  Sentry Android Maven coordinate used by both Gradle and the export plugin.
  Keeping this file at the addon root makes it part of the runtime package
  while the Gradle module reads it from its parent directory.

`configure` will close an active Sentry client before applying new settings,
validate the DSN when enabled, map environment/release/dist/debug settings,
and initialize Sentry with the Foundry activity's application context.
Initialization exceptions return a failure code and leave the bridge
unavailable. Disabled configuration closes any active client and returns OK.

`capture` will build a `SentryEvent`, map the normalized severity, message,
logger, exception, and merged extras, then return the Sentry event ID. It will
return an empty string when unavailable. `flush` will pass the requested
millisecond timeout to the Android SDK. `shutdown` will be idempotent and
close the SDK once.

The Android mapper will recursively convert supported Foundry values to
Sentry-compatible Java values: null, booleans, integral and floating-point
numbers, strings, dictionaries/maps, and arrays/lists. Unsupported values
will be omitted rather than throwing while capturing an event.

Global attributes are copied first, event attributes override matching global
keys, exception attributes override matching event keys, and reserved
metadata is written last:

```text
foundry.kind
foundry.source
foundry.timestamp_msec
foundry.exception_type
foundry.stack_trace (when non-empty)
```

## Apple artifact exposure

The existing Swift project already contains iOS and macOS targets. The native
build task will additionally build the arm64 macOS framework and copy it to
`bin/macos_arm64`. The Foundry extension descriptor will declare
`macos.arm64` with an empty dependency map, matching the existing Apple
artifact ownership model.

The iOS export plugin will continue to embed only the Sentry iOS xcframework.
The macOS framework will be supplied through the Foundry extension descriptor,
as in the AuthenticationKit addon. No Apple export path will embed Android
artifacts.

## Android export and distribution

The addon export plugin will add the debug or release AAR based on the export
configuration and return the Sentry Maven dependency coordinates from
`android-dependencies.txt`. The Android AAR and Sentry dependency will be
selected only for Android exports.

The package script will copy all present runtime artifacts from the iOS,
macOS, and Android bin directories while excluding Swift sources, Gradle
source, generated Xcode state, Gradle caches, and tests. Empty artifact
directories will retain `.gitkeep` files so source-only package contract tests
remain deterministic.

## Testing strategy

Tests will cover behavior at three layers:

1. FoundryScript provider tests will verify the existing fake-bridge behavior,
   Android singleton resolution contract, configuration forwarding, event and
   exception forwarding, flush timeout forwarding, disabled operation, and
   idempotent shutdown.
2. Android Java unit tests will verify severity mapping, recursive value
   conversion, attribute precedence, reserved metadata, exception mapping, and
   timeout conversion without sending data to Sentry.
3. Shell build/package contracts will verify Android Gradle/manifest naming,
   Sentry dependency pinning, AAR export selection, macOS descriptor entries,
   Apple/Android artifact paths, and package exclusions.

The implementation will run the Android Gradle tests, lint, and assemble tasks;
the existing Swift mapper tests; FoundryScript tests; package checks; and the
full repository validation task before completion.

## Acceptance criteria

- A single installed `FoundryObservabilitySentry` addon contains the shared
  FoundryScript provider and all three platform artifact paths.
- The provider selects the Android singleton on Android and the Swift bridge
  on iOS/macOS without caller-side platform branching.
- Enabled configuration requires a DSN and reports native initialization
  failures through the existing error contract.
- Messages and exceptions preserve severity, message, source, attributes,
  timestamp metadata, and exception details on both native backends.
- Disabled configuration, unsupported hosts, repeated shutdown, and missing
  native artifacts remain safe.
- The repository’s tests, build contracts, package checks, and lint pass with
  fresh command output.

## References

- [Official Sentry Godot Android library](https://github.com/getsentry/sentry-godot/tree/main/android_lib)
- [Foundry AuthenticationKit Android reference](https://github.com/cafecito-games/AuthenticationKit/tree/main/addons/AuthenticationKit/AndroidAuthenticationKit)
