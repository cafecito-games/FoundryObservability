package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import io.sentry.SentryEvent;
import io.sentry.SentryLevel;
import java.util.HashMap;
import java.util.Map;
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
        global, event, "exception", "game", 1234L, exception);

    assertEquals("event", extras.get("shared"));
    assertEquals(42L, extras.get("build"));
    assertEquals("exception", extras.get("foundry.kind"));
    assertEquals("game", extras.get("foundry.source"));
    assertEquals(1234L, extras.get("foundry.timestamp_msec"));
    assertEquals("InvalidState", extras.get("foundry.exception_type"));
    assertEquals("trace", extras.get("foundry.stack_trace"));
  }

  @Test
  public void buildsMessageAndExceptionEvent() {
    Map<String, Object> payload = new HashMap<>();
    payload.put("kind", "exception");
    payload.put("level", 50);
    payload.put("message", "boom");
    payload.put("source", "game");
    payload.put("timestamp_msec", 1234L);
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
    assertEquals("title", result.getExtras().get("screen"));
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
}
