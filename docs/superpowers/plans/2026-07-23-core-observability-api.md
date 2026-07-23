# Core Observability API Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Replace the empty FoundryObservability bootstrap with a typed provider-neutral event API, a null/test provider, and an optional FoundryLib logging adapter.

**Architecture:** Keep the core addon independent of FoundryLib and native SDKs. FoundryObservability owns typed event construction, provider configuration, capture dispatch, error state, and lifecycle. A separate FoundryObservabilityFoundryLib addon implements LogSink and forwards structured log records into the core API.

**Tech Stack:** Foundry v0.1.0-alpha.7, FoundryScript, FoundryLib logging/testlib, Anvil, Task, Bash, zip/unzip.

---

## File map

~~~text
addons/FoundryObservability/
  FoundryObservability.fs
  FoundryObservabilityApi.fs
  ObservabilityLevel.fs
  ObservabilityConfig.fs
  ObservabilityException.fs
  ObservabilityEvent.fs
  ObservabilityProvider.fs
  NullObservabilityProvider.fs
  MemoryObservabilityProvider.fs

addons/FoundryObservabilityFoundryLib/
  plugin.cfg
  FoundryLibObservabilitySink.fs

test_project/tests/
  observability-core.test.fs
  observability-foundrylib.test.fs
~~~

Every new FoundryScript resource needs a tracked .fs.uid companion. The test
project will symlink both addon directories and already pins FoundryLib.

## Task 1: Add typed core value objects

**Files:**

- Create: addons/FoundryObservability/ObservabilityLevel.fs
- Create: addons/FoundryObservability/ObservabilityConfig.fs
- Create: addons/FoundryObservability/ObservabilityException.fs
- Create: addons/FoundryObservability/ObservabilityEvent.fs
- Create: test_project/tests/observability-core.test.fs
- Generate and track UID companions for the new resources

- [ ] **Step 1: Write failing value-object tests**

Create ObservabilityCoreTests with tests that:

~~~foundryscript
func test_levels_are_ordered_and_named() -> void:
	Expect.that(ObservabilityLevel.TRACE).to_be_less_than(ObservabilityLevel.DEBUG)
	Expect.that(ObservabilityLevel.DEBUG).to_be_less_than(ObservabilityLevel.INFO)
	Expect.that(ObservabilityLevel.INFO).to_be_less_than(ObservabilityLevel.WARN)
	Expect.that(ObservabilityLevel.WARN).to_be_less_than(ObservabilityLevel.ERROR)
	Expect.that(ObservabilityLevel.ERROR).to_be_less_than(ObservabilityLevel.FATAL)
	Expect.that(ObservabilityLevel.name(ObservabilityLevel.ERROR)).to_equal("ERROR")


func test_exception_and_event_copy_attributes() -> void:
	var source := {"request_id": "abc", "nested": {"attempt": 1}}
	var exception := ObservabilityException.new("InvalidState", "bad state", "stack", source)
	source["request_id"] = "changed"
	var event_source := {"scene": "battle"}
	var event := ObservabilityEvent.new(
			&"exception", ObservabilityLevel.ERROR, "bad state",
			&"game", 1234, event_source, exception)
	event_source["scene"] = "changed"
	var exposed: Dictionary = event.attributes()
	exposed["new_field"] = true

	Expect.that(exception.attributes()).to_equal({
			"request_id": "abc", "nested": {"attempt": 1}
		})
	Expect.that(event.kind()).to_equal(&"exception")
	Expect.that(event.exception()).to_equal(exception)
	Expect.that(event.timestamp_msec()).to_equal(1234)
	Expect.that(event.attributes()).to_equal({"scene": "battle"})


func test_config_copies_attributes_and_options() -> void:
	var attributes := {"build": 42}
	var options := {"provider_key": "value"}
	var config := ObservabilityConfig.new(
			true, "production", "1.2.3", "arm64", attributes, options)
	attributes["build"] = 99
	options["provider_key"] = "changed"

	Expect.that(config.enabled).to_be_true()
	Expect.that(config.environment).to_equal("production")
	Expect.that(config.global_attributes()).to_equal({"build": 42})
	Expect.that(config.provider_options()).to_equal({"provider_key": "value"})
~~~

Use namespace games.cafecito.foundryobservability.tests, import
foundry.testlib and games.cafecito.foundryobservability, extend RefCounted, and
use Test.

- [ ] **Step 2: Run the focused test and verify the failure**

~~~sh
scripts/test-project
~~~

Expected: discovery or parsing fails because the four value classes do not
exist.

- [ ] **Step 3: Implement the value classes**

ObservabilityLevel is a RefCounted class with constants TRACE=10, DEBUG=20,
INFO=30, WARN=40, ERROR=50, FATAL=60 and static name(level) returning the
uppercase name or LEVEL(value).

ObservabilityException is a RefCounted class with private type-name, message,
stack-trace, and attributes fields. Its constructor accepts
(type:String="Error", message:String="", stack_trace:String="", attributes:Dictionary={}),
deep-copies the dictionary, and exposes type_name(), message(), stack_trace(),
and a deep-copying attributes().

ObservabilityEvent is a RefCounted class with private kind:StringName, level:int,
message:String, source:StringName, timestamp_msec:int, attributes:Dictionary,
and exception:ObservabilityException? fields. Its constructor accepts those
values in that order with defaults of message/INFO/empty/empty/0/empty/null.
Provide kind(), level(), message(), source(), timestamp_msec(), attributes(),
and exception() accessors. Deep-copy dictionaries at construction and access.

ObservabilityConfig is a RefCounted class with public enabled, environment,
release, and dist fields, private global-attributes and provider-options
dictionaries, and constructor arguments
(enabled=true, environment="", release="", dist="", global_attributes={},
provider_options={}). Provide deep-copying global_attributes() and
provider_options() accessors.

- [ ] **Step 4: Run tests and UID validation**

~~~sh
scripts/test-project
scripts/test-foundry-uids
~~~

Expected: all value tests pass and every new resource has a tracked UID.

- [ ] **Step 5: Commit the value-object slice**

~~~sh
git add addons/FoundryObservability test_project/tests/observability-core.test.fs
git commit -m "feat: add observability value objects"
~~~

## Task 2: Add provider contracts and the autoload service

**Files:**

- Modify: addons/FoundryObservability/FoundryObservabilityApi.fs
- Modify: addons/FoundryObservability/FoundryObservability.fs
- Create: addons/FoundryObservability/ObservabilityProvider.fs
- Create: addons/FoundryObservability/NullObservabilityProvider.fs
- Create: addons/FoundryObservability/MemoryObservabilityProvider.fs
- Modify: test_project/tests/observability-core.test.fs
- Generate and track UID companions

- [ ] **Step 1: Write failing provider lifecycle tests**

Add tests for default null behavior, successful MemoryObservabilityProvider
capture, disabled capture, failed replacement, timeout forwarding, and
idempotent shutdown. Add a separate test that configuring the already-active
provider updates its config without incrementing its shutdown count. The core
assertions are:

~~~foundryscript
var service := FoundryObservability.new()
var provider := MemoryObservabilityProvider.new()
Expect.that(service.provider_name()).to_equal(&"null")
Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
Expect.that(service.capture_message(
		"hello", ObservabilityLevel.WARN, {"screen": "title"})).to_equal("memory:1")
Expect.that(service.capture_exception(
		ObservabilityException.new("Error", "boom", "trace"))).to_equal("memory:2")
Expect.that(provider.events()).to_have_size(2)
~~~

Also set a failing provider's configure_result to Error.FAILED and assert the
working provider remains active with last_error() equal to Error.FAILED.
Configure a disabled config, assert capture returns an empty ID, call shutdown
twice, and assert the test provider shutdown count is one.

- [ ] **Step 2: Run the focused test and verify the provider API is missing**

~~~sh
scripts/test-project
~~~

Expected: parsing or discovery fails because the provider trait, provider
classes, and autoload methods do not exist.

- [ ] **Step 3: Define the provider trait and implementations**

Create ObservabilityProvider.fs:

~~~foundryscript
namespace games.cafecito.foundryobservability

trait_name ObservabilityProvider

abstract func provider_name() -> StringName
abstract func is_available() -> bool
abstract func configure(config: ObservabilityConfig) -> int
abstract func capture(event: ObservabilityEvent) -> String
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
~~~

NullObservabilityProvider extends RefCounted, uses the trait, returns &"null",
reports unavailable, returns Error.OK for configure/flush, returns an empty
capture ID, and has an idempotent no-op shutdown.

MemoryObservabilityProvider extends RefCounted, uses the trait, stores
configure_result, flush_result, last_flush_timeout_msec, flush_count, and
shutdown_count, and exposes events() -> Array[ObservabilityEvent] and clear().
It appends exact event objects, returns memory:1/memory:2 IDs, skips capture
while disabled, and shuts down once.

- [ ] **Step 4: Replace the public marker and implement the autoload**

Replace FoundryObservabilityApi.fs with:

~~~foundryscript
namespace games.cafecito.foundryobservability

trait_name FoundryObservabilityApi

abstract func configure(provider: ObservabilityProvider, config: ObservabilityConfig? = null) -> int
abstract func is_enabled() -> bool
abstract func is_available() -> bool
abstract func provider_name() -> StringName
abstract func last_error() -> int
abstract func capture_event(event: ObservabilityEvent) -> String
abstract func capture_message(message: String, level: int = ObservabilityLevel.INFO, attributes: Dictionary = {}) -> String
abstract func capture_exception(exception: ObservabilityException, attributes: Dictionary = {}) -> String
abstract func flush(timeout_msec: int = 2000) -> int
abstract func shutdown() -> void
~~~

Update FoundryObservability.fs to use @autoload, extends Node, and uses the
trait. Store a NullObservabilityProvider, disabled config, Error.OK last error,
and an idempotent shutdown flag.

Implement these rules:

1. A null config becomes ObservabilityConfig.new(false).
2. configure configures the candidate before replacing the active provider.
3. Failure leaves the current provider/config unchanged and updates last_error.
4. If the candidate is the active provider, success updates its config without
   shutting it down.
5. If the candidate differs from the active provider, success shuts down the
   old provider once, stores candidate/config, clears the error, and enables the
   service.
6. capture_message creates a message event with source &"game" and current
   Time.get_ticks_msec().
7. capture_exception creates an exception event with source &"game" and the
   exception message as its event message.
8. capture_event returns an empty ID while disabled and stores Error.FAILED if
   an enabled provider returns an empty ID.
9. flush forwards the timeout and stores the provider error.
10. shutdown is idempotent, flushes/shuts down once, restores disabled null state,
   and is called from _exit_tree.

- [ ] **Step 5: Run core tests, strict lint, and UID validation**

~~~sh
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
~~~

Expected: all core suites pass with no FoundryScript warnings.

- [ ] **Step 6: Commit the provider slice**

~~~sh
git add addons/FoundryObservability test_project/tests/observability-core.test.fs
git commit -m "feat: add provider-neutral observability service"
~~~

## Task 3: Add the optional FoundryLib LogSink addon

**Files:**

- Create: addons/FoundryObservabilityFoundryLib/plugin.cfg
- Create: addons/FoundryObservabilityFoundryLib/FoundryLibObservabilitySink.fs
- Create: test_project/addons/FoundryObservabilityFoundryLib symlink
- Create: test_project/tests/observability-foundrylib.test.fs
- Generate and track the integration UID

- [ ] **Step 1: Write failing structured-log tests**

Create a suite that injects a MemoryObservabilityProvider into a local service,
constructs FoundryLibObservabilitySink(service, ObservabilityLevel.INFO), and
emits:

~~~foundryscript
sink.emit(LogRecord.new(
		LogLevel.WARN,
		"combat",
		"player {id} missed",
		{"id": 7, "weapon": "axe"},
		99))
~~~

Assert event kind log, WARN level, rendered message player 7 missed, source
foundry.logging, timestamp 99, and attributes logger_name=combat plus the
original fields. Add tests that WARN is filtered with minimum ERROR and that
sink.flush() forwards to the provider.

- [ ] **Step 2: Run the integration test and verify the sink is missing**

~~~sh
scripts/test-project
~~~

Expected: discovery fails because FoundryLibObservabilitySink is undefined.

- [ ] **Step 3: Implement the optional addon and sink**

Create plugin.cfg:

~~~ini
[plugin]

name="FoundryObservabilityFoundryLib"
description="FoundryLib logging integration for FoundryObservability"
author="Cafecito Games"
version="0.1.0"
~~~

Create the tracked symlink
test_project/addons/FoundryObservabilityFoundryLib -> ../../addons/FoundryObservabilityFoundryLib.

Create FoundryLibObservabilitySink.fs in namespace
games.cafecito.foundryobservability.foundrylib, import foundry.logging and
games.cafecito.foundryobservability, extend RefCounted, and use LogSink.

The constructor accepts a FoundryObservabilityApi target and a minimum level
defaulting to ObservabilityLevel.ERROR. emit(record) returns below the
threshold, deep-copies fields, adds logger_name, maps all six LogLevel constants
explicitly (unknown to INFO), renders with LogFormatter.render_message(record),
constructs a log event with source &"foundry.logging" and the record timestamp,
and forwards it without calling Log. flush() calls the service flush and stores
the discarded result in an underscore-prefixed local.

- [ ] **Step 4: Run integration tests and strict validation**

~~~sh
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
~~~

Expected: integration tests pass, both addon paths lint without warnings, and
the integration UID is tracked.

- [ ] **Step 5: Commit the integration slice**

~~~sh
git add addons/FoundryObservabilityFoundryLib test_project/addons/FoundryObservabilityFoundryLib test_project/tests/observability-foundrylib.test.fs
git commit -m "feat: add FoundryLib observability sink"
~~~

## Task 4: Extend project contracts and packaging

**Files:**

- Modify: scripts/test-foundry-script
- Modify: scripts/test-foundry-uids
- Modify: scripts/package-addon
- Modify: scripts/test-package
- Modify: test_project/project.foundry

- [ ] **Step 1: Add consumer-project wiring assertions**

Keep the existing FoundryLib package pin. Assert the optional integration directory
exists and its descriptor names FoundryObservabilityFoundryLib. Do not add an
autoload or enable a plugin; the sink is explicitly instantiated.

- [ ] **Step 2: Validate both addon trees**

Define integration=repo_root/addons/FoundryObservabilityFoundryLib in
scripts/test-foundry-script. Require its plugin.cfg and sink, verify name,
version, namespace, class_name, and uses LogSink, and reject legacy engine names
in both trees.

Install test-project packages before import unless
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1:

~~~bash
command -v anvil >/dev/null 2>&1 || fail "anvil is required"
if [[ "${FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL:-0}" != "1" ]]; then
	anvil pkg install --dir "$project_dir"
fi
~~~

Lint addons/FoundryObservability and
addons/FoundryObservabilityFoundryLib in one Foundry CLI invocation.

- [ ] **Step 3: Validate all tracked UIDs**

Update scripts/test-foundry-uids to iterate over both explicit addon directories
while retaining valid UID, tracked-file, and non-ignored checks.

- [ ] **Step 4: Package both runtime payloads**

Update scripts/package-addon to stage both source directories, substitute VERSION
into both plugin descriptors, and zip exactly:

~~~text
addons/FoundryObservability
addons/FoundryObservabilityFoundryLib
~~~

Keep the archive name dist/FoundryObservability-$version.zip and generated-state
exclusions.

- [ ] **Step 5: Extend package assertions**

Update scripts/test-package to require both plugin descriptors, the core
autoload/API files, and FoundryLibObservabilitySink.fs. Iterate over both addon
directories for UID companions and continue rejecting test_project, .foundry,
.git, .build, and .DS_Store content.

- [ ] **Step 6: Run contracts and commit**

~~~sh
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-package
git add scripts test_project/project.foundry
git commit -m "test: validate and package observability integrations"
~~~

Expected: both addons lint, all UID checks pass, and the package contains only
the two runtime payloads and metadata.

## Task 5: Document the first usable API

**Files:**

- Modify: README.md
- Modify: BUILD.md
- Modify: CONTRIBUTING.md
- Modify: CHANGELOG.md
- Create: docs/API.md

- [ ] **Step 1: Document setup and capture**

Update README.md with:

~~~foundryscript
var config := ObservabilityConfig.new(true, "production", "1.0.0")
var provider: ObservabilityProvider = MemoryObservabilityProvider.new()
FoundryObservability.configure(provider, config)
FoundryObservability.capture_message("game started")
~~~

Document that Sentry, native bindings, and crash reporting are outside this
release and that FoundryLib logging is a separate optional addon.

- [ ] **Step 2: Add docs/API.md**

Document every public type and method from Tasks 1–3, including defensive
dictionary copying, null defaults, Error returns, event IDs, replacement
behavior, and:

~~~foundryscript
var sink := FoundryLibObservabilitySink.new(
		FoundryObservability, ObservabilityLevel.ERROR)
Log.add_sink(sink)
~~~

- [ ] **Step 3: Update build, contribution, and changelog guidance**

Document FoundryLib installation from task test:project, the two addon payloads
in the release zip, typed provider-neutral APIs, non-recursive failures, UID
requirements, and an Unreleased 2026-07-23 entry. Do not claim Sentry or crash
reporting support.

- [ ] **Step 4: Run hygiene checks and commit**

~~~sh
git diff --check
prek run --all-files
git add README.md BUILD.md CONTRIBUTING.md CHANGELOG.md docs/API.md
git commit -m "docs: document core observability API"
~~~

Expected: no whitespace or repository-hygiene failures.

## Task 6: Full verification and handoff

- [ ] **Step 1: Run focused project tests**

~~~sh
scripts/test-project
~~~

Expected: core and FoundryLib suites pass with zero failures.

- [ ] **Step 2: Run the complete repository gate**

~~~sh
task test
~~~

Expected: lint, FoundryScript diagnostics, consumer tests, workflow contracts,
and package checks all pass.

- [ ] **Step 3: Inspect the final branch state**

~~~sh
git status --short --branch
git log --oneline --decorate -8
~~~

Expected: branch feature/core-observability-api is current, only intentional
commits are present, and generated dist/ and package-install directories remain
ignored.

- [ ] **Step 4: Report the handoff**

Summarize the public API, optional FoundryLib setup, exact validation commands
and results, and the explicit boundary that Sentry/native bindings remain
separate from this core branch.
