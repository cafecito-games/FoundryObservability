package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.Attachment;
import io.sentry.IScope;
import io.sentry.ScopeType;
import io.sentry.Sentry;
import io.sentry.android.core.SentryAndroid;
import io.sentry.android.core.SentryAndroidOptions;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.Predicate;

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
    replaceAttachments(List.of());
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
    options.setMaxBreadcrumbs(Math.max(0, configuration.maxBreadcrumbs));
    options.setMaxAttachmentSize(configuration.maxAttachmentBytes);
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

  static void applyCaptureScope(
      IScope scope,
      Object runtimeContexts,
      SentryEventMapper.ScopePayload localScope) {
    applyCaptureScope(scope, runtimeContexts, localScope, List.of());
  }

  static void applyCaptureScope(
      IScope scope,
      Object runtimeContexts,
      SentryEventMapper.ScopePayload localScope,
      List<Attachment> attachments) {
    applyContexts(scope, SentryEventMapper.contexts(runtimeContexts));
    SentryEventMapper.applyScope(scope, localScope);
    for (Attachment attachment : attachments) {
      scope.addAttachment(attachment);
    }
  }

  static boolean replaceAttachments(List<Attachment> attachments) {
    IScope scope = Sentry.getGlobalScope();
    try {
      scope.clearAttachments();
      for (Attachment attachment : attachments) {
        scope.addAttachment(attachment);
      }
      return true;
    } catch (RuntimeException exception) {
      return false;
    }
  }

  static boolean replaceFoundryScope(
      SentryEventMapper.ScopePayload candidate,
      Set<String> previousTagKeys,
      Set<String> previousContextKeys) {
    boolean[] applied = {false};
    Sentry.configureScope(ScopeType.COMBINED, scope -> {
      if (hasUnownedCollision(
              candidate.tags.keySet(),
              previousTagKeys,
              scope.getTags()::containsKey)
          || hasUnownedCollision(
              candidate.contexts.keySet(),
              previousContextKeys,
              scope.getContexts()::containsKey)) {
        return;
      }
      for (String key : previousTagKeys) {
        scope.removeTag(key);
      }
      for (String key : previousContextKeys) {
        scope.removeContexts(key);
      }
      scope.setUser(null);
      SentryEventMapper.applyScope(scope, candidate);
      applied[0] = true;
    });
    return applied[0];
  }

  private static boolean hasUnownedCollision(
      Set<String> candidateKeys,
      Set<String> previousKeys,
      Predicate<String> currentContainsKey) {
    for (String key : candidateKeys) {
      if (!previousKeys.contains(key) && currentContainsKey.test(key)) {
        return true;
      }
    }
    return false;
  }

  static boolean clearBreadcrumbs() {
    boolean[] cleared = {false};
    Sentry.configureScope(scope -> {
      scope.clearBreadcrumbs();
      cleared[0] = true;
    });
    return cleared[0];
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
