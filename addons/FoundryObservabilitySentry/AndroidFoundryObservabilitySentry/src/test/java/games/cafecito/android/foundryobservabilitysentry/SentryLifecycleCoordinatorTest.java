package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import io.sentry.android.core.SentryAndroidOptions;
import java.util.ArrayList;
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
        Map.of(),
        true,
        true,
        true,
        6_400L,
        true);
    SentryAndroidOptions options = new SentryAndroidOptions();

    AndroidSentrySdkDriver.applyOptions(options, configuration);

    assertTrue(options.isEnableUncaughtExceptionHandler());
    assertTrue(options.isEnableNdk());
    assertTrue(options.isEnableScopeSync());
    assertEquals(2_000L, options.getShutdownTimeoutMillis());
    assertEquals("game@1.2.3", options.getRelease());
    assertEquals("qa", options.getEnvironment());
    assertEquals("android", options.getDist());
    assertEquals(
        Map.of("global_attributes", Map.of("build", 42)),
        AndroidSentrySdkDriver.foundryCrashContext(configuration));
  }

  private static SentryLifecycleConfiguration configuration(String release) {
    return new SentryLifecycleConfiguration(
        null,
        "https://public@example.com/1",
        "test",
        release,
        "android",
        Map.of(),
        Map.of(),
        true,
        true,
        true,
        3_200L,
        true);
  }

  private static final class FakeDriver implements SentryLifecycleDriver {
    private boolean enabled;
    private boolean failNextStart;
    private final List<String> operations = new ArrayList<>();

    @Override
    public boolean isEnabled() {
      return enabled;
    }

    @Override
    public boolean start(SentryLifecycleConfiguration configuration) {
      operations.add("start:" + configuration.release);
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
