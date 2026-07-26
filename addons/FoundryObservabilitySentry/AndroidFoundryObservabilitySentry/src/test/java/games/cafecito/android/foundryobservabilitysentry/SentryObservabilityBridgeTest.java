package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import io.sentry.Breadcrumb;
import io.sentry.IScope;
import io.sentry.ScopeType;
import io.sentry.Sentry;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.protocol.SentryId;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.After;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class SentryObservabilityBridgeTest {
  private static final String OWNER = "test-owner";

  @After
  public void closeSentry() {
    if (Sentry.isEnabled()) {
      for (ScopeType scopeType :
          List.of(ScopeType.GLOBAL, ScopeType.ISOLATION, ScopeType.CURRENT)) {
        Sentry.configureScope(scopeType, IScope::clear);
      }
    }
    Sentry.close();
  }

  @Test
  public void rejectsEnabledConfigurationWithoutDsn() {
    SentryObservabilityBridge bridge = newBridge();
    assertEquals(1, bridge.lifecycleVersion());

    Dictionary payload = new Dictionary();
    payload.put("enabled", true);
    payload.put("lifecycle_owner", OWNER);

    assertEquals(1, bridge.configure(payload));
    assertFalse(bridge.isAvailable(OWNER));
  }

  @Test
  public void mapsEmptySentryIdToAnEmptyProviderId() {
    assertEquals("", SentryObservabilityBridge.eventIdString(SentryId.EMPTY_ID));
    assertEquals("", SentryObservabilityBridge.eventIdString(null));
    assertNotEquals("", SentryObservabilityBridge.eventIdString(new SentryId()));
  }

  @Test
  public void disabledConfigurationIsUnavailableAndShutdownIsIdempotent() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary payload = new Dictionary();
    payload.put("enabled", false);
    payload.put("lifecycle_owner", OWNER);

    assertEquals(0, bridge.configure(payload));
    assertFalse(bridge.isAvailable(OWNER));
    assertEquals("", bridge.capture(new Dictionary()));
    assertEquals(0, bridge.flush(OWNER, 100));

    bridge.shutdown(OWNER);
    bridge.shutdown(OWNER);
    assertFalse(bridge.isAvailable(OWNER));
  }

  @Test
  public void configuredBridgeCapturesFlushesAndShutsDown() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("environment", "test");
    configuration.put("release", "1.2.3");
    configuration.put("lifecycle_owner", OWNER);

    assertEquals(0, bridge.configure(configuration));
    assertTrue(bridge.isAvailable(OWNER));

    Dictionary event = new Dictionary();
    event.put("kind", "message");
    event.put("message", "hello");
    assertNotEquals("", bridge.capture(event));
    assertEquals(0, bridge.flush(OWNER, 0));

    bridge.shutdown(OWNER);
    assertFalse(bridge.isAvailable(OWNER));
    bridge.shutdown(OWNER);
  }

  @Test
  public void staleDisabledConfigurationDoesNotClearCurrentOwnerState() {
    SentryObservabilityBridge bridge = newBridge();
    String currentOwner = "current-owner";

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("environment", "test");
    configuration.put("release", "1.2.3");
    configuration.put("global_attributes", Map.of("build", 42));
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));

    configuration.put("lifecycle_owner", currentOwner);
    assertEquals(0, bridge.configure(configuration));
    assertTrue(bridge.isAvailable(currentOwner));

    Dictionary staleDisabledConfiguration = new Dictionary();
    staleDisabledConfiguration.put("enabled", false);
    staleDisabledConfiguration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(staleDisabledConfiguration));
    assertTrue(bridge.isAvailable(currentOwner));

    Dictionary event = new Dictionary();
    event.put("kind", "message");
    event.put("message", "current owner still captures");
    assertNotEquals("", bridge.capture(event));

    bridge.shutdown(currentOwner);
  }

  @Test
  public void configuredBridgeCapturesStructuredLogs() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("logs_enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);

    assertEquals(0, bridge.configure(configuration));

    Dictionary log = new Dictionary();
    log.put("kind", "log");
    log.put("level", 40);
    log.put("message", "warning");
    log.put("source", "foundry.logging");
    log.put("timestamp_msec", 1234L);
    log.put("attributes", java.util.Map.of("logger_name", "combat"));

    assertFalse(bridge.captureLog(log).isEmpty());
    bridge.shutdown(OWNER);
  }

  @Test
  public void scopedStructuredLogIsRejectedBeforeNativeLoggingAndDoesNotLeakToNextLog() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("logs_enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));
    AtomicInteger nativeLogCalls = new AtomicInteger();
    Sentry.getCurrentScopes().getOptions().getLogs().setBeforeSend(event -> {
      nativeLogCalls.incrementAndGet();
      return event;
    });

    Dictionary log = new Dictionary();
    log.put("kind", "log");
    log.put("level", 40);
    log.put("message", "warning");
    log.put("source", "foundry.logging");
    log.put(
        "scope",
        Map.of(
            "tags", Map.of("region", "iad"),
            "contexts", Map.of("match", Map.of("teams", List.of("red", "blue")))));

    assertEquals("", bridge.captureLog(log));
    assertEquals(0, nativeLogCalls.get());

    log.remove("scope");
    assertFalse(bridge.captureLog(log).isEmpty());
    assertEquals(1, nativeLogCalls.get());
    bridge.shutdown(OWNER);
  }

  @Test
  public void configuredBridgeCapturesBreadcrumbs() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));

    Dictionary breadcrumb = new Dictionary();
    breadcrumb.put("message", "warning");
    breadcrumb.put("category", "error");
    breadcrumb.put("level", 40);
    breadcrumb.put("timestamp_msec", 1234L);
    breadcrumb.put("attributes", java.util.Map.of("error.file", "res://player.fs"));

    assertTrue(bridge.captureBreadcrumb(breadcrumb));
    bridge.shutdown(OWNER);
  }

  @Test
  public void configuredBridgeCapturesFeedbackWithOptionalAssociation() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("provider_options", java.util.Map.of("send_default_pii", true));
    configuration.put("lifecycle_owner", OWNER);

    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("message", "The tutorial was confusing.");
    feedback.put("name", "Player One");
    feedback.put("contact_email", "player@example.com");
    feedback.put("associated_event_id", "0123456789abcdef0123456789abcdef");

    assertFalse(bridge.captureFeedback(feedback).isEmpty());
    bridge.shutdown(OWNER);
  }

  @Test
  public void rejectsFeedbackWithoutMessage() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("name", "Player One");

    assertEquals("", bridge.captureFeedback(feedback));
    bridge.shutdown(OWNER);
  }

  @Test
  public void rejectsFeedbackWithInvalidAssociatedEventId() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("message", "Feedback with a bad association");
    feedback.put("associated_event_id", "not-a-sentry-id");

    assertEquals("", bridge.captureFeedback(feedback));
    bridge.shutdown(OWNER);
  }

  @Test
  public void appliesAndroidAnrOptionsFromPayload() {
    SentryAndroidOptions options = new SentryAndroidOptions();

    SentryObservabilityBridge.applyAndroidAnrDiagnostics(
        options,
        Map.of(
            "android_anr_detection_enabled", false,
            "android_anr_timeout_msec", 6400L,
            "android_anr_attach_thread_dump", true));

    assertFalse(options.isAnrEnabled());
    assertEquals(6400L, options.getAnrTimeoutIntervalMillis());
    assertTrue(options.isAttachAnrThreadDump());
  }

  @Test
  public void missingAndroidAnrKeysPreserveNativeDefaults() {
    SentryAndroidOptions options = new SentryAndroidOptions();
    options.setAnrEnabled(false);
    options.setAnrTimeoutIntervalMillis(7300L);
    options.setAttachAnrThreadDump(true);

    SentryObservabilityBridge.applyAndroidAnrDiagnostics(
        options,
        Collections.emptyMap());

    assertFalse(options.isAnrEnabled());
    assertEquals(7300L, options.getAnrTimeoutIntervalMillis());
    assertTrue(options.isAttachAnrThreadDump());
  }

  @Test
  public void malformedAndroidAnrBooleanValuesPreserveExistingOptions() {
    Object[] malformedValues = {"false", 0, null};
    Map<String, Object> payload = new HashMap<>();

    for (Object malformedValue : malformedValues) {
      SentryAndroidOptions options = new SentryAndroidOptions();
      options.setAnrEnabled(true);
      options.setAttachAnrThreadDump(true);
      payload.put("android_anr_detection_enabled", malformedValue);
      payload.put("android_anr_attach_thread_dump", malformedValue);

      SentryObservabilityBridge.applyAndroidAnrDiagnostics(options, payload);

      assertTrue(options.isAnrEnabled());
      assertTrue(options.isAttachAnrThreadDump());
    }
  }

  @Test
  public void malformedAndroidAnrTimeoutValuesPreserveExistingOption() {
    Object[] malformedValues = {
        "6400",
        null,
        Double.NaN,
        Double.POSITIVE_INFINITY,
        Double.NEGATIVE_INFINITY,
        6400.5,
        Double.MAX_VALUE,
        BigInteger.valueOf(Long.MAX_VALUE).add(BigInteger.ONE),
        new BigDecimal("6400.5"),
        new BigDecimal(BigInteger.valueOf(Long.MAX_VALUE).add(BigInteger.ONE))
    };
    Map<String, Object> payload = new HashMap<>();

    for (Object malformedValue : malformedValues) {
      SentryAndroidOptions options = new SentryAndroidOptions();
      options.setAnrTimeoutIntervalMillis(7300L);
      payload.put("android_anr_timeout_msec", malformedValue);

      SentryObservabilityBridge.applyAndroidAnrDiagnostics(options, payload);

      assertEquals(7300L, options.getAnrTimeoutIntervalMillis());
    }
  }

  @Test
  public void belowMinimumAndroidAnrTimeoutValuesPreserveExistingOption() {
    Object[] belowMinimumValues = {0, -1L, 999.0};
    Map<String, Object> payload = new HashMap<>();

    for (Object belowMinimumValue : belowMinimumValues) {
      SentryAndroidOptions options = new SentryAndroidOptions();
      options.setAnrTimeoutIntervalMillis(7300L);
      payload.put("android_anr_timeout_msec", belowMinimumValue);

      SentryObservabilityBridge.applyAndroidAnrDiagnostics(options, payload);

      assertEquals(7300L, options.getAnrTimeoutIntervalMillis());
    }
  }

  @Test
  public void parsesExactBoundedMaxBreadcrumbsWithCompatibleFallbackAndClamp() {
    assertEquals(2, SentryObservabilityBridge.maxBreadcrumbsValue(2));
    assertEquals(2, SentryObservabilityBridge.maxBreadcrumbsValue(2.0D));
    assertEquals(0, SentryObservabilityBridge.maxBreadcrumbsValue(-5));
    assertEquals(100, SentryObservabilityBridge.maxBreadcrumbsValue(null));
    assertEquals(100, SentryObservabilityBridge.maxBreadcrumbsValue("2"));
    assertEquals(100, SentryObservabilityBridge.maxBreadcrumbsValue(2.5D));
    assertEquals(
        100,
        SentryObservabilityBridge.maxBreadcrumbsValue(
            BigInteger.valueOf(Integer.MAX_VALUE).add(BigInteger.ONE)));
  }

  @SuppressWarnings("unchecked")
  @Test
  public void applyScopeReplacesFoundryValuesAndPreservesUnrelatedScopeData() {
    SentryObservabilityBridge bridge = configuredBridge();
    configureCurrentScope(scope -> {
      scope.setTag("native", "preserved");
      scope.setContexts("device", Map.of("model", "test-device"));
    });

    Dictionary first = new Dictionary();
    first.put("tags", Map.of("region", "iad", "stale", "remove-me"));
    first.put(
        "contexts",
        Map.of(
            "match",
            Map.of(
                "id", 7,
                "teams", List.of("red", "blue")),
            "empty", Map.of()));
    first.put(
        "user",
        Map.of(
            "id", "player-7",
            "display_name", "Mina",
            "contact_email", "mina@example.com",
            "ip_address", "127.0.0.1"));

    assertTrue(bridge.applyScope(first));

    IScope applied = currentScope();
    assertEquals("iad", applied.getTags().get("region"));
    assertEquals("preserved", applied.getTags().get("native"));
    assertEquals(7, ((Map<?, ?>) applied.getContexts().get("match")).get("id"));
    assertEquals(
        List.of("red", "blue"),
        ((Map<?, ?>) applied.getContexts().get("match")).get("teams"));
    assertEquals(Collections.emptyMap(), applied.getContexts().get("empty"));
    assertEquals("test-device", ((Map<?, ?>) applied.getContexts().get("device")).get("model"));
    assertEquals("player-7", applied.getUser().getId());
    assertEquals("Mina", applied.getUser().getUsername());
    assertEquals("mina@example.com", applied.getUser().getEmail());
    assertEquals(null, applied.getUser().getIpAddress());

    Dictionary replacement = new Dictionary();
    replacement.put("tags", Map.of("region", "fra"));
    replacement.put("contexts", Map.of("session", Map.of("round", 2)));
    assertTrue(bridge.applyScope(replacement));

    IScope replaced = currentScope();
    assertEquals("fra", replaced.getTags().get("region"));
    assertFalse(replaced.getTags().containsKey("stale"));
    assertEquals("preserved", replaced.getTags().get("native"));
    assertFalse(replaced.getContexts().containsKey("match"));
    assertFalse(replaced.getContexts().containsKey("empty"));
    assertEquals(2, ((Map<?, ?>) replaced.getContexts().get("session")).get("round"));
    assertEquals("test-device", ((Map<?, ?>) replaced.getContexts().get("device")).get("model"));
    assertEquals(null, replaced.getUser());

    assertTrue(bridge.applyScope(new Dictionary()));
    IScope cleared = currentScope();
    assertFalse(cleared.getTags().containsKey("region"));
    assertFalse(cleared.getContexts().containsKey("session"));
    assertEquals("preserved", cleared.getTags().get("native"));
    assertEquals("test-device", ((Map<?, ?>) cleared.getContexts().get("device")).get("model"));

    bridge.shutdown(OWNER);
  }

  @Test
  public void applyScopeRejectsLiveNativeCollisionsAtomicallyAndRetainsOwnership() {
    SentryObservabilityBridge bridge = configuredBridge();
    Dictionary initial = new Dictionary();
    initial.put("tags", Map.of("owned-tag-0", "owned", "stale", "keep-on-rejection"));
    initial.put("contexts", Map.of("owned-context-0", Map.of("value", 0)));
    initial.put("user", Map.of("id", "player-0"));
    assertTrue(bridge.applyScope(initial));

    List<String> tagCollisions = List.of("native-tag");
    List<String> contextCollisions = List.of("device", "foundry", "foundry_engine");
    int index = 0;
    for (String collisionKey : tagCollisions) {
      configureCurrentScope(scope -> scope.setTag(collisionKey, "native"));
      index = assertCollisionRejectedAndSafeReplacementSucceeds(
          bridge, collisionKey, true, index);
    }
    for (String collisionKey : contextCollisions) {
      if ("device".equals(collisionKey)) {
        configureCurrentScope(scope -> scope.setContexts(collisionKey, Collections.emptyMap()));
      } else if (!"foundry".equals(collisionKey)) {
        configureCurrentScope(
            scope -> scope.setContexts(collisionKey, Map.of("owner", "native")));
      }
      index = assertCollisionRejectedAndSafeReplacementSucceeds(
          bridge, collisionKey, false, index);
    }

    assertTrue(bridge.applyScope(new Dictionary()));
    IScope cleared = currentScope();
    assertFalse(cleared.getTags().containsKey("owned-tag-" + index));
    assertFalse(cleared.getContexts().containsKey("owned-context-" + index));
    assertEquals("native", cleared.getTags().get("native-tag"));
    assertEquals(Collections.emptyMap(), cleared.getContexts().get("device"));
    assertTrue(cleared.getContexts().containsKey("foundry"));
    assertEquals(
        "native",
        ((Map<?, ?>) cleared.getContexts().get("foundry_engine")).get("owner"));

    bridge.shutdown(OWNER);
  }

  @Test
  public void applyScopeRejectsGlobalAndCurrentLayerCollisionsAndWritesOnlyDefaultLayer() {
    SentryObservabilityBridge bridge = configuredBridge();
    Dictionary initial = new Dictionary();
    initial.put("tags", Map.of("owned-layer-tag-0", "owned"));
    initial.put("contexts", Map.of("owned-layer-context-0", Map.of("value", 0)));
    initial.put("user", Map.of("id", "layer-player-0"));
    assertTrue(bridge.applyScope(initial));

    List<ScopeType> scopeTypes = List.of(
        ScopeType.GLOBAL,
        ScopeType.GLOBAL,
        ScopeType.CURRENT,
        ScopeType.CURRENT);
    List<String> collisionKeys = List.of(
        "global-native-tag",
        "foundry_engine",
        "current-native-tag",
        "device");
    List<Boolean> tagCollisions = List.of(true, false, true, false);
    int index = 0;
    for (int caseIndex = 0; caseIndex < scopeTypes.size(); caseIndex++) {
      ScopeType scopeType = scopeTypes.get(caseIndex);
      String collisionKey = collisionKeys.get(caseIndex);
      boolean tagCollision = tagCollisions.get(caseIndex);
      Object nativeValue = tagCollision
          ? "native-" + scopeType.name()
          : Map.of("owner", "native-" + scopeType.name());
      Sentry.configureScope(scopeType, scope -> {
        if (tagCollision) {
          scope.setTag(collisionKey, (String) nativeValue);
        } else {
          scope.setContexts(collisionKey, nativeValue);
        }
      });

      Dictionary rejected = new Dictionary();
      rejected.put(
          "tags",
          tagCollision
              ? Map.of(collisionKey, "foundry", "rejected-layer-tag", "blocked")
              : Map.of("rejected-layer-tag", "blocked"));
      rejected.put(
          "contexts",
          tagCollision
              ? Map.of("rejected-layer-context", Map.of("value", "blocked"))
              : Map.of(
                  collisionKey,
                  Map.of("owner", "foundry"),
                  "rejected-layer-context",
                  Map.of("value", "blocked")));
      rejected.put("user", Map.of("id", "layer-player-rejected"));

      assertFalse(bridge.applyScope(rejected));
      IScope defaultScope = currentScope();
      assertEquals("owned", defaultScope.getTags().get("owned-layer-tag-" + index));
      assertEquals(
          index,
          ((Map<?, ?>) defaultScope.getContexts().get("owned-layer-context-" + index))
              .get("value"));
      assertEquals("layer-player-" + index, defaultScope.getUser().getId());
      assertFalse(defaultScope.getTags().containsKey("rejected-layer-tag"));
      assertFalse(defaultScope.getContexts().containsKey("rejected-layer-context"));
      if (tagCollision) {
        assertEquals(nativeValue, scope(scopeType).getTags().get(collisionKey));
      } else {
        assertEquals(nativeValue, scope(scopeType).getContexts().get(collisionKey));
      }

      int nextIndex = index + 1;
      Dictionary safe = new Dictionary();
      safe.put("tags", Map.of("owned-layer-tag-" + nextIndex, "owned"));
      safe.put(
          "contexts",
          Map.of("owned-layer-context-" + nextIndex, Map.of("value", nextIndex)));
      safe.put("user", Map.of("id", "layer-player-" + nextIndex));
      assertTrue(bridge.applyScope(safe));
      if (tagCollision) {
        assertEquals(nativeValue, scope(scopeType).getTags().get(collisionKey));
      } else {
        assertEquals(nativeValue, scope(scopeType).getContexts().get(collisionKey));
      }
      index = nextIndex;
    }

    int finalIndex = index;
    Sentry.configureScope(
        ScopeType.GLOBAL,
        scope -> scope.setTag("owned-layer-tag-" + finalIndex, "global-native"));
    Sentry.configureScope(
        ScopeType.GLOBAL,
        scope -> scope.setContexts(
            "owned-layer-context-" + finalIndex,
            Map.of("owner", "global-native")));
    assertEquals(
        ScopeType.CURRENT,
        scope(ScopeType.GLOBAL).getOptions().getDefaultScopeType());
    assertEquals(
        Map.of("owner", "global-native"),
        scope(ScopeType.GLOBAL)
            .getContexts()
            .get("owned-layer-context-" + finalIndex));

    assertTrue(bridge.applyScope(new Dictionary()));
    assertEquals(
        "global-native",
        scope(ScopeType.GLOBAL).getTags().get("owned-layer-tag-" + finalIndex));
    assertEquals(
        Map.of("owner", "global-native"),
        scope(ScopeType.GLOBAL)
            .getContexts()
            .get("owned-layer-context-" + finalIndex));
    assertFalse(currentScope().getTags().containsKey("owned-layer-tag-" + finalIndex));
    assertFalse(
        currentScope().getContexts().containsKey("owned-layer-context-" + finalIndex));
    assertEquals(null, currentScope().getUser());
  }

  @Test
  public void clearBreadcrumbsClearsCurrentScopeAndBothMethodsHonorOwnerAvailability() {
    SentryObservabilityBridge unavailable = newBridge();
    assertFalse(unavailable.applyScope(new Dictionary()));
    assertFalse(unavailable.clearBreadcrumbs());

    SentryObservabilityBridge bridge = configuredBridge();
    configureCurrentScope(scope -> {
      scope.addBreadcrumb(new Breadcrumb("first"));
      scope.addBreadcrumb(new Breadcrumb("second"));
    });
    assertEquals(2, currentScope().getBreadcrumbs().size());

    assertTrue(bridge.clearBreadcrumbs());
    assertTrue(currentScope().getBreadcrumbs().isEmpty());

    bridge.shutdown(OWNER);
    assertFalse(bridge.applyScope(new Dictionary()));
    assertFalse(bridge.clearBreadcrumbs());
  }

  @Test
  public void sameConfigurationOwnerTransferRetainsTrackingSoEmptyScopeClearsPriorValues() {
    SentryObservabilityBridge bridge = newBridge();
    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));

    Dictionary scope = new Dictionary();
    scope.put("tags", Map.of("region", "iad"));
    scope.put("contexts", Map.of("match", Map.of("id", 7)));
    assertTrue(bridge.applyScope(scope));

    String replacementOwner = "replacement-owner";
    configuration.put("lifecycle_owner", replacementOwner);
    assertEquals(0, bridge.configure(configuration));
    assertTrue(bridge.applyScope(new Dictionary()));

    assertFalse(currentScope().getTags().containsKey("region"));
    assertFalse(currentScope().getContexts().containsKey("match"));
    bridge.shutdown(replacementOwner);
  }

  @Test
  public void identicalConfigurationTransfersGlobalScopeOwnershipAcrossBridgeInstances() {
    SentryObservabilityBridge first = newBridge();
    SentryObservabilityBridge second = newBridge();
    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", "first-owner");
    assertEquals(0, first.configure(configuration));
    configureCurrentScope(scope -> {
      scope.setTag("native", "preserved");
      scope.setContexts("device", Map.of("model", "test-device"));
    });

    Dictionary installed = new Dictionary();
    installed.put("tags", Map.of("region", "iad"));
    installed.put("contexts", Map.of("match", Map.of("id", 7)));
    installed.put("user", Map.of("id", "player-7"));
    assertTrue(first.applyScope(installed));

    configuration.put("lifecycle_owner", "second-owner");
    assertEquals(0, second.configure(configuration));
    assertTrue(second.applyScope(new Dictionary()));

    IScope cleared = currentScope();
    assertFalse(cleared.getTags().containsKey("region"));
    assertFalse(cleared.getContexts().containsKey("match"));
    assertEquals(null, cleared.getUser());
    assertEquals("preserved", cleared.getTags().get("native"));
    assertEquals("test-device", ((Map<?, ?>) cleared.getContexts().get("device")).get("model"));

    Dictionary stale = new Dictionary();
    stale.put("tags", Map.of("stale", "blocked"));
    assertFalse(first.applyScope(stale));
    configureCurrentScope(scope -> scope.addBreadcrumb(new Breadcrumb("preserved")));
    assertFalse(first.clearBreadcrumbs());
    assertEquals(1, currentScope().getBreadcrumbs().size());

    Dictionary replacement = new Dictionary();
    replacement.put("tags", Map.of("region", "fra"));
    assertTrue(second.applyScope(replacement));
    assertEquals("fra", currentScope().getTags().get("region"));
    assertFalse(currentScope().getTags().containsKey("stale"));
    assertTrue(second.clearBreadcrumbs());
    assertTrue(currentScope().getBreadcrumbs().isEmpty());
    second.shutdown("second-owner");
  }

  private static SentryObservabilityBridge configuredBridge() {
    SentryObservabilityBridge bridge = newBridge();
    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));
    return bridge;
  }

  private static int assertCollisionRejectedAndSafeReplacementSucceeds(
      SentryObservabilityBridge bridge,
      String collisionKey,
      boolean tagCollision,
      int previousIndex) {
    Object nativeValue = tagCollision
        ? currentScope().getTags().get(collisionKey)
        : currentScope().getContexts().get(collisionKey);
    Dictionary rejected = new Dictionary();
    rejected.put(
        "tags",
        tagCollision
            ? Map.of(collisionKey, "foundry", "rejected-tag", "must-not-install")
            : Map.of("rejected-tag", "must-not-install"));
    rejected.put(
        "contexts",
        tagCollision
            ? Map.of("rejected-context", Map.of("value", "must-not-install"))
            : Map.of(
                collisionKey,
                Map.of("owner", "foundry"),
                "rejected-context",
                Map.of("value", "must-not-install")));
    rejected.put("user", Map.of("id", "player-rejected"));

    assertFalse(bridge.applyScope(rejected));
    IScope unchanged = currentScope();
    assertEquals("owned", unchanged.getTags().get("owned-tag-" + previousIndex));
    if (previousIndex == 0) {
      assertEquals("keep-on-rejection", unchanged.getTags().get("stale"));
    }
    assertEquals(
        previousIndex,
        ((Map<?, ?>) unchanged.getContexts().get("owned-context-" + previousIndex))
            .get("value"));
    assertEquals("player-" + previousIndex, unchanged.getUser().getId());
    assertFalse(unchanged.getTags().containsKey("rejected-tag"));
    assertFalse(unchanged.getContexts().containsKey("rejected-context"));
    if (tagCollision) {
      assertEquals(nativeValue, unchanged.getTags().get(collisionKey));
    } else {
      assertEquals(nativeValue, unchanged.getContexts().get(collisionKey));
    }

    int nextIndex = previousIndex + 1;
    Dictionary safe = new Dictionary();
    safe.put("tags", Map.of("owned-tag-" + nextIndex, "owned"));
    safe.put(
        "contexts",
        Map.of("owned-context-" + nextIndex, Map.of("value", nextIndex)));
    safe.put("user", Map.of("id", "player-" + nextIndex));
    assertTrue(bridge.applyScope(safe));

    IScope replaced = currentScope();
    assertFalse(replaced.getTags().containsKey("owned-tag-" + previousIndex));
    assertFalse(replaced.getContexts().containsKey("owned-context-" + previousIndex));
    assertEquals("owned", replaced.getTags().get("owned-tag-" + nextIndex));
    assertEquals(
        nextIndex,
        ((Map<?, ?>) replaced.getContexts().get("owned-context-" + nextIndex))
            .get("value"));
    assertEquals("player-" + nextIndex, replaced.getUser().getId());
    if (tagCollision) {
      assertEquals(nativeValue, replaced.getTags().get(collisionKey));
    } else {
      assertEquals(nativeValue, replaced.getContexts().get(collisionKey));
    }
    return nextIndex;
  }

  private static void configureCurrentScope(io.sentry.ScopeCallback callback) {
    Sentry.configureScope(callback);
  }

  private static IScope currentScope() {
    AtomicReference<IScope> result = new AtomicReference<>();
    Sentry.configureScope(result::set);
    return result.get();
  }

  private static IScope scope(ScopeType scopeType) {
    AtomicReference<IScope> result = new AtomicReference<>();
    Sentry.configureScope(scopeType, result::set);
    return result.get();
  }

  private static SentryObservabilityBridge newBridge() {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()));
  }
}
