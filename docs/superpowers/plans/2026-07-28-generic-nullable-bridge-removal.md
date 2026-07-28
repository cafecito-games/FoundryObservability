# Generic Nullable Bridge Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade validation to Foundry alpha.9, remove the obsolete generic nullable bridge and every redundant addon `unsafe_call_argument` suppression, and lock the supported type boundary with analyzer contracts.

**Architecture:** Keep production behavior unchanged and express the safe `T -> T?` widening directly in the generic normalization factory. Extend the existing shell-based source contracts with a zero-addon/one-negative-test suppression invariant plus temporary positive and negative analyzer fixtures. Keep dynamic casts localized and retain their existing `unsafe_cast` suppressions.

**Tech Stack:** Foundry Script, Bash, ripgrep, GitHub Actions YAML, Foundry `script lint`, Task.

---

## File Map

- `scripts/test-foundry-script`: source invariants, suppression inventory, temporary analyzer fixtures, cleanup, and diagnostic assertions.
- `scripts/test-ci-workflows`: exact Foundry version contracts for PR and release workflows.
- `addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs`: direct same-parameter generic nullable passage.
- `addons/FoundryObservability/processing/ObservabilityValueWalker.fs`: remove two redundant standalone call-warning suppressions.
- `addons/FoundryObservability/processing/ObservabilityRedactor.fs`: retain required `unsafe_cast` suppressions while removing the redundant call-warning name.
- `.github/workflows/pr-check.yml`: PR validation Foundry pin.
- `.github/workflows/release.yml`: release validation Foundry pin.

### Task 1: Add Failing Source and Analyzer Contracts

**Files:**

- Modify: `scripts/test-foundry-script:401-404`
- Modify: `scripts/test-foundry-script:2147-2223`
- Modify: `scripts/test-ci-workflows:183-184`
- Modify: `scripts/test-ci-workflows:363-364`

- [ ] **Step 1: Replace the obsolete normalization bridge contract**

Replace the requirement for the old compiler-limitation comment with:

```bash
rg -Fq \
	'return ObservabilityNormalizationResult[T].new(true, value, Error.OK)' \
	"$normalization_result_source" \
	|| fail "normalization result must pass T directly to its matching T? constructor parameter"
if rg -n 'nullable_value: Variant|Current Foundry rejects direct generic T-to-T\?' \
		"$normalization_result_source"; then
	fail "normalization result retains an obsolete generic-nullable bridge"
fi
```

- [ ] **Step 2: Add the exact suppression-boundary contract**

Add this source inventory before runtime lint:

```bash
if rg -n --glob '*.fs' \
		'@warning_ignore\([^)]*"unsafe_call_argument"' \
		"$addon" "$sentry_addon"; then
	fail "shipped addons must not suppress unsafe_call_argument"
fi
test_unsafe_call_suppressions=$(
	rg -n --glob '*.fs' \
		'@warning_ignore\([^)]*"unsafe_call_argument"' \
		"$project_dir/tests" \
		| wc -l \
		| tr -d ' '
)
[[ "$test_unsafe_call_suppressions" == "1" ]] \
	|| fail "tests must retain exactly one intentional unsafe_call_argument suppression"
null_event_test=$(
	sed -n \
		'/^func test_normalizer_rejects_null_event_before_sampling_capture_time()/,/^func /p' \
		"$project_dir/tests/observability-core.test.fs"
)
rg -Fq '@warning_ignore("unsafe_call_argument")' <<<"$null_event_test" \
	|| fail "null-event negative test must retain its intentional call-warning suppression"
rg -Fq 'normalizer.normalize_event(null, ObservabilityConfig.new())' \
	<<<"$null_event_test" \
	|| fail "null-event negative test must exercise the guarded non-null call"
```

- [ ] **Step 3: Add temporary-fixture cleanup state**

Extend the variables and `cleanup()` function:

```bash
generic_nullable_fixture=""
generic_distinct_log=""
generic_distinct_fixture=""
cleanup() {
	rm -f "$import_log"
	if [[ -n "$invalid_callable_log" ]]; then
		rm -f "$invalid_callable_log"
	fi
	if [[ -n "$invalid_callable_fixture" ]]; then
		rm -f "$invalid_callable_fixture"
	fi
	if [[ -n "$generic_nullable_fixture" ]]; then
		rm -f "$generic_nullable_fixture"
	fi
	if [[ -n "$generic_distinct_log" ]]; then
		rm -f "$generic_distinct_log"
	fi
	if [[ -n "$generic_distinct_fixture" ]]; then
		rm -f "$generic_distinct_fixture"
	fi
	restore_test_addons
}
```

- [ ] **Step 4: Add the positive all-surfaces analyzer fixture**

After addon lint, create and lint:

```bash
generic_nullable_fixture="$test_addon/processing/GenericNullableSameParameterContract.fixture.fs"
printf '%s\n' \
	'namespace foundry.observability.processing.contract' \
	'' \
	'class Holder[T] extends RefCounted:' \
	'	var stored: T?' \
	'' \
	'	func _init(p_value: T?) -> void:' \
	'		stored = p_value' \
	'' \
	'	static func of(value: T) -> Holder[T]:' \
	'		return Holder[T].new(value)' \
	'' \
	'	func widen_local(value: T) -> T?:' \
	'		var widened: T? = value' \
	'		return widened' \
	'' \
	'	func widen_return(value: T) -> T?:' \
	'		return value' \
	'' \
	'	func store(value: T) -> void:' \
	'		stored = value' \
	'' \
	'func prove_same_parameter_widening() -> void:' \
	'	var holder: Holder[int] = Holder[int].of(7)' \
	'	assert(holder.widen_local(3) == 3)' \
	'	assert(holder.widen_return(4) == 4)' \
	'	holder.store(9)' \
	'	assert(holder.stored == 9)' \
	>"$generic_nullable_fixture"
(
	cd "$project_dir"
	"$foundry_bin" --headless script lint \
		--project "$project_dir" \
		--fail-on=warning \
		addons/FoundryObservability/processing/GenericNullableSameParameterContract.fixture.fs
)
rm -f "$generic_nullable_fixture"
generic_nullable_fixture=""
```

- [ ] **Step 5: Add the distinct-parameter rejection fixture**

Create a negative fixture and verify its exact analyzer category:

```bash
generic_distinct_log=$(mktemp "${TMPDIR:-/tmp}/foundryobservability-generic-distinct.XXXXXX")
generic_distinct_fixture="$test_addon/processing/GenericDistinctNullableContract.fixture.fs"
printf '%s\n' \
	'namespace foundry.observability.processing.contract' \
	'' \
	'class Pair[T, U] extends RefCounted:' \
	'	var second: U?' \
	'' \
	'	func store_first(value: T) -> void:' \
	'		second = value' \
	'' \
	'func prove_distinct_parameter_rejection() -> void:' \
	'	var pair: Pair[int, String] = Pair[int, String].new()' \
	'	pair.store_first(7)' \
	>"$generic_distinct_fixture"
if (
	cd "$project_dir"
	"$foundry_bin" --headless script lint \
		--project "$project_dir" \
		--fail-on=warning \
		addons/FoundryObservability/processing/GenericDistinctNullableContract.fixture.fs
) >"$generic_distinct_log" 2>&1; then
	cat "$generic_distinct_log" >&2
	fail "Foundry Script analyzer accepted T where independent U? was required"
fi
if ! rg -Fq 'Value of type "T" cannot be assigned to a variable of type "U?".' \
		"$generic_distinct_log" \
		|| ! rg -Fq '"ruleId": "analyzer-error"' "$generic_distinct_log"; then
	cat "$generic_distinct_log" >&2
	fail "Foundry Script analyzer did not report the expected T-to-U? rejection"
fi
rm -f "$generic_distinct_fixture" "$generic_distinct_log"
generic_distinct_fixture=""
generic_distinct_log=""
```

- [ ] **Step 6: Update workflow test expectations to alpha.9**

Change both exact checks and failure messages:

```bash
rg -q -U '^env:\n  FOUNDRY_VERSION: "v0\.1\.0-alpha\.9"$' "$release_workflow" \
	|| fail "release.yml must pin Foundry v0.1.0-alpha.9"
```

```bash
rg -Fxq '      FOUNDRY_VERSION: "v0.1.0-alpha.9"' <<<"$core_job" \
	|| fail "validate-core job must pin Foundry v0.1.0-alpha.9"
```

- [ ] **Step 7: Run focused contracts and verify RED**

Run:

```bash
scripts/test-ci-workflows
```

Expected: FAIL with `release.yml must pin Foundry v0.1.0-alpha.9`.

Run:

```bash
scripts/test-foundry-script
```

Expected: FAIL because `ObservabilityNormalizationResult.success()` does not yet contain the required direct constructor call. The script must restore test-addon symlinks and leave no `*.fixture.fs` file behind.

### Task 2: Implement the Minimal Typed Cleanup

**Files:**

- Modify: `addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs:29-34`
- Modify: `addons/FoundryObservability/processing/ObservabilityValueWalker.fs:150-154`
- Modify: `addons/FoundryObservability/processing/ObservabilityRedactor.fs:278,330,366,460,693,707,743,750`
- Modify: `.github/workflows/pr-check.yml:16`
- Modify: `.github/workflows/release.yml:24`

- [ ] **Step 1: Replace the normalization bridge**

Use the direct factory body:

```foundryscript
@warning_ignore("shadowed_variable")
static func success(value: T) -> ObservabilityNormalizationResult[T]:
	return ObservabilityNormalizationResult[T].new(true, value, Error.OK)
```

- [ ] **Step 2: Remove redundant walker suppressions**

Leave the dispatch as:

```foundryscript
if value is Dictionary:
	result = _walk_dictionary(value, path, depth, policy, state)
else:
	result = _walk_array(value, path, depth, policy, state)
```

- [ ] **Step 3: Narrow redactor decorators to unsafe casts only**

Change each combined decorator:

```foundryscript
@warning_ignore("unsafe_cast")
```

Delete the standalone `@warning_ignore("unsafe_call_argument")` above the
scope-tag condition entirely. Do not change any redactor data flow, casts, or
runtime validation.

- [ ] **Step 4: Upgrade both workflow pins**

Set:

```yaml
FOUNDRY_VERSION: "v0.1.0-alpha.9"
```

in both `.github/workflows/pr-check.yml` and
`.github/workflows/release.yml`.

- [ ] **Step 5: Run focused contracts and verify GREEN**

Run:

```bash
scripts/test-ci-workflows
scripts/test-foundry-script
```

Expected:

```text
CI workflow contract checks passed
FoundryScript contract checks passed
```

Also verify:

```bash
rg -n 'unsafe_call_argument' addons --glob '*.fs'
```

Expected: no matches and exit status 1.

Run:

```bash
rg -n 'unsafe_call_argument' test_project/tests --glob '*.fs'
```

Expected: exactly the line in
`test_normalizer_rejects_null_event_before_sampling_capture_time`.

- [ ] **Step 6: Commit the red-green implementation**

```bash
git add \
	.github/workflows/pr-check.yml \
	.github/workflows/release.yml \
	addons/FoundryObservability/processing/ObservabilityNormalizationResult.fs \
	addons/FoundryObservability/processing/ObservabilityRedactor.fs \
	addons/FoundryObservability/processing/ObservabilityValueWalker.fs \
	scripts/test-ci-workflows \
	scripts/test-foundry-script
git commit -m "refactor: remove obsolete nullable call bridges"
```

### Task 3: Verify Repository Behavior and Distribution

**Files:**

- Verify only; no planned source changes.

- [ ] **Step 1: Run project behavior tests**

Run:

```bash
scripts/test-project
```

Expected: `Ran 412 tests: 412 passed, 0 failed, 0 skipped.`

- [ ] **Step 2: Run UID and package contracts**

Run:

```bash
scripts/test-foundry-uids
scripts/test-package
```

Expected:

```text
Foundry UID contract checks passed
Package contract checks passed: ...
```

- [ ] **Step 3: Run repository hygiene**

Run:

```bash
git diff --check origin/main...HEAD
prek run --all-files
```

Expected: both commands exit 0.

- [ ] **Step 4: Run the complete validation gate**

Ensure the ignored Android SDK configuration exists:

```properties
sdk.dir=/Users/christian/Library/Android/sdk
```

Run:

```bash
task test
```

Expected: exit 0, including Foundry Script, 412 project tests, 59 Swift tests,
Android debug and release tests, platform contracts, package checks, workflow
checks, and repository lint.

- [ ] **Step 5: Verify the final branch diff**

Run:

```bash
git status --short --branch
git diff --stat origin/main...HEAD
git diff --check origin/main...HEAD
```

Expected: clean `issue-34` worktree, focused design/plan/implementation changes,
and no whitespace errors.

### Task 4: Review and Publish

**Files:**

- No planned source changes; findings may require focused amendments.

- [ ] **Step 1: Run supervised adversarial review**

Run:

```bash
python3 ~/.claude/scripts/codex_review/await_review.py start-wait \
	--cwd /Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-34 \
	--scope branch \
	--base origin/main \
	--deadline 540
```

Expected: exit 0 with a clean verdict. Triage every finding; fix all in-scope
critical or blocking findings and rerun from the new HEAD until clean.

- [ ] **Step 2: Push the issue branch**

```bash
git push -u origin issue-34
```

- [ ] **Step 3: Open the pull request**

Create a PR against `main` with:

```markdown
## Summary

- upgrade Foundry validation to v0.1.0-alpha.9
- replace the obsolete same-parameter generic nullable Variant bridge with direct typed passage
- remove redundant addon unsafe-call suppressions and lock the exact analyzer boundary with positive and negative contracts

## Validation

- `task test`
- supervised Codex review: clean

Closes #34
```

- [ ] **Step 4: Enable squash auto-merge**

```bash
gh pr merge --squash --auto
```

- [ ] **Step 5: Clean up only after merge**

After GitHub reports the PR as merged:

```bash
git -C /Users/christian/CafecitoGames/FoundryObservability worktree remove \
	/Users/christian/CafecitoGames/FoundryObservability/.worktrees/issue-34
git -C /Users/christian/CafecitoGames/FoundryObservability branch -D issue-34
```

If auto-merge is pending, preserve the worktree and branch.
