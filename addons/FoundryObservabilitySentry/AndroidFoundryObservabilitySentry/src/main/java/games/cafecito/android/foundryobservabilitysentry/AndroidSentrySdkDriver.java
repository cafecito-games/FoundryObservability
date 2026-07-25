package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.IScope;
import io.sentry.Sentry;
import io.sentry.android.core.SentryAndroid;
import io.sentry.android.core.SentryAndroidOptions;
import java.util.Map;

final class AndroidSentrySdkDriver implements SentryLifecycleDriver {
  static final long SHUTDOWN_TIMEOUT_MSEC = 2_000L;

  @Override
  public boolean isEnabled() {
    return Sentry.isEnabled();
  }

  @Override
  public boolean start(SentryLifecycleConfiguration configuration) {
    if (configuration.applicationContext == null) {
      return false;
    }
    try {
      SentryAndroid.init(
          configuration.applicationContext,
          options -> applyOptions(options, configuration));
      if (!Sentry.isEnabled()) {
        return false;
      }
      Sentry.configureScope(scope -> {
        scope.setContexts(
            "foundry",
            foundryCrashContext(configuration));
        applyContexts(
            scope,
            SentryEventMapper.contexts(configuration.stableContexts));
      });
      return true;
    } catch (RuntimeException exception) {
      if (Sentry.isEnabled()) {
        Sentry.close();
      }
      return false;
    }
  }

  @Override
  public void flush(long timeoutMsec) {
    Sentry.flush(Math.max(0L, timeoutMsec));
  }

  @Override
  public void close() {
    Sentry.close();
  }

  static void applyOptions(
      SentryAndroidOptions options,
      SentryLifecycleConfiguration configuration) {
    options.setDsn(configuration.dsn);
    options.setEnableUncaughtExceptionHandler(true);
    options.setEnableNdk(true);
    options.setEnableScopeSync(true);
    options.setShutdownTimeoutMillis(SHUTDOWN_TIMEOUT_MSEC);
    options.setSendDefaultPii(booleanValue(
        configuration.providerOptions.get("send_default_pii")));
    options.getLogs().setEnabled(configuration.logsEnabled);
    options.getMetrics().setEnabled(configuration.metricsEnabled);
    options.setDebug(booleanValue(configuration.providerOptions.get("debug")));
    options.setAnrEnabled(configuration.anrDetectionEnabled);
    options.setAnrTimeoutIntervalMillis(configuration.anrTimeoutMsec);
    options.setAttachAnrThreadDump(configuration.attachAnrThreadDump);
    setIfNotEmpty(options::setEnvironment, configuration.environment);
    setIfNotEmpty(options::setRelease, configuration.release);
    setIfNotEmpty(options::setDist, configuration.dist);
  }

  static Map<String, Object> foundryCrashContext(
      SentryLifecycleConfiguration configuration) {
    return Map.of("global_attributes", configuration.globalAttributes);
  }

  static void applyContexts(
      IScope scope,
      Map<String, Map<String, Object>> contexts) {
    for (Map.Entry<String, Map<String, Object>> entry : contexts.entrySet()) {
      scope.setContexts(entry.getKey(), entry.getValue());
    }
  }

  private static void setIfNotEmpty(
      java.util.function.Consumer<String> setter,
      String value) {
    if (value != null && !value.isEmpty()) {
      setter.accept(value);
    }
  }

  private static boolean booleanValue(Object value) {
    return value instanceof Boolean && (Boolean) value;
  }
}
