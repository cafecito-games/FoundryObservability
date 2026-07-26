package games.cafecito.android.foundryobservabilitysentry;

import android.content.Context;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

final class SentryLifecycleConfiguration {
  final Context applicationContext;
  final String dsn;
  final String environment;
  final String release;
  final String dist;
  final Map<String, Object> globalAttributes;
  final Map<String, Object> stableContexts;
  final Map<String, Object> providerOptions;
  final boolean logsEnabled;
  final boolean metricsEnabled;
  final boolean anrDetectionEnabled;
  final long anrTimeoutMsec;
  final boolean attachAnrThreadDump;
  final int maxBreadcrumbs;

  SentryLifecycleConfiguration(
      Context applicationContext,
      String dsn,
      String environment,
      String release,
      String dist,
      Map<String, Object> globalAttributes,
      Map<String, Object> stableContexts,
      Map<String, Object> providerOptions,
      boolean logsEnabled,
      boolean metricsEnabled,
      boolean anrDetectionEnabled,
      long anrTimeoutMsec,
      boolean attachAnrThreadDump,
      int maxBreadcrumbs) {
    this.applicationContext = applicationContext;
    this.dsn = dsn;
    this.environment = environment;
    this.release = release;
    this.dist = dist;
    this.globalAttributes = immutableCopy(globalAttributes);
    this.stableContexts = immutableStableContexts(stableContexts);
    this.providerOptions = immutableCopy(providerOptions);
    this.logsEnabled = logsEnabled;
    this.metricsEnabled = metricsEnabled;
    this.anrDetectionEnabled = anrDetectionEnabled;
    this.anrTimeoutMsec = anrTimeoutMsec;
    this.attachAnrThreadDump = attachAnrThreadDump;
    this.maxBreadcrumbs = maxBreadcrumbs;
  }

  @Override
  public boolean equals(Object value) {
    if (this == value) {
      return true;
    }
    if (!(value instanceof SentryLifecycleConfiguration)) {
      return false;
    }
    SentryLifecycleConfiguration other = (SentryLifecycleConfiguration) value;
    return dsn.equals(other.dsn)
        && environment.equals(other.environment)
        && release.equals(other.release)
        && dist.equals(other.dist)
        && globalAttributes.equals(other.globalAttributes)
        && stableContexts.equals(other.stableContexts)
        && providerOptions.equals(other.providerOptions)
        && logsEnabled == other.logsEnabled
        && metricsEnabled == other.metricsEnabled
        && anrDetectionEnabled == other.anrDetectionEnabled
        && anrTimeoutMsec == other.anrTimeoutMsec
        && attachAnrThreadDump == other.attachAnrThreadDump
        && maxBreadcrumbs == other.maxBreadcrumbs;
  }

  @Override
  public int hashCode() {
    return Objects.hash(
        dsn,
        environment,
        release,
        dist,
        globalAttributes,
        stableContexts,
        providerOptions,
        logsEnabled,
        metricsEnabled,
        anrDetectionEnabled,
        anrTimeoutMsec,
        attachAnrThreadDump,
        maxBreadcrumbs);
  }

  private static Map<String, Object> immutableCopy(Map<String, Object> values) {
    return Collections.unmodifiableMap(new LinkedHashMap<>(values));
  }

  private static Map<String, Object> immutableStableContexts(
      Map<String, Object> values) {
    Map<String, Object> sanitized = new LinkedHashMap<>();
    sanitized.putAll(SentryEventMapper.contexts(values));
    return immutableMap(sanitized);
  }

  private static Map<String, Object> immutableMap(Map<?, ?> values) {
    Map<String, Object> result = new LinkedHashMap<>();
    for (Map.Entry<?, ?> entry : values.entrySet()) {
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
}
