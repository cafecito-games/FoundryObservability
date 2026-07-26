package games.cafecito.android.foundryobservabilitysentry;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import games.cafecito.foundry.plugin.FoundryPlugin;
import games.cafecito.foundry.plugin.UsedByFoundry;
import io.sentry.Sentry;
import io.sentry.SentryAttributes;
import io.sentry.SentryEvent;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.logger.SentryLogParameters;
import io.sentry.metrics.SentryMetricsParameters;
import io.sentry.protocol.Feedback;
import io.sentry.protocol.SentryId;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.Collections;
import java.util.Map;
import java.util.UUID;

public final class SentryObservabilityBridge extends FoundryPlugin {
  static final int BRIDGE_ERROR_OK = 0;
  static final int BRIDGE_ERROR_FAILED = 1;
  static final int LIFECYCLE_VERSION = 1;
  private static final SentryLifecycleCoordinator LIFECYCLE_COORDINATOR =
      new SentryLifecycleCoordinator(new AndroidSentrySdkDriver());

  private Map<String, Object> globalAttributes = Collections.emptyMap();
  private String lifecycleOwner = "";
  private boolean logsEnabled;
  private boolean metricsEnabled;

  public SentryObservabilityBridge(Foundry foundry) {
    super(foundry);
  }

  @Override
  public String getPluginName() {
    return "SentryObservabilityBridge";
  }

  @UsedByFoundry
  public int lifecycleVersion() {
    return LIFECYCLE_VERSION;
  }

  @UsedByFoundry
  public int configure(Dictionary payload) {
    return configureLocked(payload);
  }

  private synchronized int configureLocked(Dictionary payload) {
    if (payload == null) {
      return BRIDGE_ERROR_FAILED;
    }

    String candidateOwner = stringValue(payload.get("lifecycle_owner"));
    if (candidateOwner.isEmpty()) {
      return BRIDGE_ERROR_FAILED;
    }

    if (!booleanValue(payload.get("enabled"))) {
      LIFECYCLE_COORDINATOR.shutdown(candidateOwner);
      if (candidateOwner.equals(lifecycleOwner)) {
        lifecycleOwner = "";
        globalAttributes = Collections.emptyMap();
        logsEnabled = false;
        metricsEnabled = false;
      }
      return BRIDGE_ERROR_OK;
    }

    String dsn = stringValue(payload.get("dsn"));
    if (dsn.isEmpty()) {
      return BRIDGE_ERROR_FAILED;
    }

    Object globalAttributesValue = payload.get("global_attributes");
    Map<String, Object> candidateGlobalAttributes = globalAttributesValue instanceof Map
        ? SentryEventMapper.copyDictionary((Map<?, ?>) globalAttributesValue)
        : Collections.emptyMap();
    Map<String, Object> providerOptions = payload.get("provider_options") instanceof Map
        ? SentryEventMapper.copyDictionary((Map<?, ?>) payload.get("provider_options"))
        : Collections.emptyMap();
    Map<String, Object> stableContexts = new java.util.LinkedHashMap<>();
    stableContexts.putAll(SentryEventMapper.contexts(payload.get("stable_contexts")));
    SentryAndroidOptions diagnosticOptions = new SentryAndroidOptions();
    applyAndroidAnrDiagnostics(diagnosticOptions, payload);
    SentryLifecycleConfiguration candidateConfiguration =
        new SentryLifecycleConfiguration(
            getContext().getApplicationContext(),
            dsn,
            stringValue(payload.get("environment")),
            stringValue(payload.get("release")),
            stringValue(payload.get("dist")),
            candidateGlobalAttributes,
            stableContexts,
            providerOptions,
            booleanValue(payload.get("logs_enabled")),
            booleanValue(payload.get("metrics_enabled")),
            diagnosticOptions.isAnrEnabled(),
            diagnosticOptions.getAnrTimeoutIntervalMillis(),
            diagnosticOptions.isAttachAnrThreadDump(),
            maxBreadcrumbsValue(payload.get("max_breadcrumbs")));

    if (!LIFECYCLE_COORDINATOR.configure(candidateOwner, candidateConfiguration)) {
      return BRIDGE_ERROR_FAILED;
    }

    lifecycleOwner = candidateOwner;
    globalAttributes = candidateConfiguration.globalAttributes;
    logsEnabled = candidateConfiguration.logsEnabled;
    metricsEnabled = candidateConfiguration.metricsEnabled;
    return BRIDGE_ERROR_OK;
  }

  @UsedByFoundry
  public boolean isAvailable(String owner) {
    return LIFECYCLE_COORDINATOR.isAvailable(owner);
  }

  @UsedByFoundry
  public String capture(Dictionary payload) {
    if (!isAvailable(lifecycleOwner)) {
      return "";
    }
    SentryEvent event = SentryEventMapper.makeEvent(payload, globalAttributes);
    Object contexts = payload == null ? null : payload.get("contexts");
    SentryEventMapper.ScopePayload localScope = SentryEventMapper.scopePayload(
        payload == null ? null : payload.get("scope"));
    return eventIdString(Sentry.captureEvent(
        event,
        scope -> AndroidSentrySdkDriver.applyCaptureScope(scope, contexts, localScope)));
  }

  @UsedByFoundry
  public String captureLog(Dictionary payload) {
    if (!isAvailable(lifecycleOwner) || !logsEnabled || payload == null) {
      return "";
    }

    Map<?, ?> values = payload;
    SentryEventMapper.ScopePayload localScope =
        SentryEventMapper.scopePayload(values.get("scope"));
    if (!localScope.tags.isEmpty()
        || !localScope.contexts.isEmpty()
        || localScope.user != null) {
      return "";
    }
    Map<?, ?> eventAttributes = values.get("attributes") instanceof Map
        ? (Map<?, ?>) values.get("attributes")
        : Collections.emptyMap();
    Map<String, Object> attributes = SentryLogMapper.mergedAttributes(
        globalAttributes,
        eventAttributes,
        stringValue(values.get("kind")),
        stringValue(values.get("source")),
        longValue(values.get("timestamp_msec"), 0L),
        longValue(values.get("engine_ticks_msec"), -1L));
    SentryLogParameters parameters = SentryLogParameters.create(
        SentryAttributes.fromMap(attributes));
    Sentry.logger().log(
        SentryLogMapper.sentryLevel(intValue(values.get("level"), 50)),
        parameters,
        stringValue(values.get("message")));
    return "sentry-log:" + UUID.randomUUID();
  }

  @UsedByFoundry
  public boolean captureBreadcrumb(Dictionary payload) {
    if (!isAvailable(lifecycleOwner) || payload == null) {
      return false;
    }

    try {
      Sentry.addBreadcrumb(SentryBreadcrumbMapper.makeBreadcrumb(payload, globalAttributes));
      return true;
    } catch (RuntimeException exception) {
      return false;
    }
  }

  @UsedByFoundry
  public boolean applyScope(Dictionary payload) {
    SentryEventMapper.ScopePayload candidate = SentryEventMapper.scopePayload(payload);
    return LIFECYCLE_COORDINATOR.replaceScope(
        lifecycleOwner,
        previousKeys -> AndroidSentrySdkDriver.replaceFoundryScope(
                candidate,
                previousKeys.tagKeys,
                previousKeys.contextKeys)
            ? new SentryLifecycleCoordinator.ScopeKeys(
                candidate.tags.keySet(),
                candidate.contexts.keySet())
            : null);
  }

  @UsedByFoundry
  public boolean clearBreadcrumbs() {
    return LIFECYCLE_COORDINATOR.perform(
        lifecycleOwner,
        AndroidSentrySdkDriver::clearBreadcrumbs);
  }

  @UsedByFoundry
  public boolean captureMetric(Dictionary payload) {
    if (!isAvailable(lifecycleOwner) || !metricsEnabled) {
      return false;
    }

    SentryEventMapper.MetricPayload metric = SentryEventMapper.metricPayload(payload);
    if (metric == null) {
      return false;
    }

    try {
      SentryMetricsParameters parameters = SentryMetricsParameters.create(
          SentryAttributes.fromMap(metric.attributes));
      switch (metric.type) {
        case 0:
          Sentry.metrics().count(metric.name, metric.value, metric.unit, parameters);
          break;
        case 1:
          Sentry.metrics().gauge(metric.name, metric.value, metric.unit, parameters);
          break;
        case 2:
          Sentry.metrics().distribution(metric.name, metric.value, metric.unit, parameters);
          break;
        default:
          return false;
      }
      return true;
    } catch (RuntimeException exception) {
      return false;
    }
  }

  @UsedByFoundry
  public String captureFeedback(Dictionary payload) {
    if (!isAvailable(lifecycleOwner) || payload == null) {
      return "";
    }

    String message = stringValue(payload.get("message"));
    if (message.isEmpty()) {
      return "";
    }

    try {
      Feedback feedback = new Feedback(message);
      setIfNotEmpty(feedback::setName, payload.get("name"));
      setIfNotEmpty(feedback::setContactEmail, payload.get("contact_email"));
      String associatedEventId = stringValue(payload.get("associated_event_id"));
      if (!associatedEventId.isEmpty()) {
        SentryId associatedId = new SentryId(associatedEventId);
        if (SentryId.EMPTY_ID.equals(associatedId)) {
          return "";
        }
        feedback.setAssociatedEventId(associatedId);
      }
      Sentry.captureFeedback(feedback);
      return "sentry-feedback:" + UUID.randomUUID();
    } catch (RuntimeException exception) {
      return "";
    }
  }

  @UsedByFoundry
  public int flush(String owner, int timeoutMsec) {
    // Stale owners are idempotent no-ops. The public provider also gates this
    // call with isAvailable before reaching the bridge.
    LIFECYCLE_COORDINATOR.flush(owner, timeoutMsec);
    return BRIDGE_ERROR_OK;
  }

  @UsedByFoundry
  public void shutdown(String owner) {
    shutdownLocked(owner);
  }

  private synchronized void shutdownLocked(String owner) {
    LIFECYCLE_COORDINATOR.shutdown(owner);
    if (owner != null && owner.equals(lifecycleOwner)) {
      lifecycleOwner = "";
      globalAttributes = Collections.emptyMap();
      logsEnabled = false;
      metricsEnabled = false;
    }
  }

  static void applyAndroidAnrDiagnostics(
      SentryAndroidOptions options,
      Map<?, ?> payload) {
    if (payload.containsKey("android_anr_detection_enabled")) {
      Object anrDetectionEnabled = payload.get("android_anr_detection_enabled");
      if (anrDetectionEnabled instanceof Boolean) {
        options.setAnrEnabled((Boolean) anrDetectionEnabled);
      }
    }
    if (payload.containsKey("android_anr_timeout_msec")) {
      Long anrTimeoutMsec = exactDiagnosticLong(payload.get("android_anr_timeout_msec"));
      if (anrTimeoutMsec != null && anrTimeoutMsec >= 1000L) {
        options.setAnrTimeoutIntervalMillis(anrTimeoutMsec);
      }
    }
    if (payload.containsKey("android_anr_attach_thread_dump")) {
      Object attachAnrThreadDump = payload.get("android_anr_attach_thread_dump");
      if (attachAnrThreadDump instanceof Boolean) {
        options.setAttachAnrThreadDump((Boolean) attachAnrThreadDump);
      }
    }
  }

  static String eventIdString(SentryId eventId) {
    return eventId == null || SentryId.EMPTY_ID.equals(eventId) ? "" : eventId.toString();
  }

  static int maxBreadcrumbsValue(Object value) {
    Long exactValue = exactDiagnosticLong(value);
    if (exactValue == null
        || exactValue < Integer.MIN_VALUE
        || exactValue > Integer.MAX_VALUE) {
      return 100;
    }
    return Math.max(0, exactValue.intValue());
  }

  private static void setIfNotEmpty(
      java.util.function.Consumer<String> setter,
      Object value) {
    String string = stringValue(value);
    if (!string.isEmpty()) {
      setter.accept(string);
    }
  }

  private static boolean booleanValue(Object value) {
    return value instanceof Boolean && (Boolean) value;
  }

  private static String stringValue(Object value) {
    return value instanceof String ? (String) value : "";
  }

  private static int intValue(Object value, int fallback) {
    if (value instanceof Number) {
      return ((Number) value).intValue();
    }
    if (value instanceof String) {
      try {
        return Integer.parseInt((String) value);
      } catch (NumberFormatException ignored) {
        return fallback;
      }
    }
    return fallback;
  }

  private static Long exactDiagnosticLong(Object value) {
    try {
      if (value instanceof Byte
          || value instanceof Short
          || value instanceof Integer
          || value instanceof Long) {
        return ((Number) value).longValue();
      }
      if (value instanceof BigInteger) {
        return new BigDecimal((BigInteger) value).longValueExact();
      }
      if (value instanceof BigDecimal) {
        return ((BigDecimal) value).longValueExact();
      }
      if (value instanceof Float || value instanceof Double) {
        double doubleValue = ((Number) value).doubleValue();
        return Double.isFinite(doubleValue)
            ? BigDecimal.valueOf(doubleValue).longValueExact()
            : null;
      }
    } catch (ArithmeticException ignored) {
      return null;
    }
    return null;
  }

  private static long longValue(Object value, long fallback) {
    if (value instanceof Number) {
      return ((Number) value).longValue();
    }
    if (value instanceof String) {
      try {
        return Long.parseLong((String) value);
      } catch (NumberFormatException ignored) {
        return fallback;
      }
    }
    return fallback;
  }
}
