# Mobile Hang and ANR Diagnostics Design

## Summary

FoundryObservability will expose provider-neutral controls for native
main-thread hang diagnostics on macOS and iOS and Application Not Responding
(ANR) diagnostics on Android. The optional Sentry provider will translate
those controls to the pinned native SDKs and let those SDKs detect, classify,
and report the platform diagnostic events.

Detection will be enabled by default on all three target platforms with a
5,000 ms timeout. Android thread-dump attachment will be disabled by default.

## Goals

- Detect and report main-thread hangs on macOS and iOS.
- Detect and report ANRs on Android using the implementation appropriate for
  the device OS version.
- Expose provider-neutral enable and timeout controls.
- Expose an Android-specific thread-dump attachment control without coupling
  the core API to a provider.
- Preserve the native event severity, mechanism, threads, attachments, and
  platform diagnostic metadata.
- Keep unsupported providers and platforms safe.
- Cover configuration and native option translation deterministically.
- Document device-level validation for behavior that cannot be safely
  reproduced in an automated unit test.

## Non-goals

- Implement a second watchdog in FoundryScript, Swift, or Java.
- Synthesize provider-neutral hang events from a timer.
- Reclassify or remap native hang or ANR event severity.
- Parse, normalize, or redact native thread dumps.
- Add a runtime pause/resume API for intentional main-thread blocking.
- Freeze the automated test runner to exercise device watchdog behavior.

## Public configuration

`ObservabilityConfig` will add these public properties and matching constructor
parameters:

| Property | Default | Meaning |
| --- | ---: | --- |
| `application_hang_detection_enabled` | `true` | Enables native main-thread hang detection on macOS and iOS. |
| `application_hang_timeout_msec` | `5000` | Sets the Apple main-thread hang threshold in milliseconds. |
| `android_anr_detection_enabled` | `true` | Enables native Android ANR detection. |
| `android_anr_timeout_msec` | `5000` | Sets the watchdog threshold used by Android versions where the SDK controls the timeout. |
| `android_anr_attach_thread_dump` | `false` | Requests the operating-system thread dump when the Android implementation can provide one. |

Both timeout values will be normalized to at least 1,000 ms during
`ObservabilityConfig` construction. This prevents accidental near-zero
watchdogs while preserving exact forwarding for supported values.

The names describe observability behavior rather than a provider API. Other
providers may implement the same contract without exposing provider-specific
configuration.

## Architecture

The implementation delegates detection to the platform SDKs:

1. A caller constructs `ObservabilityConfig`.
2. `SentryObservabilityProvider.configure()` copies the normalized diagnostic
   values into the native bridge payload alongside the existing provider
   configuration.
3. The Apple bridge applies the enable flag and millisecond timeout to the
   native Sentry Cocoa options before SDK startup.
4. The Android bridge applies the enable flag, timeout, and thread-dump flag to
   the native Sentry Android options before SDK startup.
5. The native SDK monitors application responsiveness and creates any
   resulting diagnostic event.

No hang or ANR event passes through `ObservabilityProvider.capture()`. Native
generation is necessary to retain the platform mechanism, complete thread
state, attachments, OS data, and SDK lifecycle handling.

## Platform behavior

### macOS and iOS

The Apple bridge will map:

- `application_hang_detection_enabled` to native app-hang tracking enablement.
- `application_hang_timeout_msec` to the native timeout interval after
  converting milliseconds to seconds.

The same neutral settings apply to both Apple targets. The pinned native SDK
owns monitoring, event construction, severity, suspension handling, startup
and shutdown integration, and transport.

### Android

The Android bridge will map:

- `android_anr_detection_enabled` to native ANR enablement.
- `android_anr_timeout_msec` to the native ANR timeout in milliseconds.
- `android_anr_attach_thread_dump` to native ANR thread-dump attachment.

On Android versions before 11, the native SDK uses watchdog-based detection and
the configured timeout. On Android 11 and later, the native SDK uses the
operating system's historical process-exit information; the operating system
determines the ANR threshold, so the configured timeout does not control V2
detection. Thread-dump attachment applies only when the native implementation
provides the dump.

## Lifecycle and false-positive safety

FoundryObservability will not run an independent watchdog. Native SDK lifecycle
integrations remain responsible for avoiding reports while the application
cannot safely demonstrate main-thread progress, including startup, shutdown,
and suspension transitions.

A genuine main-thread block longer than the configured threshold is expected
to report. Callers with intentional long blocking work must either move that
work off the main thread or disable the relevant detector before SDK
configuration. Runtime reconfiguration continues to follow the provider's
existing configure lifecycle.

## Disabled and unsupported behavior

Disabled flags are forwarded before native SDK startup. The corresponding
native integration must not generate diagnostic events when disabled.

The core configuration remains safe for providers that do not implement these
capabilities. Such providers may ignore the settings. Platforms without an
available native bridge retain the existing configuration failure and
`is_available()` behavior; this change does not add a partial or simulated
fallback.

Apple settings have no effect on Android. Android settings have no effect on
Apple targets. These boundaries will be explicit in the public documentation.

## Error handling

- Configuration values are copied into the provider payload rather than
  retaining caller-owned mutable dictionaries.
- Timeout normalization occurs in the provider-neutral configuration object.
- Native option translation is performed before SDK startup.
- Existing bridge initialization exceptions continue to return
  `Error.FAILED`.
- No new capture-time failure path is introduced because diagnostics are
  generated by the initialized native SDK.
- Existing shutdown remains idempotent and disables the active SDK client.

## Deterministic testing

### FoundryScript

- Verify the five configuration defaults.
- Verify both timeouts normalize to at least 1,000 ms.
- Verify custom enabled, timeout, and attachment values are forwarded exactly
  in the native bridge payload.
- Verify disabled values are forwarded as `false`.

### Swift

Extract a focused native-options configuration seam that accepts the bridge
payload and Sentry Cocoa options without starting a real client. Tests will
verify:

- enabled and disabled app-hang tracking;
- conversion from timeout milliseconds to seconds;
- default and custom values;
- unrelated event mapping remains unchanged.

### Java

Extract the equivalent package-private Android options seam. Tests will verify:

- enabled and disabled ANR detection;
- exact millisecond timeout forwarding;
- enabled and disabled thread-dump attachment;
- unrelated bridge configuration remains unchanged.

### Build contracts

The Apple and Android build-contract scripts will assert that the bridges
continue to use the required native option APIs. This catches accidental
removal during source or dependency updates.

### Complete validation

The final local gate remains:

```sh
task test
```

Focused native tests and contract scripts will run during test-driven
iteration before the complete gate.

## Device validation matrix

Public documentation will include these manual checks:

| Target | Enabled check | Disabled check | Required inspection |
| --- | --- | --- | --- |
| macOS | Block the main thread longer than 5 seconds in a controlled test build. | Repeat with Apple detection disabled. | One native hang event when enabled; no event when disabled; verify severity, mechanism, blocked thread, stack, release, environment, and device/OS data. |
| iOS | Block the main thread longer than 5 seconds on a physical test device. | Repeat with Apple detection disabled. | One native hang event when enabled; no event when disabled; verify severity, mechanism, blocked thread, stack, release, environment, and device/OS data. |
| Android 10 or earlier | Block the main thread longer than the configured watchdog timeout. | Repeat with ANR detection disabled. | One watchdog ANR when enabled; no event when disabled; verify mechanism, threads, release, environment, and device/OS data. |
| Android 11 or later | Trigger a controlled system ANR and relaunch so historical exit information can be processed. | Repeat with ANR detection disabled. | One system ANR when enabled; no event when disabled; verify mechanism, threads, release, environment, device/OS data, and thread-dump attachment when requested and available. |

The validation guide will note that Android 11+ timeout behavior is
OS-controlled and that delivery may occur on the next launch.

## Documentation changes

- Update `README.md` to list mobile hang and ANR diagnostics among supported
  features.
- Update `docs/API.md` with all five configuration fields, defaults, platform
  applicability, Android version behavior, lifecycle expectations, and the
  device validation matrix.
- Update `CHANGELOG.md` with the new provider-neutral configuration and native
  Sentry delivery behavior.

## Alternatives considered

### Custom cross-platform watchdog

Rejected because it would duplicate mature platform logic, lose native
diagnostic fidelity, and increase false-positive risk around lifecycle
transitions.

### Provider options only

Rejected because applications would need provider-specific keys for a
cross-cutting observability capability, contrary to the provider-neutral API
boundary.

### One shared hang switch for every platform

Rejected because Android ANR behavior, timeout semantics, and thread-dump
attachment differ materially from Apple app-hang tracking. Separate
platform-specific controls make those differences explicit while remaining
provider-neutral.
