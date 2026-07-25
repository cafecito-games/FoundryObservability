# Platform-Specific Pull Request Validation Design

**Date:** 2026-07-25
**Status:** Approved for implementation

## Goal

Split pull-request validation into three independently visible jobs so the
provider-neutral addon, Apple Sentry addon, and Android Sentry addon each own
the checks and builds relevant to their platform boundary.

The complete local `task test` command remains the aggregate validation gate.
The change reorganizes CI ownership without weakening local coverage or
changing runtime addon behavior.

## Task boundaries

Taskfile will expose three grouped validation targets:

- `test:core` runs repository lint, workflow and package contracts, the Foundry
  project suite, and FoundryScript structure, diagnostics, and UID checks. The
  project suite may exercise the provider-neutral Sentry adapter through fake
  bridges, but it does not compile or test native Swift or Java code.
- `test:sentry-apple` runs the Swift mapper unit tests and Apple build/export
  contracts.
- `test:sentry-android` runs the Android bridge/export contracts. The native
  Android build task remains responsible for Gradle unit tests, lint, and
  debug/release AAR assembly.

`task test` depends on all three groups, preserving a single complete local
command. Existing leaf tasks remain available for focused development and are
composed by the new groups instead of duplicated.

## Pull-request jobs

`.github/workflows/pr-check.yml` will contain three jobs:

1. `Validate core addon` runs on macOS because the Foundry editor test suite is
   a required core check. It installs the existing repository tooling, Anvil,
   and Foundry editor, then runs `task test:core`.
2. `Build Apple Sentry addon` runs on macOS. It installs repository tooling,
   Task, and XcodeGen, runs `task test:sentry-apple`, and runs
   `task ios:sentry` with the GitHub token needed to resolve the pinned
   FoundrySwift companion. This compiles the iOS device and simulator
   frameworks, creates the XCFramework, and compiles the arm64 macOS
   framework.
3. `Build Android Sentry addon` runs on Linux. It retains the JDK 17 and
   Android SDK 36 setup, adds any repository tooling needed by the contract
   script, runs `task test:sentry-android`, and runs `task android:sentry`.
   Gradle therefore continues to execute Java tests, Android lint, and both
   AAR assemblies in this job.

The jobs remain independent rather than using a matrix because their runners,
toolchains, credentials, and build commands differ materially. A failure will
identify the responsible addon directly in GitHub's required-check list.

## Contracts and failure behavior

The workflow contract test will require all three job identifiers, their
user-facing names, and the corresponding grouped Task and native build
commands. It will continue validating the pinned platform setup actions and
SDK versions.

The contract will also prevent native leaf tests from drifting back into the
core job by requiring `task test:core` there and the platform groups in their
respective jobs. Each command retains normal fail-fast shell behavior, so a
unit test or contract failure prevents that platform's native build from being
reported as successful.

## Verification

Implementation follows a contract-first red/green cycle:

1. Extend `scripts/test-ci-workflows` with the three-job ownership
   requirements and confirm it fails against the current two-job workflow.
2. Add the Taskfile groups and workflow split.
3. Confirm the focused workflow contract passes.
4. Run each grouped Task target locally.
5. Build Apple artifacts with `task ios:sentry` and Android artifacts with
   `task android:sentry`.
6. Run the complete `task test` aggregate, lint, YAML validation, and diff
   checks before publishing the branch.

The resulting PR must show one core check, one Apple Sentry check, and one
Android Sentry check.
