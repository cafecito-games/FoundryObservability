package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.SentryLogLevel;
import java.util.HashMap;
import java.util.Map;

final class SentryLogMapper {
  private SentryLogMapper() {}

  static SentryLogLevel sentryLevel(int level) {
    switch (level) {
      case 10:
        return SentryLogLevel.TRACE;
      case 20:
        return SentryLogLevel.DEBUG;
      case 30:
        return SentryLogLevel.INFO;
      case 40:
        return SentryLogLevel.WARN;
      case 50:
        return SentryLogLevel.ERROR;
      case 60:
        return SentryLogLevel.FATAL;
      default:
        return SentryLogLevel.ERROR;
    }
  }

  static Map<String, Object> mergedAttributes(
      Map<String, Object> global,
      Map<?, ?> event,
      String kind,
      String source,
      long timestampMsec,
      long engineTicksMsec) {
    Map<String, Object> result = scalarAttributes(global);
    result.putAll(scalarAttributes(event));
    result.put("foundry.kind", safeString(kind));
    result.put("foundry.source", safeString(source));
    result.put("foundry.timestamp_msec", timestampMsec);
    if (engineTicksMsec >= 0L) {
      result.put("foundry.engine_ticks_msec", engineTicksMsec);
    }
    return result;
  }

  static Map<String, Object> scalarAttributes(Map<?, ?> source) {
    Map<String, Object> result = new HashMap<>();
    if (source == null) {
      return result;
    }
    for (Map.Entry<?, ?> entry : source.entrySet()) {
      if (!(entry.getKey() instanceof String)) {
        continue;
      }
      Object value = scalarValue(entry.getValue());
      if (value != null) {
        result.put((String) entry.getKey(), value);
      }
    }
    return result;
  }

  private static Object scalarValue(Object value) {
    if (value instanceof Boolean || value instanceof String) {
      return value;
    }
    if (value instanceof Byte
        || value instanceof Short
        || value instanceof Integer
        || value instanceof Long) {
      return ((Number) value).longValue();
    }
    if (value instanceof Float || value instanceof Double) {
      return ((Number) value).doubleValue();
    }
    return null;
  }

  private static String safeString(String value) {
    return value == null ? "" : value;
  }
}
