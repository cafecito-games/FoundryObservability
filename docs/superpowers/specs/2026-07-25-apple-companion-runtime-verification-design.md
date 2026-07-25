# Apple Companion Runtime Verification Design

**Date:** 2026-07-25
**Status:** Approved for implementation

## Goal

Make the Apple Sentry release path install and verify the exact shared
FoundrySwift runtime required by `FoundryObservabilitySentry`, without embedding
or declaring a second copy of FoundrySwift in the Sentry addon.

## Dependency boundary

The test project will pin a package named `FoundrySwift` from the
`cafecito-games/Foundry-Swift` GitHub release `0.1.0-alpha.2`, using the
`FoundrySwift-0.1.0-alpha.2.zip` asset and
published checksum
`51fedac51e9157430df2e3802dbb0c827c5d35500af418bb1fcb04114d040ffb`,
with the `addons/FoundrySwift` source path. Anvil installs it at
`test_project/addons/FoundrySwift`, which remains generated and ignored.

`FoundrySwiftEmbed` in that companion addon is the sole owner of the shared
FoundrySwift framework used by Apple exports and runtime loading. The Sentry
`.foundryextension` dependency maps remain intentionally empty, and the Sentry
archive does not duplicate the FoundrySwift runtime. Android uses its own Sentry
Android bridge and does not require the FoundrySwift companion addon.

The macOS Sentry framework binary is nested under
`FoundryObservabilitySentry.framework/Versions/A`. Its `LC_RPATH` therefore
uses `@loader_path/../../../../../../FoundrySwift/bin/macos_arm64` to reach the
sibling companion addon from the binary's actual loader directory. The native
build contract validates this exact path in both `project.yml` and the generated
Xcode project.

## Strict runtime smoke

`scripts/test-project` will accept
`FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1`. Normal invocations keep the
existing clean-checkout behavior and do not require ignored native outputs.
Strict mode requires a nonempty built macOS Sentry framework binary, installs
the pinned project packages, and runs the existing Foundry project test command.

The command's combined output is captured with `tee`. The script immediately
snapshots the complete `PIPESTATUS` array, which is available in macOS Bash
3.2, and fails distinctly when either Foundry or `tee` fails. Strict mode also
checks the installed companion's `plugin.cfg` version and fails if the captured
log contains Foundry diagnostics indicating that the
FoundryObservabilitySentry or FoundrySwift dynamic libraries/extensions could
not be opened or loaded. The existing test runner exit status continues to
prove the complete project suite passed. Provider compatibility tests inject an
explicitly incompatible bridge instead of assuming the native class is absent,
so the same 78-test suite is deterministic with and without loaded native
artifacts.

## Release integration

The read-only release build job will run the strict project test immediately
after `task ios:sentry`. The step receives `GH_TOKEN` for Anvil's GitHub release
access and sets the strict-mode environment variable. It must precede Android
building, release packaging, archive verification, artifact upload, and the
write-only publish job. Both pull-request and release workflows install Anvil
from the immutable v0.0.1 commit rather than a mutable `latest` reference.

## Documentation and contracts

README installation instructions will include a complete Anvil manifest
fragment and command for the exact compatible Apple companion release. They
will state that the Sentry addon requires the companion on Apple, explain
single ownership by FoundrySwiftEmbed, and state that Android does not require
it. API and BUILD documentation will repeat only the runtime and release-test
details relevant to their audiences.

Shell contracts will assert the exact package pin, generated-directory ignore,
documentation qualifier, intentionally empty Sentry dependency maps, strict
mode behavior, release command/environment, and release-step ordering.

## Verification

The implementation will first demonstrate contract failures for the missing
package/docs/strict-mode/workflow behavior. After implementation, Anvil will
install the exact companion release into the test project. The strict smoke
will run against the pinned local Foundry binary and a freshly rebuilt Apple
artifact carrying the six-level companion runpath, with no loader errors and
all 78 project tests passing. A no-native ordinary run will also prove that
clean development tests remain non-strict. Focused contracts, workflow checks,
lint, Bash syntax, and diff checks complete the gate.
