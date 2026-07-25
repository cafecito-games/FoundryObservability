package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import io.sentry.SentryEvent;
import io.sentry.SentryLevel;
import io.sentry.protocol.SentryException;
import io.sentry.protocol.SentryStackFrame;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TimeZone;
import org.junit.Test;

public class SentryEventMapperTest {
  @Test
  public void mapsFoundryLevels() {
    assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(10));
    assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(20));
    assertEquals(SentryLevel.INFO, SentryEventMapper.sentryLevel(30));
    assertEquals(SentryLevel.WARNING, SentryEventMapper.sentryLevel(40));
    assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(50));
    assertEquals(SentryLevel.FATAL, SentryEventMapper.sentryLevel(60));
    assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(999));
  }

  @Test
  public void convertsUnixMillisecondsWithoutTimezoneDependence() {
    TimeZone original = TimeZone.getDefault();
    try {
      TimeZone.setDefault(TimeZone.getTimeZone("GMT+09:00"));
      Date result = SentryEventMapper.sentryDate(1612325106123L);
      assertEquals(1612325106123L, result.getTime());
    } finally {
      TimeZone.setDefault(original);
    }
  }

  @Test
  public void mergesAttributesAndWritesReservedMetadataLast() {
    Map<String, Object> global = new HashMap<>();
    global.put("shared", "global");
    global.put("build", 42);
    Map<String, Object> event = new HashMap<>();
    event.put("shared", "event");
    event.put("foundry.kind", "caller-value");
    Map<String, Object> exception = new HashMap<>();
    exception.put("type_name", "InvalidState");
    exception.put("message", "boom");
    exception.put("stack_trace", "trace");

    Map<String, Object> extras = SentryEventMapper.mergedExtras(
        global, event, "exception", "game", 1612325106123L, 4567L, exception);

    assertEquals("event", extras.get("shared"));
    assertEquals(42L, extras.get("build"));
    assertEquals("exception", extras.get("foundry.kind"));
    assertEquals("game", extras.get("foundry.source"));
    assertEquals(1612325106123L, extras.get("foundry.timestamp_msec"));
    assertEquals(4567L, extras.get("foundry.engine_ticks_msec"));
    assertEquals("InvalidState", extras.get("foundry.exception_type"));
    assertEquals("trace", extras.get("foundry.stack_trace"));
  }

  @Test
  public void unavailableEngineTicksRemoveCallerControlledReservedMetadata() {
    Map<String, Object> callerAttributes = Map.of("foundry.engine_ticks_msec", 999L);

    Map<String, Object> extras = SentryEventMapper.mergedExtras(
        callerAttributes,
        callerAttributes,
        "message",
        "game",
        1612325106123L,
        -1L,
        null);

    assertFalse(extras.containsKey("foundry.engine_ticks_msec"));
  }

  @Test
  public void buildsMessageAndExceptionEvent() {
    Map<String, Object> payload = new HashMap<>();
    payload.put("kind", "exception");
    payload.put("level", 50);
    payload.put("message", "boom");
    payload.put("source", "game");
    payload.put("timestamp_msec", 1612325106123L);
    payload.put("engine_ticks_msec", 4567L);
    payload.put("attributes", Map.of("screen", "title"));
    payload.put("exception", Map.of(
        "type_name", "InvalidState",
        "message", "boom",
        "stack_trace", "trace",
        "attributes", Map.of()));

    SentryEvent result = SentryEventMapper.makeEvent(payload, Map.of("build", 42));

    assertEquals("boom", result.getMessage().getFormatted());
    assertEquals(SentryLevel.ERROR, result.getLevel());
    assertEquals("game", result.getLogger());
    assertEquals(1612325106123L, result.getTimestamp().getTime());
    assertEquals("title", result.getExtras().get("screen"));
    assertEquals(4567L, result.getExtras().get("foundry.engine_ticks_msec"));
    assertEquals(1, result.getExceptions().size());
    assertEquals("InvalidState", result.getExceptions().get(0).getType());
    assertEquals("boom", result.getExceptions().get(0).getValue());
  }

  @Test
  public void omitsUnsupportedNestedValues() {
    Map<String, Object> input = new HashMap<>();
    input.put("supported", "yes");
    input.put("unsupported", new Object());

    Map<String, Object> result = SentryEventMapper.copyDictionary(input);

    assertEquals("yes", result.get("supported"));
    assertTrue(!result.containsKey("unsupported"));
  }

  @Test
  public void mapsMetricPayloadAndScalarAttributes() {
    Map<String, Object> attributes = new HashMap<>();
    attributes.put("string", "value");
    attributes.put("bool", true);
    attributes.put("int", 42);
    attributes.put("long", 43L);
    attributes.put("float", 1.5F);
    attributes.put("double", 2.5D);
    attributes.put("nested", Map.of("unsupported", true));

    SentryEventMapper.MetricPayload result = SentryEventMapper.metricPayload(Map.of(
        "type", 2,
        "name", "request.duration",
        "value", 12.5D,
        "unit", "millisecond",
        "attributes", attributes));

    assertNotNull(result);
    assertEquals(2, result.type);
    assertEquals("request.duration", result.name);
    assertEquals(12.5D, result.value, 0.0D);
    assertEquals("millisecond", result.unit);
    assertEquals("value", result.attributes.get("string"));
    assertEquals(true, result.attributes.get("bool"));
    assertEquals(42L, result.attributes.get("int"));
    assertEquals(43L, result.attributes.get("long"));
    assertEquals(1.5D, (Double) result.attributes.get("float"), 0.0D);
    assertEquals(2.5D, (Double) result.attributes.get("double"), 0.0D);
    assertFalse(result.attributes.containsKey("nested"));
  }

  @Test
  public void rejectsInvalidMetricPayloads() {
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 9, "name", "metric", "value", 1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 0, "name", "counter", "value", -1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 0, "name", "counter", "value", 1.5D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 1, "name", "", "value", 1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 1, "name", "metric", "value", Double.NaN)));
  }

  @Test
  public void mapsStructuredExceptionFramesInProviderOrder() {
    Map<Object, Object> variables = new HashMap<>();
    variables.put("damage", 10);
    variables.put("critical", false);
    variables.put(7, "ignored");
    Map<String, Object> firstFrame = new HashMap<>();
    firstFrame.put("file", "res://Player.fs");
    firstFrame.put("function", "Player.attack");
    firstFrame.put("line", 24L);
    firstFrame.put("language", "fsharp");
    firstFrame.put("in_app", true);
    firstFrame.put("context_line", "let damage = 10");
    firstFrame.put("pre_context", new Object[] {"let weapon = sword", 2, "let target = goblin"});
    firstFrame.put("post_context", Arrays.asList("applyDamage target damage", false));
    firstFrame.put("variables", variables);
    Map<String, Object> secondFrame = new HashMap<>();
    secondFrame.put("file", "res://Combat.fs");
    secondFrame.put("function", "Combat.resolve");
    secondFrame.put("line", new BigDecimal("8"));
    secondFrame.put("language", "fsharp");
    secondFrame.put("in_app", false);

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "legacy formatted stack",
        "frames", new Object[] {firstFrame, secondFrame}));

    assertEquals("legacy formatted stack", result.getExtras().get("foundry.stack_trace"));
    SentryException exception = result.getExceptions().get(0);
    assertEquals("InvalidState", exception.getType());
    assertEquals("bad state", exception.getValue());
    assertNotNull(exception.getStacktrace());
    List<SentryStackFrame> frames = exception.getStacktrace().getFrames();
    assertEquals(2, frames.size());

    SentryStackFrame first = frames.get(0);
    assertEquals("res://Player.fs", first.getFilename());
    assertEquals("Player.attack", first.getFunction());
    assertEquals(Integer.valueOf(24), first.getLineno());
    assertEquals("fsharp", first.getPlatform());
    assertEquals(Boolean.TRUE, first.isInApp());
    assertEquals("let damage = 10", first.getContextLine());
    assertEquals(Arrays.asList("let weapon = sword", "let target = goblin"), first.getPreContext());
    assertEquals(List.of("applyDamage target damage"), first.getPostContext());
    assertEquals(10, first.getVars().get("damage"));
    assertEquals(false, first.getVars().get("critical"));
    assertFalse(first.getVars().containsKey("7"));
    assertFalse(first.getVars() == (Object) variables);

    SentryStackFrame second = frames.get(1);
    assertEquals("res://Combat.fs", second.getFilename());
    assertEquals("Combat.resolve", second.getFunction());
    assertEquals(Integer.valueOf(8), second.getLineno());
    assertEquals("fsharp", second.getPlatform());
    assertEquals(Boolean.FALSE, second.isInApp());
  }

  @Test
  public void skipsMalformedFramesAndKeepsUsefulPartialFrames() {
    List<Object> frames = new ArrayList<>();
    frames.add("not a frame");
    frames.add(Map.of(
        "file", 42,
        "function", false,
        "language", 99,
        "line", -1,
        "in_app", "yes",
        "context_line", "context without identity",
        "pre_context", new Object[] {"valid", 2, false},
        "post_context", Arrays.asList(true, 4),
        "variables", List.of("not", "a map")));
    frames.add(Map.of(
        "file", "",
        "function", "",
        "language", "",
        "line", 0,
        "context_line", "context with empty identity",
        "variables", Map.of("value", 1)));
    frames.add(Map.of("context_line", "context only", "pre_context", List.of("nearby")));
    frames.add(Map.of("variables", Map.of("value", 1)));
    frames.add(Map.of("in_app", false));
    frames.add(Map.of("line", 1.5D));
    frames.add(Map.of("line", new BigDecimal("1.0000000000000000000000000001")));
    frames.add(Map.of("line", new BigDecimal("2147483646.0000000001")));
    frames.add(Map.of("line", Double.NaN));
    frames.add(Map.of("line", Double.POSITIVE_INFINITY));
    frames.add(Map.of("line", Long.MAX_VALUE));
    frames.add(Map.of("line", true));
    frames.add(Map.of("line", "1"));
    frames.add(Map.of(
        "file", "res://partial.fs",
        "function", "",
        "language", "",
        "line", -1,
        "context_line", "",
        "pre_context", new String[] {"discarded"},
        "post_context", new String[] {"also discarded"},
        "variables", Map.of()));
    frames.add(Map.of(
        "file", "res://no-context.fs",
        "context_line", 123,
        "pre_context", new Object[] {"discarded"},
        "post_context", List.of("also discarded")));
    frames.add(Map.of("file", "res://missing-in-app.fs"));
    frames.add(Map.of("file", "res://wrong-in-app.fs", "in_app", 1));

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "legacy formatted stack",
        "frames", frames));

    assertEquals("legacy formatted stack", result.getExtras().get("foundry.stack_trace"));
    List<SentryStackFrame> mapped = result.getExceptions().get(0).getStacktrace().getFrames();
    assertEquals(4, mapped.size());
    SentryStackFrame partial = mapped.get(0);
    assertEquals("res://partial.fs", partial.getFilename());
    assertNull(partial.getFunction());
    assertNull(partial.getLineno());
    assertNull(partial.getPlatform());
    assertEquals(Boolean.TRUE, partial.isInApp());
    assertNull(partial.getContextLine());
    assertNull(partial.getPreContext());
    assertNull(partial.getPostContext());
    assertNull(partial.getVars());

    SentryStackFrame withoutContext = mapped.get(1);
    assertEquals("res://no-context.fs", withoutContext.getFilename());
    assertNull(withoutContext.getContextLine());
    assertNull(withoutContext.getPreContext());
    assertNull(withoutContext.getPostContext());
    assertEquals(Boolean.TRUE, withoutContext.isInApp());

    assertEquals("res://missing-in-app.fs", mapped.get(2).getFilename());
    assertEquals(Boolean.TRUE, mapped.get(2).isInApp());
    assertEquals("res://wrong-in-app.fs", mapped.get(3).getFilename());
    assertEquals(Boolean.TRUE, mapped.get(3).isInApp());
  }

  @Test
  public void leavesStringOnlyExceptionsWithoutStructuredStacktrace() {
    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "at Player.attack()"));

    assertEquals("at Player.attack()", result.getExtras().get("foundry.stack_trace"));
    assertNull(result.getExceptions().get(0).getStacktrace());
  }

  private static SentryEvent eventWithException(Map<String, Object> exception) {
    Map<String, Object> payload = new HashMap<>();
    payload.put("kind", "exception");
    payload.put("level", 50);
    payload.put("message", "boom");
    payload.put("source", "combat");
    payload.put("timestamp_msec", 1612325106123L);
    payload.put("exception", exception);
    return SentryEventMapper.makeEvent(payload, Map.of());
  }
}
