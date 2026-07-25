package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import io.sentry.Sentry;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.protocol.SentryId;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
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

  private static SentryObservabilityBridge newBridge() {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()));
  }
}
