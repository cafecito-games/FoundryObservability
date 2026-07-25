package games.cafecito.android.foundryobservabilitysentry;

interface SentryLifecycleDriver {
  boolean isEnabled();

  boolean start(SentryLifecycleConfiguration configuration);

  void flush(long timeoutMsec);

  void close();
}

final class SentryLifecycleCoordinator {
  private final SentryLifecycleDriver driver;
  private String activeOwner;
  private SentryLifecycleConfiguration activeConfiguration;

  SentryLifecycleCoordinator(SentryLifecycleDriver driver) {
    this.driver = driver;
  }

  synchronized boolean configure(
      String candidateOwner,
      SentryLifecycleConfiguration candidateConfiguration) {
    if (candidateOwner == null
        || candidateOwner.isEmpty()
        || candidateConfiguration == null
        || candidateConfiguration.dsn.isEmpty()) {
      return false;
    }

    if (candidateConfiguration.equals(activeConfiguration) && driver.isEnabled()) {
      activeOwner = candidateOwner;
      return true;
    }

    String previousOwner = activeOwner;
    SentryLifecycleConfiguration previousConfiguration = activeConfiguration;
    if (driver.isEnabled()) {
      driver.close();
    }
    activeOwner = null;
    activeConfiguration = null;

    if (driver.start(candidateConfiguration)) {
      activeOwner = candidateOwner;
      activeConfiguration = candidateConfiguration;
      return true;
    }

    if (previousOwner != null
        && previousConfiguration != null
        && driver.start(previousConfiguration)) {
      activeOwner = previousOwner;
      activeConfiguration = previousConfiguration;
    }
    return false;
  }

  synchronized boolean isAvailable(String owner) {
    return owner != null
        && !owner.isEmpty()
        && owner.equals(activeOwner)
        && driver.isEnabled();
  }

  synchronized boolean flush(String owner, long timeoutMsec) {
    if (!isAvailable(owner)) {
      return false;
    }
    driver.flush(Math.max(0L, timeoutMsec));
    return true;
  }

  synchronized void shutdown(String owner) {
    if (owner == null || owner.isEmpty() || !owner.equals(activeOwner)) {
      return;
    }
    if (driver.isEnabled()) {
      driver.close();
    }
    activeOwner = null;
    activeConfiguration = null;
  }

  synchronized String activeOwner() {
    return activeOwner;
  }

  synchronized SentryLifecycleConfiguration activeConfiguration() {
    return activeConfiguration;
  }
}
