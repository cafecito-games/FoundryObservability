package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.Breadcrumb;
import io.sentry.SentryLevel;
import java.util.Date;
import java.util.HashMap;
import java.util.Map;

final class SentryBreadcrumbMapper {
  private SentryBreadcrumbMapper() {}

  static SentryLevel sentryLevel(int level) {
    switch (level) {
      case 10:
      case 20:
        return SentryLevel.DEBUG;
      case 30:
        return SentryLevel.INFO;
      case 40:
        return SentryLevel.WARNING;
      case 50:
        return SentryLevel.ERROR;
      case 60:
        return SentryLevel.FATAL;
      default:
        return SentryLevel.ERROR;
    }
  }

  static Map<String, Object> mergedData(
      Map<String, Object> global,
      Map<?, ?> breadcrumb,
      long timestampMsec) {
    Map<String, Object> result = SentryEventMapper.copyDictionary(global);
    result.putAll(SentryEventMapper.copyDictionary(breadcrumb));
    result.put("foundry.timestamp_msec", timestampMsec);
    return result;
  }

  static Breadcrumb makeBreadcrumb(
      Map<?, ?> payload,
      Map<String, Object> globalAttributes) {
    return makeBreadcrumb(payload, globalAttributes, new Date());
  }

  static Breadcrumb makeBreadcrumb(
      Map<?, ?> payload,
      Map<String, Object> globalAttributes,
      Date sdkTimestamp) {
    Map<?, ?> values = payload == null ? Map.of() : payload;
    long timestampMsec = longValue(values.get("timestamp_msec"), 0L);
    Breadcrumb breadcrumb = new Breadcrumb(sdkTimestamp);
    breadcrumb.setMessage(stringValue(values.get("message")));
    breadcrumb.setCategory(stringValue(values.get("category")));
    breadcrumb.setType(stringValue(values.get("type")));
    breadcrumb.setLevel(sentryLevel(intValue(values.get("level"), 50)));

    Map<?, ?> attributes = values.get("attributes") instanceof Map
        ? (Map<?, ?>) values.get("attributes")
        : new HashMap<>();
    for (Map.Entry<String, Object> entry
        : mergedData(globalAttributes, attributes, timestampMsec).entrySet()) {
      breadcrumb.setData(entry.getKey(), entry.getValue());
    }
    return breadcrumb;
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
