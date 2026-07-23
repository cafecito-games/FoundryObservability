# FoundryObservability Bootstrap Design

**Date:** 2026-07-23

**Status:** Approved

## Goal

Bootstrap `FoundryObservability` as an installable FoundryScript addon for game
projects. The repository will establish the addon packaging, test-project
wiring, documentation, local validation, and CI/CD release path without
committing to an observability API or provider implementation yet.

## Scope

The initial repository will provide:

- A minimal `FoundryObservability` addon and autoload.
- A public FoundryScript contract marker for future API additions.
- A consumer test project with strict FoundryScript settings and FoundryLib
  project-wiring tests.
- Repository hygiene, local task commands, and package validation.
- Pull-request CI and a manually dispatched semver release workflow.
- Documentation for the current bootstrap and future provider direction.

The initial repository will not provide Sentry integration, another provider
integration, event capture, error serialization, network transport, buffering,
retry behavior, or any other observability semantics.

## Architecture

The repository follows the addon layout used by AuthenticationKit:

```text
addons/FoundryObservability/
  plugin.cfg
  export_plugin.fs
  FoundryObservability.fs
  FoundryObservabilityApi.fs
test_project/
  project.foundry
  packages.toml
  packages.lock
  tests/project-wiring.test.fs
```

The public namespace is `games.cafecito.foundryobservability`.

`plugin.cfg` identifies the addon and starts at version `0.1.0`. The editor
plugin in `export_plugin.fs` registers the `FoundryObservability` autoload when
the addon is enabled. `FoundryObservability.fs` is a parseable `Node` autoload
with no provider behavior. `FoundryObservabilityApi.fs` is an intentionally
behavior-free public contract marker with only a stable identity constant. The
autoload does not consume the marker until the first real API method exists, so
the bootstrap does not introduce a no-op runtime contract.

The test project enables the editor plugin and autoload, declares FoundryLib as
a Git package, and checks the project wiring through the FoundryLib test
runner. FoundryScript `.uid` companions for tracked script resources are
checked in and validated.

## Development workflow

The repository provides these Task commands:

- `task lint` runs the configured generic repository hooks.
- `task test:foundry-script` checks addon structure, namespaces, autoload
  declarations, legacy-name absence, Foundry imports, and FoundryScript lint.
- `task test:project` installs the test-project packages with Anvil and runs
  the project-wiring tests.
- `task test` runs lint, FoundryScript checks, project tests, workflow contract
  checks, and packaging validation.
- `task package` creates a distributable addon zip from the addon directory.

The project uses a small `.pre-commit-config.yaml` for trailing whitespace,
end-of-file, YAML, JSON, large-file, and line-ending checks. `requirements.txt`
pins the lightweight Python tooling used by the repository checks.

## CI/CD

`.github/workflows/pr-check.yml` runs for pull requests targeting `main`. It
uses a macOS runner, installs Python tooling, `prek`, Anvil, and Foundry
`v0.1.0-alpha.7`, then runs the same lint, FoundryScript, project, workflow,
and packaging gates used locally.

`.github/workflows/release.yml` is manually dispatched with a `patch`, `minor`,
or `major` bump. It computes the next semver tag from existing `v*.*.*` tags,
runs the validation gate, stages a copy of the addon with the computed version
in `plugin.cfg`, creates `FoundryObservability-vX.Y.Z.zip`, and publishes a
GitHub release with the zip attached. It does not push directly to protected
`main`, and it requires no provider credentials.

The release workflow's staged version substitution keeps release artifacts
accurate without adding an automated version-bump commit to the bootstrap
scope. Version synchronization in the source tree can be introduced as a
separate release-process change once the addon has a first real API release.

## Verification

The contract scripts will use Foundry's current command-first CLI:

- `foundry project import --project PROJECT_DIR` verifies project resource
  import.
- `foundry script lint --project PROJECT_DIR --fail-on=warning PATHS` verifies
  FoundryScript diagnostics.
- `foundry project test --project PROJECT_DIR --runner RUNNER_PATH -- --path
  TEST_PATH` runs the FoundryLib project tests.

The packaging check builds a temporary zip, verifies that the addon payload is
present, and rejects generated caches, test-project dependencies, and files
outside the addon payload. Workflow contract checks verify the Foundry
`v0.1.0-alpha.7` pin, current CLI forms, read-only pull-request permissions,
and semver release inputs.

## Documentation

The bootstrap includes:

- `README.md` for purpose, status, installation, enabling, and future provider
  direction.
- `BUILD.md` for prerequisites and local commands.
- `CHANGELOG.md` for the initial unreleased entry.
- `CONTRIBUTING.md` for focused contribution and validation guidance.

These documents describe the repository as an infrastructure-complete,
behavior-incomplete addon so consumers do not mistake the empty autoload for a
working error-reporting service.
