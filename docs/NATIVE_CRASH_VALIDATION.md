# Native Crash Validation

This procedure deliberately terminates a running game process. Use a
non-production Sentry project, a disposable test build, and test player data.
Never point these steps at a production DSN or a player's production process.
The repository helper requires an explicit
`--i-understand-this-will-crash` confirmation and is not included in packages.

## What this validates

A complete check proves that the native SDK:

1. installs its crash handler after either automatic project-settings
   initialization or successful manual provider configuration;
2. records a fatal macOS, iOS, or Android failure;
3. discovers, processes, and sends the durable Run A event after the selected
   readiness path starts the native backend again in Run B; and
4. assigns the expected release, environment, distribution, attributes,
   mechanism, stack, device, OS, and app contexts.

It does not prove capture before successful automatic or manual provider
configuration. That interval is the documented pre-configuration gap.

## Prepare the test build

Build the native artifact for the target and install a debuggable,
non-production game build. Choose one readiness path and use it unchanged for
both runs.

### Automatic readiness

Prefer automatic startup by setting the deployment identity in
`project.foundry`:

```ini
[foundry_observability]

startup/auto_init=true
startup/enabled=true
options/dsn="NON_PRODUCTION_SENTRY_DSN"
options/environment="crash-validation"
options/release="foundry-crash-validation@2026-07-25.1"
options/dist="local-macos"
options/debug_diagnostics=2
options/provider_options={}
```

Use a target-specific `dist`. The automatic path is ready only when startup
reports `initialized` and the provider is available:

```foundryscript
import foundry.observability

func _automatic_crash_validation_ready() -> bool:
	if FoundryObservability.startup_status() \
			!= ObservabilityStartupStatus.INITIALIZED:
		push_error(
				"Automatic native crash reporting did not start: %s — %s"
				% [
					FoundryObservability.startup_status(),
					FoundryObservability.startup_message(),
				],
			)
		return false
	if not FoundryObservability.is_available():
		push_error("Automatic native crash backend is unavailable.")
		return false
	return true


func _run_guarded_automatic_crash_validation() -> void:
	if not _automatic_crash_validation_ready():
		return
	# Invoke the selected platform crash trigger only after this guard.
	pass
```

If the guard returns, stop the procedure. Do not trigger a crash.

### Manual readiness

Manual `configure()` remains useful when validating a custom configuration or
global attributes. Disable automatic startup in `project.foundry`:

```ini
[foundry_observability]

startup/auto_init=false
```

Then configure from the earliest supported hook:

```foundryscript
import foundry.observability
import foundry.observability.sentry

func _configure_manual_crash_validation() -> bool:
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
	if result != Error.OK:
		push_error("Manual native crash reporting did not start: %s" % result)
		return false
	if not FoundryObservability.is_available():
		push_error("Manual native crash backend is unavailable.")
		return false
	return true


func _run_guarded_manual_crash_validation() -> void:
	if not _configure_manual_crash_validation():
		return
	# Invoke the selected platform crash trigger only after this guard.
	pass
```

Successful `configure()` plus availability verifies native backend ownership.
Use the same DSN, release, environment, and distribution in Run A and Run B.
Keep global attributes and other provider options identical as well.

Do not invoke any platform crash trigger when either readiness function returns false.
Record the selected path's result and do not call `flush()` or `shutdown()` as
part of the crash trigger.

## Two-run protocol

Run A is the destructive run:

1. Launch the test build with the selected deployment identity.
2. Establish readiness using the selected path:
   - **Automatic Run A readiness:** require
     `_automatic_crash_validation_ready()` to return true, startup status
     `initialized`, and availability.
   - **Manual Run A readiness:** with `startup/auto_init=false`, require
     `_configure_manual_crash_validation()` to return true after
     `FoundryObservability.configure()` returns `Error.OK` and availability
     verifies native backend ownership.
3. Record the correct PID or Android package, then use the applicable platform
   trigger below.
4. Confirm that the process exits because of the fatal signal.

Run B processes the durable event:

1. Relaunch normally with the exact same deployment identity.
2. Reestablish readiness using the same path:
   - **Automatic Run B readiness:** require
     `_automatic_crash_validation_ready()` to return true, startup status
     `initialized`, and availability.
   - **Manual Run B readiness:** keep `startup/auto_init=false`, call
     `_configure_manual_crash_validation()` again with the identical DSN,
     release, environment, and distribution, then require it to return true.
     This confirms `Error.OK`, availability, and native backend ownership.
3. Keep the game running and network-connected long enough for the native
   backend to discover, process, and send the durable Run A report.
4. Find the event in the non-production Sentry project and inspect all fields
   in the verification checklist below.

After the event arrives, shut down Run B normally. Launch once more and confirm
that the already-delivered event is not duplicated.

## macOS

Build with `task ios:sentry`, launch the test game outside a debugger, and get
its PID from Activity Monitor or `pgrep`. Verify the PID carefully, then run:

```sh
scripts/trigger-test-native-crash macos <pid> --i-understand-this-will-crash
```

The helper sends a fatal signal only to that numeric PID. Relaunch the game
normally and complete Run B.

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
   complete Run B.

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
processes. Relaunch the app normally and complete Run B.

## Verification checklist

Inspect the delivered fatal event rather than only checking that an issue was
created:

- The event belongs to the intended release, environment, and distribution.
- For targeted manual validation,
  `foundry.global_attributes.validation_run` equals the test run identifier.
- The mechanism and fatal level describe a native crash.
- The stack and crashed thread identify the signaled process.
- Device, OS, and app contexts match the test target.
- The event timestamp belongs to Run A even though delivery occurred on Run B.
- A clean shutdown and subsequent relaunch do not resend the event.

When validation is complete, remove the disposable build and test credentials,
stop using the test release, and delete or archive test events according to the
non-production project's retention policy.
