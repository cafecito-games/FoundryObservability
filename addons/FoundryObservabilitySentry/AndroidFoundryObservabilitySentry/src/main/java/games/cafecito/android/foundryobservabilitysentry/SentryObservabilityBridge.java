package games.cafecito.android.foundryobservabilitysentry;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import games.cafecito.foundry.plugin.FoundryPlugin;
import games.cafecito.foundry.plugin.UsedByFoundry;
import io.sentry.Sentry;
import io.sentry.SentryAttributes;
import io.sentry.SentryEvent;
import io.sentry.android.core.SentryAndroid;
import io.sentry.android.core.SentryAndroidOptions;
import io.sentry.logger.SentryLogParameters;
import io.sentry.metrics.SentryMetricsParameters;
import io.sentry.protocol.Feedback;
import io.sentry.protocol.SentryId;
import java.util.Collections;
import java.util.Map;
import java.util.UUID;

public final class SentryObservabilityBridge extends FoundryPlugin {
  static final int BRIDGE_ERROR_OK = 0;
  static final int BRIDGE_ERROR_FAILED = 1;

  private Map<String, Object> globalAttributes = Collections.emptyMap();
  private boolean configured;
  private boolean didShutdown;
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
  public int configure(Dictionary payload) {
    closeActiveClient();
    configured = false;
    didShutdown = false;
    logsEnabled = false;
    metricsEnabled = false;

    if (payload == null) {
      return BRIDGE_ERROR_FAILED;
    }

    Object globalAttributesValue = payload.get("global_attributes");
    globalAttributes = globalAttributesValue instanceof Map
        ? SentryEventMapper.copyDictionary((Map<?, ?>) globalAttributesValue)
        : Collections.emptyMap();

    if (!booleanValue(payload.get("enabled"))) {
      return BRIDGE_ERROR_OK;
    }

    String dsn = stringValue(payload.get("dsn"));
    if (dsn.isEmpty()) {
      return BRIDGE_ERROR_FAILED;
    }

    try {
      logsEnabled = booleanValue(payload.get("logs_enabled"));
      metricsEnabled = booleanValue(payload.get("metrics_enabled"));
      Map<?, ?> providerOptions = payload.get("provider_options") instanceof Map
          ? (Map<?, ?>) payload.get("provider_options")
          : Collections.emptyMap();
      SentryAndroid.init(
          getContext().getApplicationContext(),
          (SentryAndroidOptions options) -> {
            options.setDsn(dsn);
            options.setSendDefaultPii(booleanValue(providerOptions.get("send_default_pii")));
            options.getLogs().setEnabled(logsEnabled);
            options.getMetrics().setEnabled(metricsEnabled);
            options.setDebug(booleanValue(providerOptions.get("debug")));
            setIfNotEmpty(options::setEnvironment, payload.get("environment"));
            setIfNotEmpty(options::setRelease, payload.get("release"));
            setIfNotEmpty(options::setDist, payload.get("dist"));
          });
      configured = Sentry.isEnabled();
      return configured ? BRIDGE_ERROR_OK : BRIDGE_ERROR_FAILED;
    } catch (RuntimeException exception) {
      configured = false;
      return BRIDGE_ERROR_FAILED;
    }
  }

  @UsedByFoundry
  public boolean isAvailable() {
    return configured && !didShutdown && Sentry.isEnabled();
  }

  @UsedByFoundry
  public String capture(Dictionary payload) {
    if (!isAvailable()) {
      return "";
    }
    SentryEvent event = SentryEventMapper.makeEvent(payload, globalAttributes);
    return eventIdString(Sentry.captureEvent(event));
  }

  @UsedByFoundry
  public String captureLog(Dictionary payload) {
    if (!isAvailable() || !logsEnabled || payload == null) {
      return "";
    }

    Map<?, ?> values = payload;
    Map<?, ?> eventAttributes = values.get("attributes") instanceof Map
        ? (Map<?, ?>) values.get("attributes")
        : Collections.emptyMap();
    Map<String, Object> attributes = SentryLogMapper.mergedAttributes(
        globalAttributes,
        eventAttributes,
        stringValue(values.get("kind")),
        stringValue(values.get("source")),
        longValue(values.get("timestamp_msec"), 0L));
    SentryLogParameters parameters = SentryLogParameters.create(
        SentryAttributes.fromMap(attributes));
    Sentry.logger().log(
        SentryLogMapper.sentryLevel(intValue(values.get("level"), 50)),
        parameters,
        stringValue(values.get("message")));
    return "sentry-log:" + UUID.randomUUID();
  }

  @UsedByFoundry
  public boolean captureMetric(Dictionary payload) {
    if (!isAvailable() || !metricsEnabled) {
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
    if (!isAvailable() || payload == null) {
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

  private void closeActiveClient() {
    if (configured) {
      Sentry.close();
    }
    configured = false;
    logsEnabled = false;
    metricsEnabled = false;
  }

  static String eventIdString(SentryId eventId) {
    return eventId == null || SentryId.EMPTY_ID.equals(eventId) ? "" : eventId.toString();
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
