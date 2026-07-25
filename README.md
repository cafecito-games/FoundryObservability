# FoundryObservability

FoundryObservability is a FoundryScript addon for game projects that provides a
stable, provider-neutral observability API. It is designed to become the common
boundary for logging, error reporting, crash reporting, and future providers.

## Status

The first core slice is available now:

- Typed messages, exceptions, events, configuration, and severity levels.
- A null provider by default and an in-memory provider for tests and local work.
- Provider replacement, flush, failure reporting, and idempotent shutdown.
- Automatic engine error, warning, script-error, shader-error, fatal, and
  output-message capture with independent event, breadcrumb, and log policies.
- First-class structured logs with level filtering, optional rate limiting, and
  global/per-record scalar attributes.
- Explicit player feedback capture with validation, optional identity, and
  optional association to a returned event ID.
- First-class counters, gauges, and distributions with validation, filtering,
  deterministic sampling, units, and scalar attributes.
- Provider-neutral structured exception frames and bounded source context for
  macOS, iOS, and Android Sentry delivery, while retaining formatted-stack
  fallback compatibility. Frame data is caller-supplied; the addon does not
  automatically acquire engine locals or redact supplied source text or values.
- Provider-neutral controls for default-enabled native main-thread hang
  diagnostics on macOS and iOS and ANR diagnostics on Android when a supporting
  provider is configured; the included Sentry provider implements those
  platform mappings, with enable, timeout, and Android thread-dump controls.
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

## Quick start

Configure a provider during game startup and emit typed events through the
autoload:

```foundryscript
import foundry.observability

var config := ObservabilityConfig.new(
		p_enabled = true,
		p_environment = "production",
		p_release = "1.0.0",
		p_global_attributes = {},
		p_provider_options = {},
		p_automatic_log_mask = ObservabilityCaptureMask.ALL_ERRORS,
	)
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, config)
FoundryObservability.capture_message("game started")
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
```

Successful enabled configuration automatically installs the engine logger.
By default, errors, script errors, and shader errors become events; every
diagnostic category and ordinary output message becomes a breadcrumb; and no
automatic structured logs are emitted. The example opts all error categories
into structured logs as well. Each destination can be configured independently
or automatic capture can be disabled entirely.

`MemoryObservabilityProvider` is intended for tests and local integration work.
The Sentry provider is optional and requires an export containing its native
Apple framework or Android AAR on supported platforms.

See [docs/API.md](docs/API.md) for the complete contract and FoundryLib sink
setup.

## Development

The local validation gate is:

```sh
task test
```

See [BUILD.md](BUILD.md) for prerequisites and individual commands. See
[CONTRIBUTING.md](CONTRIBUTING.md) for change and review expectations.
