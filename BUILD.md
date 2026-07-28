# Build and Test

## Prerequisites

- Foundry `v0.1.0-alpha.9` or a compatible local development build
- Go with the `anvil` package tool available on `PATH`
- Task
- Xcode 15+, Swift 6, XcodeGen, and LLDB for Apple native crash validation
- Java 17, Android SDK Platform 36, and ADB on `PATH` for Android device
  validation
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

Run the repository validation gates with:

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

`task test` is the complete ordinary local gate. The equivalent individual
checks are:

```sh
prek run --all-files
scripts/test-ci-workflows
scripts/test-package
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
(cd addons/FoundryObservabilitySentry/FoundryObservabilitySentry && swift test)
scripts/test-sentry-ios-build-contract
(cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry && ./gradlew test)
scripts/test-sentry-android-build-contract
git diff --check
```

Run `scripts/test-foundry-script` and `scripts/test-project` sequentially. Both
materialize test-project addon state and are not safe to overlap.

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

### Android plugin namespace

Android plugin metadata uses the hard-fork namespace
`org.foundryengine.plugin.v2`. Legacy plugin discovery is intentionally not
supported. Until the Foundry Android plugin registry adopts that namespace,
the native Android Sentry bridge will build and package but will not be
discovered at runtime.

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

## Recursive Foundry Script source layout

Packaging and validation scan `.fs` and `.fs.uid` files recursively. Public
sources are organized by responsibility:

```text
addons/
├── FoundryObservability/
│   ├── *.fs                         foundry.observability
│   ├── runtime/*.fs                 foundry.observability.runtime
│   ├── processing/*.fs              foundry.observability.processing
│   └── foundrylib/*.fs              foundry.observability.foundrylib
└── FoundryObservabilitySentry/
    └── *.fs                         foundry.observability.sentry
```

Do not flatten the runtime or processing directories when packaging. Their
namespace declarations and imports are part of the source contract. The core
archive retains the full recursive `addons/FoundryObservability` tree; the
Sentry archive retains its complete addon tree plus available native artifacts.

## Native crash validation tooling

Native crash validation also requires a non-production Sentry project, a
disposable test release, and the applicable Xcode simulator or physical iOS
device, macOS test process, or debuggable Android emulator/device. The guarded
repository helper can signal macOS and Android processes:

```text
scripts/trigger-test-native-crash macos <pid> --i-understand-this-will-crash
scripts/trigger-test-native-crash android <debuggable-package> --i-understand-this-will-crash
```

The helper is intentionally excluded from release packages. iOS validation
uses Xcode and LLDB instead. Read
[docs/NATIVE_CRASH_VALIDATION.md](docs/NATIVE_CRASH_VALIDATION.md) before
running either workflow; every trigger terminates the selected process.
