package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import io.sentry.Attachment;
import io.sentry.Breadcrumb;
import io.sentry.IScope;
import io.sentry.Scope;
import io.sentry.ScopeType;
import io.sentry.Sentry;
import io.sentry.SentryEvent;
import io.sentry.SentryOptions;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.protocol.SentryId;
import java.lang.reflect.Proxy;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;
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

  @Test
  public void parsesNormalizedNonnegativeMaxAttachmentBytes() {
    long defaultMaximum = 20L * 1024L * 1024L;
    assertEquals(2L, SentryObservabilityBridge.maxAttachmentBytesValue(2));
    assertEquals(2L, SentryObservabilityBridge.maxAttachmentBytesValue(2.0D));
    assertEquals(0L, SentryObservabilityBridge.maxAttachmentBytesValue(-5));
    assertEquals(defaultMaximum, SentryObservabilityBridge.maxAttachmentBytesValue(null));
    assertEquals(defaultMaximum, SentryObservabilityBridge.maxAttachmentBytesValue("2"));
    assertEquals(defaultMaximum, SentryObservabilityBridge.maxAttachmentBytesValue(2.5D));
    assertEquals(
        defaultMaximum,
        SentryObservabilityBridge.maxAttachmentBytesValue(
            BigInteger.valueOf(Long.MAX_VALUE).add(BigInteger.ONE)));
  }

  @Test
  public void configurationAppliesNormalizedAttachmentMaximumToNativeOptions() {
    SentryObservabilityBridge bridge = newBridge();
    Dictionary configuration = configuration(OWNER);
    configuration.put("max_attachment_bytes", -5);

    assertEquals(0, bridge.configure(configuration));
    assertEquals(0L, currentScope().getOptions().getMaxAttachmentSize());

    configuration.put("max_attachment_bytes", 12L);
    assertEquals(0, bridge.configure(configuration));
    assertEquals(12L, currentScope().getOptions().getMaxAttachmentSize());

    bridge.shutdown(OWNER);
  }

  @Test
  public void replaceAttachmentsIsCompleteAtomicAndOwnerGuarded() {
    SentryObservabilityBridge first = configuredBridge();
    currentScope().addAttachment(
        new Attachment(new byte[] {8}, "current-only.bin"));
    Object[] initial = {
        attachmentPayload("first.bin", new byte[] {1}),
        Map.of(
            "filename", "second.txt",
            "content_type", "text/plain",
            "category", "event.attachment",
            "path", "/tmp/second.txt")
    };

    assertTrue(first.replaceAttachments(initial));
    assertEquals(
        List.of("first.bin", "second.txt"),
        attachmentFilenames(globalScope()));
    assertEquals(
        List.of("current-only.bin"),
        attachmentFilenames(currentScope()));

    Object[] malformed = {
        attachmentPayload("new.bin", new byte[] {2}),
        "unsupported-slot",
        attachmentPayload("never-reached.bin", new byte[] {3})
    };
    assertFalse(first.replaceAttachments(malformed));
    assertEquals(
        List.of("first.bin", "second.txt"),
        attachmentFilenames(globalScope()));
    assertFalse(first.replaceAttachments(new Object[] {
        attachmentPayload("new.bin", new byte[] {2}),
        null
    }));
    assertEquals(
        List.of("first.bin", "second.txt"),
        attachmentFilenames(globalScope()));

    SentryObservabilityBridge replacement = newBridge();
    Dictionary configuration = configuration("replacement-owner");
    assertEquals(0, replacement.configure(configuration));
    assertFalse(first.replaceAttachments(
        new Object[] {attachmentPayload("stale.bin", new byte[] {9})}));
    assertEquals(
        List.of("first.bin", "second.txt"),
        attachmentFilenames(globalScope()));
    assertTrue(replacement.replaceAttachments(
        new Object[] {attachmentPayload("current.bin", new byte[] {4})}));
    assertEquals(List.of("current.bin"), attachmentFilenames(globalScope()));
    assertEquals(
        List.of("current-only.bin"),
        attachmentFilenames(currentScope()));

    replacement.shutdown("replacement-owner");
  }

  @Test
  public void shutdownAndLifecycleRestartClearGlobalAttachmentState() {
    SentryObservabilityBridge bridge = configuredBridge();
    assertTrue(bridge.replaceAttachments(
        new Object[] {attachmentPayload("before-restart.bin", new byte[] {1})}));
    IScope priorScope = globalScope();
    assertEquals(List.of("before-restart.bin"), attachmentFilenames(priorScope));

    Dictionary changed = configuration(OWNER);
    changed.put("release", "changed-release");
    assertEquals(0, bridge.configure(changed));

    assertTrue(priorScope.getAttachments().isEmpty());
    assertTrue(globalScope().getAttachments().isEmpty());
    assertTrue(bridge.replaceAttachments(
        new Object[] {attachmentPayload("before-shutdown.bin", new byte[] {2})}));
    IScope shutdownScope = globalScope();

    bridge.shutdown(OWNER);

    assertTrue(shutdownScope.getAttachments().isEmpty());
  }

  @Test
  public void failedGlobalReplacementRestoresPreviousSnapshotInOrder() {
    Scope liveScope = new Scope(new SentryOptions());
    liveScope.addAttachment(new Attachment(new byte[] {1}, "prior-first.bin"));
    liveScope.addAttachment(new Attachment(new byte[] {2}, "prior-second.bin"));
    IScope faultingScope = scopeFailingOnAttachmentAdds(liveScope, 2);
    AtomicBoolean closed = new AtomicBoolean();

    assertFalse(AndroidSentrySdkDriver.replaceAttachments(
        faultingScope,
        List.of(
            new Attachment(new byte[] {3}, "candidate-first.bin"),
            new Attachment(new byte[] {4}, "candidate-second.bin")),
        () -> closed.set(true)));

    assertEquals(
        List.of("prior-first.bin", "prior-second.bin"),
        attachmentFilenames(liveScope));
    assertFalse(closed.get());
  }

  @Test
  public void failedGlobalReplacementAndRollbackCloseSdkAndInvalidateOwner() {
    SentryObservabilityBridge bridge = configuredBridge();
    assertTrue(bridge.isAvailable(OWNER));
    Scope liveScope = new Scope(new SentryOptions());
    liveScope.addAttachment(new Attachment(new byte[] {1}, "prior.bin"));
    IScope faultingScope = scopeFailingOnAttachmentAdds(liveScope, 2, 3);
    AtomicInteger closeCalls = new AtomicInteger();

    assertFalse(AndroidSentrySdkDriver.replaceAttachments(
        faultingScope,
        List.of(
            new Attachment(new byte[] {2}, "candidate-first.bin"),
            new Attachment(new byte[] {3}, "candidate-second.bin")),
        () -> {
          closeCalls.incrementAndGet();
          Sentry.close();
        }));

    assertEquals(1, closeCalls.get());
    assertFalse(Sentry.isEnabled());
    assertFalse(bridge.isAvailable(OWNER));
  }

  @Test
  public void attachmentAwareCaptureHandlesAllEventRoutesLocallyAndStrictly() {
    RecordingCapturer capturer = new RecordingCapturer();
    SentryObservabilityBridge bridge = newBridge(capturer);
    assertEquals(0, bridge.configure(configuration(OWNER)));
    assertTrue(bridge.replaceAttachments(
        new Object[] {attachmentPayload("global.bin", new byte[] {7})}));

    for (String kind : List.of("message", "event", "exception")) {
      Dictionary event = new Dictionary();
      event.put("kind", kind);
      event.put("message", kind + " message");
      event.put("contexts", Map.of("foundry_runtime", Map.of("scene", "Arena")));
      event.put("scope", Map.of("tags", Map.of("round", "final")));
      event.put(
          "attachments",
          new Object[] {attachmentPayload(kind + ".bin", new byte[] {1, 2})});
      if ("exception".equals(kind)) {
        event.put(
            "exception",
            Map.of(
                "type_name", "InvalidState",
                "message", "bad state",
                "stack_trace", "frame",
                "attributes", Map.of()));
      }

      assertNotEquals("", bridge.captureWithAttachments(event));
    }

    assertEquals(3, capturer.events.size());
    assertEquals(
        List.of("global.bin", "message.bin"),
        attachmentFilenames(capturer.scopes.get(0)));
    assertEquals(
        List.of("global.bin", "event.bin"),
        attachmentFilenames(capturer.scopes.get(1)));
    assertEquals(
        List.of("global.bin", "exception.bin"),
        attachmentFilenames(capturer.scopes.get(2)));
    for (IScope scope : capturer.scopes) {
      assertEquals("final", scope.getTags().get("round"));
      assertEquals(
          "Arena",
          ((Map<?, ?>) scope.getContexts().get("foundry_runtime")).get("scene"));
    }
    assertEquals("InvalidState", capturer.events.get(2).getExceptions().get(0).getType());
    assertEquals(List.of("global.bin"), attachmentFilenames(globalScope()));

    Dictionary mixed = new Dictionary();
    mixed.put("kind", "message");
    mixed.put("message", "must reject");
    mixed.put(
        "attachments",
        new Object[] {
            attachmentPayload("valid.bin", new byte[] {1}),
            Map.of(
                "filename", "bad.bin",
                "category", "unsupported",
                "bytes", new byte[] {2})
        });
    assertEquals("", bridge.captureWithAttachments(mixed));
    assertEquals(3, capturer.events.size());
    assertEquals(List.of("global.bin"), attachmentFilenames(globalScope()));

    Dictionary absent = new Dictionary();
    absent.put("kind", "message");
    absent.put("message", "compatible");
    assertNotEquals("", bridge.captureWithAttachments(absent));
    absent.put("attachments", new Object[0]);
    assertNotEquals("", bridge.captureWithAttachments(absent));
    assertEquals(
        List.of("global.bin"),
        attachmentFilenames(capturer.scopes.get(3)));
    assertEquals(
        List.of("global.bin"),
        attachmentFilenames(capturer.scopes.get(4)));
    assertEquals(List.of("global.bin"), attachmentFilenames(globalScope()));

    bridge.shutdown(OWNER);
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
  public void applyScopeRejectsCollisionsAcrossAllLayersAndWritesOnlyDefaultLayer() {
    SentryObservabilityBridge bridge = configuredBridge();
    Dictionary initial = new Dictionary();
    initial.put("tags", Map.of("owned-layer-tag-0", "owned"));
    initial.put("contexts", Map.of("owned-layer-context-0", Map.of("value", 0)));
    initial.put("user", Map.of("id", "layer-player-0"));
    assertTrue(bridge.applyScope(initial));

    List<LayerCollisionCase> collisions = List.of(
        LayerCollisionCase.tag(ScopeType.ISOLATION, "isolation-native-tag"),
        LayerCollisionCase.context(ScopeType.ISOLATION, "runtime"),
        LayerCollisionCase.tag(ScopeType.GLOBAL, "global-native-tag"),
        LayerCollisionCase.context(ScopeType.GLOBAL, "foundry_engine"),
        LayerCollisionCase.tag(ScopeType.CURRENT, "current-native-tag"),
        LayerCollisionCase.context(ScopeType.CURRENT, "device"));
    int index = 0;
    for (LayerCollisionCase collision : collisions) {
      Object nativeValue =
          collision.tag
              ? "native-" + collision.scopeType.name() + "-" + collision.key
              : Map.of(
                  "owner",
                  "native-" + collision.scopeType.name() + "-" + collision.key);
      Sentry.configureScope(collision.scopeType, scope -> {
        if (collision.tag) {
          scope.setTag(collision.key, (String) nativeValue);
        } else {
          scope.setContexts(collision.key, nativeValue);
        }
      });

      Dictionary rejected = new Dictionary();
      rejected.put(
          "tags",
          collision.tag
              ? Map.of(collision.key, "foundry", "rejected-layer-tag", "blocked")
              : Map.of("rejected-layer-tag", "blocked"));
      rejected.put(
          "contexts",
          collision.tag
              ? Map.of("rejected-layer-context", Map.of("value", "blocked"))
              : Map.of(
                  collision.key,
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
      if (collision.tag) {
        assertEquals(nativeValue, scope(collision.scopeType).getTags().get(collision.key));
      } else {
        assertEquals(nativeValue, scope(collision.scopeType).getContexts().get(collision.key));
      }

      int nextIndex = index + 1;
      Dictionary safe = new Dictionary();
      safe.put("tags", Map.of("owned-layer-tag-" + nextIndex, "owned"));
      safe.put(
          "contexts",
          Map.of("owned-layer-context-" + nextIndex, Map.of("value", nextIndex)));
      safe.put("user", Map.of("id", "layer-player-" + nextIndex));
      assertTrue(bridge.applyScope(safe));

      IScope replaced = currentScope();
      assertFalse(replaced.getTags().containsKey("owned-layer-tag-" + index));
      assertFalse(replaced.getContexts().containsKey("owned-layer-context-" + index));
      assertEquals("owned", replaced.getTags().get("owned-layer-tag-" + nextIndex));
      assertEquals(
          nextIndex,
          ((Map<?, ?>) replaced.getContexts().get("owned-layer-context-" + nextIndex))
              .get("value"));
      assertEquals("layer-player-" + nextIndex, replaced.getUser().getId());
      if (collision.tag) {
        assertEquals(nativeValue, scope(collision.scopeType).getTags().get(collision.key));
      } else {
        assertEquals(nativeValue, scope(collision.scopeType).getContexts().get(collision.key));
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
    assertEquals(0, bridge.configure(configuration(OWNER)));
    return bridge;
  }

  private static Dictionary configuration(String owner) {
    Dictionary configuration = new Dictionary();
    configuration.put("enabled", true);
    configuration.put("dsn", "https://public@example.com/1");
    configuration.put("lifecycle_owner", owner);
    return configuration;
  }

  private static Map<String, Object> attachmentPayload(String filename, byte[] bytes) {
    return Map.of(
        "filename", filename,
        "category", "event.attachment",
        "bytes", bytes);
  }

  private static List<String> attachmentFilenames(IScope scope) {
    List<String> filenames = new ArrayList<>();
    for (Attachment attachment : scope.getAttachments()) {
      filenames.add(attachment.getFilename());
    }
    return filenames;
  }

  private static IScope scopeFailingOnAttachmentAdds(
      IScope delegate,
      Integer... failingCalls) {
    Set<Integer> failures = new HashSet<>(List.of(failingCalls));
    AtomicInteger addCalls = new AtomicInteger();
    return (IScope) Proxy.newProxyInstance(
        IScope.class.getClassLoader(),
        new Class<?>[] {IScope.class},
        (proxy, method, arguments) -> {
          if ("addAttachment".equals(method.getName())
              && failures.contains(addCalls.incrementAndGet())) {
            throw new IllegalStateException("injected attachment add failure");
          }
          return method.invoke(delegate, arguments);
        });
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

  private static IScope globalScope() {
    return Sentry.getGlobalScope();
  }

  private static IScope scope(ScopeType scopeType) {
    AtomicReference<IScope> result = new AtomicReference<>();
    Sentry.configureScope(scopeType, result::set);
    return result.get();
  }

  private static final class LayerCollisionCase {
    final ScopeType scopeType;
    final String key;
    final boolean tag;

    private LayerCollisionCase(ScopeType scopeType, String key, boolean tag) {
      this.scopeType = scopeType;
      this.key = key;
      this.tag = tag;
    }

    static LayerCollisionCase tag(ScopeType scopeType, String key) {
      return new LayerCollisionCase(scopeType, key, true);
    }

    static LayerCollisionCase context(ScopeType scopeType, String key) {
      return new LayerCollisionCase(scopeType, key, false);
    }
  }

  private static SentryObservabilityBridge newBridge() {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()));
  }

  private static SentryObservabilityBridge newBridge(RecordingCapturer capturer) {
    return new SentryObservabilityBridge(
        Foundry.getInstance(RuntimeEnvironment.getApplication()),
        capturer);
  }

  private static final class RecordingCapturer
      implements SentryObservabilityBridge.EventCapturer {
    final List<SentryEvent> events = new ArrayList<>();
    final List<IScope> scopes = new ArrayList<>();

    @Override
    public SentryId capture(SentryEvent event, io.sentry.ScopeCallback callback) {
      IScope scope = globalScope().clone();
      callback.run(scope);
      events.add(event);
      scopes.add(scope);
      return new SentryId();
    }
  }
}
