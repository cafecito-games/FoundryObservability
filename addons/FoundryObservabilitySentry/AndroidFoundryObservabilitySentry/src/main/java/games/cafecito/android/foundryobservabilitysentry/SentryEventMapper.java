package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.SentryEvent;
import io.sentry.SentryLevel;
import io.sentry.protocol.Message;
import io.sentry.protocol.SentryException;
import java.lang.reflect.Array;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

final class SentryEventMapper {
  private SentryEventMapper() {}

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

  static Map<String, Object> copyDictionary(Map<?, ?> source) {
    Map<String, Object> result = new HashMap<>();
    if (source == null) {
      return result;
    }
    for (Map.Entry<?, ?> entry : source.entrySet()) {
      if (!(entry.getKey() instanceof String)) {
        continue;
      }
      Object value = copyValue(entry.getValue());
      if (value != null) {
        result.put((String) entry.getKey(), value);
      }
    }
    return result;
  }

  static Map<String, Object> mergedExtras(
      Map<String, Object> global,
      Map<String, Object> event,
      String kind,
      String source,
      long timestampMsec,
      Map<?, ?> exception) {
    Map<String, Object> extras = copyDictionary(global);
    extras.putAll(copyDictionary(event));

    if (exception != null) {
      Map<String, Object> exceptionAttributes = asMap(exception.get("attributes"));
      extras.putAll(exceptionAttributes);
      extras.put("foundry.exception_type", stringValue(exception.get("type_name")));
      String stackTrace = stringValue(exception.get("stack_trace"));
      if (!stackTrace.isEmpty()) {
        extras.put("foundry.stack_trace", stackTrace);
      }
    }

    extras.put("foundry.kind", safeString(kind));
    extras.put("foundry.source", safeString(source));
    extras.put("foundry.timestamp_msec", timestampMsec);
    return extras;
  }

  static SentryEvent makeEvent(Map<?, ?> payload, Map<String, Object> globalAttributes) {
    Map<?, ?> values = payload == null ? Map.of() : payload;
    String messageText = stringValue(values.get("message"));
    String source = stringValue(values.get("source"));
    String kind = stringValue(values.get("kind"));
    int level = intValue(values.get("level"), 50);
    long timestampMsec = longValue(values.get("timestamp_msec"), 0L);
    Map<String, Object> attributes = asMap(values.get("attributes"));
    Map<?, ?> exception = values.get("exception") instanceof Map
        ? (Map<?, ?>) values.get("exception")
        : null;

    SentryEvent event = new SentryEvent();
    Message message = new Message();
    message.setFormatted(messageText);
    event.setMessage(message);
    event.setLevel(sentryLevel(level));
    if (!source.isEmpty()) {
      event.setLogger(source);
    }
    event.setExtras(mergedExtras(
        globalAttributes,
        attributes,
        kind,
        source,
        timestampMsec,
        exception));

    if (exception != null) {
      String exceptionType = stringValue(exception.get("type_name"));
      String exceptionMessage = stringValue(exception.get("message"));
      if (!exceptionType.isEmpty() || !exceptionMessage.isEmpty()) {
        SentryException sentryException = new SentryException();
        sentryException.setType(exceptionType);
        sentryException.setValue(exceptionMessage);
        List<SentryException> exceptions = new ArrayList<>();
        exceptions.add(sentryException);
        event.setExceptions(exceptions);
      }
    }
    return event;
  }

  private static Object copyValue(Object value) {
    if (value == null) {
      return null;
    }
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
    if (value instanceof Map) {
      return copyDictionary((Map<?, ?>) value);
    }
    if (value instanceof Iterable) {
      List<Object> result = new ArrayList<>();
      for (Object element : (Iterable<?>) value) {
        Object copied = copyValue(element);
        if (copied != null) {
          result.add(copied);
        }
      }
      return result;
    }
    if (value.getClass().isArray()) {
      List<Object> result = new ArrayList<>();
      for (int index = 0; index < Array.getLength(value); index++) {
        Object copied = copyValue(Array.get(value, index));
        if (copied != null) {
          result.add(copied);
        }
      }
      return result;
    }
    return null;
  }

  private static Map<String, Object> asMap(Object value) {
    return value instanceof Map ? copyDictionary((Map<?, ?>) value) : new HashMap<>();
  }

  private static String stringValue(Object value) {
    return value instanceof String ? (String) value : safeString(value);
  }

  private static String safeString(String value) {
    return value == null ? "" : value;
  }

  private static String safeString(Object value) {
    return value == null ? "" : String.valueOf(value);
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
