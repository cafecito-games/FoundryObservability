# Apple Companion Runtime Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pin the Apple FoundrySwift companion addon and make release builds prove that the built Sentry framework loads with that exact runtime before packaging.

**Architecture:** Reuse `scripts/test-project` as the single project materialization and runtime-test path, adding an opt-in strict mode that preserves Foundry's pipeline status and rejects native loader diagnostics. Keep FoundrySwift ownership in the sibling `FoundrySwiftEmbed` addon; enforce the dependency, documentation, empty Sentry dependency maps, and release ordering through shell contracts.

**Tech Stack:** Bash 3.2, Anvil package manifests, Foundry headless tests, GitHub Actions YAML, ripgrep, Task.

---

### Task 1: Add failing companion-dependency and documentation contracts

**Files:**
- Modify: `scripts/test-sentry-ios-build-contract`
- Test: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Write the failing manifest, ignore, and documentation assertions**

Add exact assertions for:

```bash
[packages.FoundrySwift]
source = "github-release"
repo = "cafecito-games/Foundry-Swift"
version = "0.1.0-alpha.2"
asset = "FoundrySwift-0.1.0-alpha.2.zip"
checksum = "51fedac51e9157430df2e3802dbb0c827c5d35500af418bb1fcb04114d040ffb"
source_path = "addons/FoundrySwift"
```

Also require `test_project/addons/FoundrySwift/` in `.gitignore`, and require
README text containing `FoundrySwiftEmbed`, the exact release/asset, and a clear
statement that Android does not require the companion. Preserve the existing
assertions that every Sentry descriptor dependency map is `{}` and that the
descriptor/export plugin do not own FoundrySwift.

- [ ] **Step 2: Run the contract and verify RED**

Run:

```sh
scripts/test-sentry-ios-build-contract
```

Expected: failure stating that the test project must pin FoundrySwift.

### Task 2: Add failing strict-runtime and workflow contracts

**Files:**
- Modify: `scripts/test-sentry-ios-build-contract`
- Modify: `scripts/test-ci-workflows`
- Test: both scripts

- [ ] **Step 1: Contract the strict test-project behavior**

Require `scripts/test-project` to contain:

```bash
FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME
tee "$runtime_log"
pipeline_status=("${PIPESTATUS[@]}")
foundry_status=${pipeline_status[0]}
tee_status=${pipeline_status[1]}
```

Also require explicit rejection patterns for
`FoundryObservabilitySentry`, `FoundrySwift`, and extension/dynamic-library
loader failures.

- [ ] **Step 2: Contract the release step and ordering**

Require the build job to contain exactly:

```yaml
- name: Verify Apple Sentry runtime
  env:
    GH_TOKEN: ${{ github.token }}
    FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME: "1"
  run: scripts/test-project
```

Extend the existing release line-order assertion to prove:

```text
ios:sentry < strict runtime < android:sentry < package < verify < upload < download < publish
```

- [ ] **Step 3: Run both contracts and verify RED**

Run:

```sh
scripts/test-sentry-ios-build-contract
scripts/test-ci-workflows
```

Expected: failures for missing strict runtime support and missing release smoke.

### Task 3: Pin and install the companion addon

**Files:**
- Modify: `test_project/packages.toml`
- Modify: `test_project/packages.lock`
- Modify: `.gitignore`

- [ ] **Step 1: Add the exact Anvil package**

Add:

```toml
[packages.FoundrySwift]
source = "github-release"
repo = "cafecito-games/Foundry-Swift"
version = "0.1.0-alpha.2"
asset = "FoundrySwift-0.1.0-alpha.2.zip"
checksum = "51fedac51e9157430df2e3802dbb0c827c5d35500af418bb1fcb04114d040ffb"
source_path = "addons/FoundrySwift"
```

Add `test_project/addons/FoundrySwift/` to `.gitignore`.

- [ ] **Step 2: Resolve the lock and verify installation**

Run:

```sh
anvil pkg install --dir test_project
```

Expected: `FoundrySwift` resolves to `0.1.0-alpha.2`,
`test_project/packages.lock` records the source path and spec hash, and
`test_project/addons/FoundrySwift` contains `FoundrySwiftEmbed` plus its native
framework.

### Task 4: Implement strict runtime smoke

**Files:**
- Modify: `scripts/test-project`
- Test: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Add opt-in prerequisites and cleanup**

Read `FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME`, require the real
`Versions/A/FoundryObservabilitySentry` binary only when the value is `1`, and
track an exact `mktemp` log path that the existing exit trap removes.

- [ ] **Step 2: Preserve Foundry status and reject loader diagnostics**

In strict mode run the existing Foundry command as:

```bash
set +e
"$foundry_bin" --headless project test \
	--project "$project_dir" \
	--runner res://addons/foundrylib/testlib/cli/run.fs \
	-- --path res://tests 2>&1 | tee "$runtime_log"
pipeline_status=("${PIPESTATUS[@]}")
set -e
foundry_status=${pipeline_status[0]}
tee_status=${pipeline_status[1]}
```

Fail distinctly when `tee_status` or `foundry_status` is nonzero. Then scan the
log for dynamic-library,
FoundryExtension, or extension-load errors naming
`FoundryObservabilitySentry` or `FoundrySwift`, and fail if any are present.
Keep the direct command path unchanged outside strict mode.

- [ ] **Step 3: Run the build contract and verify GREEN**

Run:

```sh
scripts/test-sentry-ios-build-contract
```

Expected: pass once the package/docs changes in Task 5 are also present.

### Task 5: Document and wire the release smoke

**Files:**
- Modify: `README.md`
- Modify: `BUILD.md`
- Modify: `docs/API.md`
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Correct installation documentation**

Replace independently-installable Apple wording with the exact
`[packages.FoundrySwift]` snippet from Task 3 and `anvil pkg install`. State
that `0.1.0-alpha.2` is required for Apple, `FoundrySwiftEmbed` owns the sole
shared runtime copy, Sentry dependency maps intentionally remain empty, and
Android does not require the companion.

- [ ] **Step 2: Add strict runtime release step**

Immediately after `task ios:sentry`, add:

```yaml
- name: Verify Apple Sentry runtime
  env:
    GH_TOKEN: ${{ github.token }}
    FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME: "1"
  run: scripts/test-project
```

- [ ] **Step 3: Run contracts and verify GREEN**

Run:

```sh
scripts/test-sentry-ios-build-contract
scripts/test-ci-workflows
```

Expected: both pass.

### Task 6: Correct and verify the sibling companion runpath

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/project.yml`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj/project.pbxproj`
- Modify: `scripts/test-sentry-ios-build-contract`

- [ ] **Step 1: Contract the framework binary's exact sibling runpath**

Require both native project files to contain:

```text
@loader_path/../../../../../../FoundrySwift/bin/macos_arm64
```

The six parent traversals are required because the loader starts from the
framework binary under `FoundryObservabilitySentry.framework/Versions/A`.

- [ ] **Step 2: Run the contract and verify RED**

Run:

```sh
scripts/test-sentry-ios-build-contract
```

Expected: failure stating that the native project must resolve the sibling
FoundrySwift addon from `Versions/A`.

- [ ] **Step 3: Correct and regenerate the native project**

Replace the four-level runpath in `project.yml` with the exact six-level path,
regenerate the Xcode project with XcodeGen, and rebuild:

```sh
task ios:sentry
```

Use `otool -l` on the rebuilt macOS framework binary to confirm the resulting
`LC_RPATH`. Do not inject `DYLD_FRAMEWORK_PATH`; the artifact must resolve its
declared runtime dependency itself.

- [ ] **Step 4: Run the contract and verify GREEN**

Run:

```sh
scripts/test-sentry-ios-build-contract
```

Expected: pass.

### Task 7: Make bridge-compatibility tests runtime-independent

**Files:**
- Modify: `test_project/tests/observability-sentry.test.fs`
- Create: `test_project/tests/support/incompatible_sentry_bridge.notest.fs`

- [ ] **Step 1: Preserve the strict-smoke failure as RED**

With the native extension successfully loaded, the test that constructs a
provider without an injected bridge resolves the real engine class and no
longer represents a missing-bridge case.

Expected: the strict run reaches all tests without loader diagnostics but
reports 77/78 because the test expected `Error.FAILED` from a real compatible
bridge.

- [ ] **Step 2: Inject an explicitly incompatible bridge**

Rename the test to describe a compatible native bridge requirement and inject a
minimal bridge object that deliberately implements none of the native methods.
This avoids real Sentry configuration or network side effects and behaves the
same whether native artifacts are loaded or absent.

### Task 8: Verify runtime and repository gates

**Files:**
- Modify only files required to correct verified failures.

- [ ] **Step 1: Run the strict runtime smoke**

Run:

```sh
FOUNDRY_BIN=/Users/christian/CafecitoGames/Foundry/.worktrees/version-json/bin/foundry.macos.editor.dev.arm64 \
FOUNDRYOBSERVABILITY_STRICT_NATIVE_RUNTIME=1 \
scripts/test-project
```

Expected: no FoundrySwift or FoundryObservabilitySentry loader diagnostics and
`Ran 78 tests: 78 passed, 0 failed, 0 skipped.`

- [ ] **Step 2: Verify ordinary no-native behavior**

Temporarily remove the generated macOS Sentry framework from the runtime path
and run `scripts/test-project` without strict mode. Restore the artifact after
the command.

Expected: loader diagnostics are reported, but the ordinary test command exits
zero with `Ran 78 tests: 78 passed, 0 failed, 0 skipped.`

- [ ] **Step 3: Run focused repository verification**

Run:

```sh
task test:ci
scripts/test-sentry-ios-build-contract
task lint
bash -n scripts/test-project scripts/test-sentry-ios-build-contract scripts/test-ci-workflows
git diff --check
```

Expected: every command exits zero.

- [ ] **Step 4: Commit the focused implementation**

Run:

```sh
git add .gitignore .github/workflows/release.yml README.md BUILD.md docs/API.md \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/project.yml \
  addons/FoundryObservabilitySentry/FoundryObservabilitySentry/FoundryObservabilitySentry.xcodeproj/project.pbxproj \
  scripts/test-project scripts/test-sentry-ios-build-contract \
  scripts/test-ci-workflows test_project/packages.toml test_project/packages.lock \
  test_project/tests/observability-sentry.test.fs \
  test_project/tests/support/incompatible_sentry_bridge.notest.fs
git commit -m "ci: verify Apple companion runtime"
```

- [ ] **Step 5: Audit final state**

Run:

```sh
git status --short
git log -3 --oneline
```

Expected: clean status, implementation commit at HEAD, and no push or release.
