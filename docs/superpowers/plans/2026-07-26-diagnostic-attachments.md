# Diagnostic Attachments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent provider-neutral file and byte attachments, independent game-log/screenshot/scene-tree attachments, partial-failure reporting, and complete Sentry Apple/Android delivery.

**Architecture:** Add immutable attachment and failure DTOs plus an optional provider capability. Core validation and error mapping stay in `FoundryObservability`; memory and Sentry providers own session attachment snapshots. The Sentry provider mirrors stable attachments into native SDK scope and sends capture-local packaged-resource and built-in payloads, while a focused runtime collector performs bounded, main-thread-only screenshot and scene-tree work.

**Tech Stack:** FoundryScript, Foundry testlib, Godot/Foundry `FileAccess` and scene APIs, Swift 6/XCTest, Sentry Cocoa 9.23.0, Java 17/JUnit, Sentry Android 8.50.1, Bash contract tests, Task.

---

## File structure

Create provider-neutral resources:

- `addons/FoundryObservability/ObservabilityAttachment.fs`: immutable file/byte attachment DTO, validation, snapshots, effective filename, virtual-path classification.
- `addons/FoundryObservability/ObservabilityAttachmentFailure.fs`: immutable structured partial-failure DTO.
- `addons/FoundryObservability/ObservabilityAttachmentsProvider.fs`: optional provider capability and internal error-preserving removal contract.
- Matching generated `.uid` files.

Create Sentry runtime resources:

- `addons/FoundryObservabilitySentry/SentryAttachmentRuntimeProbe.fs`: thin engine/filesystem seam for tests and platform-safe capture.
- `addons/FoundryObservabilitySentry/SentryBuiltInAttachmentCollector.fs`: bounded built-in attachment collection and failure construction.
- Matching generated `.uid` files.

Modify provider-neutral resources:

- `ObservabilityConfig.fs`: attachment maximum and three independent built-in toggles.
- `FoundryObservabilityApi.fs` and `FoundryObservability.fs`: public operations, validation, capability/error mapping, and failure visibility.
- `MemoryObservabilityProvider.fs`: persistent handle state, lazy materialization, delivered payload snapshots, and lifecycle rules.

Modify the Sentry adapter:

- `SentryObservabilityProvider.fs`: attachment candidate/commit state, native replacement, preflight, built-ins, capture-local payloads, failures, and reset/rollback.
- `test_project/tests/support/fake_sentry_bridge.notest.fs`: deterministic native replacement and attachment-bearing event capture seam.

Modify Apple native code:

- `FoundryObservabilitySentry.swift`: payload parsing, complete scope replacement, capture-local attachments, and bridge methods.
- `SentryLifecycleCoordinator.swift`: attachment limit in lifecycle configuration and Sentry options.
- Swift lifecycle and bridge/mapper tests.

Modify Android native code:

- `SentryObservabilityBridge.java`: payload parsing, complete scope replacement, capture-local attachments, and bridge methods.
- `SentryLifecycleConfiguration.java`: normalized attachment limit.
- `AndroidSentrySdkDriver.java`: attachment-limit option mapping.
- `SentryAttachmentMapper.java`: strict file/byte payload validation and defensive native mapping.
- Android lifecycle and bridge tests.

Modify contracts and docs:

- `scripts/test-foundry-script`
- `scripts/test-sentry-ios-build-contract`
- `scripts/test-sentry-android-build-contract`
- `scripts/test-package`
- `test_project/tests/observability-core.test.fs`
- `test_project/tests/observability-sentry.test.fs`
- `README.md`
- `docs/API.md`
- `CHANGELOG.md`

### Task 1: Attachment DTOs, capability, and configuration

**Files:**

- Create: `addons/FoundryObservability/ObservabilityAttachment.fs`
- Create: `addons/FoundryObservability/ObservabilityAttachmentFailure.fs`
- Create: `addons/FoundryObservability/ObservabilityAttachmentsProvider.fs`
- Modify: `addons/FoundryObservability/ObservabilityConfig.fs`
- Modify: `scripts/test-foundry-script`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Add failing resource and DTO tests**

Add the resources to the required-resource loop in `scripts/test-foundry-script`,
and require all constructor-owned fields to be `final`.

Add these focused cases near the other DTO/config tests:

```foundryscript
func test_attachment_factories_copy_sources_and_preserve_metadata() -> void:
	var bytes: PackedByteArray = "hello".to_utf8_buffer()
	var byte_attachment := ObservabilityAttachment.from_bytes(
			bytes,
			"diagnostic.txt",
			"text/plain",
			&"event.attachment",
		)
	Expect.that(byte_attachment != null).to_be_true()
	bytes[0] = 0
	Expect.that(byte_attachment.bytes().get_string_from_utf8()).to_equal("hello")
	Expect.that(byte_attachment.filename()).to_equal("diagnostic.txt")
	Expect.that(byte_attachment.content_type()).to_equal("text/plain")
	Expect.that(byte_attachment.category()).to_equal(&"event.attachment")
	Expect.that(byte_attachment.is_bytes()).to_be_true()

	var file_attachment := ObservabilityAttachment.from_path(
			"user://diagnostics/run.log",
			"",
			"text/plain",
			&"event.attachment",
		)
	Expect.that(file_attachment != null).to_be_true()
	Expect.that(file_attachment.path()).to_equal("user://diagnostics/run.log")
	Expect.that(file_attachment.effective_filename()).to_equal("run.log")
	Expect.that(file_attachment.is_path()).to_be_true()


func test_attachment_factories_reject_invalid_sources_and_metadata() -> void:
	Expect.that(ObservabilityAttachment.from_path("")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("relative/file.txt")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path("user://file.txt", "bad\nname")).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(), "")).to_be_null()
	Expect.that(ObservabilityAttachment.from_bytes(
			PackedByteArray(), "file.txt", " text/plain")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path(
			"user://file.txt", "", "", &"")).to_be_null()
	Expect.that(ObservabilityAttachment.from_path(
			"user://file.txt", "", "", &"event.minidump")).to_be_null()


func test_attachment_failure_is_immutable_and_config_defaults_are_safe() -> void:
	var failure := ObservabilityAttachmentFailure.new(
			"memory-attachment:4",
			"run.log",
			ObservabilityAttachmentFailure.MISSING_FILE,
			Error.ERR_FILE_NOT_FOUND,
		)
	Expect.that(failure.handle()).to_equal("memory-attachment:4")
	Expect.that(failure.filename()).to_equal("run.log")
	Expect.that(failure.reason()).to_equal(&"missing_file")
	Expect.that(failure.error()).to_equal(Error.ERR_FILE_NOT_FOUND)

	var defaults := ObservabilityConfig.new()
	Expect.that(defaults.max_attachment_bytes).to_equal(20 * 1024 * 1024)
	Expect.that(defaults.attach_game_log).to_be_false()
	Expect.that(defaults.attach_screenshot).to_be_false()
	Expect.that(defaults.attach_scene_tree).to_be_false()
	Expect.that(ObservabilityConfig.new(
			p_max_attachment_bytes = -1,
		).max_attachment_bytes).to_equal(0)
```

- [ ] **Step 2: Run the red tests**

Run:

```bash
scripts/test-foundry-script
```

Expected: FAIL because the three resources do not exist.

Create minimal class/trait shells, then run:

```bash
scripts/test-project
```

Expected: FAIL in the new attachment factory test because the factories and
configuration fields are not implemented.

- [ ] **Step 3: Implement the immutable attachment DTO**

Implement this public shape in `ObservabilityAttachment.fs`:

```foundryscript
namespace foundry.observability

## Immutable provider-neutral diagnostic attachment.
class_name ObservabilityAttachment
extends RefCounted

const DEFAULT_CONTENT_TYPE: String = "application/octet-stream"
const DEFAULT_CATEGORY: StringName = &"event.attachment"
const VIEW_HIERARCHY_CATEGORY: StringName = &"event.view_hierarchy"

final var _path: String
final var _bytes: PackedByteArray
final var _filename: String
final var _content_type: String
final var _category: StringName


func _init(
		p_path: String = "",
		p_bytes: PackedByteArray = PackedByteArray(),
		p_filename: String = "",
		p_content_type: String = "",
		p_category: StringName = DEFAULT_CATEGORY,
) -> void:
	_path = p_path
	_bytes = p_bytes.duplicate()
	_filename = p_filename
	_content_type = p_content_type
	_category = p_category


static func from_path(
		path: String,
		filename: String = "",
		content_type: String = "",
		category: StringName = DEFAULT_CATEGORY,
) -> ObservabilityAttachment?:
	var candidate := ObservabilityAttachment.new(
			path,
			PackedByteArray(),
			filename,
			content_type,
			category,
		)
	return candidate if candidate.is_valid() else null


static func from_bytes(
		bytes: PackedByteArray,
		filename: String,
		content_type: String = "",
		category: StringName = DEFAULT_CATEGORY,
) -> ObservabilityAttachment?:
	var candidate := ObservabilityAttachment.new(
			"",
			bytes,
			filename,
			content_type,
			category,
		)
	return candidate if candidate.is_valid() else null


func path() -> String:
	return _path


func bytes() -> PackedByteArray:
	return _bytes.duplicate()


func filename() -> String:
	return _filename


func effective_filename() -> String:
	return _filename if not _filename.is_empty() else _path.get_file()


func content_type() -> String:
	return DEFAULT_CONTENT_TYPE if _content_type.is_empty() else _content_type


func category() -> StringName:
	return _category


func is_path() -> bool:
	return not _path.is_empty()


func is_bytes() -> bool:
	return _path.is_empty()


func duplicate() -> ObservabilityAttachment:
	return ObservabilityAttachment.new(
			_path, _bytes, _filename, _content_type, _category)


func is_valid() -> bool:
	if _path.is_empty():
		if not _is_safe_nonempty(_filename):
			return false
	else:
		if not _bytes.is_empty() or not _is_supported_path(_path):
			return false
		if not _filename.is_empty() and not _is_safe_nonempty(_filename):
			return false
	if not _content_type.is_empty() and not _is_safe_nonempty(_content_type):
		return false
	return _category == DEFAULT_CATEGORY or _category == VIEW_HIERARCHY_CATEGORY


static func _is_supported_path(value: String) -> bool:
	if not _is_safe_nonempty(value):
		return false
	return value.begins_with("user://") \
			or value.begins_with("res://") \
			or value.is_absolute_path()


static func _is_safe_nonempty(value: String) -> bool:
	if value.is_empty() or value.strip_edges() != value:
		return false
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or codepoint == 127:
			return false
	return true
```

An empty byte array remains distinguishable from a path because `_path` is
empty and byte attachments require a filename.

- [ ] **Step 4: Implement failure and capability resources**

Use this complete failure DTO:

```foundryscript
namespace foundry.observability

## One attachment omitted from the latest applicable event.
class_name ObservabilityAttachmentFailure
extends RefCounted

const MISSING_FILE: StringName = &"missing_file"
const UNREADABLE_FILE: StringName = &"unreadable_file"
const OVERSIZED: StringName = &"oversized"
const PLATFORM_UNAVAILABLE: StringName = &"platform_unavailable"
const PROVIDER_REJECTED: StringName = &"provider_rejected"

final var _handle: String
final var _filename: String
final var _reason: StringName
final var _error: int


func _init(
		p_handle: String = "",
		p_filename: String = "",
		p_reason: StringName = PROVIDER_REJECTED,
		p_error: int = Error.FAILED,
) -> void:
	_handle = p_handle
	_filename = p_filename
	_reason = p_reason
	_error = p_error


func handle() -> String:
	return _handle


func filename() -> String:
	return _filename


func reason() -> StringName:
	return _reason


func error() -> int:
	return _error


func duplicate() -> ObservabilityAttachmentFailure:
	return ObservabilityAttachmentFailure.new(
			_handle, _filename, _reason, _error)
```

Use this capability:

```foundryscript
namespace foundry.observability

## Optional provider capability for persistent diagnostic attachments.
trait_name ObservabilityAttachmentsProvider

abstract func add_attachment(attachment: ObservabilityAttachment) -> String
abstract func remove_attachment(handle: String) -> int
abstract func clear_attachments() -> bool
abstract func last_attachment_failures() -> Array
```

- [ ] **Step 5: Append configuration fields without breaking positional callers**

Append these arguments after `p_max_breadcrumbs` in `ObservabilityConfig._init`:

```foundryscript
p_max_attachment_bytes: int = 20 * 1024 * 1024,
p_attach_game_log: bool = false,
p_attach_screenshot: bool = false,
p_attach_scene_tree: bool = false,
```

Add public fields and assignments:

```foundryscript
var max_attachment_bytes: int = 20 * 1024 * 1024
var attach_game_log: bool = false
var attach_screenshot: bool = false
var attach_scene_tree: bool = false

max_attachment_bytes = maxi(0, p_max_attachment_bytes)
attach_game_log = p_attach_game_log
attach_screenshot = p_attach_screenshot
attach_scene_tree = p_attach_scene_tree
```

- [ ] **Step 6: Generate UIDs and verify green**

Run:

```bash
/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64 \
  --headless project import --project test_project
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: attachment DTO/config tests pass and all existing tests remain green.

- [ ] **Step 7: Commit**

```bash
git add addons/FoundryObservability scripts/test-foundry-script \
  test_project/tests/observability-core.test.fs
git commit -m "feat: define diagnostic attachment contracts"
```

### Task 2: Core service and deterministic memory provider

**Files:**

- Modify: `addons/FoundryObservability/FoundryObservabilityApi.fs`
- Modify: `addons/FoundryObservability/FoundryObservability.fs`
- Modify: `addons/FoundryObservability/MemoryObservabilityProvider.fs`
- Create: `test_project/tests/support/attachmentless_observability_provider.notest.fs`
- Modify: `test_project/tests/observability-core.test.fs`

- [ ] **Step 1: Write failing capability/error tests**

Add tests covering:

```foundryscript
func test_attachment_service_delegates_handles_and_maps_removal_errors() -> void:
	var service := _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var attachment := ObservabilityAttachment.from_bytes(
			"first".to_utf8_buffer(), "first.txt", "text/plain")
	var handle: String = service.add_attachment(attachment)
	Expect.that(handle.begins_with("memory-attachment:")).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_attachment(handle)).to_be_true()
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.remove_attachment(handle)).to_be_false()
	Expect.that(service.last_error()).to_equal(Error.ERR_DOES_NOT_EXIST)


func test_attachment_capability_is_optional_and_does_not_block_events() -> void:
	var service := _service()
	var provider := AttachmentlessObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	Expect.that(service.add_attachment(
			ObservabilityAttachment.from_bytes(
					"x".to_utf8_buffer(), "x.txt"))).to_equal("")
	Expect.that(service.last_error()).to_equal(Error.ERR_UNAVAILABLE)
	Expect.that(service.capture_message("still sent")).to_equal("attachmentless:1")


func test_memory_attachments_persist_remove_clear_and_snapshot_bytes() -> void:
	var service := _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new())).to_equal(Error.OK)
	var bytes := "v1".to_utf8_buffer()
	var first: String = service.add_attachment(
			ObservabilityAttachment.from_bytes(bytes, "state.txt", "text/plain"))
	var second: String = service.add_attachment(
			ObservabilityAttachment.from_bytes(
					"other".to_utf8_buffer(), "state.txt", "text/plain"))
	bytes[0] = 0
	Expect.that(service.capture_message("one")).to_equal("memory:1")
	Expect.that(service.capture_message("two")).to_equal("memory:2")
	Expect.that(provider.captured_attachments()[0][0]["bytes"]
			.get_string_from_utf8()).to_equal("v1")
	Expect.that(service.remove_attachment(first)).to_be_true()
	Expect.that(service.capture_message("three")).to_equal("memory:3")
	Expect.that(provider.captured_attachments()[2].size()).to_equal(1)
	Expect.that(service.clear_attachments()).to_be_true()
	Expect.that(service.capture_message("four")).to_equal("memory:4")
	Expect.that(provider.captured_attachments()[3]).to_be_empty()
	Expect.that(second.is_empty()).to_be_false()
```

Add file cases using a test file under `user://foundry-observability-tests/`:

```foundryscript
func test_memory_path_attachments_are_lazy_and_fail_without_blocking_event() -> void:
	var directory := DirAccess.open("user://")
	directory.make_dir_recursive("foundry-observability-tests")
	var path := "user://foundry-observability-tests/lazy.txt"
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string("first")
	file.close()

	var service := _service()
	var provider := MemoryObservabilityProvider.new()
	Expect.that(service.configure(provider, ObservabilityConfig.new(
			p_max_attachment_bytes = 5))).to_equal(Error.OK)
	var handle: String = service.add_attachment(
			ObservabilityAttachment.from_path(path, "", "text/plain"))
	Expect.that(service.capture_message("one")).to_equal("memory:1")
	Expect.that(provider.captured_attachments()[0][0]["bytes"]
			.get_string_from_utf8()).to_equal("first")

	file = FileAccess.open(path, FileAccess.WRITE)
	file.store_string("oversized")
	file.close()
	Expect.that(service.capture_message("two")).to_equal("memory:2")
	Expect.that(service.last_error()).to_equal(Error.OK)
	Expect.that(service.last_attachment_failures().size()).to_equal(1)
	Expect.that(service.last_attachment_failures()[0].reason()).to_equal(
			ObservabilityAttachmentFailure.OVERSIZED)

	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	Expect.that(service.capture_message("three")).to_equal("memory:3")
	Expect.that(service.last_attachment_failures()[0].handle()).to_equal(handle)
	Expect.that(service.last_attachment_failures()[0].reason()).to_equal(
			ObservabilityAttachmentFailure.MISSING_FILE)
```

Add `test_memory_attachment_session_boundaries_are_atomic()` with one accepted
handle and these exact assertions: successful same-provider configure makes
removal return `Error.ERR_DOES_NOT_EXIST`; setting `configure_result =
Error.FAILED` preserves delivery and handle removal; replacing the provider
makes the old provider's `remove_attachment` return `Error.FAILED`; and disabled
service add/remove/clear calls leave `captured_attachments()` unchanged. Add
`test_attachment_failures_change_only_on_event_envelopes()` that captures one
missing path, calls feedback, metric, breadcrumb, log, and flush APIs, and
asserts the missing failure remains until the next message event replaces it.

- [ ] **Step 2: Run the red project test**

```bash
scripts/test-project
```

Expected: FAIL because public methods, memory capability, and captured attachment
snapshots do not exist.

- [ ] **Step 3: Add the public API and service dispatch**

Append exact signatures to `FoundryObservabilityApi.fs`:

```foundryscript
abstract func add_attachment(attachment: ObservabilityAttachment) -> String
abstract func remove_attachment(handle: String) -> bool
abstract func clear_attachments() -> bool
abstract func last_attachment_failures() -> Array
```

Implement the service methods with complete result checks:

```foundryscript
func add_attachment(attachment: ObservabilityAttachment) -> String:
	if not is_enabled():
		return ""
	if attachment == null or not attachment.is_valid():
		_last_error = Error.ERR_INVALID_PARAMETER
		return ""
	var attachment_provider: ObservabilityAttachmentsProvider? = (
			_attachment_provider())
	if attachment_provider == null:
		_last_error = Error.ERR_UNAVAILABLE
		return ""
	_begin_provider_call()
	var handle: Variant = attachment_provider.add_attachment(
			attachment.duplicate())
	_end_provider_call()
	if not (handle is String) or str(handle).is_empty():
		_last_error = Error.FAILED
		return ""
	_last_error = Error.OK
	return str(handle)


func remove_attachment(handle: String) -> bool:
	if not is_enabled():
		return false
	if handle.is_empty() or handle.strip_edges() != handle \
			or _has_control_character(handle):
		_last_error = Error.ERR_INVALID_PARAMETER
		return false
	var attachment_provider: ObservabilityAttachmentsProvider? = (
			_attachment_provider())
	if attachment_provider == null:
		_last_error = Error.ERR_UNAVAILABLE
		return false
	_begin_provider_call()
	var result: Variant = attachment_provider.remove_attachment(handle)
	_end_provider_call()
	if not (result is int):
		_last_error = Error.FAILED
		return false
	_last_error = result
	return result == Error.OK


func clear_attachments() -> bool:
	if not is_enabled():
		return false
	var attachment_provider: ObservabilityAttachmentsProvider? = (
			_attachment_provider())
	if attachment_provider == null:
		_last_error = Error.ERR_UNAVAILABLE
		return false
	_begin_provider_call()
	var result: Variant = attachment_provider.clear_attachments()
	_end_provider_call()
	if not (result is bool) or not result:
		_last_error = Error.FAILED
		return false
	_last_error = Error.OK
	return true


func last_attachment_failures() -> Array:
	var attachment_provider: ObservabilityAttachmentsProvider? = (
			_attachment_provider())
	if attachment_provider == null:
		return []
	_begin_provider_call()
	var failures: Variant = attachment_provider.last_attachment_failures()
	_end_provider_call()
	if not (failures is Array):
		return []
	var copies: Array = []
	for failure: Variant in failures:
		if failure is ObservabilityAttachmentFailure:
			copies.append(failure.duplicate())
	return copies


func _attachment_provider() -> ObservabilityAttachmentsProvider?:
	if _provider is ObservabilityAttachmentsProvider:
		return _provider as ObservabilityAttachmentsProvider
	return null
```

Do not set `last_error()` from the read-only failure accessor.

- [ ] **Step 4: Implement memory attachment ownership and materialization**

Add `ObservabilityAttachmentsProvider` to the `uses` list and state:

```foundryscript
var _attachments: Dictionary = {}
var _captured_attachments: Array[Array] = []
var _last_attachment_failures: Array[ObservabilityAttachmentFailure] = []
var _attachment_sequence: int = 0
var _max_attachment_bytes: int = 20 * 1024 * 1024
```

Reset `_attachments`, `_last_attachment_failures`, and the maximum on every
successful configure and shutdown, but leave them untouched when
`configure_result != Error.OK`.

Implement the capability:

```foundryscript
func add_attachment(attachment: ObservabilityAttachment) -> String:
	if not _enabled or _shutdown or attachment == null or not attachment.is_valid():
		return ""
	_attachment_sequence += 1
	var handle: String = "memory-attachment:%s" % _attachment_sequence
	_attachments[handle] = attachment.duplicate()
	return handle


func remove_attachment(handle: String) -> int:
	if not _enabled or _shutdown:
		return Error.FAILED
	if not _attachments.has(handle):
		return Error.ERR_DOES_NOT_EXIST
	_attachments.erase(handle)
	return Error.OK


func clear_attachments() -> bool:
	if not _enabled or _shutdown:
		return false
	_attachments.clear()
	return true


func last_attachment_failures() -> Array:
	var copies: Array = []
	for failure: ObservabilityAttachmentFailure in _last_attachment_failures:
		copies.append(failure.duplicate())
	return copies
```

At the start of `capture`, clear failures, iterate the ordered dictionary keys,
and call `_materialize_attachment(handle, attachment)`. That helper must:

1. Return `oversized` for byte data larger than `_max_attachment_bytes`.
2. For paths, use `FileAccess.file_exists`, `FileAccess.open`, `get_length`,
   and `get_buffer`.
3. Report `missing_file`, `unreadable_file`, or `oversized` with the handle and
   effective filename.
4. Return a dictionary with copied `bytes`, `filename`, `content_type`,
   `category`, and original `path` for accepted payloads.

Append the accepted array to `_captured_attachments` exactly when the event and
scope snapshots are appended. Expose:

```foundryscript
func captured_attachments() -> Array[Array]:
	return _captured_attachments.duplicate(true)
```

Clear captured attachment history from `clear()` without changing the live
session attachment set.

- [ ] **Step 5: Verify core green**

```bash
scripts/test-project
scripts/test-foundry-script
```

Expected: all core, FoundryLib, Sentry adapter, and project-wiring tests pass.

- [ ] **Step 6: Commit**

```bash
git add addons/FoundryObservability test_project/tests
git commit -m "feat: add provider-neutral attachment lifecycle"
```

### Task 3: Sentry provider state and built-in collection

**Files:**

- Create: `addons/FoundryObservabilitySentry/SentryAttachmentRuntimeProbe.fs`
- Create: `addons/FoundryObservabilitySentry/SentryBuiltInAttachmentCollector.fs`
- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Modify: `test_project/tests/support/fake_sentry_bridge.notest.fs`
- Modify: `test_project/tests/observability-sentry.test.fs`

- [ ] **Step 1: Add failing Sentry provider tests**

Add tests that configure the fake bridge and assert:

```foundryscript
func test_sentry_attachment_mutations_replace_complete_candidate_atomically() -> void:
	var bridge := FakeSentryBridge.new()
	var provider := SentryObservabilityProvider.new(bridge)
	Expect.that(provider.configure(_config())).to_equal(Error.OK)
	var first: String = provider.add_attachment(
			ObservabilityAttachment.from_bytes(
					"one".to_utf8_buffer(), "one.txt", "text/plain"))
	var second: String = provider.add_attachment(
			ObservabilityAttachment.from_path(
					"user://two.log", "", "text/plain"))
	Expect.that(bridge.replaced_attachment_payloads.size()).to_equal(3)
	Expect.that(bridge.current_attachment_payloads.size()).to_equal(2)
	Expect.that(provider.remove_attachment(first)).to_equal(Error.OK)
	Expect.that(bridge.current_attachment_payloads.size()).to_equal(1)
	Expect.that(provider.remove_attachment(first)).to_equal(
			Error.ERR_DOES_NOT_EXIST)
	Expect.that(provider.clear_attachments()).to_be_true()
	Expect.that(bridge.current_attachment_payloads).to_be_empty()
	Expect.that(second.is_empty()).to_be_false()
```

The first replacement is the built-in-only reset during configure; it is empty
under the default configuration. Assert that a queued
`replace_attachments_results = [false]` leaves
`current_attachment_payloads`, provider handles, and previous capture behavior
unchanged; a non-boolean result fails the mutation; successful reconfiguration
invalidates user handles; failed equivalent reconfiguration restores the
previous complete native snapshot; and rejected restoration makes
`provider.is_available()` false.

Add capture tests that verify:

- `res://` attachment bytes travel in the event payload but not native global
  scope;
- missing/oversized global paths create failure DTOs while `capture` returns an
  event ID;
- `last_attachment_failures()` clears on the next successful applicable event;
- attachment bridge methods are optional until attachment APIs are called;
- a bridge with `replaceAttachments` but without an attachment-aware event
  method is rejected when attachment features are enabled.

Inject a fake runtime probe and test each built-in toggle independently,
main-thread/headless skips, one-screenshot-per-frame reuse, bounded scene JSON,
and user clear preserving built-in payloads.

- [ ] **Step 2: Run red adapter tests**

```bash
scripts/test-project
```

Expected: FAIL because the Sentry provider does not implement the attachment
capability and the fake bridge lacks replacement/capture support.

- [ ] **Step 3: Implement the runtime probe and collector**

`SentryAttachmentRuntimeProbe.fs` must expose only engine reads:

```foundryscript
func is_main_thread() -> bool:
	return OS.get_thread_caller_id() == OS.get_main_thread_id()

func is_headless() -> bool:
	return DisplayServer.get_name().to_lower() == "headless"

func frames_drawn() -> int:
	return Engine.get_frames_drawn()

func main_scene_tree() -> SceneTree?:
	return Engine.get_main_loop() as SceneTree

func screenshot_png() -> PackedByteArray:
	var tree: SceneTree? = main_scene_tree()
	if tree == null or tree.root == null:
		return PackedByteArray()
	var image: Image = tree.root.get_texture().get_image()
	return PackedByteArray() if image == null else image.save_png_to_buffer()

func game_log_path() -> String:
	if not bool(ProjectSettings.get_setting(
			"debug/file_logging/enable_file_logging", false)):
		return ""
	return str(ProjectSettings.get_setting(
			"debug/file_logging/log_path", "user://logs/godot.log"))
```

`SentryBuiltInAttachmentCollector` owns the probe, last screenshot frame/bytes,
and constants `MAX_SCENE_DEPTH = 32`, `MAX_SCENE_NODES = 1024`. Its
`collect(event, config)` returns:

```text
{
  attachments: Array[Dictionary],
  failures: Array[ObservabilityAttachmentFailure]
}
```

For the log, return a file payload. For screenshots, enforce main thread,
non-headless, valid tree/root, frame reuse, PNG metadata, and the configured
size. For scene hierarchy, recursively build:

```text
{
  "type": node.get_class(),
  "name": node.name,
  "visible": node.call("is_visible_in_tree") when available,
  "children": [...]
}
```

Stop at the depth/node bounds, serialize with `JSON.stringify`, encode UTF-8,
and use `view-hierarchy.json`, `application/json`, and
`event.view_hierarchy`. Do not read other node properties.

- [ ] **Step 4: Implement Sentry attachment candidate/commit state**

Add `ObservabilityAttachmentsProvider` to the provider `uses` list and inject an
optional attachment probe after the existing runtime-context probe without
changing existing positional behavior.

Add:

```foundryscript
var _attachments: Dictionary = {}
var _attachment_sequence: int = 0
var _last_attachment_failures: Array[ObservabilityAttachmentFailure] = []
var _attachment_collector: SentryBuiltInAttachmentCollector
var _max_attachment_bytes: int = 20 * 1024 * 1024
var _attachment_builtins: Dictionary = {}
```

Forward the four config values. During every successful session reset call:

```foundryscript
bridge.call(
		"replaceAttachments",
		_persistent_builtin_attachment_payloads(candidate_config_payload),
)
```

The persistent built-in array contains the configured game-log path when
available; screenshot and scene-tree bytes remain capture-local. Require an
explicit boolean result. Preserve/restore the committed
attachment payload exactly in the same configure rollback branches that already
preserve/restore scope and breadcrumbs.

Implement add/remove/clear by duplicating `_attachments`, deriving
`_global_attachment_payloads(candidate)`, prepending
`_persistent_builtin_attachment_payloads(_last_config_payload)`, calling
`replaceAttachments`, and committing only on `true`. This makes user clearing
restore the game-log built-in. `res://` entries are excluded from global native
payloads. Global file payloads contain absolute/globalized path, filename,
content type, and category; byte payloads contain a copied byte array and the
same metadata.

- [ ] **Step 5: Preflight and attach capture-local payloads**

Before the current `bridge.call(method, payload)` in event capture:

1. Clear `_last_attachment_failures`.
2. Preflight every user attachment using current file size/content.
3. Materialize accepted `res://` attachments as capture-local bytes.
4. Collect built-ins and append their failures.
5. Put accepted capture-local dictionaries under `payload["attachments"]`.

Do not put global absolute/user paths or global byte attachments in the local
array, preventing duplicates. Preserve the event ID even when the failure list
is nonempty.

The fake bridge must record every replacement snapshot defensively:

```foundryscript
func replaceAttachments(payloads: Array) -> Variant:
	replaced_attachment_payloads.append(payloads.duplicate(true))
	if not replace_attachments_results.is_empty():
		var result: Variant = replace_attachments_results.pop_front()
		if result is bool and result:
			current_attachment_payloads = payloads.duplicate(true)
		return result
	current_attachment_payloads = payloads.duplicate(true)
	return replace_attachments_result
```

Its capture methods record the complete event payload so tests can inspect the
capture-local `attachments` array.

- [ ] **Step 6: Generate UIDs and verify Sentry FoundryScript green**

```bash
/Users/christian/CafecitoGames/Foundry/bin/foundry.macos.editor.dev.arm64 \
  --headless project import --project test_project
scripts/test-project
scripts/test-foundry-script
scripts/test-foundry-uids
```

Expected: all FoundryScript tests and contracts pass.

- [ ] **Step 7: Commit**

```bash
git add addons/FoundryObservabilitySentry test_project/tests
git commit -m "feat: prepare Sentry diagnostic attachments"
```

### Task 4: Apple native attachment mapping

**Files:**

- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/FoundryObservabilitySentry.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Sources/FoundryObservabilitySentry/SentryLifecycleCoordinator.swift`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryLifecycleCoordinatorTests.swift`
- Create: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry/Tests/FoundryObservabilitySentryTests/SentryAttachmentMapperTests.swift`

- [ ] **Step 1: Write failing Swift mapper and lifecycle tests**

Test a pure helper:

```swift
func testMapsFileAndByteAttachmentsPreservingMetadata() throws {
    let file = try XCTUnwrap(foundryAttachment(from: [
        "path": "/tmp/game.log",
        "filename": "game.log",
        "content_type": "text/plain",
        "category": "event.attachment",
    ]))
    XCTAssertEqual(file.filename, "game.log")
    XCTAssertEqual(file.contentType, "text/plain")
    XCTAssertEqual(file.attachmentType, .eventAttachment)

    let bytes = try XCTUnwrap(foundryAttachment(from: [
        "bytes": Data("hello".utf8),
        "filename": "state.txt",
        "content_type": "text/plain",
        "category": "event.attachment",
    ]))
    XCTAssertEqual(bytes.filename, "state.txt")
    XCTAssertEqual(bytes.data, Data("hello".utf8))
}
```

Add exact malformed cases:

```swift
XCTAssertNil(foundryAttachment(from: [:]))
XCTAssertNil(foundryAttachment(from: [
    "bytes": Data(), "filename": "", "category": "event.attachment",
]))
XCTAssertNil(foundryAttachment(from: [
    "path": "relative.txt", "filename": "x", "category": "event.attachment",
]))
XCTAssertNil(foundryAttachment(from: [
    "path": "/tmp/x", "bytes": Data(), "filename": "x",
    "category": "event.attachment",
]))
XCTAssertNil(foundryAttachment(from: [
    "bytes": "not bytes", "filename": "x", "category": "event.attachment",
]))
XCTAssertNil(foundryAttachment(from: [
    "bytes": Data(), "filename": "x", "category": "event.minidump",
]))
```

Add lifecycle equality and `makeAppleSentryOptions` assertions that changing
only `maxAttachmentBytes` restarts the lifecycle and that
`options.maxAttachmentSize` equals the configured value.

Use a scope-driver spy to verify complete replacement clears once then adds the
candidate, capture-local attachments do not mutate global scope, stale owners
cannot replace, and shutdown clears state.

- [ ] **Step 2: Run the red Swift tests**

```bash
task test:sentry-swift
```

Expected: compiler/test failure because attachment configuration and helpers do
not exist.

- [ ] **Step 3: Extend lifecycle configuration and Sentry options**

Append `maxAttachmentBytes: UInt` to `SentryLifecycleConfiguration`, include it
in equality, and parse the normalized nonnegative integer from the bridge
configuration. In `makeAppleSentryOptions`:

```swift
options.maxAttachmentSize = configuration.maxAttachmentBytes
```

Do not enable Sentry Cocoa's UIKit/AppKit screenshot or view-hierarchy options;
the Foundry runtime collector supplies Godot-specific payloads.

- [ ] **Step 4: Implement pure attachment mapping**

Add a focused helper that accepts one normalized payload dictionary:

```swift
func foundryAttachment(from payload: [String: Any]) -> Attachment? {
    guard let filename = payload["filename"] as? String,
          !filename.isEmpty,
          let category = payload["category"] as? String,
          !category.isEmpty
    else { return nil }
    let contentType = (payload["content_type"] as? String).flatMap {
        $0.isEmpty ? nil : $0
    }
    let attachmentType: SentryAttachmentType
    switch category {
    case "event.attachment":
        attachmentType = .eventAttachment
    case "event.view_hierarchy":
        attachmentType = .viewHierarchy
    default:
        return nil
    }
    if let path = payload["path"] as? String, path.hasPrefix("/") {
        guard payload["bytes"] == nil else { return nil }
        return Attachment(
            path: path,
            filename: filename,
            contentType: contentType,
            attachmentType: attachmentType
        )
    }
    guard payload["path"] == nil,
          let data = foundryAttachmentData(payload["bytes"])
    else { return nil }
    return Attachment(
        data: data,
        filename: filename,
        contentType: contentType,
        attachmentType: attachmentType
    )
}
```

`foundryAttachmentData` must accept the exact `PackedByteArray`/FoundrySwift
bridge representation and copy it into `Data`. Extend
`foundationValue(from:)` before the dictionary/array branches:

```swift
if let value = PackedByteArray(variant) {
    return FoundryFoundationValue(Data(value.asBytes()))
}
```

Then `foundryAttachmentData` is:

```swift
private func foundryAttachmentData(_ value: Any?) -> Data? {
    guard let data = value as? Data else {
        return nil
    }
    return Data(data)
}
```

- [ ] **Step 5: Add native replacement and capture-local methods**

Expose a Foundry bridge method named `replaceAttachments` returning `Bool`.
Parse the complete array before mutating scope; reject the whole candidate if
any entry is malformed. Under the current lifecycle owner guard:

```swift
SentrySDK.configureScope { scope in
    scope.clearAttachments()
    candidate.forEach(scope.addAttachment)
}
```

When `captureEvent`, `captureMessage`, or the exception route receives a
nonempty payload `attachments` array, use the Sentry capture-local scope
callback and add those attachments only in that callback. Existing scope and
runtime-context mapping must remain unchanged.

- [ ] **Step 6: Verify Swift and Apple contracts**

```bash
task test:sentry-swift
task test:sentry-contract
```

Expected: all XCTest and iOS/macOS contract tests pass.

- [ ] **Step 7: Commit**

```bash
git add addons/FoundryObservabilitySentry/FoundryObservabilitySentry
git commit -m "feat: deliver attachments through Sentry Apple"
```

### Task 5: Android native attachment mapping

**Files:**

- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleConfiguration.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/AndroidSentrySdkDriver.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryAttachmentMapper.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryLifecycleCoordinatorTest.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryAttachmentMapperTest.java`
- Modify: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeTest.java`

- [ ] **Step 1: Write failing Java mapper, lifecycle, and bridge tests**

Test exact file/byte mapping:

```java
@Test
public void mapsFileAndByteAttachmentsWithMetadata() {
  Attachment file = SentryAttachmentMapper.map(
      mapOf(
          "path", "/tmp/game.log",
          "filename", "game.log",
          "content_type", "text/plain",
          "category", "event.attachment"));
  assertEquals("/tmp/game.log", file.getPathname());
  assertEquals("game.log", file.getFilename());
  assertEquals("text/plain", file.getContentType());
  assertEquals("event.attachment", file.getAttachmentType());

  byte[] source = "hello".getBytes(StandardCharsets.UTF_8);
  Attachment bytes = SentryAttachmentMapper.map(
      mapOf(
          "bytes", source,
          "filename", "state.txt",
          "content_type", "text/plain",
          "category", "event.attachment"));
  source[0] = 0;
  assertArrayEquals(
      "hello".getBytes(StandardCharsets.UTF_8),
      bytes.getBytes());
}
```

Add exact malformed cases:

```java
assertNull(SentryAttachmentMapper.map(Collections.emptyMap()));
assertNull(SentryAttachmentMapper.map(
    mapOf("bytes", new byte[0], "filename", "", "category", "event.attachment")));
assertNull(SentryAttachmentMapper.map(
    mapOf("path", "relative.txt", "filename", "x", "category", "event.attachment")));
assertNull(SentryAttachmentMapper.map(
    mapOf("path", "/tmp/x", "bytes", new byte[0],
        "filename", "x", "category", "event.attachment")));
assertNull(SentryAttachmentMapper.map(
    mapOf("bytes", "not bytes", "filename", "x", "category", "event.attachment")));
assertNull(SentryAttachmentMapper.map(
    mapOf("bytes", new byte[0], "filename", "x", "category", "event.minidump")));
```

Assert complete replacement, stale-owner rejection, capture-local scope
isolation, lifecycle reset, and that changing only `maxAttachmentBytes` changes
lifecycle equality and reaches `SentryOptions.getMaxAttachmentSize()`.

- [ ] **Step 2: Run red Android tests**

```bash
task test:sentry-java
```

Expected: compile/test failure because the mapper and bridge methods are absent.

- [ ] **Step 3: Add lifecycle configuration and driver option**

Add immutable `long maxAttachmentBytes` to `SentryLifecycleConfiguration`,
include it in constructor, `equals`, and `hashCode`, and parse it from the
Foundry config payload.

In `AndroidSentrySdkDriver.start`, set the public Sentry Android 8.50.1 option:

```java
options.setMaxAttachmentSize(configuration.maxAttachmentBytes());
```

- [ ] **Step 4: Implement `SentryAttachmentMapper`**

Create a package-private mapper with:

```java
static Attachment map(Map<String, Object> payload)
```

Require one source, a nonempty filename, category equal to
`event.attachment` or `event.view_hierarchy`, optional content type, and an
absolute file path. Copy `byte[]` values with `Arrays.copyOf`. Construct:

```java
new Attachment(
    path,
    filename,
    contentTypeOrNull,
    category,
    false)
```

or:

```java
new Attachment(
    copiedBytes,
    filename,
    contentTypeOrNull,
    category,
    false)
```

Return `null` for malformed input without mutating Sentry state.

- [ ] **Step 5: Implement replacement and capture-local scope**

Add exported `replaceAttachments(Array payloads)` returning boolean. Parse the
complete candidate first, then inside the current owner guard:

```java
Sentry.getGlobalScope().clearAttachments();
for (Attachment attachment : candidate) {
  Sentry.getGlobalScope().addAttachment(attachment);
}
```

Extend the existing event-local scope callback to append normalized
`payload["attachments"]` after runtime contexts and event-local Foundry scope.
Do not change global scope from capture-local attachments.

- [ ] **Step 6: Verify Android tests and contracts**

```bash
task test:sentry-java
task test:sentry-android-contract
```

Expected: both debug/release JUnit variants and the resolver/export contract pass.

- [ ] **Step 7: Commit**

```bash
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
git commit -m "feat: deliver attachments through Sentry Android"
```

### Task 6: Contracts, documentation, and complete verification

**Files:**

- Modify: `scripts/test-foundry-script`
- Modify: `scripts/test-sentry-ios-build-contract`
- Modify: `scripts/test-sentry-android-build-contract`
- Modify: `scripts/test-package`
- Modify: `README.md`
- Modify: `docs/API.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add failing contract assertions**

Require core resources and UIDs:

```bash
for resource in \
  ObservabilityAttachment \
  ObservabilityAttachmentFailure \
  ObservabilityAttachmentsProvider; do
  [[ -f "$addon/${resource}.fs" ]] || fail "${resource}.fs is missing"
  [[ -f "$addon/${resource}.fs.uid" ]] || fail "${resource}.fs.uid is missing"
done
```

Require Sentry runtime resources and bridge names:

```bash
rg -q 'replaceAttachments' "$swift_bridge" \
  || fail "Apple attachment replacement bridge is missing"
rg -q 'replaceAttachments' "$android_bridge" \
  || fail "Android attachment replacement bridge is missing"
rg -q 'maxAttachmentBytes|maxAttachmentSize' "$swift_lifecycle" \
  || fail "Apple attachment maximum is missing"
rg -q 'maxAttachmentBytes' "$android_lifecycle" \
  || fail "Android attachment maximum is missing"
```

Require exact API docs signatures, config keys, lifetime, `user://`, partial
failures, and independent built-ins in `scripts/test-package`.

- [ ] **Step 2: Run the red contracts**

```bash
task test:foundry-script
task test:sentry-contract
task test:sentry-android-contract
task test:package
```

Expected: documentation assertions fail before the documentation update.

- [ ] **Step 3: Update public documentation**

Add to `docs/API.md`:

- `ObservabilityAttachment` factory/accessor signatures and validation;
- `ObservabilityAttachmentFailure` fields and stable reasons;
- public add/remove/clear/failure signatures;
- persistent session lifetime and handle invalidation;
- 20 MiB default and zero behavior;
- lazy absolute, `user://`, and packaged `res://` behavior;
- independently disabled game-log, screenshot, and scene-tree configuration;
- attachment/event success independence;
- supported Apple/Android native scope behavior and native-race limitation;
- optional provider capability example with integer removal result.

Add a concise README example:

```foundryscript
var attachment := ObservabilityAttachment.from_path(
		"user://logs/gameplay.log",
		"",
		"text/plain",
	)
var handle: String = FoundryObservability.add_attachment(attachment)
FoundryObservability.capture_message("match failed", ObservabilityLevel.ERROR)
for failure: ObservabilityAttachmentFailure in (
		FoundryObservability.last_attachment_failures()):
	print("%s: %s" % [failure.filename(), failure.reason()])
FoundryObservability.remove_attachment(handle)
```

Document the three built-in booleans as opt-in and add the issue to
`CHANGELOG.md` under the current unreleased section.

- [ ] **Step 4: Run focused verification**

```bash
scripts/test-project
task test:sentry-swift
task test:sentry-java
task test:foundry-script
task test:sentry-contract
task test:sentry-android-contract
task test:package
```

Expected: every focused suite exits zero.

- [ ] **Step 5: Run the complete fresh validation gate**

```bash
task test
```

Expected: lint, CI contracts, package tests, 200+ Foundry tests, Swift XCTest,
Android JUnit, and Apple/Android contract tests all exit zero.

- [ ] **Step 6: Review the complete branch diff**

```bash
git diff --check origin/main...HEAD
git status --short
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
```

Verify line by line that every issue requirement maps to code, a deterministic
test, and public documentation. Confirm no generated build outputs or
worktree-local `local.properties` are tracked.

- [ ] **Step 7: Commit final contracts and docs**

```bash
git add scripts README.md docs/API.md CHANGELOG.md
git commit -m "docs: document diagnostic attachments"
```

- [ ] **Step 8: Run final post-commit verification**

```bash
task test
git status --short --branch
```

Expected: full gate exits zero and the tracked worktree is clean.
