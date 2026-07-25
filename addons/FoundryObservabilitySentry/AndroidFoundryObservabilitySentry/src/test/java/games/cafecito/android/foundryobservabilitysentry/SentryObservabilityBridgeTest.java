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
import java.util.Collections;
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
  @After
  public void closeSentry() {
    Sentry.close();
  }

  @Test
  public void rejectsEnabledConfigurationWithoutDsn() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary payload = new Dictionary();
    payload.put("enabled", true);

    assertEquals(1, bridge.configure(payload));
    assertFalse(bridge.isAvailable());
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

    assertEquals(0, bridge.configure(payload));
    assertFalse(bridge.isAvailable());
    assertEquals("", bridge.capture(new Dictionary()));
    assertEquals(1, bridge.flush(100));

    bridge.shutdown();
    bridge.shutdown();
    assertFalse(bridge.isAvailable());
  }

  @Test
  public void configuredBridgeCapturesFlushesAndShutsDown() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("environment", "test");
    configuration.put("release", "1.2.3");

    assertEquals(0, bridge.configure(configuration));
    assertTrue(bridge.isAvailable());

    Dictionary event = new Dictionary();
    event.put("kind", "message");
    event.put("message", "hello");
    assertNotEquals("", bridge.capture(event));
    assertEquals(0, bridge.flush(0));

    bridge.shutdown();
    assertFalse(bridge.isAvailable());
    bridge.shutdown();
  }

  @Test
  public void configuredBridgeCapturesStructuredLogs() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("logs_enabled", true);
    configuration.put("dsn", "https://public@example.com/1");

    assertEquals(0, bridge.configure(configuration));

    Dictionary log = new Dictionary();
    log.put("kind", "log");
    log.put("level", 40);
    log.put("message", "warning");
    log.put("source", "foundry.logging");
    log.put("timestamp_msec", 1234L);
    log.put("attributes", java.util.Map.of("logger_name", "combat"));

    assertFalse(bridge.captureLog(log).isEmpty());
    bridge.shutdown();
  }

  @Test
  public void configuredBridgeCapturesBreadcrumbs() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    assertEquals(0, bridge.configure(configuration));

    Dictionary breadcrumb = new Dictionary();
    breadcrumb.put("message", "warning");
    breadcrumb.put("category", "error");
    breadcrumb.put("level", 40);
    breadcrumb.put("timestamp_msec", 1234L);
    breadcrumb.put("attributes", java.util.Map.of("error.file", "res://player.fs"));

    assertTrue(bridge.captureBreadcrumb(breadcrumb));
    bridge.shutdown();
  }

  @Test
  public void configuredBridgeCapturesFeedbackWithOptionalAssociation() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("provider_options", java.util.Map.of("send_default_pii", true));

    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("message", "The tutorial was confusing.");
    feedback.put("name", "Player One");
    feedback.put("contact_email", "player@example.com");
    feedback.put("associated_event_id", "0123456789abcdef0123456789abcdef");

    assertFalse(bridge.captureFeedback(feedback).isEmpty());
    bridge.shutdown();
  }

  @Test
  public void rejectsFeedbackWithoutMessage() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("name", "Player One");

    assertEquals("", bridge.captureFeedback(feedback));
    bridge.shutdown();
  }

  @Test
  public void rejectsFeedbackWithInvalidAssociatedEventId() {
    SentryObservabilityBridge bridge = newBridge();

    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    assertEquals(0, bridge.configure(configuration));

    Dictionary feedback = new Dictionary();
    feedback.put("message", "Feedback with a bad association");
    feedback.put("associated_event_id", "not-a-sentry-id");

    assertEquals("", bridge.captureFeedback(feedback));
    bridge.shutdown();
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

  private static SentryObservabilityBridge newBridge() {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()));
  }
}
