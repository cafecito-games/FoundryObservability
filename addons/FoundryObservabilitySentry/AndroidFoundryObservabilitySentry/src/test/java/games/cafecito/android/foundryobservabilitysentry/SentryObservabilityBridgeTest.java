package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import io.sentry.Breadcrumb;
import io.sentry.IScope;
import io.sentry.Sentry;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.protocol.SentryId;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
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

  private static SentryObservabilityBridge configuredBridge() {
    SentryObservabilityBridge bridge = newBridge();
    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", OWNER);
    assertEquals(0, bridge.configure(configuration));
    return bridge;
  }

  private static void configureCurrentScope(io.sentry.ScopeCallback callback) {
    Sentry.configureScope(callback);
  }

  private static IScope currentScope() {
    AtomicReference<IScope> result = new AtomicReference<>();
    Sentry.configureScope(result::set);
    return result.get();
  }

  private static SentryObservabilityBridge newBridge() {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()));
  }
}
