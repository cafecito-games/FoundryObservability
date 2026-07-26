package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import io.sentry.Scope;
import io.sentry.SentryOptions;
import io.sentry.android.core.SentryAndroidOptions;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.Test;

public class SentryLifecycleCoordinatorTest {
  @Test
  public void publishesOwnerOnlyAfterSuccessfulStart() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure("first", configuration("1.0.0")));
    assertEquals("first", coordinator.activeOwner());
    assertTrue(coordinator.isAvailable("first"));
    assertEquals(List.of("start:1.0.0"), driver.operations);

    FakeDriver failedDriver = new FakeDriver();
    failedDriver.failNextStart = true;
    SentryLifecycleCoordinator failedCoordinator =
        new SentryLifecycleCoordinator(failedDriver);
    assertFalse(failedCoordinator.configure("failed", configuration("2.0.0")));
    assertNull(failedCoordinator.activeOwner());
  }

  @Test
  public void equivalentConfigurationTransfersOwnershipWithoutRestart() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);
    SentryLifecycleConfiguration configuration = configuration("1.0.0");

    assertTrue(coordinator.configure("first", configuration));
    assertTrue(coordinator.configure("second", configuration));

    assertEquals(List.of("start:1.0.0"), driver.operations);
    assertEquals("second", coordinator.activeOwner());
    assertFalse(coordinator.isAvailable("first"));
    assertTrue(coordinator.isAvailable("second"));
  }

  @Test
  public void changedConfigurationClosesThenStarts() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure("first", configuration("1.0.0")));
    assertTrue(coordinator.configure("second", configuration("2.0.0")));

    assertEquals(
        List.of("start:1.0.0", "close", "start:2.0.0"),
        driver.operations);
    assertEquals("second", coordinator.activeOwner());
  }

  @Test
  public void changedMaxBreadcrumbsRestartsAndFailedReplacementRestoresPreviousMaximum() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure("first", configuration("1.0.0", Map.of(), 2)));
    assertTrue(coordinator.configure("second", configuration("1.0.0", Map.of(), 3)));
    driver.failNextStart = true;

    assertFalse(coordinator.configure("third", configuration("1.0.0", Map.of(), 4)));

    assertEquals(List.of(2, 3, 4, 3), driver.startedMaxBreadcrumbs);
    assertEquals(3, coordinator.activeConfiguration().maxBreadcrumbs);
    assertEquals("second", coordinator.activeOwner());
  }

  @Test
  public void maxBreadcrumbsParticipatesInConfigurationEqualityAndHashCode() {
    SentryLifecycleConfiguration first = configuration("1.0.0", Map.of(), 2);
    SentryLifecycleConfiguration equal = configuration("1.0.0", Map.of(), 2);
    SentryLifecycleConfiguration different = configuration("1.0.0", Map.of(), 3);

    assertEquals(first, equal);
    assertEquals(first.hashCode(), equal.hashCode());
    assertFalse(first.equals(different));
  }

  @Test
  public void changedStableContextsCloseThenStart() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure(
        "first",
        configuration("1.0.0", Map.of("foundry_engine", Map.of("version", "4.5")))));
    assertTrue(coordinator.configure(
        "second",
        configuration("1.0.0", Map.of("foundry_engine", Map.of("version", "4.6")))));

    assertEquals(
        List.of("start:1.0.0", "close", "start:1.0.0"),
        driver.operations);
    assertEquals("second", coordinator.activeOwner());
  }

  @SuppressWarnings("unchecked")
  @Test
  public void stableContextsAreImmutableNestedSnapshots() {
    Map<String, Object> engine = new HashMap<>();
    engine.put("version", "4.5");
    Map<String, Object> stableContexts = new HashMap<>();
    stableContexts.put("foundry_engine", engine);

    SentryLifecycleConfiguration configuration =
        configuration("1.0.0", stableContexts);
    engine.put("version", "mutated");
    stableContexts.put("display", Map.of("screen_count", 2));

    Map<String, Object> capturedEngine =
        (Map<String, Object>) configuration.stableContexts.get("foundry_engine");
    assertEquals("4.5", capturedEngine.get("version"));
    assertFalse(configuration.stableContexts.containsKey("display"));
    try {
      capturedEngine.put("version", "mutated copy");
    } catch (UnsupportedOperationException expected) {
      return;
    }
    throw new AssertionError("nested stable context should be immutable");
  }

  @Test
  public void staleOwnerCannotFlushOrShutdown() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);
    SentryLifecycleConfiguration configuration = configuration("1.0.0");

    assertTrue(coordinator.configure("first", configuration));
    assertTrue(coordinator.configure("second", configuration));

    assertFalse(coordinator.flush("first", 250L));
    coordinator.shutdown("first");
    assertEquals(List.of("start:1.0.0"), driver.operations);
    assertTrue(coordinator.isAvailable("second"));

    assertTrue(coordinator.flush("second", 250L));
    assertEquals(List.of("start:1.0.0", "flush:250"), driver.operations);
  }

  @Test
  public void failedReplacementRestoresPreviousOwnerAndConfiguration() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure("first", configuration("1.0.0")));
    driver.failNextStart = true;

    assertFalse(coordinator.configure("second", configuration("2.0.0")));

    assertEquals(
        List.of("start:1.0.0", "close", "start:2.0.0", "start:1.0.0"),
        driver.operations);
    assertEquals("first", coordinator.activeOwner());
    assertTrue(coordinator.isAvailable("first"));
    assertFalse(coordinator.isAvailable("second"));
  }

  @Test
  public void shutdownIsIdempotent() {
    FakeDriver driver = new FakeDriver();
    SentryLifecycleCoordinator coordinator = new SentryLifecycleCoordinator(driver);

    assertTrue(coordinator.configure("first", configuration("1.0.0")));
    coordinator.shutdown("first");
    coordinator.shutdown("first");

    assertEquals(List.of("start:1.0.0", "close"), driver.operations);
    assertNull(coordinator.activeOwner());
  }

  @Test
  public void androidOptionsEnableCrashHandlersAndStableMetadata() {
    SentryLifecycleConfiguration configuration = new SentryLifecycleConfiguration(
        null,
        "https://public@example.com/1",
        "qa",
        "game@1.2.3",
        "android",
        Map.of("build", 42),
        Map.of("foundry_engine", Map.of("version", "4.5")),
        Map.of(),
        true,
        true,
        true,
        6_400L,
        true,
        2);
    SentryAndroidOptions options = new SentryAndroidOptions();

    AndroidSentrySdkDriver.applyOptions(options, configuration);

    assertTrue(options.isEnableUncaughtExceptionHandler());
    assertTrue(options.isEnableNdk());
    assertTrue(options.isEnableScopeSync());
    assertEquals(2_000L, options.getShutdownTimeoutMillis());
    assertEquals("game@1.2.3", options.getRelease());
    assertEquals("qa", options.getEnvironment());
    assertEquals("android", options.getDist());
    assertEquals(2, options.getMaxBreadcrumbs());
    assertEquals(
        Map.of("global_attributes", Map.of("build", 42)),
        AndroidSentrySdkDriver.foundryCrashContext(configuration));
    Scope scope = new Scope(new SentryOptions());
    scope.setContexts(
        "foundry",
        AndroidSentrySdkDriver.foundryCrashContext(configuration));
    AndroidSentrySdkDriver.applyContexts(
        scope,
        SentryEventMapper.contexts(configuration.stableContexts));
    assertEquals(
        Map.of("global_attributes", Map.of("build", 42)),
        scope.getContexts().get("foundry"));
    assertEquals(
        Map.of("version", "4.5"),
        scope.getContexts().get("foundry_engine"));
  }

  @Test
  public void androidOptionsClampNegativeMaxBreadcrumbsToZero() {
    SentryAndroidOptions options = new SentryAndroidOptions();

    AndroidSentrySdkDriver.applyOptions(
        options,
        configuration("1.0.0", Map.of(), -5));

    assertEquals(0, options.getMaxBreadcrumbs());
  }

  private static SentryLifecycleConfiguration configuration(String release) {
    return configuration(release, Map.of());
  }

  private static SentryLifecycleConfiguration configuration(
      String release,
      Map<String, Object> stableContexts) {
    return configuration(release, stableContexts, 100);
  }

  private static SentryLifecycleConfiguration configuration(
      String release,
      Map<String, Object> stableContexts,
      int maxBreadcrumbs) {
    return new SentryLifecycleConfiguration(
        null,
        "https://public@example.com/1",
        "test",
        release,
        "android",
        Map.of(),
        stableContexts,
        Map.of(),
        true,
        true,
        true,
        3_200L,
        true,
        maxBreadcrumbs);
  }

  private static final class FakeDriver implements SentryLifecycleDriver {
    private boolean enabled;
    private boolean failNextStart;
    private final List<String> operations = new ArrayList<>();
    private final List<Integer> startedMaxBreadcrumbs = new ArrayList<>();

    @Override
    public boolean isEnabled() {
      return enabled;
    }

    @Override
    public boolean start(SentryLifecycleConfiguration configuration) {
      operations.add("start:" + configuration.release);
      startedMaxBreadcrumbs.add(configuration.maxBreadcrumbs);
      if (failNextStart) {
        failNextStart = false;
        enabled = false;
        return false;
      }
      enabled = true;
      return true;
    }

    @Override
    public void flush(long timeoutMsec) {
      operations.add("flush:" + timeoutMsec);
    }

    @Override
    public void close() {
      operations.add("close");
      enabled = false;
    }
  }
}
