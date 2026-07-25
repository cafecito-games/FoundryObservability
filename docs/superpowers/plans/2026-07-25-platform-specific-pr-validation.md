# Platform-Specific Pull Request Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split pull-request validation into independently visible core, Apple Sentry, and Android Sentry jobs without weakening the complete local test gate.

**Architecture:** Add three compositional Taskfile targets around the existing focused leaf tasks, while retaining `task test` as their aggregate. Update the PR workflow so each job invokes only its owned validation group and native build, and strengthen the workflow contract to enforce those boundaries.

**Tech Stack:** Taskfile YAML, GitHub Actions YAML, Bash contract tests, Swift/Xcode, Gradle/Java, FoundryScript

---

## File structure

- Modify `scripts/test-ci-workflows`: define the required PR job ownership and
  assert each job invokes only its grouped validation target and native build.
- Modify `Taskfile.yml`: add reusable core, Apple, and Android validation groups;
  retain existing leaf targets and aggregate all groups under `test`.
- Modify `.github/workflows/pr-check.yml`: replace the two-job workflow with
  independent core, Apple, and Android jobs.

### Task 1: Add the failing three-job workflow contract

**Files:**
- Modify: `scripts/test-ci-workflows:176-190`
- Test: `scripts/test-ci-workflows`

- [ ] **Step 1: Replace the existing Android-only PR assertions with job-specific contract extraction**

Use job boundaries instead of searching the whole workflow so a command in one
job cannot accidentally satisfy another job's contract:

```bash
for job in validate-core build-apple build-android; do
	rg -q "^  $job:$" "$pr_workflow" \
		|| fail "pull-request workflow must define the $job job"
done

core_job=$(sed -n '/^  validate-core:$/,/^  build-apple:$/p' "$pr_workflow")
apple_job=$(sed -n '/^  build-apple:$/,/^  build-android:$/p' "$pr_workflow")
android_job=$(sed -n '/^  build-android:$/,$p' "$pr_workflow")

rg -q '^    name: Validate FoundryObservability$' <<<"$core_job" \
	|| fail "pull-request workflow must preserve the existing core check context"
rg -q 'run: task test:core' <<<"$core_job" \
	|| fail "core pull-request job must run only the core validation group"
if rg -q 'task (test:sentry-(apple|android)|ios:sentry|android:sentry)' <<<"$core_job"; then
	fail "core pull-request job must not run platform-specific Sentry validation"
fi

rg -q '^    name: Build Apple Sentry addon$' <<<"$apple_job" \
	|| fail "pull-request workflow must name the Apple Sentry job"
rg -q 'run: task test:sentry-apple' <<<"$apple_job" \
	|| fail "Apple pull-request job must run Apple validation"
rg -q -U -- '- name: Build Apple artifacts\n        env:\n          GH_TOKEN: \$\{\{ github\.token \}\}\n        run: task ios:sentry' \
	<<<"$apple_job" \
	|| fail "Apple pull-request job must build native artifacts with GH_TOKEN"
rg -q 'brew install .*xcodegen' <<<"$apple_job" \
	|| fail "Apple pull-request job must install XcodeGen"

rg -q '^    name: Build Android Sentry addon$' <<<"$android_job" \
	|| fail "pull-request workflow must name the Android Sentry job"
rg -q 'run: task test:sentry-android' <<<"$android_job" \
	|| fail "Android pull-request job must run Android validation"
rg -q 'run: task android:sentry' <<<"$android_job" \
	|| fail "Android pull-request job must build the Android Sentry AARs"
```

Keep the existing Java 17, Android setup action, license acceptance, SDK 36,
and Gradle executable assertions, but apply them to `"$android_job"` instead of
the complete `"$pr_workflow"`.

- [ ] **Step 2: Run the focused contract and verify the expected failure**

Run:

```bash
scripts/test-ci-workflows
```

Expected: exit 1 with
`FAIL: pull-request workflow must define the validate-core job`.

Do not modify production configuration until this failure proves the contract
detects the current two-job workflow.

### Task 2: Add grouped Taskfile ownership

**Files:**
- Modify: `Taskfile.yml:51-62`
- Test: `scripts/test-ci-workflows`

- [ ] **Step 1: Add the three grouped validation targets**

Insert these targets after the existing platform contract leaf tasks:

```yaml
  test:core:
    desc: Run provider-neutral repository and Foundry validation
    deps:
      - lint
      - test:ci
      - test:package
    cmds:
      - task: test:project
      - task: test:foundry-script

  test:sentry-apple:
    desc: Run Apple Sentry unit and build contract tests
    deps:
      - test:sentry-swift
      - test:sentry-contract

  test:sentry-android:
    desc: Run Android Sentry build contract tests
    deps:
      - test:sentry-android-contract
```

- [ ] **Step 2: Recompose the complete local test gate from the groups**

Replace the existing `test` target with:

```yaml
  test:
    desc: Run all local validation gates
    deps:
      - test:core
      - test:sentry-apple
      - test:sentry-android
```

The original leaf tasks remain unchanged and continue to provide focused
commands.

- [ ] **Step 3: Confirm the contract still fails for the missing workflow jobs**

Run:

```bash
scripts/test-ci-workflows
```

Expected: exit 1 with
`FAIL: pull-request workflow must define the validate-core job`. A different
failure indicates the Taskfile grouping broke an existing invariant and must
be fixed before editing the workflow.

### Task 3: Split the pull-request workflow into three jobs

**Files:**
- Modify: `.github/workflows/pr-check.yml:13-93`
- Test: `scripts/test-ci-workflows`

- [ ] **Step 1: Rename and narrow the core job**

Change the job identifier and final command while leaving its display name,
Foundry, Anvil, repository-tooling, and Task setup intact. Retaining
`Validate FoundryObservability` preserves the existing branch-protection check
context, while the `validate-core` identifier and `task test:core` command make
the job's ownership core-only:

```yaml
  validate-core:
    name: Validate FoundryObservability
    runs-on: macos-26
    timeout-minutes: 20
```

The final step becomes:

```yaml
      - name: Run core validation
        env:
          GH_TOKEN: ${{ github.token }}
        run: task test:core
```

- [ ] **Step 2: Add the Apple Sentry job**

Place this job between the core and Android jobs:

```yaml
  build-apple:
    name: Build Apple Sentry addon
    runs-on: macos-26
    timeout-minutes: 20
    steps:
      - name: Checkout
        uses: actions/checkout@v7.0.1

      - name: Install repository tooling
        run: brew install ripgrep xcodegen

      - name: Install Task
        uses: go-task/setup-task@v2.1.0
        with:
          repo-token: ${{ secrets.GITHUB_TOKEN }}

      - name: Run Apple validation
        run: task test:sentry-apple

      - name: Build Apple artifacts
        env:
          GH_TOKEN: ${{ github.token }}
        run: task ios:sentry
```

- [ ] **Step 3: Make the Android job own its contract tests**

After checkout, install ripgrep for the Bash contract:

```yaml
      - name: Install repository tooling
        run: sudo apt-get update && sudo apt-get install --yes ripgrep
```

Before the native build step, add:

```yaml
      - name: Run Android validation
        run: task test:sentry-android
```

Keep `task android:sentry` unchanged so Gradle continues to run Java unit tests,
lint, and both AAR builds.

- [ ] **Step 4: Run the focused contract and verify it passes**

Run:

```bash
scripts/test-ci-workflows
```

Expected:

```text
CI workflow contract checks passed
```

- [ ] **Step 5: Run formatting and syntax checks**

Run:

```bash
prek run check-yaml --files .github/workflows/pr-check.yml Taskfile.yml
bash -n scripts/test-ci-workflows
git diff --check
```

Expected: YAML hook passes, Bash exits 0 without output, and `git diff --check`
exits 0 without output.

- [ ] **Step 6: Commit the contract and implementation together**

```bash
git add scripts/test-ci-workflows Taskfile.yml .github/workflows/pr-check.yml
git commit -m "ci: split platform-specific PR validation"
```

### Task 4: Verify every ownership boundary and native build

**Files:**
- Verify only; no expected source changes

- [ ] **Step 1: Run core validation**

Run:

```bash
FOUNDRY_BIN=/Users/christian/CafecitoGames/Foundry/.worktrees/version-json/bin/foundry.macos.editor.dev.arm64 task test:core
```

Expected: 99 Foundry tests pass, repository contracts pass, lint passes, and
FoundryScript diagnostics contain no contract failures.

- [ ] **Step 2: Run Apple validation**

Run:

```bash
task test:sentry-apple
```

Expected: 19 Swift tests pass and the Apple build/export contract passes.

- [ ] **Step 3: Build Apple native artifacts**

Run:

```bash
GH_TOKEN="$(gh auth token)" task ios:sentry
```

Expected: Xcode builds the iOS device, iOS simulator, and arm64 macOS
frameworks successfully and creates
`addons/FoundryObservabilitySentry/bin/ios/FoundryObservabilitySentry.xcframework`.

- [ ] **Step 4: Run Android validation**

Run:

```bash
task test:sentry-android
```

Expected: `Sentry Android resolver contract checks passed`.

- [ ] **Step 5: Build Android native artifacts**

Run:

```bash
ANDROID_HOME=/Users/christian/Library/Android/sdk \
ANDROID_SDK_ROOT=/Users/christian/Library/Android/sdk \
task android:sentry
```

Expected: Gradle unit tests, `lintRelease`, `assembleDebug`, and
`assembleRelease` pass and both output AARs are copied into the addon.

- [ ] **Step 6: Run the complete aggregate gate**

Run:

```bash
FOUNDRY_BIN=/Users/christian/CafecitoGames/Foundry/.worktrees/version-json/bin/foundry.macos.editor.dev.arm64 task test
```

Expected: all core, Apple, and Android contract groups pass through the
aggregate command.

- [ ] **Step 7: Verify the branch is clean and scoped**

Run:

```bash
git status --short
git diff --check origin/main...HEAD
git diff --stat origin/main...HEAD
```

Expected: no uncommitted files, no whitespace errors, and only the design,
plan, Taskfile, PR workflow, and workflow contract changes appear.
