# FoundryObservability

FoundryObservability is a FoundryScript addon for game projects that provides a
stable, provider-neutral observability API. It is designed to become the common
boundary for logging, error reporting, crash reporting, and future providers.

## Status

The first core slice is available now:

- Typed messages, exceptions, events, configuration, and severity levels.
- A null provider by default and an in-memory provider for tests and local work.
- Provider replacement, flush, failure reporting, and idempotent shutdown.
- Provider-neutral global tags, nested contexts, explicit application identity,
  and isolated event-local scope with native Apple/Android Sentry mapping.
- Automatic engine error, warning, script-error, shader-error, fatal, and
  output-message capture with independent event, breadcrumb, and log policies.
- Provider-neutral ordered event, structured-log, and metric processors,
  configurable redaction, deterministic signal-local sampling, independent
  rate limits, and payload-free processing diagnostics.
- First-class structured logs with level filtering, optional rate limiting, and
  global/per-record scalar attributes.
- Explicit player feedback capture with validation, optional identity, and
  optional association to a returned event ID.
- First-class counters, gauges, and distributions with validation, filtering,
  deterministic sampling, units, and scalar attributes.
- Provider-neutral structured exception frames and bounded source context for
  macOS, iOS, and Android Sentry delivery, while retaining formatted-stack
  fallback compatibility. Frame data is caller-supplied; the addon does not
  automatically acquire engine locals or apply redaction unless a policy is
  configured.
- Provider-neutral controls for default-enabled native main-thread hang
  diagnostics on macOS and iOS and ANR diagnostics on Android when a supporting
  provider is configured; the included Sentry provider implements those
  platform mappings, with enable, timeout, and Android thread-dump controls.
- Native crash capture through Sentry's Apple and Android SDKs, including
  previous launch delivery after the game restarts.
- A FoundryLib `LogSink` adapter included in the core addon.
- An optional `FoundryObservabilitySentry` provider addon backed by
  Foundry-Swift/Sentry Cocoa on Apple platforms and Sentry Android on Android.

The Sentry addon is optional and keeps the core addon safe to use on platforms
without the native Sentry bridge. Its native artifacts support iOS
device/simulator, macOS arm64, and Android exports. On Apple, it requires the
compatible FoundrySwift companion addon described below.

## Installation

Copy `addons/FoundryObservability` into the `addons/` directory of a Foundry
game project, enable the **FoundryObservability** editor plugin, and restart or
reload the project. The plugin registers the **FoundryObservability** autoload.

FoundryLib is a required dependency of the addon because the included
`FoundryLibObservabilitySink` integrates with FoundryLib's structured logging.
Install the FoundryLib package before importing or enabling the addon.

For Sentry reporting, also install the `FoundryObservabilitySentry` sibling
addon. Apple projects must install the exact compatible FoundrySwift
`0.1.0-alpha.2` companion release with Anvil:

```toml
[packages.FoundrySwift]
source = "github-release"
repo = "cafecito-games/Foundry-Swift"
version = "0.1.0-alpha.2"
asset = "FoundrySwift-0.1.0-alpha.2.zip"
checksum = "51fedac51e9157430df2e3802dbb0c827c5d35500af418bb1fcb04114d040ffb"
source_path = "addons/FoundrySwift"
```

```sh
anvil pkg install
```

The companion's `FoundrySwiftEmbed` extension owns the single shared
FoundrySwift runtime embedded in Apple exports. The Sentry extension links that
runtime but intentionally keeps its own dependency maps empty, preventing
duplicate framework embedding. Android uses the Sentry Android AAR and does not
require the FoundrySwift companion addon. Configure
`SentryObservabilityProvider` as shown in [docs/API.md](docs/API.md).

The public namespace is `foundry.observability`.

## Automatic Sentry startup

For games using the optional Sentry addon, configure early startup in
`project.foundry`:

```ini
[foundry_observability]

startup/auto_init=true
startup/enabled=true
options/dsn="NON_PRODUCTION_SENTRY_DSN"
options/environment="production"
options/release="oakhaven@1.0.0"
options/dist="store-macos"
options/debug_diagnostics=2
options/provider_options={
"send_default_pii": false
}
```

`provider_options` must contain data only; see [docs/API.md](docs/API.md) for
the accepted types and bounds. The fully qualified automatic-startup control is
`foundry_observability/startup/auto_init`. Order `FoundryObservability` as the
earliest startup hook that needs observability. Startup runs synchronously during
`FoundryObservability` autoload construction, so it completes before the main
scene and before later-ordered autoloads. Those later hooks may capture
immediately. An autoload ordered before `FoundryObservability` is outside this
guarantee.

Do not call `configure()` after successful automatic Sentry startup unless replacing the active startup provider is intentional.
Configuring a different provider shuts down and replaces the provider created
by automatic startup.

## Provider-neutral manual quick start

As a provider-neutral manual alternative, configure a provider during game
startup and emit typed events through the autoload:

```foundryscript
import foundry.observability

var policy: ObservabilityRedactionPolicy = ObservabilityRedactionPolicy.new([
	ObservabilityRedactionRule.sensitive_key("password"),
	ObservabilityRedactionRule.replace_text(
			PackedStringArray(["**"]),
			"[0-9]{3}-[0-9]{2}-[0-9]{4}",
			"[ssn]",
		),
])
var config: ObservabilityConfig = ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "production",
		p_release = "1.0.0",
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_log_mask = ObservabilityCaptureMask.ALL_ERRORS,
		p_max_breadcrumbs = 100,
		p_automatic_message_filter_prefixes = PackedStringArray(
				["FoundryObservability: "],
		),
		p_event_processors = [func(event: ObservabilityEvent) -> Variant:
			if event.level() < ObservabilityLevel.WARN:
				return null
			return event,
		],
		p_log_processors = [],
		p_metric_processors = [],
		p_event_limits = ObservabilitySignalLimits.new(5, 1000, 20, 10000),
		p_log_limits = ObservabilitySignalLimits.new(100, 0, 1000, 10000),
		p_metric_limits = ObservabilitySignalLimits.new(100, 0, 1000, 10000),
		p_redaction_policy = policy,
	)
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, config)
FoundryObservability.set_tag("region", "iad")
FoundryObservability.set_context("match", {
		"mode": "ranked",
		"party": {"size": 4},
})
FoundryObservability.set_user(ObservabilityUser.new(
		p_application_user_id = "player-7",
		p_display_name = "Mina",
))
FoundryObservability.capture_message("game started")
var local_scope := ObservabilityScope.new()
local_scope.set_tag("round", "final")
FoundryObservability.capture_message(
		"boss phase started",
		ObservabilityLevel.INFO,
		{},
		local_scope,
)
FoundryObservability.capture_log(
		"match started",
		ObservabilityLevel.INFO,
		&"matchmaking",
		-1,
		{"region": "iad"},
)
FoundryObservability.capture_counter("match.started", 1, {"region": "iad"})
FoundryObservability.capture_gauge("players.active", 12.0, "player")
FoundryObservability.capture_distribution(
		"matchmaking.duration",
		187.5,
		"millisecond",
		{"region": "iad"},
)
FoundryObservability.capture_feedback(ObservabilityFeedback.new(
		p_message = "The tutorial was confusing.",
))
var diagnostic: ObservabilityProcessingDiagnostic? = (
		FoundryObservability.last_processing_diagnostic()
	)
if diagnostic != null:
	print("%s: %s" % [
		diagnostic.processing_signal(),
		diagnostic.outcome(),
	])
FoundryObservability.clear_breadcrumbs()
```

The event processor above receives an already-redacted immutable event and may
return that event, a replacement `ObservabilityEvent`, or `null` to drop it.
Log and metric processors are configured separately. Redaction runs again after
processors, and event, log, and metric sampling and limits never consume one
another's capacity. Processing diagnostics contain only stable outcome
metadata, never the captured payload.

Persistent diagnostic attachments use provider-local handles:

```foundryscript
var attachment := ObservabilityAttachment.from_path(
		"user://logs/foundry.log",
		"foundry.log",
		"text/plain",
)
if attachment != null:
	var handle := FoundryObservability.add_attachment(attachment)
	FoundryObservability.capture_message("save failed", ObservabilityLevel.ERROR)
	for failure: ObservabilityAttachmentFailure in \
			FoundryObservability.last_attachment_failures():
		print("%s: %s" % [failure.filename(), failure.reason()])
	if not handle.is_empty():
		FoundryObservability.remove_attachment(handle)
```

`attach_game_log`, `attach_screenshot`, and `attach_scene_tree` are independent
false-by-default configuration opt-ins. `max_attachment_bytes` defaults to
20 MiB per attachment; setting it to zero disables attachment delivery while
still allowing attachment management.

See the [identity privacy guidance](docs/API.md#observabilityuser) before
supplying optional identifying or contact fields.

Successful enabled configuration automatically installs the engine logger.
By default, errors, script errors, and shader errors become events; every
diagnostic category and ordinary output message becomes a breadcrumb; and no
automatic structured logs are emitted. The example opts all error categories
into structured logs as well. Each destination can be configured independently
or automatic capture can be disabled entirely. `max_breadcrumbs` defaults to
100, while zero disables breadcrumb storage; `clear_breadcrumbs()` explicitly
clears the current trail. Event-local scope overrides matching global tags and
contexts for one capture without changing later events.

`MemoryObservabilityProvider` is intended for tests and local integration work.
The Sentry provider is optional and requires an export containing its native
Apple framework or Android AAR on supported platforms.

### Native crash lifecycle

For native crash reporting, prefer the project-settings startup above. Manual
`SentryObservabilityProvider` configuration remains available for targeted
integration. A successful configuration starts Sentry's process-wide crash
handlers; a crash is persisted by the native SDK and normally delivered on the
previous launch's next startup. Crashes before successful configuration cannot
be recovered by the addon.

Require `FoundryObservability.startup_status()` to report `initialized` and
then check `FoundryObservability.is_available()` before considering automatic
reporting active. For manual setup, check the return value from
`FoundryObservability.configure()` before availability.
Use [docs/NATIVE_CRASH_VALIDATION.md](docs/NATIVE_CRASH_VALIDATION.md) for the
destructive, non-production macOS, iOS, and Android validation procedure.

See [docs/API.md](docs/API.md) for the complete contract and FoundryLib sink
setup.

## Development

The local validation gate is:

```sh
task test
```

See [BUILD.md](BUILD.md) for prerequisites and individual commands. See
[CONTRIBUTING.md](CONTRIBUTING.md) for change and review expectations.
