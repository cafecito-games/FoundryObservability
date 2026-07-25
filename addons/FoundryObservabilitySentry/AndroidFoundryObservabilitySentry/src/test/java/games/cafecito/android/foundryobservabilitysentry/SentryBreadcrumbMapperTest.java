package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;

import io.sentry.Breadcrumb;
import io.sentry.SentryLevel;
import java.util.Date;
import java.util.Map;
import org.junit.Test;

public class SentryBreadcrumbMapperTest {
  @Test
  public void mapsAllBreadcrumbLevels() {
    assertEquals(SentryLevel.DEBUG, SentryBreadcrumbMapper.sentryLevel(10));
    assertEquals(SentryLevel.DEBUG, SentryBreadcrumbMapper.sentryLevel(20));
    assertEquals(SentryLevel.INFO, SentryBreadcrumbMapper.sentryLevel(30));
    assertEquals(SentryLevel.WARNING, SentryBreadcrumbMapper.sentryLevel(40));
    assertEquals(SentryLevel.ERROR, SentryBreadcrumbMapper.sentryLevel(50));
    assertEquals(SentryLevel.FATAL, SentryBreadcrumbMapper.sentryLevel(60));
    assertEquals(SentryLevel.ERROR, SentryBreadcrumbMapper.sentryLevel(999));
  }

  @Test
  public void mergesBreadcrumbDataWithReservedTimestampLast() {
    Map<String, Object> result = SentryBreadcrumbMapper.mergedData(
        Map.of("shared", "global", "build", 42L),
        Map.of("shared", "breadcrumb", "foundry.timestamp_msec", -1L),
        1234L);

    assertEquals("breadcrumb", result.get("shared"));
    assertEquals(42L, result.get("build"));
    assertEquals(1234L, result.get("foundry.timestamp_msec"));
  }

  @Test
  public void buildsBreadcrumbWithWallClockTimestampAndEngineTickData() {
    Date wallClock = new Date(1_700_000_000_000L);
    Breadcrumb result = SentryBreadcrumbMapper.makeBreadcrumb(
        Map.of(
            "message", "warning",
            "category", "error",
            "level", 40,
            "timestamp_msec", 1234L,
            "attributes", Map.of("error.file", "res://player.fs")),
        Map.of("build", 42L),
        wallClock);

    assertEquals("warning", result.getMessage());
    assertEquals("error", result.getCategory());
    assertEquals(SentryLevel.WARNING, result.getLevel());
    assertEquals(wallClock, result.getTimestamp());
    assertEquals("res://player.fs", result.getData("error.file"));
    assertEquals(42L, result.getData("build"));
    assertEquals(1234L, result.getData("foundry.timestamp_msec"));
  }
}
