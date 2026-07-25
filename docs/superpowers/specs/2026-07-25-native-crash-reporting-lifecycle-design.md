# Native Crash Reporting Lifecycle Design

## Summary

FoundryObservability will make native crash reporting an explicit lifecycle
guarantee of the optional Sentry provider on macOS, iOS, and Android. The
provider will activate the platform SDK as soon as complete configuration is
available, preserve native crash data for next-launch delivery, attach stable
deployment metadata before a crash can occur, and prevent stale provider
instances from shutting down a newer native client.

Crash generation will remain outside the production addon. Repository-only
validation tooling and documented debugger or device commands will exercise
real native crash handlers without adding a callable crash trigger to
distributed builds.

## Goals

- Activate the Sentry Cocoa or Sentry Android crash handler at the first point
  where DSN, release, and environment are available.
- Preserve crash reports across process termination and let the native SDK
  deliver them on the next launch.
- Attach release, environment, distribution, global attributes, device, OS,
  and application metadata without relying on normal shutdown.
- Keep native crash capture active across successful provider
  reconfiguration and provider replacement.
- Preserve the current active client when a replacement configuration fails.
- Make a missing, outdated, inactive, or failed bridge distinguishable through
  deterministic provider results and availability.
- Keep disabled and unsupported configurations safe.
- Provide repeatable macOS, iOS, and Android validation without shipping a
  deliberate-crash API.
- Add deterministic lifecycle, native option, dependency, and packaging tests.

## Non-goals

- Add a provider-neutral method that intentionally terminates the application.
- Add a Sentry-specific crash trigger to the production FoundryScript, Swift,
  or Java bridge.
- Reimplement crash persistence, signal handling, native unwinding, symbol
  processing, or upload queues.
- Automatically execute destructive crash checks in the ordinary local or CI
  test gate.
- Guarantee capture before the game supplies a valid enabled provider
  configuration.
- Add a second project-settings configuration source that could disagree with
  `ObservabilityConfig`.
- Generalize the work into a new provider capability registry.

## Public API boundary

The provider-neutral API remains unchanged. Applications continue to use:

- `FoundryObservability.configure()` to activate a provider;
- `FoundryObservability.is_available()` to check the active native client;
- `FoundryObservability.last_error()` and the configure result to distinguish
  configuration failures;
- `FoundryObservability.flush()` and `shutdown()` for normal lifecycle
  completion.

`SentryObservabilityProvider.configure()` will return:

| Condition | Result |
| --- | --- |
| Disabled configuration without a native bridge | `Error.OK` |
| Enabled configuration without the platform bridge or with an outdated lifecycle contract | `Error.ERR_UNAVAILABLE` |
| Enabled configuration without a DSN | `Error.FAILED` |
| Native SDK validation or startup failure | `Error.FAILED` |
| Active native SDK owned by the provider instance | `Error.OK` |

`is_available()` will be true only when the provider is enabled, has not shut
down, owns the active native lifecycle generation, and the native Sentry SDK is
enabled. This keeps unsupported behavior observable without coupling the core
API to Sentry.

## Startup boundary

The production bridge cannot start a correctly attributed crash handler before
it knows the DSN, release, and environment. The earliest supported boundary is
therefore the first successful
`FoundryObservability.configure(SentryObservabilityProvider, config)` call.

Documentation will tell applications to make that call from their earliest
startup hook after the FoundryObservability autoload is available and before
gameplay systems or background work begin. Applications that configure later
accept an explicit pre-configuration crash-coverage gap.

The design will not add parallel project settings for early native bootstrap.
Two configuration sources would create a greater risk: a crash could be stored
with a release or environment different from the values used by the active
observability service.

## Native lifecycle ownership

Sentry Cocoa and Sentry Android expose process-global SDK clients. Multiple
`SentryObservabilityProvider` objects can nevertheless exist while the core
evaluates a replacement provider. A native lifecycle generation will prevent
one provider object from closing a client activated by another.

Each Sentry provider instance will create a stable opaque owner token and pass
it in the configuration payload. The Apple and Android bridges will retain the
token belonging to the active native client.

The bridge lifecycle will follow these rules:

1. Validate the complete candidate payload before changing the active client.
2. Snapshot the active owner and validated configuration.
3. Flush and close the active SDK using a bounded shutdown timeout.
4. Start the SDK with the candidate configuration.
5. Publish the candidate owner only after startup succeeds.
6. If startup fails, restart the snapshotted configuration and restore its
   owner before returning failure.
7. Ignore `shutdown()` from an owner that is no longer active.
8. Treat repeated shutdown from the active owner as a no-op after the first
   close.

The provider will include its owner token in availability, flush, and shutdown
bridge calls. Capture paths will retain their existing payload shapes; the
provider availability check will reject capture from a stale owner.

This protocol fixes the current Sentry-to-Sentry replacement hazard in which a
new provider can start the global SDK and the outgoing provider can
immediately close it.

## Reconfiguration and shutdown

Reconfiguring the same provider instance uses the same owner token. A
configuration that is equivalent for native startup will keep the current
client rather than cycling the crash handler. A configuration that changes
DSN, release, environment, distribution, native diagnostics, logging,
metrics, privacy, or other SDK-start options will use the bounded
flush-close-start transition.

The bridges will set the native SDK shutdown timeout explicitly to 2,000 ms,
matching the public flush default. Normal shutdown will perform the native
SDK's supported bounded flush and close. The native SDK remains responsible
for leaving durable crash data intact if the process terminates before
delivery finishes.

No shutdown code will delete cache directories, envelopes, session state, or
crash artifacts. Reconfiguration will not clear native persistent storage.

## Crash metadata

Release, environment, and distribution will be set on native SDK options
before the crash handler is considered active. The native SDK will continue to
collect its platform device, OS, and application contexts.

Global attributes currently enrich provider-translated events. The lifecycle
change will also apply supported global attributes to the initial native scope
so that native crashes receive the same application metadata. Attribute
conversion will use the existing bounded, copied payload and the native SDK's
supported scalar and structured value handling.

Metadata is attached during SDK startup or retained native scope
configuration, not during application shutdown. A crash from run A must
therefore retain run A's release and environment when the native SDK uploads
it during run B.

## Platform behavior

### macOS and iOS

The Swift bridge will start Sentry Cocoa with crash handling explicitly
enabled, deployment metadata assigned, global attributes installed on the
initial scope, and a 2-second shutdown interval. Sentry Cocoa owns signal and
exception handling, durable crash storage, next-launch processing, native
contexts, and transport.

### Android

The Java bridge will start Sentry Android with uncaught-exception and NDK crash
handling enabled, deployment metadata assigned, global attributes installed
on the initial scope, and a 2-second shutdown interval. The pinned
`io.sentry:sentry-android` dependency currently resolves
`io.sentry:sentry-android-ndk`; the build and packaging contract will make that
native dependency a tested requirement rather than an incidental transitive
detail.

Sentry Android owns Java exception handling, NDK signal handling, durable
envelopes, next-launch processing, device/application contexts, and transport.

## Error handling

- Payload validation must finish before the bridge closes an active client.
- A missing or lifecycle-incompatible bridge returns
  `Error.ERR_UNAVAILABLE`.
- An SDK startup exception returns `Error.FAILED`.
- A failed replacement attempts to restore the previous validated
  configuration before returning failure.
- Availability is false for disabled, unconfigured, stale-owner, failed, and
  shut-down states.
- Flush from an inactive or stale owner is a safe no-op at the provider
  boundary.
- Shutdown is idempotent and owner-aware.
- No crash data is synthesized when a platform bridge is unavailable.

## Deterministic testing

### FoundryScript provider tests

- Missing and lifecycle-incompatible bridges return
  `Error.ERR_UNAVAILABLE` for enabled configuration.
- Disabled configuration remains safe without a bridge.
- The owner token is forwarded to configure, availability, flush, and
  shutdown.
- Configure, reconfigure, and shutdown produce the expected state
  transitions.
- Shutdown from a replaced provider does not deactivate the replacement.
- Repeated shutdown is a no-op.
- Capture and flush reject stale or inactive owners.
- Existing event, log, breadcrumb, feedback, and metric behavior remains
  unchanged.

### Swift lifecycle tests

A focused lifecycle coordinator with an injected SDK driver will cover:

- initial activation and active-owner publication;
- equivalent reconfiguration without a restart;
- changed reconfiguration with bounded flush-close-start ordering;
- stale-owner shutdown;
- startup failure followed by previous-client restoration;
- idempotent shutdown;
- release, environment, distribution, global attributes, crash-handler enable,
  and shutdown-timeout option translation.

The production driver will call Sentry Cocoa. Tests will use an in-memory
driver and will not start a real crash handler.

### Java lifecycle tests

The Android bridge will use the equivalent package-private coordinator/driver
seam. Robolectric/JUnit tests will cover the same state transitions and verify
uncaught-exception handling, NDK handling, deployment metadata, global
attributes, and shutdown timeout on `SentryAndroidOptions`.

### Build and package contracts

- Resolve and verify the pinned Sentry Android NDK runtime dependency.
- Preserve required Apple and Android native artifacts in strict packages.
- Assert that distributed core and Sentry addon archives contain no
  crash-trigger script, source, class, callable, or fixture.
- Keep repository validation tooling outside both addon archive roots.
- Run the complete `task test` gate after focused tests.

## Native crash validation

Native crash verification will be deliberate and opt-in. Repository
documentation will define this two-run protocol for each platform:

1. Build and launch a debuggable test export with a non-production Sentry
   project, a unique release, and a known environment.
2. Confirm `FoundryObservability.is_available()` before triggering the crash.
3. Terminate the process with a genuine native fatal signal using repository
   tooling or the platform debugger.
4. Relaunch the same build so the native SDK can process and upload the stored
   crash.
5. Confirm exactly one crash event with the run-A release, environment,
   distribution, global attributes, native stack, device/OS context, and
   application context.
6. Shut down normally, relaunch again, and confirm no duplicate or corrupted
   pending crash is produced.

The validation guide will cover:

| Target | Trigger surface | Delivery check |
| --- | --- | --- |
| macOS | Repository helper invoking a fatal signal against an explicit test PID, or LLDB | Relaunch the exported app and inspect the recovered native crash |
| iOS | Xcode/LLDB against a simulator or physical test device | Relaunch from Xcode and inspect the recovered native crash |
| Android | Repository helper using ADB against an explicit debuggable package/PID, or Android Studio | Relaunch the app and inspect the recovered Java/NDK crash |

Any executable helper will require an explicit destructive confirmation flag,
accept only an explicit PID or package identifier, and print the target before
signalling it. It will not be copied by either addon packaging script.

## Documentation changes

- Update `README.md` to list native crash capture and next-launch delivery as
  Sentry provider capabilities.
- Update `docs/API.md` with the startup boundary, lifecycle guarantees,
  availability/error behavior, metadata behavior, and normal shutdown
  expectations.
- Add a dedicated native crash validation guide with the macOS, iOS, and
  Android two-run procedures and safety warnings.
- Update `BUILD.md` with prerequisites for LLDB/Xcode and ADB/Android Studio
  validation.
- Update `CHANGELOG.md` with explicit cross-platform native crash lifecycle
  support.

## Alternatives considered

### Ship a `bad_code` API like sentry-godot

Rejected because the convenience does not justify placing process-terminating
methods in every consumer build. A warning cannot prevent an accidental
production call, and issue #7 explicitly requires validation without shipping
a crash trigger.

### Documentation-only debugger commands

Rejected as the complete solution because it leaves repeated macOS and Android
checks unnecessarily error-prone. Debugger commands remain the iOS fallback,
while guarded repository helpers standardize targets that can be signalled
externally.

### Native auto-start from duplicate project settings

Rejected because it creates two sources of truth for DSN, release, and
environment. A slightly earlier handler is less valuable than guaranteed
metadata consistency. Configuration from the earliest application startup
hook is the supported boundary.

### Trust the current start/close calls and add only documentation

Rejected because the current process-global SDK can be closed by a stale
provider during replacement, missing bridges collapse to a generic failure,
global attributes do not enrich native crashes, and native dependency and
package-safety expectations are not enforced.
