# FoundryObservability Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Create an installable, parser-validated FoundryScript observability addon skeleton with local tooling, a consumer test project, pull-request CI, and semver release packaging.

**Architecture:** Keep the runtime addon behavior-free. The editor plugin registers a minimal autoload implementing an empty public contract marker. A separate consumer project symlinks the addon, installs FoundryLib through Anvil, and runs project-wiring tests. Shell contract scripts and Taskfile commands are reused by GitHub Actions.

**Tech Stack:** Foundry v0.1.0-alpha.7, FoundryScript, FoundryLib testlib, Anvil, Task, prek, Bash, GitHub Actions, zip/unzip.

---

### Task 1: Add repository metadata and documentation

**Files:** Create .gitignore, .editorconfig, .pre-commit-config.yaml, requirements.txt, BUILD.md, CHANGELOG.md, CONTRIBUTING.md; replace README.md.

- [ ] **Step 1: Add ignore rules**

Create .gitignore:

~~~gitignore
.foundry/
.godot/
.import/
*.import
*.translation
*.tmp
export.cfg
export_credentials.cfg
export_presets.cfg
test_project/.foundry/
test_project/addons/foundrylib/
test_project/addons/vest/
test_project/vest.log
.build/
.task/
dist/
.venv/
venv/
__pycache__/
*.pyc
.*_cache/
.idea/
.vscode/
.zed/
.DS_Store
._*
.worktrees/
~~~

Do not ignore *.fs.uid; tracked FoundryScript UID companions are part of the addon payload.

- [ ] **Step 2: Add formatting and hygiene configuration**

Create .editorconfig with UTF-8, LF, final newline, trimmed whitespace; tabs of width 4 for *.fs; and two-space indentation for Markdown/YAML/TOML/JSON.

Create .pre-commit-config.yaml using pre-commit-hooks v5.0.0 with trailing-whitespace, end-of-file-fixer, check-yaml, check-json, check-added-large-files --maxkb=4096, and mixed-line-ending --fix=lf. Exclude test_project/addons/foundrylib and *.uid.

Create requirements.txt:

~~~text
gdtoolkit==4.5.0
~~~

- [ ] **Step 3: Write documentation**

Replace README.md with the addon purpose, behavior-free bootstrap status, public namespace games.cafecito.foundryobservability, installation by copying addons/FoundryObservability and enabling the editor plugin, and task test.

Create BUILD.md with prerequisites Foundry v0.1.0-alpha.7, Anvil, Task, Python 3.12+, prek, ripgrep, zip, and unzip, plus task lint, task test:foundry-script, task test:project, task test:ci, task test:package, task test, and task package. Document FOUNDRY_BIN.

Create CHANGELOG.md with an Unreleased 2026-07-23 entry for the addon skeleton, test project, validation, PR CI, and release packaging.

Create CONTRIBUTING.md requiring typed/provider-neutral APIs, tracked FoundryScript UID companions, and task test before review.

- [ ] **Step 4: Verify and commit metadata**

~~~sh
git diff --check
git add .gitignore .editorconfig .pre-commit-config.yaml requirements.txt README.md BUILD.md CHANGELOG.md CONTRIBUTING.md
git commit -m "chore: bootstrap repository metadata"
~~~

Expected: git diff --check exits 0 and the commit contains only metadata/docs.

### Task 2: Add the minimal FoundryScript addon

**Files:** Create addons/FoundryObservability/plugin.cfg, export_plugin.fs, FoundryObservability.fs, and FoundryObservabilityApi.fs.

- [ ] **Step 1: Add the descriptor**

~~~ini
[plugin]

name="FoundryObservability"
description="FoundryScript observability API and provider integrations for games"
author="CafecitoGames"
version="0.1.0"
script="export_plugin.fs"
~~~

- [ ] **Step 2: Add the editor plugin**

Create export_plugin.fs:

~~~foundryscript
@tool
namespace games.cafecito.foundryobservability

extends EditorPlugin

func _enable_plugin() -> void:
	add_autoload_singleton(
			"FoundryObservability",
			"res://addons/FoundryObservability/FoundryObservability.fs")

func _disable_plugin() -> void:
	remove_autoload_singleton("FoundryObservability")
~~~

- [ ] **Step 3: Add the empty public marker and autoload**

Create FoundryObservabilityApi.fs:

~~~foundryscript
namespace games.cafecito.foundryobservability

## Public contract marker for the future FoundryObservability autoload API.
trait_name FoundryObservabilityApi

const CONTRACT_MARKER: String = "FoundryObservability"
~~~

Create FoundryObservability.fs. Keep the marker file separate but do not add a
uses clause yet; Foundry's current analyzer rejects a behavior-free marker in
an autoload's uses list, and the first real API method will establish that
contract in a later change:

~~~foundryscript
@autoload
namespace games.cafecito.foundryobservability

## Autoload entry point for the future game observability API.
class_name FoundryObservability extends Node
~~~

- [ ] **Step 4: Link the addon into the test project and commit sources**

~~~sh
mkdir -p test_project/addons
ln -s ../../addons/FoundryObservability test_project/addons/FoundryObservability
git add addons/FoundryObservability test_project/addons/FoundryObservability
git commit -m "feat: add FoundryObservability addon skeleton"
~~~

### Task 3: Add the consumer test project

**Files:** Create test_project/project.foundry, packages.toml, packages.lock, and tests/project-wiring.test.fs.

- [ ] **Step 1: Add project wiring**

Use config_version=5, project name FoundryObservabilityTest, the autoload
FoundryObservability="*res://addons/FoundryObservability/FoundryObservability.fs",
and enabled plugin res://addons/FoundryObservability/plugin.cfg.

Under [debug], add exactly:

~~~ini
foundry_script/warnings/directory_rules={
"res://addons/foundrylib": 0,
"res://addons": 1
}
foundry_script/warnings/inferred_declaration=1
foundry_script/warnings/unsafe_call_argument=2
foundry_script/warnings/unsafe_cast=2
foundry_script/warnings/unsafe_method_access=2
foundry_script/warnings/unsafe_property_access=2
foundry_script/warnings/untyped_declaration=2
~~~

- [ ] **Step 2: Add the FoundryLib package**

Create packages.toml:

~~~toml
[packages]
  [packages.foundrylib]
    source = "git"
    url = "https://github.com/cafecito-games/FoundryLib.git"
    version = "1e0a7d707562d4ef4435b6b2015bbb5d84266ecb"
    source_path = "addons/foundrylib"
    install_as = "foundrylib"
~~~

Create packages.lock with the same resolved version and source path, and spec hash 574b1acf7f65afd308272014c87b5664a8e9d092cca71a127a1eba329c32df31.

- [ ] **Step 3: Add project-wiring tests**

Create a ProjectWiringTests class in namespace
games.cafecito.foundryobservability.tests, importing foundry.testlib,
extending RefCounted, and using Test.

Test that the autoload setting ends with
addons/FoundryObservability/FoundryObservability.fs, the enabled plugin list
contains res://addons/FoundryObservability/plugin.cfg, all six strict warning
settings equal the values above, and the project source contains
foundry_script/warnings/directory_rules but none of the legacy
debug/gdscript/warnings/* keys.

- [ ] **Step 4: Install packages, import, generate UIDs, and commit**

~~~sh
anvil pkg install --dir test_project
"${FOUNDRY_BIN:-/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64}" \
	--headless project import --project test_project
git add test_project
git commit -m "test: add FoundryObservability consumer project"
~~~

Expected: package installation creates ignored test_project/addons/foundrylib,
import exits 0, and every tracked *.fs resource has a generated tracked
*.fs.uid companion containing uid://[a-z0-9]+.

### Task 4: Add local validation and packaging scripts

**Files:** Create executable scripts/test-foundry-script, test-project,
test-ci-workflows, package-addon, test-package, and test-foundry-uids.

- [ ] **Step 1: Implement FoundryScript checks**

scripts/test-foundry-script must resolve FOUNDRY_BIN, foundry on PATH, or the
local macOS binary; require all four addon files; use rg to check the descriptor
name/version, namespace, class name, and trait_name, and
add_autoload_singleton; reject legacy names; run:

~~~sh
foundry project import --project "$repo_root/test_project"
foundry script lint --project "$repo_root/test_project" --fail-on=warning addons/FoundryObservability
~~~

Capture import output and fail on SCRIPT ERROR, Parse Error, or Failed to load script.

- [ ] **Step 2: Implement project tests**

scripts/test-project must require Anvil, install packages unless
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1, resolve Foundry, and run:

~~~sh
foundry project test \
	--project "$repo_root/test_project" \
	--runner res://addons/foundrylib/testlib/cli/run.fs \
	-- --path res://tests
~~~

The --path after -- is a FoundryLib runner argument.

- [ ] **Step 3: Implement workflow and UID checks**

scripts/test-ci-workflows must check both workflow files for the
v0.1.0-alpha.7 pin, foundry project import --project, task test, PR
contents: read, release bump choices patch/minor/major, GITHUB_OUTPUT, and
gh release create. Reject deprecated command patterns --headless --path,
--headless --editor, and --check-only.

scripts/test-foundry-uids must require tracked, non-ignored UID companions
with valid uid://[a-z0-9]+ values for addon *.fs files.

- [ ] **Step 4: Implement packaging and package checks**

scripts/package-addon must read the descriptor version, accept a numeric VERSION
override, stage only addons/FoundryObservability, substitute the staged
descriptor version when overridden, and create
dist/FoundryObservability-VERSION.zip with zip -qr.

scripts/test-package must invoke the packager, require the descriptor, autoload,
public marker, and UID files in the zip, and reject repository state such as
test_project, .foundry, .git, .build, and .DS_Store.

- [ ] **Step 5: Make scripts executable, check syntax, and commit**

~~~sh
chmod +x scripts/test-foundry-script scripts/test-project scripts/test-ci-workflows scripts/package-addon scripts/test-package scripts/test-foundry-uids
for script in scripts/test-foundry-script scripts/test-project scripts/test-ci-workflows scripts/package-addon scripts/test-package scripts/test-foundry-uids; do bash -n "$script"; done
git add scripts
git commit -m "test: add addon and package contract checks"
~~~

Expected: all shell syntax checks exit 0.

### Task 5: Add Taskfile entry points

**Files:** Create Taskfile.yml.

- [ ] **Step 1: Define tasks**

Create tasks named lint, test:foundry-script, test:project, test:ci,
test:package, test, and package. Use this task graph:

~~~yaml
version: '3'

tasks:
  default:
    cmds:
      - task: test
  lint:
    cmds:
      - prek run --all-files
  test:foundry-script:
    cmds:
      - scripts/test-foundry-script
      - scripts/test-foundry-uids
  test:project:
    cmds:
      - scripts/test-project
  test:ci:
    cmds:
      - scripts/test-ci-workflows
  test:package:
    cmds:
      - scripts/test-package
  test:
    deps:
      - lint
      - test:foundry-script
      - test:project
      - test:ci
      - test:package
  package:
    cmds:
      - scripts/package-addon
~~~

- [ ] **Step 2: Validate and commit**

~~~sh
task --list
git diff --check
git add Taskfile.yml
git commit -m "chore: add local validation tasks"
~~~

Expected: task --list includes all named tasks and exits 0.

### Task 6: Add pull-request CI

**Files:** Create .github/workflows/pr-check.yml.

- [ ] **Step 1: Create the workflow**

Use a macOS pull_request job targeting main with contents: read and
FOUNDRY_VERSION: "v0.1.0-alpha.7". Steps must be ordered: checkout; Python
3.12; install requirements.txt, prek, and ripgrep; install Task with
arduino/setup-task@v2 and the GitHub token; install Go >=1.26.2; install Anvil;
download and unpack Foundry_VERSION_macos.universal.zip from the Foundry
release URL and export FOUNDRY_BIN; run task test with GH_TOKEN.

Use actions/checkout@v4, actions/setup-python@v5, actions/setup-go@v5, and
arduino/setup-task@v2. The Foundry archive name must interpolate the workflow
FOUNDRY_VERSION environment value.

- [ ] **Step 2: Validate and commit PR CI**

~~~sh
scripts/test-ci-workflows
git add .github/workflows/pr-check.yml
git commit -m "ci: validate FoundryObservability pull requests"
~~~

Expected: CI workflow contract checks passed.

### Task 7: Add manual semver release packaging

**Files:** Create .github/workflows/release.yml.

- [ ] **Step 1: Add release calculation and setup**

Create a workflow_dispatch workflow with patch, minor, and major choices and
contents: write. Fetch full history, compute the latest v*.*.* tag with
git tag --sort=-v:refname, calculate the next version, reject an existing tag,
and write version and tag to GITHUB_OUTPUT. Use the same Foundry
v0.1.0-alpha.7 installation and validation setup as PR CI.

- [ ] **Step 2: Package and publish**

Run task test, then package with the computed version and publish:

~~~sh
task package VERSION="${{ steps.version.outputs.version }}"
gh release create "${{ steps.version.outputs.tag }}" \
  "dist/FoundryObservability-${{ steps.version.outputs.version }}.zip" \
  --title "FoundryObservability ${{ steps.version.outputs.tag }}" \
  --generate-notes
~~~

In the workflow, set VERSION from steps.version.outputs.version and use the
steps.version.outputs.tag and steps.version.outputs.version expressions in the
release command. Do not push directly to main and do not require provider
credentials.

- [ ] **Step 3: Validate and commit**

~~~sh
scripts/test-ci-workflows
git add .github/workflows/release.yml
git commit -m "ci: add FoundryObservability release workflow"
~~~

### Task 8: Run the complete verification gate

- [ ] **Step 1: Run syntax and whitespace checks**

~~~sh
for script in scripts/test-foundry-script scripts/test-project scripts/test-ci-workflows scripts/package-addon scripts/test-package scripts/test-foundry-uids; do bash -n "$script"; done
git diff --check
~~~

Expected: exit 0 with no whitespace errors.

- [ ] **Step 2: Install dependencies and run all tests**

~~~sh
pip install -r requirements.txt
go install github.com/cafecito-games/foundry-tools/cmd/anvil@latest
export PATH="$(go env GOPATH)/bin:$PATH"
FOUNDRY_BIN="${FOUNDRY_BIN:-/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64}" task test
~~~

Expected: lint, Foundry import/lint, FoundryLib wiring tests, workflow checks,
and zip checks all pass.

- [ ] **Step 3: Inspect package and status**

~~~sh
unzip -Z1 dist/FoundryObservability-0.1.0.zip
git status --short --branch
~~~

Expected: the zip contains only addons/FoundryObservability runtime files and
UID companions; generated dist, installed packages, and Foundry state are
ignored; no unrelated files are modified.

- [ ] **Step 4: Commit generated tracked UID files if needed**

If import generated new tracked UID companions after their source commit, add
only those UID files:

~~~sh
git add addons/FoundryObservability/*.fs.uid test_project/tests/*.fs.uid
git commit -m "chore: track FoundryScript resource UIDs"
~~~

Do not commit dist, test_project/addons/foundrylib, or generated Foundry state.

## Plan self-review

- The behavior-free addon boundary is covered by Tasks 2 and 3.
- The approved namespace, initial 0.1.0 version, and Foundry v0.1.0-alpha.7 pin
  are checked by source validation, project wiring, and both workflows.
- Documentation, local commands, package validation, PR CI, and manual semver
  release are covered by Tasks 1, 4, 5, 6, and 7.
- The current command-first Foundry CLI is used; --path appears only after --
  as a FoundryLib runner argument.
- No provider implementation or provider credential is introduced.
- No unresolved design marker or incomplete implementation step remains.
