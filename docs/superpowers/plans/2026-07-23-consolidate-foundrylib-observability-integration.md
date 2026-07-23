# Consolidate FoundryLib Observability Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Move the FoundryLib LogSink adapter into the core FoundryObservability addon, remove the redundant integration addon, and update all package, test, script, and documentation contracts.

**Architecture:** FoundryLib continues to own the general-purpose foundry.logging framework. FoundryObservability owns the inbound adapter at addons/FoundryObservability/FoundryLibObservabilitySink.fs; its class name and namespace remain unchanged. The test project retains its pinned FoundryLib package, enables only the core Observability plugin, and exercises the adapter from the core addon.

**Tech Stack:** FoundryScript, Foundry headless CLI, FoundryLib foundry.logging, Bash contract scripts, Anvil package installation, Markdown, and tracked FoundryScript .uid files.

---

## File map

- addons/FoundryObservability/FoundryLibObservabilitySink.fs and its .uid: the adapter in the core addon, retaining its public API.
- addons/FoundryObservabilityFoundryLib/: deleted; it is no longer an installable addon.
- test_project/tests/project-wiring.test.fs: verifies the adapter is in core, FoundryLib is declared, and no separate plugin exists or is enabled.
- test_project/addons/FoundryObservabilityFoundryLib: deleted tracked symlink; the core addon symlink remains.
- scripts/test-foundry-script, scripts/test-foundry-uids, scripts/package-addon, scripts/test-package, scripts/test-project: validate one addon and one package payload.
- README.md, BUILD.md, CONTRIBUTING.md, CHANGELOG.md, docs/API.md: current user-facing documentation.

The earlier bootstrap specs and plans remain historical records and are not rewritten.

### Task 1: Add the failing project-wiring regression test

**Files:** Modify test_project/tests/project-wiring.test.fs.

- [ ] **Step 1: Replace the old optional-addon test.**

Replace test_project_contains_optional_foundrylib_integration_without_enabling_it() with:

~~~
func test_project_uses_foundrylib_adapter_from_core_addon() -> void:
	var sink_source: String = FileAccess.get_file_as_string(
			"res://addons/FoundryObservability/FoundryLibObservabilitySink.fs")
	Expect.that(sink_source).to_contain(
			"namespace games.cafecito.foundryobservability.foundrylib")
	Expect.that(sink_source).to_contain(
			"class_name FoundryLibObservabilitySink")
	Expect.that(sink_source).to_contain("uses LogSink")
	var packages_source: String = FileAccess.get_file_as_string(
			"res://packages.toml")
	Expect.that(packages_source).to_contain("[packages.foundrylib]")
	Expect.that(packages_source).to_contain(
			"url = \"https://github.com/cafecito-games/FoundryLib.git\"")
	var enabled_plugins: PackedStringArray = ProjectSettings.get_setting(
			"editor_plugins/enabled", PackedStringArray())
	Expect.that(
			"res://addons/FoundryObservability/plugin.cfg" in enabled_plugins).to_be_true()
	Expect.that(
			"res://addons/FoundryObservabilityFoundryLib/plugin.cfg" in enabled_plugins).to_be_false()
	Expect.that(FileAccess.file_exists(
			"res://addons/FoundryObservabilityFoundryLib/plugin.cfg")).to_be_false()
~~~

- [ ] **Step 2: Run scripts/test-project and verify this fails** because the core addon lacks the sink and the old integration symlink still exists.

- [ ] **Step 3: Commit the red test.**

~~~
git add test_project/tests/project-wiring.test.fs
git commit -m "test: require FoundryLib adapter in core addon"
~~~

### Task 2: Move the adapter and remove the redundant addon

**Files:** Create addons/FoundryObservability/FoundryLibObservabilitySink.fs and its .uid; delete the three files under addons/FoundryObservabilityFoundryLib/ and the tracked symlink test_project/addons/FoundryObservabilityFoundryLib.

- [ ] **Step 1: Add the sink at the core path with the unchanged API.**

Create addons/FoundryObservability/FoundryLibObservabilitySink.fs with the existing sink source copied exactly. It must retain this namespace, class, and behavior:

~~~
namespace games.cafecito.foundryobservability.foundrylib

import foundry.logging
import games.cafecito.foundryobservability

## Forwards selected FoundryLib records into the core observability API.
class_name FoundryLibObservabilitySink
extends RefCounted
uses LogSink

var _service: FoundryObservabilityApi
var _minimum_level: int

func _init(p_service: FoundryObservabilityApi, p_minimum_level: int = ObservabilityLevel.ERROR) -> void:
	_service = p_service
	_minimum_level = p_minimum_level

func emit(record: LogRecord) -> void:
	if record == null or record.level < _minimum_level or _service == null:
		return
	var attributes: Dictionary = record.fields.duplicate(true)
	attributes["logger_name"] = record.logger_name
	var event_level: int = _map_level(record.level)
	var event: ObservabilityEvent = ObservabilityEvent.new(
			&"log", event_level, LogFormatter.render_message(record),
			&"foundry.logging", record.timestamp_msec, attributes)
	_service.capture_event(event)

func flush() -> void:
	if _service == null:
		return
	var _result: int = _service.flush()

func _map_level(level: int) -> int:
	match level:
		LogLevel.TRACE: return ObservabilityLevel.TRACE
		LogLevel.DEBUG: return ObservabilityLevel.DEBUG
		LogLevel.INFO: return ObservabilityLevel.INFO
		LogLevel.WARN: return ObservabilityLevel.WARN
		LogLevel.ERROR: return ObservabilityLevel.ERROR
		LogLevel.FATAL: return ObservabilityLevel.FATAL
	return ObservabilityLevel.INFO
~~~

Use the existing source verbatim rather than reformatting it; preserve its full formatting, defensive checks, rendered message handling, timestamp and field copying, explicit level mapping, and flush forwarding.

Create addons/FoundryObservability/FoundryLibObservabilitySink.fs.uid containing:

~~~
uid://cgcvd1krt7on8
~~~

- [ ] **Step 2: Delete the old addon and symlink.**

~~~
git rm addons/FoundryObservabilityFoundryLib/plugin.cfg \
	addons/FoundryObservabilityFoundryLib/FoundryLibObservabilitySink.fs \
	addons/FoundryObservabilityFoundryLib/FoundryLibObservabilitySink.fs.uid \
	test_project/addons/FoundryObservabilityFoundryLib
rmdir addons/FoundryObservabilityFoundryLib
~~~

Do not remove test_project/addons/FoundryObservability.

- [ ] **Step 3: Run scripts/test-project.** Expected: project wiring, core, and all three existing FoundryLib sink behavior tests pass.

- [ ] **Step 4: Commit the move.**

~~~
git add addons/FoundryObservability/FoundryLibObservabilitySink.fs \
	addons/FoundryObservability/FoundryLibObservabilitySink.fs.uid
git commit -m "refactor: consolidate FoundryLib adapter into core addon"
~~~

### Task 3: Update validation and packaging scripts

**Files:** Modify scripts/test-foundry-script, scripts/test-foundry-uids, scripts/package-addon, scripts/test-package, and scripts/test-project.

- [ ] **Step 1: Consolidate scripts/test-foundry-script.** Remove integration and test_integration variables and all separate-plugin checks. Check the sink at $addon/FoundryLibObservabilitySink.fs, scan only $addon, materialize and restore only $test_addon, and lint only:

~~~
"$foundry_bin" --headless script lint \
	--project "$project_dir" \
	--fail-on=warning \
	addons/FoundryObservability
~~~

The script must contain no FoundryObservabilityFoundryLib path.

- [ ] **Step 2: Reduce scripts/test-foundry-uids to the core directory.** Preserve the existing UID format, Git tracking, and success checks, but replace the two-entry loop with:

~~~
addon="$repo_root/addons/FoundryObservability"
[[ -d "$addon" ]] || fail "addon directory is missing"
while IFS= read -r resource; do
	uid_file="$resource.uid"
	[[ -f "$uid_file" ]] || fail "missing UID companion"
	uid=$(<"$uid_file")
	[[ "$uid" =~ ^uid://[a-z0-9]+$ ]] || fail "invalid UID"
	relative="$uid_file"
	git -C "$repo_root" check-ignore -q "$relative" && fail "$relative is ignored by Git"
	git -C "$repo_root" ls-files --error-unmatch "$relative" >/dev/null 2>&1 || fail "$relative is not tracked by Git"
done < <(find "$addon" -type f -name '*.fs' | sort)
~~~

- [ ] **Step 3: Make scripts/package-addon use only core_source and source_version.** Remove integration version checks, copy only the core directory, apply version overrides only to its plugin.cfg, and zip only:

~~~
zip -qr "$archive" addons/FoundryObservability
~~~

- [ ] **Step 4: Update scripts/test-package.** Require addons/FoundryObservability/FoundryLibObservabilitySink.fs, scan UIDs only under addons/FoundryObservability, and reject:

~~~
if grep -q '^addons/FoundryObservabilityFoundryLib/' <<<"$listing"; then
	fail "package contains the removed FoundryLib integration addon"
fi
~~~

- [ ] **Step 5: Update scripts/test-project.** Remove integration_addon and its cleanup/materialization blocks. Keep Anvil installation and the pinned FoundryLib package. The only local addon materialization is:

~~~
if [[ -L "$core_addon" ]]; then
	rm "$core_addon"
	cp -R "$repo_root/addons/FoundryObservability" "$core_addon"
fi
~~~

The EXIT restore recreates only the core addon symlink.

- [ ] **Step 6: Run the four scripts.**

~~~
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-package
scripts/test-project
~~~

Expected: all exit 0, the archive contains only addons/FoundryObservability, and the test project restores only its core symlink.

- [ ] **Step 7: Commit script changes.**

~~~
git add scripts/test-foundry-script scripts/test-foundry-uids \
	scripts/package-addon scripts/test-package scripts/test-project
git commit -m "test: align contracts with consolidated addon package"
~~~

### Task 4: Update current documentation

**Files:** Modify README.md, BUILD.md, CONTRIBUTING.md, CHANGELOG.md, and docs/API.md.

- [ ] **Step 1: Document the required dependency and single package.** In README.md, say the FoundryLib sink is included in the core addon and replace the old optional-addon paragraph with:

~~~
FoundryLib is a required dependency of the addon because the included
FoundryLibObservabilitySink integrates with FoundryLib's structured logging.
Install the FoundryLib package before importing or enabling the addon.
~~~

In BUILD.md, say project tests install FoundryLib and the release archive contains only addons/FoundryObservability. In CONTRIBUTING.md, state that FoundryLib owns logging while the core addon includes the adapter and provider/native work remains behind ObservabilityProvider.

- [ ] **Step 2: Update release history.** Replace the optional-sink bullet in CHANGELOG.md with:

~~~
- Included the FoundryLib logging sink in the core addon and removed the
  redundant integration addon; FoundryLib remains a required package
  dependency.
~~~

- [ ] **Step 3: Update docs/API.md.** Replace the claim that core does not depend on FoundryLib with the provider-neutral-contract/required-package statement. Add the package requirement to Setup, rename “Optional FoundryLib integration” to “FoundryLib integration”, and open it with:

~~~
The core addon includes the explicit FoundryLibObservabilitySink adapter.
FoundryLib must be installed as a project package before the source is imported;
the adapter does not install itself or register an autoload.
~~~

Keep the existing adapter example and behavior bullets unchanged.

- [ ] **Step 4: Check current docs for stale claims.**

~~~
rg -n 'Install addons/FoundryObservabilityFoundryLib|optional FoundryLib|separate addon|core addon must not depend on FoundryLib' \
	README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md
~~~

Expected: no matches. Historical specs and plans may retain the superseded architecture.

- [ ] **Step 5: Commit docs.**

~~~
git add README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md
git commit -m "docs: document FoundryLib as core dependency"
~~~

### Task 5: Complete verification

- [ ] **Step 1:** Run git status --short and git diff --check HEAD~4..HEAD. Expected: no generated files, dist changes, stale tracked integration addon, or whitespace errors.

- [ ] **Step 2:** Run task test. Expected: prek, FoundryScript lint, UID checks, project consumer tests, workflow checks, and package checks all pass; the three FoundryLib sink tests pass and the package contains one addon.

- [ ] **Step 3:** Run git status --short and git log --oneline -5. Expected: the worktree is clean and the recent commits are the focused test, source move, contract, and documentation commits plus the approved design and plan commits.
