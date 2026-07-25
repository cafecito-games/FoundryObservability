# Build and Test

## Prerequisites

- Foundry `v0.1.0-alpha.7` or a compatible local development build
- Go with the `anvil` package tool available on `PATH`
- Task
- Xcode 15+, Swift 6, and XcodeGen
- Java 17 and Android SDK Platform 36
- GitHub CLI (`gh`) authenticated for Foundry-Swift release downloads; set
  `GH_TOKEN` for non-interactive builds and CI
- `jq`, `prek`, `ripgrep`, `zip`, and `unzip`

The repository scripts use the local Foundry development binary at
`/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64`
when it exists, then fall back to `foundry` on `PATH`. Set `FOUNDRY_BIN` to
override that resolution:

```sh
export FOUNDRY_BIN=/path/to/foundry
```

## Commands

```sh
task lint
task test:foundry-script
task test:project
task test:ci
task test:package
task test:sentry-swift
scripts/test-sentry-android-build-contract
task test
task package
task package:sentry
task ios:sentry
FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1 task test:project
task android:sentry
```

`task test:project` installs the packages declared in
`test_project/packages.toml` with Anvil and runs both the core and FoundryLib
sink suites. The installed `test_project/addons/foundrylib/` and
`test_project/addons/FoundrySwift/` directories are generated and ignored by
Git. The local core addon symlink is materialized temporarily during headless
runtime tests because Foundry's source scan intentionally skips directory
symlinks.

Set `FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1` only after
`task ios:sentry`. Strict mode requires the built macOS Sentry framework,
captures the Foundry test output, and fails on any Sentry or FoundrySwift
dynamic-library/extension loader error. The ordinary aggregate test remains
runnable before ignored native artifacts exist.

`task package` creates the core and Sentry addon zips. The core archive contains
exactly this runtime payload:

- `addons/FoundryObservability`

The Sentry archive contains the runtime `FoundryObservabilitySentry` addon and
any built artifacts under `bin/ios`, `bin/macos_arm64`, and `bin/android`, but
not the Swift source, Android source module, or generated native project state.
Run `task ios:sentry` to build the iOS and macOS Apple artifacts and
`task android:sentry` to build the Android debug/release AARs. The Apple task
downloads and checksum-verifies the prebuilt Foundry-Swift alpha.2 framework
and macro artifact into derived data, then compiles only the Sentry bridge.

## Native release packaging

Release archives must be assembled only after both native builds complete:

```sh
task ios:sentry
FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1 task test:project
task android:sentry
REQUIRE_NATIVE_ARTIFACTS=1 task package VERSION=1.2.3
task verify:sentry-package VERSION=1.2.3
```

`REQUIRE_NATIVE_ARTIFACTS=1` rejects a release package when any expected Apple
framework or Android debug/release AAR is missing or empty. The verification
step checks the framework symlinks, macOS binary, and both nested AAR zip files.
Set `DIST_DIR` on the packaging and verification commands to write and inspect
the archives in an isolated output directory. The strict project test installs
the pinned FoundrySwift `0.1.0-alpha.2` companion addon and proves the built
Sentry framework loads against its shared runtime before packaging.

Current public source namespaces are `foundry.observability`,
`foundry.observability.foundrylib`, and `foundry.observability.sentry`.
