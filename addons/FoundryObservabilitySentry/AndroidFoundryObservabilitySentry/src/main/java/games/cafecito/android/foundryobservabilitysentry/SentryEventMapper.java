package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.IScope;
import io.sentry.SentryEvent;
import io.sentry.SentryLevel;
import io.sentry.protocol.Message;
import io.sentry.protocol.SentryException;
import io.sentry.protocol.SentryStackFrame;
import io.sentry.protocol.SentryStackTrace;
import io.sentry.protocol.User;
import java.lang.reflect.Array;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Date;
import java.util.HashMap;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

final class SentryEventMapper {
  private static final int MAX_VARIABLE_CONTAINER_DEPTH = 8;
  private static final int MAX_VARIABLE_ITEMS = 256;

  private SentryEventMapper() {}

  static final class MetricPayload {
    final int type;
    final String name;
    final double value;
    final String unit;
    final Map<String, Object> attributes;

    MetricPayload(
        int type,
        String name,
        double value,
        String unit,
        Map<String, Object> attributes) {
      this.type = type;
      this.name = name;
      this.value = value;
      this.unit = unit;
      this.attributes = attributes;
    }
  }

  static final class ScopePayload {
    final Map<String, String> tags;
    final Map<String, Map<String, Object>> contexts;
    final Map<String, String> user;

    ScopePayload(
        Map<String, String> tags,
        Map<String, Map<String, Object>> contexts,
        Map<String, String> user) {
      this.tags = Collections.unmodifiableMap(new LinkedHashMap<>(tags));
      this.contexts = immutableContexts(contexts);
      this.user = user == null
          ? null
          : Collections.unmodifiableMap(new LinkedHashMap<>(user));
    }
  }

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

  static Map<String, Map<String, Object>> contexts(Object value) {
    Map<String, Map<String, Object>> result = new HashMap<>();
    if (!(value instanceof Map)) {
      return result;
    }
    for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
      if (!(entry.getKey() instanceof String)
          || ((String) entry.getKey()).isEmpty()
          || !(entry.getValue() instanceof Map)) {
        continue;
      }
      Map<String, Object> context = sanitizeVariableMap(
          (Map<?, ?>) entry.getValue(),
          0,
          new VariableCopyState(),
          true);
      if (context != null) {
        result.put((String) entry.getKey(), context);
      }
    }
    return result;
  }

  static ScopePayload scopePayload(Object value) {
    if (!(value instanceof Map)) {
      return new ScopePayload(Map.of(), Map.of(), null);
    }
    Map<?, ?> source = (Map<?, ?>) value;
    return new ScopePayload(
        scopeTags(source.get("tags")),
        contexts(source.get("contexts")),
        scopeUser(source.get("user")));
  }

  static void applyScope(IScope scope, ScopePayload payload) {
    for (Map.Entry<String, String> entry : payload.tags.entrySet()) {
      scope.setTag(entry.getKey(), entry.getValue());
    }
    for (Map.Entry<String, Map<String, Object>> entry : payload.contexts.entrySet()) {
      scope.setContexts(entry.getKey(), entry.getValue());
    }
    if (payload.user != null) {
      User user = new User();
      user.setId(payload.user.get("id"));
      user.setUsername(payload.user.get("display_name"));
      user.setEmail(payload.user.get("contact_email"));
      scope.setUser(user);
    }
  }

  static Map<String, Object> mergedExtras(
      Map<String, Object> global,
      Map<String, Object> event,
      String kind,
      String source,
      long timestampMsec,
      long engineTicksMsec,
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
    if (engineTicksMsec >= 0L) {
      extras.put("foundry.engine_ticks_msec", engineTicksMsec);
    } else {
      extras.remove("foundry.engine_ticks_msec");
    }
    return extras;
  }

  static Date sentryDate(long timestampMsec) {
    return new Date(timestampMsec);
  }

  static SentryEvent makeEvent(Map<?, ?> payload, Map<String, Object> globalAttributes) {
    Map<?, ?> values = payload == null ? Map.of() : payload;
    String messageText = stringValue(values.get("message"));
    String source = stringValue(values.get("source"));
    String kind = stringValue(values.get("kind"));
    int level = intValue(values.get("level"), 50);
    long timestampMsec = longValue(values.get("timestamp_msec"), 0L);
    long engineTicksMsec = longValue(values.get("engine_ticks_msec"), -1L);
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
    event.setTimestamp(sentryDate(timestampMsec));
    event.setExtras(mergedExtras(
        globalAttributes,
        attributes,
        kind,
        source,
        timestampMsec,
        engineTicksMsec,
        exception));

    if (exception != null) {
      String exceptionType = stringValue(exception.get("type_name"));
      String exceptionMessage = stringValue(exception.get("message"));
      SentryStackTrace stacktrace = structuredStacktrace(exception.get("frames"));
      if (!exceptionType.isEmpty() || !exceptionMessage.isEmpty() || stacktrace != null) {
        SentryException sentryException = new SentryException();
        sentryException.setType(exceptionType);
        sentryException.setValue(exceptionMessage);
        if (stacktrace != null) {
          sentryException.setStacktrace(stacktrace);
        }
        List<SentryException> exceptions = new ArrayList<>();
        exceptions.add(sentryException);
        event.setExceptions(exceptions);
      }
    }
    return event;
  }

  static MetricPayload metricPayload(Map<?, ?> payload) {
    if (payload == null
        || !(payload.get("type") instanceof Number)
        || !(payload.get("name") instanceof String)
        || !(payload.get("value") instanceof Number)) {
      return null;
    }

    int type = ((Number) payload.get("type")).intValue();
    String name = (String) payload.get("name");
    double value = ((Number) payload.get("value")).doubleValue();
    if (type < 0 || type > 2 || name.isEmpty() || !Double.isFinite(value)) {
      return null;
    }
    if (type == 0 && (value < 0.0D || value != Math.rint(value))) {
      return null;
    }

    String unit = payload.get("unit") instanceof String
        ? (String) payload.get("unit")
        : null;
    if (unit != null && unit.isEmpty()) {
      unit = null;
    }
    Map<String, Object> attributes = metricAttributes(payload.get("attributes"));
    return new MetricPayload(type, name, value, unit, attributes);
  }

  private static Map<String, Object> metricAttributes(Object value) {
    Map<String, Object> result = new HashMap<>();
    if (!(value instanceof Map)) {
      return result;
    }
    for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
      if (!(entry.getKey() instanceof String)) {
        continue;
      }
      Object attribute = entry.getValue();
      if (attribute instanceof Boolean || attribute instanceof String) {
        result.put((String) entry.getKey(), attribute);
      } else if (attribute instanceof Byte
          || attribute instanceof Short
          || attribute instanceof Integer
          || attribute instanceof Long) {
        result.put((String) entry.getKey(), ((Number) attribute).longValue());
      } else if (attribute instanceof Float || attribute instanceof Double) {
        double number = ((Number) attribute).doubleValue();
        if (Double.isFinite(number)) {
          result.put((String) entry.getKey(), number);
        }
      }
    }
    return result;
  }

  private static SentryStackTrace structuredStacktrace(Object value) {
    List<SentryStackFrame> frames = new ArrayList<>();
    for (Object frameValue : collectionValues(value)) {
      if (!(frameValue instanceof Map)) {
        continue;
      }
      SentryStackFrame frame = structuredStackFrame((Map<?, ?>) frameValue);
      if (frame != null) {
        frames.add(frame);
      }
    }
    return frames.isEmpty() ? null : new SentryStackTrace(frames);
  }

  private static SentryStackFrame structuredStackFrame(Map<?, ?> value) {
    String file = nonEmptyString(value.get("file"));
    String function = nonEmptyString(value.get("function"));
    Integer line = positiveLineNumber(value.get("line"));
    String language = nonEmptyString(value.get("language"));
    if (file == null && function == null && line == null && language == null) {
      return null;
    }

    SentryStackFrame frame = new SentryStackFrame();
    frame.setFilename(file);
    frame.setFunction(function);
    frame.setLineno(line);
    frame.setPlatform(language);
    frame.setInApp(value.get("in_app") instanceof Boolean
        ? (Boolean) value.get("in_app")
        : true);

    String contextLine = nonEmptyString(value.get("context_line"));
    if (contextLine != null) {
      frame.setContextLine(contextLine);
      List<String> preContext = stringCollection(value.get("pre_context"));
      if (preContext != null) {
        frame.setPreContext(preContext);
      }
      List<String> postContext = stringCollection(value.get("post_context"));
      if (postContext != null) {
        frame.setPostContext(postContext);
      }
    }

    Map<String, Object> variables = stringKeyedMap(value.get("variables"));
    if (variables != null) {
      frame.setVars(variables);
    }
    return frame;
  }

  private static List<Object> collectionValues(Object value) {
    List<Object> values = new ArrayList<>();
    if (value instanceof Iterable) {
      for (Object element : (Iterable<?>) value) {
        values.add(element);
      }
    } else if (value != null && value.getClass().isArray()) {
      for (int index = 0; index < Array.getLength(value); index++) {
        values.add(Array.get(value, index));
      }
    }
    return values;
  }

  private static List<String> stringCollection(Object value) {
    List<String> values = new ArrayList<>();
    for (Object element : collectionValues(value)) {
      if (element instanceof String) {
        values.add((String) element);
      }
    }
    return values.isEmpty() ? null : values;
  }

  private static Map<String, Object> stringKeyedMap(Object value) {
    if (!(value instanceof Map)) {
      return null;
    }
    Map<String, Object> result = sanitizeVariableMap(
        (Map<?, ?>) value,
        0,
        new VariableCopyState(),
        false);
    return result == null || result.isEmpty() ? null : result;
  }

  private static Map<String, Object> sanitizeVariableMap(
      Map<?, ?> value,
      int depth,
      VariableCopyState state,
      boolean preserveNull) {
    if (depth > MAX_VARIABLE_CONTAINER_DEPTH || !state.enter(value)) {
      return null;
    }
    try {
      Map<String, Object> result = new HashMap<>();
      for (Map.Entry<?, ?> entry : value.entrySet()) {
        if (!state.consumeItem()) {
          break;
        }
        if (!(entry.getKey() instanceof String)) {
          continue;
        }
        Object rawValue = entry.getValue();
        Object copied = sanitizeVariableValue(rawValue, depth, state, preserveNull);
        if (copied != null || (preserveNull && rawValue == null)) {
          result.put((String) entry.getKey(), copied);
        }
      }
      return result;
    } finally {
      state.leave(value);
    }
  }

  private static List<Object> sanitizeVariableCollection(
      Object value,
      int depth,
      VariableCopyState state,
      boolean preserveNull) {
    if (depth > MAX_VARIABLE_CONTAINER_DEPTH || !state.enter(value)) {
      return null;
    }
    try {
      List<Object> result = new ArrayList<>();
      if (value instanceof Iterable) {
        for (Object element : (Iterable<?>) value) {
          if (!state.consumeItem()) {
            break;
          }
          Object copied = sanitizeVariableValue(element, depth, state, preserveNull);
          if (copied != null || (preserveNull && element == null)) {
            result.add(copied);
          }
        }
      } else {
        for (int index = 0; index < Array.getLength(value); index++) {
          if (!state.consumeItem()) {
            break;
          }
          Object element = Array.get(value, index);
          Object copied = sanitizeVariableValue(element, depth, state, preserveNull);
          if (copied != null || (preserveNull && element == null)) {
            result.add(copied);
          }
        }
      }
      return result;
    } finally {
      state.leave(value);
    }
  }

  private static Object sanitizeVariableValue(
      Object value,
      int parentDepth,
      VariableCopyState state,
      boolean preserveNull) {
    if (value instanceof Boolean
        || value instanceof String
        || value instanceof Byte
        || value instanceof Short
        || value instanceof Integer
        || value instanceof Long
        || value instanceof BigInteger
        || value instanceof BigDecimal) {
      return value;
    }
    if (value instanceof Float) {
      return Float.isFinite((Float) value) ? value : null;
    }
    if (value instanceof Double) {
      return Double.isFinite((Double) value) ? value : null;
    }
    if (value instanceof Map) {
      return sanitizeVariableMap(
          (Map<?, ?>) value,
          parentDepth + 1,
          state,
          preserveNull);
    }
    if (value instanceof Iterable || (value != null && value.getClass().isArray())) {
      return sanitizeVariableCollection(value, parentDepth + 1, state, preserveNull);
    }
    return null;
  }

  private static Map<String, String> scopeTags(Object value) {
    Map<String, String> tags = new LinkedHashMap<>();
    if (!(value instanceof Map)) {
      return tags;
    }
    for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
      if (entry.getKey() instanceof String
          && !((String) entry.getKey()).isEmpty()
          && entry.getValue() instanceof String) {
        tags.put((String) entry.getKey(), (String) entry.getValue());
      }
    }
    return tags;
  }

  private static Map<String, String> scopeUser(Object value) {
    if (!(value instanceof Map)) {
      return null;
    }
    Map<String, String> user = new LinkedHashMap<>();
    for (String key : List.of("id", "display_name", "contact_email")) {
      Object candidate = ((Map<?, ?>) value).get(key);
      if (candidate instanceof String && !((String) candidate).isEmpty()) {
        user.put(key, (String) candidate);
      }
    }
    return user.isEmpty() ? null : user;
  }

  private static Map<String, Map<String, Object>> immutableContexts(
      Map<String, Map<String, Object>> contexts) {
    Map<String, Map<String, Object>> result = new LinkedHashMap<>();
    for (Map.Entry<String, Map<String, Object>> entry : contexts.entrySet()) {
      result.put(entry.getKey(), immutableMap(entry.getValue()));
    }
    return Collections.unmodifiableMap(result);
  }

  private static Map<String, Object> immutableMap(Map<?, ?> source) {
    Map<String, Object> result = new LinkedHashMap<>();
    for (Map.Entry<?, ?> entry : source.entrySet()) {
      if (entry.getKey() instanceof String) {
        result.put((String) entry.getKey(), immutableValue(entry.getValue()));
      }
    }
    return Collections.unmodifiableMap(result);
  }

  private static Object immutableValue(Object value) {
    if (value instanceof Map) {
      return immutableMap((Map<?, ?>) value);
    }
    if (value instanceof List) {
      List<Object> result = new ArrayList<>();
      for (Object element : (List<?>) value) {
        result.add(immutableValue(element));
      }
      return Collections.unmodifiableList(result);
    }
    return value;
  }

  private static final class VariableCopyState {
    private final Map<Object, Boolean> activeContainers = new IdentityHashMap<>();
    private int itemCount;

    boolean enter(Object container) {
      return activeContainers.put(container, Boolean.TRUE) == null;
    }

    void leave(Object container) {
      activeContainers.remove(container);
    }

    boolean consumeItem() {
      if (itemCount >= MAX_VARIABLE_ITEMS) {
        return false;
      }
      itemCount++;
      return true;
    }
  }

  private static String nonEmptyString(Object value) {
    if (!(value instanceof String) || ((String) value).isEmpty()) {
      return null;
    }
    return (String) value;
  }

  private static Integer positiveLineNumber(Object value) {
    if (value instanceof Byte
        || value instanceof Short
        || value instanceof Integer
        || value instanceof Long) {
      long number = ((Number) value).longValue();
      return number > 0L && number <= Integer.MAX_VALUE ? (int) number : null;
    }
    if (value instanceof BigInteger) {
      BigInteger number = (BigInteger) value;
      return number.signum() > 0
              && number.compareTo(BigInteger.valueOf(Integer.MAX_VALUE)) <= 0
          ? number.intValue()
          : null;
    }
    if (value instanceof BigDecimal) {
      try {
        int number = ((BigDecimal) value).intValueExact();
        return number > 0 ? number : null;
      } catch (ArithmeticException ignored) {
        return null;
      }
    }
    if (!(value instanceof Float) && !(value instanceof Double)) {
      return null;
    }
    double number = ((Number) value).doubleValue();
    if (!Double.isFinite(number)
        || number <= 0.0D
        || number != Math.rint(number)
        || number > Integer.MAX_VALUE) {
      return null;
    }
    return (int) number;
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
