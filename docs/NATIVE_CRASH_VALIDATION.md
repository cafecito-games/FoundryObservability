# Native Crash Validation

This procedure deliberately terminates a running game process. Use a
non-production Sentry project, a disposable test build, and test player data.
Never point these steps at a production DSN or a player's production process.
The repository helper requires an explicit
`--i-understand-this-will-crash` confirmation and is not included in packages.

## What this validates

A complete check proves that the native SDK:

1. installs its crash handler after the earliest supported configuration;
2. records a fatal macOS, iOS, or Android failure;
3. sends the stored event from the previous launch after relaunch; and
4. assigns the expected release, environment, distribution, attributes,
   mechanism, stack, device, OS, and app contexts.

It does not prove capture before provider configuration. That interval is the
documented pre-configuration gap.

## Prepare the test build

Build the native artifact for the target and install a debuggable,
non-production game build. Configure the Sentry provider in the earliest
startup hook that has access to the DSN and deployment metadata:

```foundryscript
import foundry.observability
import foundry.observability.sentry

var config := ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "crash-validation",
		p_release = "foundry-crash-validation@2026-07-25.1",
		p_dist = "local-macos",
		p_global_attributes = {"validation_run": "issue-7"},
		p_provider_options = {"dsn": "NON_PRODUCTION_SENTRY_DSN"},
	)
var result: int = FoundryObservability.configure(
		SentryObservabilityProvider.new(),
		config,
	)
if result != Error.OK or not FoundryObservability.is_available():
	push_error("Native crash reporting did not start: %s" % result)
```

Use a target-specific `p_dist`, but keep the release, environment, distribution,
DSN, and global attributes identical for the crash run and its recovery launch.
Record the configure result and require `is_available()` before triggering the
crash. Do not call `flush()` or `shutdown()` as part of the crash trigger.

## Two-run protocol

Run 1 is the destructive run:

1. Launch the test build and wait until enabled configuration returns
   `Error.OK`.
2. Confirm `FoundryObservability.is_available()` is true.
3. Record the correct PID or Android package, then use the applicable platform
   trigger below.
4. Confirm that the process exits because of the fatal signal.

Run 2 delivers the previous launch:

1. Relaunch normally with the exact same deployment identity.
2. Configure the Sentry provider at the same early startup boundary.
3. Keep the game running and network-connected long enough for the native SDK
   to process and send the stored report.
4. Find the event in the non-production Sentry project and inspect all fields
   in the verification checklist below.

After the event arrives, shut down Run 2 normally. Launch once more and confirm
that the already-delivered event is not duplicated.

## macOS

Build with `task ios:sentry`, launch the test game outside a debugger, and get
its PID from Activity Monitor or `pgrep`. Verify the PID carefully, then run:

```sh
scripts/trigger-test-native-crash macos <pid> --i-understand-this-will-crash
```

The helper sends a fatal signal only to that numeric PID. Relaunch the game
normally and complete Run 2.

## iOS simulator and physical device

There is no shipped iOS crash API or production helper. Use an Xcode debug build
and LLDB for controlled validation:

1. Install and launch the game from Xcode on the simulator or physical device.
2. Wait for successful provider configuration and availability.
3. Pause the process in Xcode and enter `process signal SIGABRT` in the LLDB
   console.
4. If LLDB stops on the signal, press Continue so the app receives it. Confirm
   that the process exits, then end the debug session.
5. Relaunch from the simulator or device Home Screen without the debugger and
   complete Run 2.

A debugger can intercept fatal signals and alter crash behavior. Treat this as
a controlled integration check; the recovery launch should not be attached to
LLDB. On a physical iOS device, keep the device unlocked and connected while
Xcode installs the test build, but relaunch the recovery run from the device.

## Android

Build with `task android:sentry`, install a debuggable export, connect the
emulator or device, and verify it appears in `adb devices`. Supply the exact
application ID:

```sh
scripts/trigger-test-native-crash android <debuggable-package> --i-understand-this-will-crash
```

The helper verifies `run-as` access before using ADB to signal the package's
process. It rejects non-debuggable applications and missing or ambiguous
processes. Relaunch the app normally and complete Run 2.

## Verification checklist

Inspect the delivered fatal event rather than only checking that an issue was
created:

- The event belongs to the intended release, environment, and distribution.
- `foundry.global_attributes.validation_run` equals the test run identifier.
- The mechanism and fatal level describe a native crash.
- The stack and crashed thread identify the signaled process.
- Device, OS, and app contexts match the test target.
- The event timestamp belongs to Run 1 even though delivery occurred on Run 2.
- A clean shutdown and subsequent relaunch do not resend the event.

When validation is complete, remove the disposable build and test credentials,
stop using the test release, and delete or archive test events according to the
non-production project's retention policy.
