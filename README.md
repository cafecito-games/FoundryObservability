# FoundryObservability

FoundryObservability is a FoundryScript addon for game projects that will
provide a stable in-game observability API and integrations with error-reporting
providers such as Sentry.

## Status

The repository is bootstrapped for addon packaging, FoundryScript validation,
consumer-project testing, and releases. The current autoload is intentionally
empty; provider integrations and observability behavior will be added in later
changes.

## Installation

Copy `addons/FoundryObservability` into the `addons/` directory of a Foundry
game project, enable the **FoundryObservability** editor plugin, and restart or
reload the project. The plugin registers the **FoundryObservability** autoload.

The public namespace is `games.cafecito.foundryobservability`.

## Development

The local validation gate is:

```sh
task test
```

See [BUILD.md](BUILD.md) for prerequisites and individual commands. See
[CONTRIBUTING.md](CONTRIBUTING.md) for change and review expectations.
