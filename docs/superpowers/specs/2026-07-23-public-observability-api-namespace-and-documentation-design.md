# Public Observability API Namespace and Documentation

**Date:** 2026-07-23

**Status:** Draft for review

## Goal

Make `foundry.observability` the only public namespace for the pre-release
observability API and fully document the public surface both in source code and
in the API reference.

## Namespace decision

This is a hard pre-release rename. The existing
`games.cafecito.foundryobservability` namespace will not remain as a
compatibility alias.

The namespaces become:

- Core API: `foundry.observability`
- FoundryLib adapter: `foundry.observability.foundrylib`
- Consumer tests: `foundry.observability.tests`

Global class and trait names remain unchanged:

- `FoundryObservability`
- `FoundryObservabilityApi`
- `ObservabilityProvider`
- `ObservabilityLevel`
- `ObservabilityConfig`
- `ObservabilityException`
- `ObservabilityEvent`
- `NullObservabilityProvider`
- `MemoryObservabilityProvider`
- `FoundryLibObservabilitySink`

The adapter remains under the matching
`addons/FoundryObservability/foundrylib/` directory so FoundryScript namespace
directory checks continue to pass.

All current source, tests, examples, scripts, README/API documentation, and
current contributor/build documentation will use the new namespaces. Historical
design and implementation records under `docs/superpowers/` may retain the old
namespace as historical context, but no current consumer-facing file may do so.

## Inline source documentation

Every public declaration in `addons/FoundryObservability/` and its `foundrylib`
subdirectory will receive a concise FoundryScript `##` comment immediately
above the declaration. This includes:

- The `FoundryObservability` autoload, its public lifecycle and capture methods,
  and its public contract marker.
- The `ObservabilityProvider` trait and every required method.
- `ObservabilityLevel`, all severity constants, and `name()`.
- `ObservabilityConfig`, its public fields, constructor, and copy-returning
  accessors.
- `ObservabilityException`, its constructor, and all accessors.
- `ObservabilityEvent`, its constructor, and all accessors.
- `NullObservabilityProvider` and every provider method.
- `MemoryObservabilityProvider`, its public test controls, provider methods,
  `events()`, and `clear()`.
- `FoundryLibObservabilitySink`, its constructor, `emit()`, and `flush()`.

Comments will describe caller-visible contracts: parameter meaning, defaults,
return values, disabled/no-op behavior, error reporting, lifecycle ownership,
and defensive-copy guarantees. Private implementation fields and private
helpers do not need public API documentation.

The inline comments and `docs/API.md` will describe the same names and
semantics. Neither will introduce behavior or a second compatibility API.

## Canonical API reference

`docs/API.md` remains the detailed normative reference. It will be reorganized
around the public namespace and include:

1. Installation, FoundryLib dependency, namespace imports, and autoload setup.
2. A public API index with each class and trait.
3. `ObservabilityLevel` constants and unknown-level formatting.
4. `ObservabilityConfig` fields, constructor defaults, opaque provider options,
   and deep-copy behavior.
5. `ObservabilityException` and `ObservabilityEvent` constructors, accessors,
   payload semantics, and copy behavior.
6. The `ObservabilityProvider` contract, including configuration, availability,
   capture IDs, errors, flush timeouts, and shutdown expectations.
7. Null and memory provider behavior, including deterministic test controls.
8. The `FoundryObservability` autoload API, default state, disabled capture,
   provider replacement, reconfiguration, error state, flush, shutdown, and
   non-recursive failure handling.
9. FoundryLib sink construction, explicit registration, filtering, level
   mapping, rendered messages, timestamps, copied fields, and flush forwarding.
10. Complete examples for startup configuration, message capture, exception
    capture, provider implementation, and FoundryLib logging.

The README will provide the short quick start and link to this reference.
Current build/contributor/changelog documentation will use the new namespace
and describe the hard pre-release rename where relevant.

## Validation

The consumer test project will:

- Import `foundry.observability` and
  `foundry.observability.foundrylib`.
- Use `foundry.observability.tests` for test namespaces.
- Assert that the core and adapter source declare the new namespaces.
- Assert that current project sources no longer contain the old namespace.

The validation scripts will lint the renamed sources, enforce UID companions,
and package the same single core addon. A current-document scan will reject old
namespace references outside historical superpowers records.

The full `task test` gate must pass, including all consumer tests, FoundryScript
lint, UID checks, package checks, CI workflow checks, and repository hygiene.

## Scope boundaries

This change does not add Sentry, native integrations, crash reporting,
persistence, retries, breadcrumbs, attachments, performance transactions, or a
`foundry-cpp` project. It also does not change runtime behavior or global class
names; it changes namespace imports and improves documentation only.
