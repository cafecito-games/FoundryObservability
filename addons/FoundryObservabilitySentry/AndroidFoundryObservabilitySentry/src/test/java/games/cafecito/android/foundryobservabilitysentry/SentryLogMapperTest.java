package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;

import io.sentry.SentryLogLevel;
import java.util.Map;
import org.junit.Test;

public class SentryLogMapperTest {
  @Test
  public void mapsAllStructuredLogLevels() {
    assertEquals(SentryLogLevel.TRACE, SentryLogMapper.sentryLevel(10));
    assertEquals(SentryLogLevel.DEBUG, SentryLogMapper.sentryLevel(20));
    assertEquals(SentryLogLevel.INFO, SentryLogMapper.sentryLevel(30));
    assertEquals(SentryLogLevel.WARN, SentryLogMapper.sentryLevel(40));
    assertEquals(SentryLogLevel.ERROR, SentryLogMapper.sentryLevel(50));
    assertEquals(SentryLogLevel.FATAL, SentryLogMapper.sentryLevel(60));
    assertEquals(SentryLogLevel.ERROR, SentryLogMapper.sentryLevel(999));
  }

  @Test
  public void mergesScalarAttributesAndReservedMetadataLast() {
    Map<String, Object> global = Map.of("shared", "global", "build", 42L);
    Map<String, Object> event = Map.of("shared", "event", "foundry.kind", "caller");
    Map<String, Object> result = SentryLogMapper.mergedAttributes(
        global, event, "log", "foundry.logging", 1612325106123L, 4567L);

    assertEquals("event", result.get("shared"));
    assertEquals(42L, result.get("build"));
    assertEquals("log", result.get("foundry.kind"));
    assertEquals("foundry.logging", result.get("foundry.source"));
    assertEquals(1612325106123L, result.get("foundry.timestamp_msec"));
    assertEquals(4567L, result.get("foundry.engine_ticks_msec"));
  }

  @Test
  public void omitsNestedAndUnsupportedValues() {
    Map<String, Object> input = Map.of(
        "supported", "yes",
        "nested", Map.of("value", true),
        "unsupported", new Object());

    Map<String, Object> result = SentryLogMapper.scalarAttributes(input);

    assertEquals("yes", result.get("supported"));
    assertFalse(result.containsKey("nested"));
    assertFalse(result.containsKey("unsupported"));
  }
}
