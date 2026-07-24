# FoundryObservability

FoundryObservability is a FoundryScript addon for game projects that provides a
stable, provider-neutral observability API. It is designed to become the common
boundary for logging, error reporting, crash reporting, and future providers.

## Status

The first core slice is available now:

- Typed messages, exceptions, events, configuration, and severity levels.
- A null provider by default and an in-memory provider for tests and local work.
- Provider replacement, flush, failure reporting, and idempotent shutdown.
- First-class structured logs with level filtering, optional rate limiting, and
  global/per-record scalar attributes.
- A FoundryLib `LogSink` adapter included in the core addon.
- An optional `FoundryObservabilitySentry` provider addon backed by
  Foundry-Swift/Sentry Cocoa on Apple platforms and Sentry Android on Android.

The Sentry addon is independently installable and keeps the core addon safe to
use on platforms without the native Sentry bridge. Its native artifacts support
iOS device/simulator, macOS arm64, and Android exports.

## Installation

Copy `addons/FoundryObservability` into the `addons/` directory of a Foundry
game project, enable the **FoundryObservability** editor plugin, and restart or
reload the project. The plugin registers the **FoundryObservability** autoload.

FoundryLib is a required dependency of the addon because the included
`FoundryLibObservabilitySink` integrates with FoundryLib's structured logging.
Install the FoundryLib package before importing or enabling the addon.

For Sentry reporting, also install the `FoundryObservabilitySentry` sibling
addon. The addon supplies the Apple framework or Android AAR for the selected
export platform; the Apple build uses the shared `Foundry-Swift` dependency.
Configure `SentryObservabilityProvider` as shown in [docs/API.md](docs/API.md).

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
```

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
