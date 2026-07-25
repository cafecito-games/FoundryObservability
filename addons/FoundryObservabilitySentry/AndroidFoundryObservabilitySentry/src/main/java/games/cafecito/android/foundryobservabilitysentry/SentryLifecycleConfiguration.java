package games.cafecito.android.foundryobservabilitysentry;

import android.content.Context;
import java.util.Collections;
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
  final Map<String, Object> providerOptions;
  final boolean logsEnabled;
  final boolean metricsEnabled;
  final boolean anrDetectionEnabled;
  final long anrTimeoutMsec;
  final boolean attachAnrThreadDump;

  SentryLifecycleConfiguration(
      Context applicationContext,
      String dsn,
      String environment,
      String release,
      String dist,
      Map<String, Object> globalAttributes,
      Map<String, Object> providerOptions,
      boolean logsEnabled,
      boolean metricsEnabled,
      boolean anrDetectionEnabled,
      long anrTimeoutMsec,
      boolean attachAnrThreadDump) {
    this.applicationContext = applicationContext;
    this.dsn = dsn;
    this.environment = environment;
    this.release = release;
    this.dist = dist;
    this.globalAttributes = immutableCopy(globalAttributes);
    this.providerOptions = immutableCopy(providerOptions);
    this.logsEnabled = logsEnabled;
    this.metricsEnabled = metricsEnabled;
    this.anrDetectionEnabled = anrDetectionEnabled;
    this.anrTimeoutMsec = anrTimeoutMsec;
    this.attachAnrThreadDump = attachAnrThreadDump;
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
        && providerOptions.equals(other.providerOptions)
        && logsEnabled == other.logsEnabled
        && metricsEnabled == other.metricsEnabled
        && anrDetectionEnabled == other.anrDetectionEnabled
        && anrTimeoutMsec == other.anrTimeoutMsec
        && attachAnrThreadDump == other.attachAnrThreadDump;
  }

  @Override
  public int hashCode() {
    return Objects.hash(
        dsn,
        environment,
        release,
        dist,
        globalAttributes,
        providerOptions,
        logsEnabled,
        metricsEnabled,
        anrDetectionEnabled,
        anrTimeoutMsec,
        attachAnrThreadDump);
  }

  private static Map<String, Object> immutableCopy(Map<String, Object> values) {
    return Collections.unmodifiableMap(new LinkedHashMap<>(values));
  }
}
