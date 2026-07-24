# Build and Test

## Prerequisites

- Foundry `v0.1.0-alpha.7` or a compatible local development build
- Go with the `anvil` package tool available on `PATH`
- Task
- Python 3.12+ with the dependencies in `requirements.txt`
- Xcode 15+, Swift 6, and XcodeGen
- GitHub CLI (`gh`) authenticated for Foundry-Swift release downloads
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
task test
task package
task package:sentry
task ios:sentry
```

`task test:project` installs the packages declared in
`test_project/packages.toml` with Anvil and runs both the core and FoundryLib
sink suites. The installed `test_project/addons/foundrylib/` directory is
generated and ignored by Git. The local core addon symlink is materialized
temporarily during headless runtime tests because Foundry's source scan
intentionally skips directory symlinks.

`task package` creates the core and Sentry addon zips. The core archive contains
exactly this runtime payload:

- `addons/FoundryObservability`

The Sentry archive contains the runtime `FoundryObservabilitySentry` addon and
the built iOS xcframework, but not the Swift source or generated Xcode project.
Run `task ios:sentry` first when rebuilding the native artifact. That task
downloads and checksum-verifies the prebuilt Foundry-Swift alpha.2 framework
and macro artifact into derived data, then compiles only the Sentry bridge.

Current public source namespaces are `foundry.observability`,
`foundry.observability.foundrylib`, and `foundry.observability.sentry`.
