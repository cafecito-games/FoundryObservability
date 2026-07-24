# Consolidate FoundryLib Observability Integration

**Date:** 2026-07-23

**Status:** Draft for review

## Decision

FoundryLib remains the owner of the general-purpose `foundry.logging`
framework. FoundryObservability owns the adapter that forwards selected
FoundryLib log records into the provider-neutral observability API.

The adapter will live in the main `FoundryObservability` addon. The separate
`FoundryObservabilityFoundryLib` addon will be removed because FoundryLib is a
required project dependency for the supported Foundry game setup.

## Rationale

Logging is a foundational local diagnostic facility. It must remain useful in
headless tools, tests, editor processes, and games before telemetry is
configured or when a provider is unavailable. Its console sink, memory sink,
formatting, level configuration, and logger handles therefore belong in
FoundryLib.

Observability owns provider lifecycle, event normalization, external
telemetry, and future native integrations. The FoundryLib sink is an inbound
integration at that boundary; it must not make FoundryLib depend on telemetry
or a provider SDK.

The resulting dependency direction is one-way:

```text
FoundryLib (foundry.logging) -> no observability dependency
FoundryObservability -> FoundryLib logging adapter
```

This avoids duplicated logging types, dependency cycles, and coupling the
general logging package to network or native provider availability.

## Addon layout

The core addon will contain:

- `addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs`
- the existing observability value types, provider contracts, providers, and
  autoload service

The sink may retain its existing namespace,
`games.cafecito.foundryobservability.foundrylib`, because that namespace
describes the integration source rather than an independently installed addon.
It will live under the matching `foundrylib/` subdirectory so FoundryScript's
directory namespace checks remain warning-free.

The separate directory and plugin descriptor
`addons/FoundryObservabilityFoundryLib/` will be deleted. The core plugin will
remain the only Observability plugin and package payload.

## Runtime behavior

`FoundryLibObservabilitySink` continues to:

- implement `foundry.logging.LogSink`;
- accept a `FoundryObservabilityApi` target and configurable minimum level;
- map all known `LogLevel` values explicitly;
- render templates with `LogFormatter.render_message()`;
- preserve the log timestamp and logger name;
- deep-copy structured fields;
- forward through `capture_event()` without logging provider failures back into
  FoundryLib.

No changes are required to FoundryLib's public logging API. No automatic sink
installation is introduced; game code still explicitly adds the sink to
`Log`.

## Packaging and project contracts

The release archive will contain only `addons/FoundryObservability`, including
the FoundryLib adapter. The addon documentation will state that FoundryLib is
a required dependency. The test project will keep its FoundryLib package pin,
remove the separate integration symlink, and continue testing the adapter
through the core addon.

Contract scripts will:

- lint the core addon with FoundryLib installed;
- validate the sink as part of the core addon;
- validate UIDs and packaging only for the core addon;
- assert that the test project uses FoundryLib while only enabling the core
  Observability plugin and autoload.

## Migration

Consumers that previously installed both Observability addons will install the
core `FoundryObservability` addon and retain their existing FoundryLib package.
The sink's class and namespace remain stable, so consumer source changes are
not required beyond removing the redundant integration addon installation.

## Out of scope

This consolidation does not add Sentry, native Swift/Android bindings, crash
handlers, persistence, retries, user identity, breadcrumbs, attachments, or
performance transactions. It also does not create a `foundry-cpp` project.
