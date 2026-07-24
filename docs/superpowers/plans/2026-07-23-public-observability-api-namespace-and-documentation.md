# Public Observability API Namespace and Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Rename the public API to foundry.observability and fully document every public FoundryScript declaration plus the canonical API reference.

**Architecture:** This is a hard pre-release namespace rename with no aliases. Core declarations use foundry.observability, the FoundryLib adapter uses foundry.observability.foundrylib in its existing matching subdirectory, and global class/trait names remain unchanged. Inline ## comments document declaration-level contracts while docs/API.md provides the complete user-facing reference and examples.

**Tech Stack:** FoundryScript, Foundry headless CLI, FoundryLib foundry.logging, Bash validation scripts, Anvil package installation, Markdown, and tracked .uid resources.

---

## File map

- addons/FoundryObservability/*.fs: rename core namespaces and add inline documentation to public declarations.
- addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs: rename adapter namespace and document its public contract.
- test_project/tests/*.fs: use foundry.observability namespaces/imports and enforce the new namespace.
- scripts/test-foundry-script: expect the new source namespaces.
- docs/API.md: complete canonical API reference.
- README.md, BUILD.md, CONTRIBUTING.md, CHANGELOG.md: update current namespace and migration wording.
- docs/superpowers/specs/2026-07-23-public-observability-api-namespace-and-documentation-design.md: approved design record; no implementation details need to be added after this plan is committed.
- Generated test_project/.foundry state: may be rebuilt locally if stale, but is never committed.

Historical bootstrap specs and plans may retain the old namespace as history. Current source, tests, scripts, and user-facing docs may not.

### Task 1: Add the failing namespace contract test

**Files:** Modify test_project/tests/project-wiring.test.fs.

- [ ] **Step 1: Add a test for the new core and adapter namespaces.**

Add:

~~~
func test_project_uses_foundry_observability_namespace() -> void:
	var core_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservability.fs")
	var api_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryObservabilityApi.fs")
	var adapter_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/foundrylib/FoundryLibObservabilitySink.fs")
	Expect.that(core_source).to_contain(
			"namespace foundry.observability")
	Expect.that(api_source).to_contain(
			"namespace foundry.observability")
	Expect.that(adapter_source).to_contain(
			"namespace foundry.observability.foundrylib")
	Expect.that(core_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)
	Expect.that(api_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)
	Expect.that(adapter_source.find(
			"games.cafecito.foundryobservability")).to_equal(-1)
~~~

Use the existing FoundryLib testlib matcher style. Keep the existing project-wiring tests.

- [ ] **Step 2: Run the focused project suite and verify the test fails for the old namespace.**

Run:

~~~
scripts/test-project
~~~

Expected: the existing tests run, but the new namespace test fails because the current sources still declare games.cafecito.foundryobservability.

- [ ] **Step 3: Commit the red test.**

~~~
git add test_project/tests/project-wiring.test.fs
git commit -m "test: require foundry observability namespace"
~~~

### Task 2: Apply the hard namespace rename

**Files:** Modify every tracked FoundryScript file under addons/FoundryObservability and test_project/tests; modify scripts/test-foundry-script; modify current docs later in Task 4.

- [ ] **Step 1: Rename core source declarations and imports.**

Every core source declaration currently beginning with:

~~~
namespace games.cafecito.foundryobservability
~~~

must become:

~~~
namespace foundry.observability
~~~

The adapter declaration:

~~~
namespace games.cafecito.foundryobservability.foundrylib
~~~

must become:

~~~
namespace foundry.observability.foundrylib
~~~

Do not change global class names, autoload names, file paths, UIDs, method signatures, or runtime logic.

- [ ] **Step 2: Rename consumer test namespaces and imports.**

Change test namespaces from games.cafecito.foundryobservability.tests to:

~~~
namespace foundry.observability.tests
~~~

Change imports from games.cafecito.foundryobservability to foundry.observability and from games.cafecito.foundryobservability.foundrylib to foundry.observability.foundrylib. Keep the existing test behavior unchanged.

- [ ] **Step 3: Update the FoundryScript namespace contract.**

In scripts/test-foundry-script, change the core namespace assertion to:

~~~
rg -q '^namespace foundry\\.observability$' "$addon/FoundryObservability.fs" \
	|| fail "autoload has the wrong namespace"
~~~

Change the adapter assertion to:

~~~
rg -q '^namespace foundry\\.observability\\.foundrylib$' \
	"$addon/foundrylib/FoundryLibObservabilitySink.fs" \
	|| fail "FoundryLib sink has the wrong namespace"
~~~

Add a current-source guard:

~~~
if rg -n --glob '*.fs' 'games\\.cafecito\\.foundryobservability' "$addon" "$project_dir/tests"; then
	fail "current FoundryScript sources contain the removed namespace"
fi
~~~

- [ ] **Step 4: Run the namespace tests and lint.**

If a local generated test_project/.foundry cache still reports stale classes from the old namespace, move that exact generated directory aside and let Foundry rebuild it before rerunning tests. Do not commit generated state.

Run:

~~~
scripts/test-foundry-script
scripts/test-project
~~~

Expected: the new namespace contract, all existing core/sink/project-wiring tests, and FoundryScript lint pass.

- [ ] **Step 5: Commit the namespace rename.**

~~~
git add addons/FoundryObservability test_project/tests scripts/test-foundry-script
git commit -m "refactor: rename observability namespace"
~~~

### Task 3: Add inline documentation to every public declaration

**Files:** Modify all public source files under addons/FoundryObservability and addons/FoundryObservability/foundrylib.

- [ ] **Step 1: Document the service and API trait.**

In FoundryObservability.fs, keep the existing class comment and add comments immediately above each public method. The comments must state these contracts:

- configure(): rejects a null provider, configures a candidate before replacing the active provider, preserves the old provider on configuration failure, reconfigures the same provider without shutting it down, and returns a Foundry Error value.
- is_enabled(): reports the current config enabled flag.
- is_available(): reports provider availability.
- provider_name(): returns the active provider name and null-provider name before configuration.
- last_error(): returns the most recent provider/configuration/capture/flush error.
- capture_event(): returns a provider event ID, or an empty string when disabled, invalid, unavailable, or failed.
- capture_message(): creates a message event with game source and current engine time.
- capture_exception(): creates an error-level exception event with game source and current engine time.
- flush(): forwards the timeout in milliseconds and records the returned Error value.
- shutdown(): flushes and shuts down once, restores disabled null-provider state, and is safe to call repeatedly.
- _exit_tree(): shuts down the service when the autoload leaves the tree.

In FoundryObservabilityApi.fs, document the trait as the contract implemented by the autoload and document every abstract method with its parameters, defaults, return value, and failure behavior.

Use the existing FoundryScript style:

~~~
## Configures an observability provider and activates it after successful setup.
func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int:
~~~

- [ ] **Step 2: Document value types and severity constants.**

In ObservabilityLevel.fs, add comments immediately above TRACE, DEBUG, INFO, WARN, ERROR, and FATAL explaining their numeric order and intended severity. Document name() as returning the uppercase name or LEVEL(value) for unknown values.

In ObservabilityConfig.fs, document enabled, environment, release, and dist; document the constructor defaults; and document global_attributes() and provider_options() as deep-copying accessors whose contents are opaque to the core.

In ObservabilityException.fs, document the constructor parameters and all four accessors, including the fact that attributes are copied on construction and access.

In ObservabilityEvent.fs, document the constructor parameters/defaults and all accessors, including the event exception being optional and attributes being copied on construction and access.

- [ ] **Step 3: Document provider contracts and implementations.**

In ObservabilityProvider.fs, document each abstract method:

~~~
## Returns the stable provider identifier used by status and diagnostics.
abstract func provider_name() -> StringName
## Reports whether the backend can currently accept events.
abstract func is_available() -> bool
## Applies provider configuration and returns Error.OK or a failure code.
abstract func configure(config: ObservabilityConfig) -> int
## Captures one normalized event and returns a backend event ID or an empty string.
abstract func capture(event: ObservabilityEvent) -> String
## Flushes pending events within the requested timeout in milliseconds.
abstract func flush(timeout_msec: int = 2000) -> int
## Releases provider resources; repeated calls must be safe.
abstract func shutdown() -> void
~~~

In NullObservabilityProvider.fs, document that it is the safe pre-configuration/no-backend provider and that configure/flush/shutdown are no-op-safe.

In MemoryObservabilityProvider.fs, document configure_result, flush_result, last_flush_timeout_msec, flush_count, and shutdown_count as deterministic test controls. Document events() as returning a copy of captured events and clear() as removing captured events without changing provider configuration.

- [ ] **Step 4: Document the FoundryLib adapter.**

In foundrylib/FoundryLibObservabilitySink.fs, document the class, constructor parameters, emit(), and flush(). State that emit filters below the configured minimum, copies fields, adds logger_name, renders the message, maps known levels, preserves timestamps, and forwards through FoundryObservabilityApi. State that it never reports provider failures recursively through FoundryLib.

- [ ] **Step 5: Run lint after inline documentation.**

Run:

~~~
scripts/test-foundry-script
~~~

Expected: no parse errors, mixed namespace warnings, or warning-level diagnostics. Comments must not change runtime behavior.

- [ ] **Step 6: Commit inline documentation.**

~~~
git add addons/FoundryObservability
git commit -m "docs: document observability declarations inline"
~~~

### Task 4: Rewrite the canonical API reference and current docs

**Files:** Modify docs/API.md, README.md, BUILD.md, CONTRIBUTING.md, CHANGELOG.md, and scripts/test-foundry-script.

- [ ] **Step 1: Rebuild docs/API.md around the new namespace.**

The opening must identify foundry.observability as the canonical namespace and show:

~~~
import foundry.observability
FoundryObservability.configure(provider, config)
~~~

Include a public API index linking to every class/trait listed in the design spec.

- [ ] **Step 2: Document every public type and method in docs/API.md.**

Use the actual signatures and defaults from source. Include constructor signatures, constants, fields, methods, return types, disabled/null-provider behavior, error semantics, lifecycle order, defensive-copy guarantees, and the adapter mapping table. Explain that Error values are Foundry engine error codes and that provider capture failures return empty IDs and set last_error().

- [ ] **Step 3: Add complete usage examples.**

Include examples for:

~~~
import foundry.observability

var config := ObservabilityConfig.new(true, "production", "1.0.0")
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, config)
var event_id := FoundryObservability.capture_message(
		"game started", ObservabilityLevel.INFO, {"build": 42})
~~~

Also include exception capture, a minimal custom ObservabilityProvider outline, and explicit FoundryLib sink registration with foundry.logging.

- [ ] **Step 4: Update current docs and stale-reference contracts.**

Change README, BUILD, CONTRIBUTING, and CHANGELOG to use foundry.observability and describe the hard pre-release rename. Add a current-document guard to the validation flow or test package that rejects games.cafecito.foundryobservability in README.md, BUILD.md, CONTRIBUTING.md, CHANGELOG.md, and docs/API.md.

Implement the guard in scripts/test-foundry-script:

~~~
for current_doc in README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md; do
	if rg -n 'games\\.cafecito\\.foundryobservability' "$repo_root/$current_doc"; then
		fail "current documentation contains the removed namespace: $current_doc"
	fi
done
~~~

Historical docs/superpowers specs and plans remain exempt.

- [ ] **Step 5: Run documentation and hygiene checks.**

Run:

~~~
rg -n 'games\\.cafecito\\.foundryobservability' README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md
git diff --check
~~~

Expected: the namespace scan exits with no matches and diff checks report no whitespace errors.

- [ ] **Step 6: Commit the reference documentation.**

~~~
git add docs/API.md README.md BUILD.md CONTRIBUTING.md CHANGELOG.md
git commit -m "docs: fully document observability public API"
~~~

### Task 5: Complete verification

- [ ] **Step 1: Run all focused contracts.**

~~~
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-package
scripts/test-project
~~~

Expected: all commands exit 0, all 18 consumer tests pass, and the package contains only addons/FoundryObservability with the adapter under foundrylib/.

- [ ] **Step 2: Run the full repository gate.**

~~~
task test
~~~

Expected: prek, FoundryScript lint, UID checks, project consumer tests, CI workflow checks, and package checks all pass.

- [ ] **Step 3: Confirm current namespace coverage and clean state.**

~~~
if rg -n 'games\\.cafecito\\.foundryobservability' \
	addons test_project/tests scripts README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md; then
	exit 1
fi
git diff --check
git status --short
~~~

Expected: no current-source/document matches, no whitespace errors, and only generated ignored state outside Git.

- [ ] **Step 4: Commit no generated artifacts.**

Confirm test_project/.foundry, installed packages, and dist/ remain ignored/generated and are not staged. The final worktree must be clean.
