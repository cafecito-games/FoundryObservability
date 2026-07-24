# FoundryObservability Cross-Platform Sentry Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing `FoundryObservabilitySentry` addon with transparent iOS, macOS arm64, and Android Sentry backends while preserving the current FoundryScript provider API.

**Architecture:** Keep `SentryObservabilityProvider` as the only public provider. Resolve the Android `Engine` singleton named `SentryObservabilityBridge` before falling back to the existing Apple/macOS `ClassDB` bridge. Add a Java `FoundryPlugin` Android library implementing the same dictionary bridge contract as Swift, then package platform artifacts through the existing descriptor, export plugin, and package script.

**Tech Stack:** FoundryScript, Foundry testlib, Java 17, Android Gradle Plugin 8.13.2, Foundry Android `0.1.0-alpha3-SNAPSHOT` compile-only artifacts, Sentry Android `8.50.1`, Swift 6, Sentry Cocoa `9.23.0`, XcodeGen, XCTest, JUnit 4, Gradle, Task, Bash, zip/unzip.

---

## File map

- Modify `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs` for Android-first bridge lookup.
- Create `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/` with Gradle, manifest, Java bridge, Java mapper, and JUnit tests.
- Modify `addons/FoundryObservabilitySentry/export_plugin.fs` for Android AAR/dependency export.
- Modify `addons/FoundryObservabilitySentry/FoundryObservabilitySentry.foundryextension` for macOS arm64.
- Modify `Taskfile.yml` for Android AAR and macOS framework builds.
- Create `scripts/test-sentry-android-build-contract` and extend `scripts/test-sentry-ios-build-contract` for the platform contracts.
- Modify `scripts/package-sentry-addon`, `scripts/test-package`, `.gitignore`, `README.md`, `BUILD.md`, and the Sentry plugin description.
- Add `.gitkeep` files under `bin/macos_arm64`, `bin/android/debug`, and `bin/android/release`.

## Task 1: Add Android-aware provider resolution

**Files:**

- Modify: `addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs`
- Create: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Write the failing resolver contract test**

Create an executable `scripts/test-sentry-android-build-contract` containing:

```bash
#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
provider="$repo_root/addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

[[ -f "$provider" ]] || fail "Sentry provider is missing"
rg -q 'Engine\.has_singleton\(_NATIVE_CLASS\)' "$provider" \
  || fail "provider must check the Android singleton"
rg -q 'Engine\.get_singleton\(_NATIVE_CLASS\)' "$provider" \
  || fail "provider must retrieve the Android singleton"
rg -q 'ClassDB\.class_exists\(_NATIVE_CLASS\)' "$provider" \
  || fail "provider must preserve Apple ClassDB resolution"

echo "Sentry Android resolver contract checks passed"
```

Run `chmod +x scripts/test-sentry-android-build-contract`.

- [ ] **Step 2: Run the test and verify the intended red failure**

Run:

```sh
scripts/test-sentry-android-build-contract
```

Expected: fail with `provider must check the Android singleton` because the current provider only resolves `ClassDB`.

- [ ] **Step 3: Implement Android-first resolution**

Keep the injected bridge path unchanged and replace `_resolve_bridge()` with:

```foundryscript
func _resolve_bridge() -> Object?:
	if _bridge != null:
		return _bridge
	if Engine.has_singleton(_NATIVE_CLASS):
		_bridge = Engine.get_singleton(_NATIVE_CLASS)
		return _bridge
	if not ClassDB.class_exists(_NATIVE_CLASS) or not ClassDB.can_instantiate(_NATIVE_CLASS):
		return null
	_bridge = ClassDB.instantiate(_NATIVE_CLASS)
	return _bridge
```

- [ ] **Step 4: Run the resolver test green and commit**

Run `scripts/test-sentry-android-build-contract`; expect all three resolver assertions to pass. Commit:

```sh
git add addons/FoundryObservabilitySentry/SentryObservabilityProvider.fs scripts/test-sentry-android-build-contract
git commit -m "feat: resolve Android Sentry bridge"
```

## Task 2: Scaffold the Android library and write mapper tests

**Files:**

- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/settings.gradle`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/build.gradle`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/gradle.properties`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/consumer-rules.pro`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/AndroidManifest.xml`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/android-dependencies.txt`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapperTest.java`
- Copy: `gradlew` and `gradle/wrapper/*` from the AuthenticationKit Android reference

- [ ] **Step 1: Add the Gradle module**

Use the reference project’s Gradle wrapper and create `settings.gradle` with:

```gradle
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.PREFER_PROJECT)
    repositories {
        google()
        mavenCentral()
        maven { url 'https://central.sonatype.com/repository/maven-snapshots/' }
    }
}

rootProject.name = "AndroidFoundryObservabilitySentry"
```

Create `gradle.properties` with `android.useAndroidX=true` and `consumer-rules.pro` with `# FoundryObservabilitySentry consumer rules.`. Put exactly `io.sentry:sentry-android:8.50.1` in `android-dependencies.txt`.

Create `build.gradle` with Android Gradle Plugin `8.13.2`, namespace `games.cafecito.android.foundryobservabilitysentry`, compile/target SDK 36, min SDK 24, Java 17 source/target compatibility, release minification disabled, and these dependencies:

```gradle
dependencies {
    compileOnly "games.cafecito.foundry:foundry:0.1.0-alpha3-SNAPSHOT"
    compileOnly "games.cafecito.foundry:foundry-debug:0.1.0-alpha3-SNAPSHOT"
    compileOnly "games.cafecito.foundry:foundry-tools:0.1.0-alpha3-SNAPSHOT"
    runtimeDependencies.each { coordinate -> implementation coordinate }
    testImplementation 'junit:junit:4.13.2'
    testImplementation "games.cafecito.foundry:foundry:0.1.0-alpha3-SNAPSHOT"
    testImplementation "games.cafecito.foundry:foundry-debug:0.1.0-alpha3-SNAPSHOT"
    testImplementation "games.cafecito.foundry:foundry-tools:0.1.0-alpha3-SNAPSHOT"
    testImplementation 'org.robolectric:robolectric:4.14.1'
}
```

Read `android-dependencies.txt` into `runtimeDependencies` exactly as the AuthenticationKit module does, ignoring blank/comment lines. Do not copy AuthenticationKit Java sources.

- [ ] **Step 2: Add the manifest**

Create `src/main/AndroidManifest.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <application>
        <meta-data
            android:name="org.godotengine.plugin.v2.SentryObservabilityBridge"
            android:value="games.cafecito.android.foundryobservabilitysentry.SentryObservabilityBridge" />
        <meta-data android:name="io.sentry.auto-init" android:value="false" />
        <meta-data android:name="io.sentry.ndk.scope-sync.enable" android:value="true" />
    </application>
</manifest>
```

- [ ] **Step 3: Write the failing mapper tests**

Create `SentryEventMapperTest.java` with tests for these exact behaviors:

```java
assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(10));
assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(20));
assertEquals(SentryLevel.INFO, SentryEventMapper.sentryLevel(30));
assertEquals(SentryLevel.WARNING, SentryEventMapper.sentryLevel(40));
assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(50));
assertEquals(SentryLevel.FATAL, SentryEventMapper.sentryLevel(60));
assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(999));
```

The test must merge global attributes, event attributes, and exception attributes in that order; assert that `foundry.kind`, `foundry.source`, `foundry.timestamp_msec`, `foundry.exception_type`, and non-empty `foundry.stack_trace` override caller keys; build a Sentry exception with type `InvalidState` and value `boom`; and omit an unsupported nested `Object` while preserving supported strings. Use `SentryEventMapper.makeEvent(payload, globalAttributes)` with a `Map<String,Object>` payload containing `kind`, `level`, `message`, `source`, `timestamp_msec`, `attributes`, and `exception` maps.

- [ ] **Step 4: Run the mapper tests red**

Run:

```sh
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew test
```

Expected: test compilation fails because `SentryEventMapper` does not exist.

## Task 3: Implement the Android event mapper

**Files:**

- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryEventMapper.java`

- [ ] **Step 1: Implement mapper methods**

Implement package-private static methods with these signatures:

```java
static SentryLevel sentryLevel(int level)
static Map<String, Object> copyDictionary(Map<?, ?> source)
static Map<String, Object> mergedExtras(
    Map<String, Object> global,
    Map<String, Object> event,
    String kind,
    String source,
    long timestampMsec,
    Map<?, ?> exception)
static SentryEvent makeEvent(Map<?, ?> payload, Map<String, Object> globalAttributes)
```

Map levels 10/20 to DEBUG, 30 to INFO, 40 to WARNING, 50 to ERROR, 60 to FATAL, and unknown values to ERROR. Recursively copy maps with string keys and lists/arrays. Preserve booleans, integral numbers, floating-point numbers, and strings; normalize integer types to `Long`, floating types to `Double`, and omit null/unsupported values. Missing or malformed payload fields use empty strings, level ERROR, timestamp `0L`, or empty maps.

- [ ] **Step 2: Implement extras precedence**

Copy global attributes first, overlay event attributes, overlay exception attributes, then write reserved metadata last:

```java
extras.put("foundry.kind", kind);
extras.put("foundry.source", source);
extras.put("foundry.timestamp_msec", timestampMsec);
extras.put("foundry.exception_type", exceptionType);
if (!stackTrace.isEmpty()) {
    extras.put("foundry.stack_trace", stackTrace);
}
```

Only write exception metadata when the exception payload is a map. Preserve the engine timestamp as metadata because it is not a Unix timestamp.

- [ ] **Step 3: Implement Sentry event construction**

Create `SentryEvent`, set its formatted message, mapped level, non-empty logger, and extras. When an exception map has a non-empty type or message, add one `io.sentry.protocol.SentryException` with type/value and a stack trace object when a stack string is present. `makeEvent` must not send data or access global Sentry state.

- [ ] **Step 4: Run mapper tests green and commit**

Run `./gradlew test` from the Android module; expect all mapper tests to pass. Commit:

```sh
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
git commit -m "feat: map Foundry events for Sentry Android"
```

## Task 4: Add the Android Sentry bridge

**Files:**

- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/main/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridge.java`
- Create: `addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/src/test/java/games/cafecito/android/foundryobservabilitysentry/SentryObservabilityBridgeNamingTest.java`
- Modify: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Extend the Android contract test and verify red**

Assert that the Android source contains `extends FoundryPlugin`, `return "SentryObservabilityBridge"`, `SentryAndroid.init`, `Sentry.captureEvent`, `Sentry.flush`, and `Sentry.close`; that the manifest contains the exact plugin class; and that `android-dependencies.txt` pins `io.sentry:sentry-android:8.50.1`. Run the script before adding the bridge and confirm it fails on the missing bridge source.

- [ ] **Step 2: Add the bridge naming test**

Create a JUnit test that reads the manifest resource and asserts:

```java
assertTrue(manifest.contains("org.godotengine.plugin.v2.SentryObservabilityBridge"));
assertTrue(manifest.contains(
    "games.cafecito.android.foundryobservabilitysentry.SentryObservabilityBridge"));
```

- [ ] **Step 3: Implement configuration and availability**

Implement `SentryObservabilityBridge extends FoundryPlugin` with `BRIDGE_ERROR_OK = 0`, `BRIDGE_ERROR_FAILED = 1`, a `Map<String,Object> globalAttributes`, `configured`, and `didShutdown` fields. The constructor accepts `Foundry` and `getPluginName()` returns `SentryObservabilityBridge`.

Annotate `configure(Dictionary)`, `isAvailable()`, `capture(Dictionary)`, `flush(int)`, and `shutdown()` with `@UsedByFoundry`. `configure` must close an active client, copy global attributes, return OK for disabled configuration, reject enabled empty DSNs, and call `SentryAndroid.init(getContext().getApplicationContext(), options -> ...)`. Set DSN, debug, environment, release, and dist, treating empty strings as unset. Catch initialization exceptions and return the failed code. A successful enabled configure resets `didShutdown` and sets `configured = Sentry.isEnabled()`.

- [ ] **Step 4: Implement capture, flush, and shutdown**

Use these behaviors:

```java
@UsedByFoundry
public String capture(Dictionary payload) {
    if (!isAvailable()) {
        return "";
    }
    return Sentry.captureEvent(
        SentryEventMapper.makeEvent(payload, globalAttributes)).toString();
}

@UsedByFoundry
public int flush(int timeoutMsec) {
    if (!isAvailable()) {
        return BRIDGE_ERROR_FAILED;
    }
    Sentry.flush(Math.max(0, timeoutMsec));
    return BRIDGE_ERROR_OK;
}

@UsedByFoundry
public void shutdown() {
    if (didShutdown) {
        return;
    }
    didShutdown = true;
    closeActiveClient();
}
```

`closeActiveClient()` calls `Sentry.close()` only when configured and then clears the configured flag. Repeated shutdowns are no-ops.

- [ ] **Step 5: Run Android tests, lint, and AAR builds**

Run:

```sh
scripts/test-sentry-android-build-contract
cd addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
./gradlew test lintRelease assembleDebug assembleRelease
```

Expected: contract checks pass, Java tests pass, lint has zero errors, and both AAR variants assemble.

- [ ] **Step 6: Commit the Android bridge**

```sh
git add addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry scripts/test-sentry-android-build-contract
git commit -m "feat: add Sentry Android bridge"
```

## Task 5: Wire Android export and macOS descriptor/build support

**Files:**

- Modify: `addons/FoundryObservabilitySentry/export_plugin.fs`
- Modify: `addons/FoundryObservabilitySentry/FoundryObservabilitySentry.foundryextension`
- Modify: `Taskfile.yml`
- Modify: `scripts/test-sentry-ios-build-contract`
- Modify: `scripts/test-sentry-android-build-contract`

- [ ] **Step 1: Add failing platform assertions**

Extend the Apple contract script with checks for `macos.arm64`, an empty macOS dependency map, `generic/platform=macOS`, and the `bin/macos_arm64/FoundryObservabilitySentry.framework` Taskfile path. Extend the Android contract script with checks for `_get_android_libraries`, `_get_android_dependencies`, Android-only platform support, debug/release AAR paths, and `android-dependencies.txt`. Run both scripts before changing the implementation and confirm the new assertions fail.

- [ ] **Step 2: Add macOS descriptor entries**

Add these exact lines to the extension descriptor:

```ini
macos.arm64 = "res://addons/FoundryObservabilitySentry/bin/macos_arm64/FoundryObservabilitySentry.framework"

macos.arm64 = {}
```

Keep all four iOS entries and empty dependency maps unchanged. Do not add FoundrySwift as a descriptor dependency.

- [ ] **Step 3: Add Android export behavior**

Register an `AndroidExportPlugin` beside the existing iOS export plugin. It must return `FoundryObservabilitySentryAndroid` from `_get_name()`, choose `bin/android/debug/FoundryObservabilitySentry-debug.aar` for debug and the release path otherwise, read non-empty non-comment lines from `res://addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/android-dependencies.txt`, return them from `_get_android_dependencies`, and support only `Android`. Preserve the existing iOS class and iOS-only support check.

- [ ] **Step 4: Extend native build tasks**

Keep the `ios:sentry` task name. After the current iOS xcframework build, add the `FoundryObservabilitySentry_macOS` build with destination `generic/platform=macOS`, `ARCHS=arm64`, and copy the result to `../bin/macos_arm64/FoundryObservabilitySentry.framework`.

Add this Android task:

```yaml
  android:sentry:
    desc: Build FoundryObservabilitySentry Android AARs
    dir: addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry
    cmds:
      - ./gradlew test lintRelease assembleDebug assembleRelease
      - mkdir -p ../bin/android/debug ../bin/android/release
      - cp build/outputs/aar/AndroidFoundryObservabilitySentry-debug.aar ../bin/android/debug/FoundryObservabilitySentry-debug.aar
      - cp build/outputs/aar/AndroidFoundryObservabilitySentry-release.aar ../bin/android/release/FoundryObservabilitySentry-release.aar
```

Add `scripts/test-sentry-android-build-contract` to the `test` task dependencies while retaining the existing Apple contract task.

- [ ] **Step 5: Run platform contracts green and commit**

Run `scripts/test-sentry-ios-build-contract` and `scripts/test-sentry-android-build-contract`; expect both to pass. Commit:

```sh
git add addons/FoundryObservabilitySentry/export_plugin.fs addons/FoundryObservabilitySentry/FoundryObservabilitySentry.foundryextension Taskfile.yml scripts/test-sentry-ios-build-contract scripts/test-sentry-android-build-contract
git commit -m "build: wire cross-platform Sentry artifacts"
```

## Task 6: Update package contents and repository hygiene

**Files:**

- Modify: `scripts/package-sentry-addon`
- Modify: `scripts/test-package`
- Modify: `.gitignore`
- Create: `addons/FoundryObservabilitySentry/bin/macos_arm64/.gitkeep`
- Create: `addons/FoundryObservabilitySentry/bin/android/debug/.gitkeep`
- Create: `addons/FoundryObservabilitySentry/bin/android/release/.gitkeep`

- [ ] **Step 1: Add failing package assertions**

Add assertions requiring these archive entries:

```bash
grep -qx 'addons/FoundryObservabilitySentry/bin/macos_arm64/.gitkeep' "$sentry_listing" || fail "Sentry package is missing macOS artifacts"
grep -qx 'addons/FoundryObservabilitySentry/bin/android/debug/.gitkeep' "$sentry_listing" || fail "Sentry package is missing Android debug artifacts"
grep -qx 'addons/FoundryObservabilitySentry/bin/android/release/.gitkeep' "$sentry_listing" || fail "Sentry package is missing Android release artifacts"
grep -qx 'addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/android-dependencies.txt' "$sentry_listing" || fail "Sentry package is missing Android dependencies"
```

Run `scripts/test-package`; expected failure is that the package script currently copies only `bin/ios`.

- [ ] **Step 2: Copy all runtime artifact directories**

Replace the single iOS staging/copy block in `scripts/package-sentry-addon` with:

```bash
for artifact_dir in ios macos_arm64 android/debug android/release; do
  mkdir -p "$tmp_dir/addons/FoundryObservabilitySentry/bin/$artifact_dir"
  cp -R "$sentry_source/bin/$artifact_dir/." "$tmp_dir/addons/FoundryObservabilitySentry/bin/$artifact_dir/"
done
```

Keep top-level runtime files, version override, `.DS_Store` removal, and zip creation. Do not copy the nested Swift project or Android Gradle module.

- [ ] **Step 3: Ignore generated artifacts and add empty directories**

Add to `.gitignore`:

```gitignore
addons/FoundryObservabilitySentry/bin/macos_arm64/*.framework/
addons/FoundryObservabilitySentry/bin/android/debug/*.aar
addons/FoundryObservabilitySentry/bin/android/release/*.aar
addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/.gradle/
addons/FoundryObservabilitySentry/AndroidFoundryObservabilitySentry/build/
```

Create the three `.gitkeep` files as zero-length files.

- [ ] **Step 4: Run package checks green and commit**

Run `scripts/test-package`; expect both archives to pass listing and exclusion assertions. Commit:

```sh
git add .gitignore scripts/package-sentry-addon scripts/test-package addons/FoundryObservabilitySentry/bin
git commit -m "build: package Android and macOS Sentry artifacts"
```

## Task 7: Document the unified addon workflow

**Files:**

- Modify: `README.md`
- Modify: `BUILD.md`
- Modify: `addons/FoundryObservabilitySentry/plugin.cfg`

- [ ] **Step 1: Update documentation**

State in README that one optional `FoundryObservabilitySentry` addon supports iOS, macOS arm64, and Android, and that `SentryObservabilityProvider` selects the native backend for the export platform. State that unsupported hosts remain safe but unavailable.

In `BUILD.md`, document Java 17, Android SDK Platform 36, the Gradle wrapper, staged paths `bin/ios`, `bin/macos_arm64`, `bin/android/debug`, and `bin/android/release`, and these commands:

```sh
task ios:sentry
task android:sentry
scripts/test-sentry-android-build-contract
```

Change the plugin description to `Optional cross-platform Sentry provider for FoundryObservability` without changing its name, version, or script path.

- [ ] **Step 2: Run lint and commit documentation**

Run `prek run --all-files`; expect all hooks to pass. Commit:

```sh
git add README.md BUILD.md addons/FoundryObservabilitySentry/plugin.cfg
git commit -m "docs: document cross-platform Sentry addon"
```

## Task 8: Build native artifacts and run the complete verification gate

**Files:**

- Generated/ignored: `addons/FoundryObservabilitySentry/bin/ios/FoundryObservabilitySentry.xcframework`
- Generated/ignored: `addons/FoundryObservabilitySentry/bin/macos_arm64/FoundryObservabilitySentry.framework`
- Generated/ignored: `addons/FoundryObservabilitySentry/bin/android/debug/FoundryObservabilitySentry-debug.aar`
- Generated/ignored: `addons/FoundryObservabilitySentry/bin/android/release/FoundryObservabilitySentry-release.aar`

- [ ] **Step 1: Build Apple artifacts**

Run `task ios:sentry`; expect iOS device/simulator slices in the xcframework and the arm64 macOS framework in `bin/macos_arm64`.

- [ ] **Step 2: Build Android artifacts**

Run `task android:sentry`; expect Android tests/lint to pass and both staged AARs to exist.

- [ ] **Step 3: Run targeted verification**

Run:

```sh
task test:sentry-swift
task test:sentry-contract
scripts/test-sentry-android-build-contract
scripts/test-package
scripts/test-project
scripts/test-foundry-script
```

Expected: all targeted commands exit 0.

- [ ] **Step 4: Run the full repository gate**

Run `task test`; expect lint, CI contracts, package checks, Swift tests, Android contracts, Foundry project tests, FoundryScript diagnostics, and UID checks to pass.

- [ ] **Step 5: Inspect and commit final changes**

Run `git diff --check`, `git status --short`, and `git diff main...HEAD --stat`. Expected: no whitespace errors, only intended tracked changes, and generated native binaries remain ignored. Commit any final fixes with:

```sh
git add addons scripts Taskfile.yml README.md BUILD.md .gitignore
git commit -m "test: verify cross-platform Sentry addon"
```
