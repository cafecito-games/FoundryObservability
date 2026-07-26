# Early Initialization and Deployment Configuration Design

## Summary

FoundryObservability will provide a supported project-settings startup path that
configures the optional Sentry provider during construction of the
`FoundryObservability` autoload. Initialization will therefore complete before
the main scene and before any autoload ordered after `FoundryObservability`.

The core service will own startup, configuration, reconfiguration, and
shutdown. It will load the Sentry provider lazily from the optional addon and
continue to expose only provider-neutral lifecycle and diagnostic methods.
Missing settings, a missing provider addon, an unavailable native bridge, and
explicitly skipped runtime contexts will all leave the null provider active
with a programmatically distinguishable startup status.

This follows the useful parts of
[Sentry Godot's startup model](https://github.com/getsentry/sentry-godot):
project settings, environment-variable fallbacks, deployment defaults, early
automatic initialization, editor/development skip controls, and safe no-ops.
It does not copy Sentry Godot's process-global public API into the
provider-neutral core.

## Goals

- Initialize a configured Sentry provider during the
  `FoundryObservability` autoload's construction.
- Capture events emitted from the main scene and later autoloads immediately
  after the core autoload becomes available.
- Start the existing Apple or Android native crash lifecycle early enough to
  process durable crash data from the preceding launch before the main scene.
- Configure enabled state, DSN, environment, release, distribution, debug
  diagnostics, and additional provider options through project settings.
- Resolve useful release and environment defaults while preserving explicit
  project-setting and environment-variable overrides.
- Support safe skips for the editor, editor-launched game runs, and debug
  exports.
- Make automatic initialization, explicit reinitialization, manual
  reconfiguration, and shutdown idempotent.
- Keep missing, invalid, disabled, skipped, and unsupported configurations safe
  and observable.
- Document the exact startup ordering guarantee and its limits.

## Non-goals

- Add a general provider registry or arbitrary project-setting provider
  selection.
- Start a native SDK independently of `FoundryObservability.configure()`.
- Duplicate deployment configuration in the native bridge.
- Capture failures that occur before the Foundry extension and autoload system
  has loaded.
- Change native crash persistence, upload, symbolication, or transport.
- Treat DSNs as secrets. Environment-variable fallback is supported for
  deployment convenience, but normal Sentry DSNs are public client keys.
- Permit callbacks, resources, or other executable values in project-setting
  provider options.

## Architecture

### Core-owned startup

`FoundryObservability._init()` will first establish its existing disabled null
provider state and then call `initialize_from_project_settings()`. That method
will:

1. Read and validate the startup settings.
2. Detect whether the current runtime must be skipped.
3. Resolve DSN and deployment metadata.
4. Lazily load the Sentry provider script from the optional addon.
5. Create or reuse the startup-owned provider instance.
6. Call the existing provider-neutral `configure()` method.
7. Record a startup status, diagnostic message, and error result.

The core addon will not import the Sentry namespace or require the Sentry addon
to parse. The default loader will use the conventional optional-addon path:

```text
res://addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs
```

The loaded object must satisfy `ObservabilityProvider`. A missing script,
failed load, invalid instance, or unsupported platform bridge produces a safe
unavailable result without replacing the null provider.

The startup-owned provider instance is retained. Repeated
`initialize_from_project_settings()` calls reuse it, allowing the existing
Sentry lifecycle coordinator to recognize equivalent configuration and avoid
restarting native crash handlers. Changed configuration follows the existing
bounded close/start/restore behavior.

### Settings resolution

A focused startup settings resolver will own project-setting reads, runtime
classification, defaults, validation, and provider-option merging. Keeping
resolution separate from the service lifecycle makes precedence and skip
behavior deterministic to test.

The core editor plugin will register the following settings:

| Project setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `foundry_observability/startup/auto_init` | `bool` | `true` | Run the automatic project-settings startup path. |
| `foundry_observability/startup/enabled` | `bool` | `true` | Enable capture when startup configuration is valid. |
| `foundry_observability/startup/skip_editor_play` | `bool` | `false` | Skip a game launched from an editor build. |
| `foundry_observability/startup/skip_debug_exports` | `bool` | `false` | Skip debug exports after editor-play classification. |
| `foundry_observability/options/dsn` | `String` | empty | Sentry DSN. |
| `foundry_observability/options/environment` | `String` | empty | Explicit environment override. |
| `foundry_observability/options/release` | `String` | empty | Explicit release override. |
| `foundry_observability/options/dist` | `String` | empty | Optional distribution value. |
| `foundry_observability/options/debug_diagnostics` | enum | `Auto` | Resolve provider debug output and startup diagnostics as Off, On, or Auto. |
| `foundry_observability/options/provider_options` | `Dictionary` | empty | Additional data-only Sentry provider options. |

The editor process itself is always skipped. It loads tool scripts and project
metadata but is not a game execution context. `skip_editor_play` governs a
running game whose binary has the `editor` feature.
`skip_debug_exports` governs remaining debug builds. The checks occur in that
order so each skip has a stable diagnostic status.

### Precedence and defaults

DSN precedence:

1. Nonempty `foundry_observability/options/dsn`.
2. Nonempty `SENTRY_DSN`.
3. Missing configuration.

Release precedence:

1. Nonempty `foundry_observability/options/release`.
2. Nonempty `SENTRY_RELEASE`.
3. `{app_name}@{app_version}`, using
   `application/config/name` and `application/config/version`.

An absent application name resolves to `Unknown Foundry project`; an absent
version resolves to `noversion`. Literal `{app_name}` and `{app_version}`
tokens in an explicit project-setting release are expanded as well.

Environment precedence:

1. Nonempty `foundry_observability/options/environment`.
2. Nonempty `SENTRY_ENVIRONMENT`.
3. Detected runtime context.

Detected environments use these stable values in precedence order:

| Runtime context | Environment |
| --- | --- |
| Dedicated server | `dedicated_server` |
| Editor process | `editor_dev` |
| Game launched from editor | `editor_dev_run` |
| Debug export | `export_debug` |
| Release export | `export_release` |

Distribution has no derived default. A configured value is forwarded
unchanged.

The resolver starts with a deep copy of
`foundry_observability/options/provider_options`. It then assigns the resolved
`dsn` and `debug` values, so typed settings cannot be contradicted by duplicate
dictionary keys. `debug` is true for On, false for Off, and follows
`Engine.is_debug_build()` for Auto. The dictionary must contain data values
that can be serialized through the existing provider and native bridge
boundaries.

### Provider-neutral API

`FoundryObservabilityApi` will add:

```foundryscript
abstract func initialize_from_project_settings() -> int
abstract func startup_status() -> StringName
abstract func startup_message() -> String
```

`initialize_from_project_settings()` returns `Error.OK` for successful,
disabled, or intentionally skipped startup. It returns:

| Condition | Error |
| --- | --- |
| Missing DSN | `Error.ERR_UNCONFIGURED` |
| Invalid enum or provider-options value | `Error.ERR_INVALID_PARAMETER` |
| Missing or invalid provider script | `Error.ERR_UNAVAILABLE` |
| Missing or incompatible native bridge | Provider result, normally `Error.ERR_UNAVAILABLE` |
| Native validation or startup failure | Provider result |

`startup_status()` returns one of these stable `StringName` values:

- `not_started`
- `initialized`
- `disabled`
- `skipped_editor`
- `skipped_editor_play`
- `skipped_debug`
- `missing_dsn`
- `provider_unavailable`
- `configuration_failed`

`startup_message()` supplies a concise human-readable explanation associated
with the latest startup attempt. These diagnostics describe the startup path;
they do not replace `provider_name()`, `is_enabled()`, `is_available()`, or
`last_error()` as the current service state.

Debug diagnostics control whether the startup message is printed. Status and
message accessors remain populated even when printing is disabled.

## Lifecycle Semantics

### Automatic initialization

Construction starts from the disabled null-provider state. Automatic
initialization is attempted once when `auto_init` is true. A successful call
returns only after the provider and native bridge report configuration success,
so later startup code can capture immediately.

When `auto_init` or `enabled` is false, the service remains a disabled safe
no-op and records `disabled`. No Sentry provider or native bridge is loaded.

### Explicit reinitialization

Calling `initialize_from_project_settings()` again rereads settings and
environment variables. It reuses the startup provider object. Equivalent
native configuration does not restart the SDK; changed configuration uses the
existing owner-aware replacement lifecycle.

A failed candidate configuration leaves the currently working provider
unchanged, matching the existing `configure()` transaction.

### Manual configuration

Manual `configure(provider, config)` remains authoritative. It may replace,
disable, or reconfigure the automatically selected provider. A later explicit
`initialize_from_project_settings()` call intentionally selects the retained
startup provider again using the latest settings.

Startup status continues to describe the latest startup-settings attempt and
does not claim to describe later manual configuration.

### Shutdown and restart

`shutdown()` retains its current repeated-call no-op behavior and restores the
disabled null provider. It must not discard the retained startup provider
object. A later explicit project-settings initialization can reactivate that
provider; the provider and native lifecycle already reset their shutdown flags
on successful configuration.

No shutdown or reconfiguration path deletes the native SDK's durable crash
data. A crash stored by run A can therefore be discovered and delivered when
the native backend starts during run B's autoload initialization.

## Ordering Guarantee

The supported guarantee is:

1. The Foundry extension system loads required native extensions.
2. The scene tree constructs project autoloads in configured order.
3. `FoundryObservability` resolves settings and synchronously configures the
   provider during its `_init()`.
4. Autoloads ordered after `FoundryObservability` are constructed.
5. The main scene is constructed and enters the tree.

Application code in step 4 or 5 may capture events immediately. An autoload
ordered before `FoundryObservability`, a native failure before extension
loading, or a process failure before the core autoload is constructed remains
outside this guarantee.

Projects that need startup capture from another autoload must order
`FoundryObservability` before it. The editor plugin's registered autoload is
the supported default and precedes the main scene.

## Error Handling

- Every startup failure leaves the null provider or the previously working
  provider active.
- Missing DSN never attempts to load or start the provider.
- Invalid debug enum and non-dictionary provider options fail validation before
  provider creation.
- Missing optional Sentry addon and unsupported native platforms report
  unavailable rather than throwing through autoload construction.
- Whitespace-only DSN, environment, and release inputs are treated as empty.
- Provider and native error codes remain available through the initialization
  result and `last_error()`.
- Diagnostic printing never changes startup success or failure.
- Automatic logger installation still occurs only after successful enabled
  provider configuration.

## Testing

### Startup settings resolver

Deterministic tests will cover:

- project-setting, environment-variable, and default precedence;
- application-name and version release formatting;
- dedicated-server, editor, editor-play, debug-export, and release-export
  environment classification;
- Off, On, and Auto debug resolution;
- editor-play and debug-export skip precedence;
- deep-copy and typed-key precedence for provider options;
- whitespace-only values;
- invalid enum and provider-options input.

### Core service

Tests will register the existing fake Sentry bridge, construct a real
`FoundryObservability` service, and verify:

- startup configuration completes during construction;
- an event captured immediately after construction reaches the fake bridge;
- disabled, skipped, and missing-DSN startup remains a safe no-op;
- provider script or bridge unavailability produces the expected status;
- repeated project-settings initialization reuses the startup provider;
- equivalent initialization does not duplicate native startup;
- changed settings reconfigure the active provider;
- failed reconfiguration preserves the working provider;
- manual provider replacement remains authoritative;
- repeated shutdown is safe;
- explicit initialization after shutdown succeeds.

Tests that temporarily modify `ProjectSettings` or `SENTRY_*` variables will
restore every value in teardown.

### Wiring, packaging, and native contracts

- Project-wiring tests verify the setting names, defaults, and
  `FoundryObservability` autoload.
- Package tests verify the core package does not embed the optional Sentry
  provider while the Sentry package retains the conventional provider path.
- Existing Swift and Java lifecycle tests continue to prove that native startup
  enables crash handling, applies deployment metadata before activation,
  preserves durable data, handles equivalent and changed reconfiguration, and
  shuts down idempotently.
- Existing destructive native-crash validation remains the end-to-end proof
  that a crash from one run is delivered after the next native startup.

## Documentation

`README.md` will show the minimal project-settings setup and the earliest safe
capture point. `docs/API.md` will document:

- all startup settings and their defaults;
- environment variables and precedence;
- derived release and environment values;
- skip behavior;
- startup status and error interpretation;
- manual override and reinitialization semantics;
- autoload ordering requirements;
- the boundary between current-run startup capture and previous-run native
  crash delivery.

`docs/NATIVE_CRASH_VALIDATION.md` will identify project-settings
auto-initialization as the preferred validation path while retaining manual
configuration as a supported explicit alternative.

## Acceptance Criteria Mapping

| Issue requirement | Design coverage |
| --- | --- |
| Initialize before the main scene | Synchronous autoload `_init()` startup and documented ordering guarantee. |
| Capture events immediately after initialization | Configure returns only after provider activation; fake-bridge construction test. |
| Report a pre-scene crash after restart | Existing durable native lifecycle starts during next-run autoload construction. |
| Configure deployment metadata and provider options | Typed project settings, environment fallbacks, derived defaults, and opaque options dictionary. |
| Safe missing or invalid configuration | Null-provider fallback, error result, startup status, and diagnostic message. |
| Skip editor or development contexts | Always-skip editor plus explicit editor-play and debug-export controls. |
| Idempotent initialization, reconfiguration, and shutdown | Retained startup provider, existing lifecycle equivalence checks, transactional configure, and repeated shutdown. |
| Preserve provider neutrality | Core lifecycle API remains typed to `ObservabilityProvider`; Sentry is loaded only by optional path. |
| macOS, iOS, and Android | Shared FoundryScript startup feeds the existing Apple and Android native lifecycle implementations. |
