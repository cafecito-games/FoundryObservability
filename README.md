# FoundryObservability

FoundryObservability is a FoundryScript addon for game projects that provides a
stable, provider-neutral observability API. It is designed to become the common
boundary for logging, error reporting, crash reporting, and future providers.

## Status

The first core slice is available now:

- Typed messages, exceptions, events, configuration, and severity levels.
- A null provider by default and an in-memory provider for tests and local work.
- Provider replacement, flush, failure reporting, and idempotent shutdown.
- A FoundryLib `LogSink` adapter included in the core addon.

Sentry, native Swift/Android bindings, crash detection, and crash reporting are
not included yet. They will be built behind this stable core contract.

## Installation

Copy `addons/FoundryObservability` into the `addons/` directory of a Foundry
game project, enable the **FoundryObservability** editor plugin, and restart or
reload the project. The plugin registers the **FoundryObservability** autoload.

FoundryLib is a required dependency of the addon because the included
`FoundryLibObservabilitySink` integrates with FoundryLib's structured logging.
Install the FoundryLib package before importing or enabling the addon.

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
```

`MemoryObservabilityProvider` is intended for tests and local integration work.
The first production provider will be added separately.

See [docs/API.md](docs/API.md) for the complete contract and FoundryLib sink
setup.

## Development

The local validation gate is:

```sh
task test
```

See [BUILD.md](BUILD.md) for prerequisites and individual commands. See
[CONTRIBUTING.md](CONTRIBUTING.md) for change and review expectations.
