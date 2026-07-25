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
`addons/FoundrySwift` source path. Anvil installs it at
`test_project/addons/FoundrySwift`, which remains generated and ignored.

`FoundrySwiftEmbed` in that companion addon is the sole owner of the shared
FoundrySwift framework used by Apple exports and runtime loading. The Sentry
`.foundryextension` dependency maps remain intentionally empty, and the Sentry
archive does not duplicate the FoundrySwift runtime. Android uses its own Sentry
Android bridge and does not require the FoundrySwift companion addon.

## Strict runtime smoke

`scripts/test-project` will accept
`FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1`. Normal invocations keep the
existing clean-checkout behavior and do not require ignored native outputs.
Strict mode requires a nonempty built macOS Sentry framework binary, installs
the pinned project packages, and runs the existing Foundry project test command.

The command's combined output is captured with `tee`. The script reads
`PIPESTATUS[0]`, which is available in macOS Bash 3.2, so a failing Foundry
process cannot be hidden by a successful `tee`. Strict mode also fails if the
captured log contains Foundry diagnostics indicating that the
FoundryObservabilitySentry or FoundrySwift dynamic libraries/extensions could
not be opened or loaded. The existing test runner exit status continues to
prove the complete project suite passed.

## Release integration

The read-only release build job will run the strict project test immediately
after `task ios:sentry`. The step receives `GH_TOKEN` for Anvil's GitHub release
access and sets the strict-mode environment variable. It must precede Android
building, release packaging, archive verification, artifact upload, and the
write-only publish job.

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
will run against the pinned local Foundry binary and current Apple artifact,
with no loader errors and all 78 project tests passing. Focused contracts,
workflow checks, lint, Bash syntax, and diff checks complete the gate.
