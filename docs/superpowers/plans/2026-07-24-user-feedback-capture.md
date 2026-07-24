# User Feedback Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add provider-neutral player feedback capture with anonymous or explicitly supplied contact fields, optional event association, deterministic validation, and Apple/Android Sentry delivery.

**Architecture:** Introduce a dedicated `ObservabilityFeedback` value and `capture_feedback()` methods on the public service and provider contracts. The core validates and forwards feedback without persistence; providers own queueing, offline retry, and flush behavior. The Sentry addon maps the value to native Sentry feedback APIs and reports a local acceptance ID.

**Tech Stack:** FoundryScript, Foundry testlib, Sentry Cocoa 9.23.0, Sentry Android 8.50.1, Swift 6, Java 17, shell contract tests, Task.

---

## File map

- Create: `addons/FoundryObservability/ObservabilityFeedback.fs` — typed provider-neutral feedback value.
- Create: `addons/FoundryObservability/ObservabilityFeedback.fs.uid` — tracked Foundry UID companion.
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs` — public `capture_feedback` signature.
- Modify: `addons/FoundryObservability/ObservabilityProvider.fs` — provider `capture_feedback` signature.
- Modify: `addons/FoundryObservability/FoundryObservability.fs` — validation and forwarding.
- Modify: `addons/FoundryObservability/NullObservabilityProvider.fs` — safe no-op implementation.
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs` — isolated deterministic feedback storage.
- Modify: `test_project/tests/observability-core.test.fs` — core red/green tests.
- Modify: `test_project/tests/support/recording_observability_api.notest.fs` — satisfy the expanded public trait.
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs` — Sentry feedback payload/capability mapping.
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs` — record feedback payloads.
- Create: `test_project/tests/support/feedbackless_sentry_bridge.notest.fs` — bridge mismatch fixture.
- Modify: `test_project/tests/observability-sentry.test.fs` — Sentry provider tests.
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift` — Apple feedback bridge and privacy option.
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java` — Android feedback bridge and privacy option.
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java` — Android feedback test.
- Modify: `scripts/test-package` — public API/package contract checks.
- Modify: `scripts/test-foundry-script` — source contract checks where needed.
- Modify: `docs/API.md` — public feedback contract and Sentry delivery/privacy behavior.
- Modify: `README.md` — status and quick-start mention.
- Modify: `CHANGELOG.md` — unreleased feature entry.
- Create: `docs/superpowers/plans/2026-07-24-user-feedback-capture.md` — this plan.

## Task 1: Add failing core feedback tests

**Files:**
- Test: `test_project/tests/observability-core.test.fs`
- Test: `test_project/tests/support/recording_observability_api.notest.fs`

- [ ] **Step 1: Add value and forwarding tests before production changes.**

Add tests with these behaviors:

```foundryscript
func test_feedback_value_preserves_fields() -> void:
	var feedback := ObservabilityFeedback.new(
			p_message = "The tutorial is confusing.",
			p_name = "Ada",
			p_contact_email = "ada@example.com",
			p_associated_event_id = "event-123",
		)

	Expect.that(feedback.message()).to_equal("The tutorial is confusing.")
	Expect.that(feedback.name()).to_equal("Ada")
	Expect.that(feedback.contact_email()).to_equal("ada@example.com")
	Expect.that(feedback.associated_event_id()).to_equal("event-123")


func test_memory_provider_captures_feedback_separately_from_events() -> void:
	var service: FoundryObservability = _service()
	var provider := MemoryObservabilityProvider.new()

	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.capture_message("ordinary event")).to_equal("memory:1")
	Expect.that(service.capture_feedback(ObservabilityFeedback.new(
			p_message = "Please add controller support.",
			p_associated_event_id = "memory:1",
		))).to_equal("memory-feedback:1")

	Expect.that(provider.events()).to_have_size(1)
	Expect.that(provider.feedback()).to_have_size(1)
	Expect.that(provider.feedback()[0].message()).to_equal("Please add controller support.")
	Expect.that(provider.feedback()[0].associated_event_id()).to_equal("memory:1")
	service.shutdown()
```

Add separate tests for anonymous feedback, identified feedback, empty/whitespace messages, a message of 4097 characters, malformed email, control characters in optional strings, disabled capture, and an enabled `NullObservabilityProvider`. Assert invalid input returns an empty string, does not add to Memory, and sets `Error.ERR_INVALID_PARAMETER`; assert unavailable capture sets `Error.FAILED`.

Add a `capture_feedback` implementation to `RecordingObservabilityApi` returning `"feedback:1"` so the fixture still satisfies `FoundryObservabilityApi`.

- [ ] **Step 2: Run the focused project tests and verify the failure is feature-related.**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: the test project fails to import or compile because `ObservabilityFeedback`, `capture_feedback`, and the provider implementations do not exist yet. Do not proceed if the failure is caused by a typo in the test code.

- [ ] **Step 3: Commit the red tests.**

```sh
git add test_project/tests/observability-core.test.fs test_project/tests/support/recording_observability_api.notest.fs
git commit -m "test: specify user feedback capture"
```

## Task 2: Implement the provider-neutral core contract

**Files:**
- Create: `addons/FoundryObservability/ObservabilityFeedback.fs`
- Create: `addons/FoundryObservability/ObservabilityFeedback.fs.uid`
- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/ObservabilityProvider.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/NullObservabilityProvider.fs`
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs`

- [ ] **Step 1: Add the typed feedback value and trait methods.**

Implement `ObservabilityFeedback` with private strings, the `p_message`, `p_name`, `p_contact_email`, and `p_associated_event_id` constructor arguments, and accessors matching the existing `ObservabilityException` style.

Add these exact contract signatures:

```foundryscript
## Captures user feedback and returns a provider acceptance ID.
abstract func capture_feedback(feedback: ObservabilityFeedback) -> String
```

Add the same method to `ObservabilityProvider`.

- [ ] **Step 2: Implement core validation and forwarding.**

Add a 4096-character constant and a `capture_feedback` method to `FoundryObservability`. Validate before provider invocation:

```foundryscript
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if feedback == null or not _is_valid_feedback(feedback):
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	if not is_enabled() or _provider == null:
		return ""
	return _capture_feedback(feedback)


func _capture_feedback(feedback: ObservabilityFeedback) -> String:
	var feedback_id: String = _provider.capture_feedback(feedback)
	if feedback_id.is_empty():
		_last_error = Error.FAILED
	return feedback_id
```

Implement helpers for trimmed-empty message, `message.length() <= 4096`, control-character rejection for optional values, and the one-`@` email shape described by the spec. Do not mutate accepted strings. Leave `capture_event`, message, exception, and log paths unchanged.

- [ ] **Step 3: Implement Null and Memory provider behavior.**

Keep Null as a no-op returning an empty string. Add `_feedback`, `_feedback_sequence`, `capture_feedback()`, `feedback() -> Array[ObservabilityFeedback]`, and `clear_feedback()` to Memory. Return IDs as `"memory-feedback:" + str(_feedback_sequence)`; do not share the ordinary event sequence.

- [ ] **Step 4: Generate and verify the UID companion.**

Use the repository's Foundry import/UID workflow to create the tracked UID for `ObservabilityFeedback.fs`. Confirm it matches `^uid://[a-z0-9]+$` and is not ignored.

- [ ] **Step 5: Run the core tests green.**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: all core and FoundryLib tests pass, including the new validation, privacy, unavailable-provider, and separation cases.

- [ ] **Step 6: Commit the core implementation.**

```sh
git add addons/FoundryObservability test_project/tests/observability-core.test.fs test_project/tests/support/recording_observability_api.notest.fs
git commit -m "feat: add provider-neutral feedback capture"
```

## Task 3: Add the Sentry FoundryScript contract tests

**Files:**
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Create: `test_project/tests/support/feedbackless_sentry_bridge.notest.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing Sentry tests.**

Extend `FakeSentryBridge` with `captured_feedback_payloads` and a `captureFeedback(payload)` method that records a deep copy and returns `"sentry-feedback:1"`. Add tests that configure the provider with `send_default_pii = false`, capture anonymous feedback, and assert the payload contains `message` and `associated_event_id` when supplied while omitting absent `name` and `contact_email`.

Add `FeedbacklessSentryBridge` with working `configure`, `isAvailable`, `capture`, `captureLog`, `flush`, and `shutdown` methods but no `captureFeedback`. Assert enabled provider configuration fails and the provider remains unavailable.

- [ ] **Step 2: Run the Sentry FoundryScript tests and verify they fail for the missing implementation.**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: the new payload and capability assertions fail because the provider does not yet expose `capture_feedback` or require/forward `captureFeedback`.

- [ ] **Step 3: Commit the red Sentry tests.**

```sh
git add test_project/tests/support/fake_sentry_bridge.notest.fs test_project/tests/support/feedbackless_sentry_bridge.notest.fs test_project/tests/observability-sentry.test.fs
git commit -m "test: specify Sentry feedback delivery"
```

## Task 4: Implement Sentry FoundryScript delivery

**Files:**
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Require the native feedback capability during enabled configuration.**

In `configure`, after resolving the bridge and checking existing structured-log support, reject enabled configurations whose bridge lacks `captureFeedback`. Preserve disabled configurations as safe `Error.OK` no-ops.

Forward `send_default_pii` from `provider_options` as a boolean with a false default in the native configuration payload.

- [ ] **Step 2: Add the provider method and payload mapping.**

Implement:

```foundryscript
func capture_feedback(feedback: ObservabilityFeedback) -> String:
	if feedback == null or not _enabled or _shutdown:
		return ""

	var bridge: Object? = _resolve_bridge()
	if bridge == null or not is_available() or not bridge.has_method("captureFeedback"):
		return ""

	var payload: Dictionary = {"message": feedback.message()}
	if not feedback.name().is_empty():
		payload["name"] = feedback.name()
	if not feedback.contact_email().is_empty():
		payload["contact_email"] = feedback.contact_email()
	if not feedback.associated_event_id().is_empty():
		payload["associated_event_id"] = feedback.associated_event_id()
	return str(bridge.call("captureFeedback", payload))
```

Keep empty results observable through the core service's `Error.FAILED` behavior. Do not route feedback through `capture`.

- [ ] **Step 3: Run the Sentry FoundryScript tests green.**

Run:

```sh
FOUNDRYOBSERVABILITY_SKIP_ANVIL_INSTALL=1 scripts/test-project
```

Expected: core, FoundryLib, and Sentry FoundryScript tests pass.

- [ ] **Step 4: Commit the Sentry FoundryScript implementation.**

```sh
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs test_project/tests/support/fake_sentry_bridge.notest.fs test_project/tests/observability-sentry.test.fs
git commit -m "feat: map feedback through Sentry provider"
```

## Task 5: Add native Apple and Android feedback delivery

**Files:**
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`

- [ ] **Step 1: Add the Android native test before implementation.**

Add a configured-bridge test with a valid DSN and payload:

```java
@Test
public void configuredBridgeCapturesFeedback() {
  SentryObservabilityBridge bridge = newBridge();
  Dictionary configuration = new Dictionary();
  configuration.put("enabled", true);
  configuration.put("dsn", "https://public@example.com/1");
  assertEquals(0, bridge.configure(configuration));

  Dictionary feedback = new Dictionary();
  feedback.put("message", "The tutorial is confusing.");
  feedback.put("name", "Ada");
  feedback.put("contact_email", "ada@example.com");
  feedback.put("associated_event_id", "00000000000000000000000000000001");

  assertFalse(bridge.captureFeedback(feedback).isEmpty());
  bridge.shutdown();
}
```

Run `cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry && ./gradlew test`. Expected: compilation fails because `captureFeedback` does not exist.

- [ ] **Step 2: Implement Android feedback mapping.**

Import `io.sentry.protocol.Feedback`. Add `captureFeedback(Dictionary payload)` that rejects unavailable/null/empty-message payloads, creates `new Feedback(message)`, sets contact email and name only when non-empty, parses `associated_event_id` into `SentryId` inside a guarded conversion, calls `Sentry.feedback().capture(feedback)`, and returns `"sentry-feedback:" + UUID.randomUUID()`. Return an empty string for malformed IDs or runtime failures.

Forward `send_default_pii` to `SentryAndroidOptions.setSendDefaultPii`, defaulting false.

- [ ] **Step 3: Run the Android tests green.**

Run:

```sh
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew test lintRelease assembleDebug assembleRelease
```

Expected: unit tests, lint, and both AAR assemblies succeed.

- [ ] **Step 4: Implement Apple feedback mapping.**

In `SentryObservabilityBridge.swift`, parse `send_default_pii` into `Options.sendDefaultPii`. Add `captureFeedback(payload: VariantDictionary) -> String` that rejects unavailable or empty-message payloads, maps absent name/email to nil, creates `SentryFeedback` with `.custom` source and an optional `SentryId(uuidString:)`, calls `SentrySDK.capture(feedback:)`, and returns `"sentry-feedback:" + UUID().uuidString`. Invalid association IDs return an empty string without calling the SDK.

- [ ] **Step 5: Run Apple mapper and build-contract verification.**

Run:

```sh
task test:sentry-swift
scripts/test-sentry-ios-build-contract
```

Expected: existing deterministic mapper tests and Apple export contract checks pass. If native artifacts must be rebuilt for a compile check, run `task ios:sentry` and then repeat the contract test.

- [ ] **Step 6: Commit native delivery.**

```sh
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java
git commit -m "feat: deliver feedback through native Sentry bridges"
```

## Task 6: Update documentation and repository contracts

**Files:**
- Modify: `docs/API.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `scripts/test-package`
- Modify: `scripts/test-foundry-script`

- [ ] **Step 1: Document the public feedback model and service method.**

Add an API section showing:

```foundryscript
var feedback := ObservabilityFeedback.new(
		p_message = "Please add controller support.",
		p_associated_event_id = last_event_id,
	)
var feedback_id: String = FoundryObservability.capture_feedback(feedback)
```

Document the four fields, 4096-character limit, malformed-input error, anonymous default, provider acceptance-ID semantics, and `Error.FAILED` for provider rejection.

- [ ] **Step 2: Document delivery, privacy, and unsupported capabilities.**

State that core feedback is not persisted and does not retry; provider SDKs own offline queues and retries; `flush(timeout_msec)` is best effort. State that name/email are sent only when supplied, `send_default_pii` defaults false, and the Sentry addon requires a native `captureFeedback` bridge method.

- [ ] **Step 3: Update the README and unreleased changelog.**

Mention user feedback capture in the status list and add a short quick-start call. Add an Unreleased changelog entry covering the provider-neutral feedback API, validation, event association, and Apple/Android Sentry delivery.

- [ ] **Step 4: Extend contract checks.**

Require `ObservabilityFeedback.fs` in `scripts/test-foundry-script`, require its UID companion through the existing UID loop, and require `capture_feedback`, `ObservabilityFeedback`, and the feedback docs in `scripts/test-package`. Keep checks provider-neutral and do not add generated build outputs to package listings.

- [ ] **Step 5: Run package and source contract checks.**

Run:

```sh
scripts/test-foundry-script
scripts/test-foundry-uids
scripts/test-package
```

Expected: source import/lint, UID validation, addon packaging, and documentation/package contract checks pass.

- [ ] **Step 6: Commit documentation and contracts.**

```sh
git add docs/API.md README.md CHANGELOG.md scripts/test-package scripts/test-foundry-script addons/FoundryObservability/ObservabilityFeedback.fs.uid
git commit -m "docs: document feedback capture"
```

## Task 7: Full verification and handoff

**Files:**
- Verify all changed files and commits from Tasks 1–6.

- [ ] **Step 1: Run the complete validation gate.**

Run:

```sh
task test
```

Expected: lint, CI workflow checks, package checks, Swift tests/contracts, Android contracts, project tests, and FoundryScript/UID checks all exit 0.

- [ ] **Step 2: Inspect the final diff and repository state.**

Run:

```sh
git diff --check origin/main...HEAD
git status --short --branch
git log --oneline -8
```

Confirm no unrelated files changed, no generated build state is staged, the new UID is tracked, and all implementation files listed in the spec are covered.

- [ ] **Step 3: Report verified results.**

Include the exact test commands run, their successful exit status, the core/Sentry/native behavior delivered, and any platform build that could not run because a prerequisite was unavailable. Do not claim completion without fresh output from the verification commands.
