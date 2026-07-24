package games.cafecito.android.foundryobservabilitysentry;

import games.cafecito.foundry.Dictionary;
import games.cafecito.foundry.Foundry;
import games.cafecito.foundry.plugin.FoundryPlugin;
import games.cafecito.foundry.plugin.UsedByFoundry;
import io.sentry.Sentry;
import io.sentry.SentryEvent;
import io.sentry.android.core.SentryAndroid;
import io.sentry.android.core.SentryAndroidOptions;
import java.util.Collections;
import java.util.Map;

public final class SentryObservabilityBridge extends FoundryPlugin {
  static final int BRIDGE_ERROR_OK = 0;
  static final int BRIDGE_ERROR_FAILED = 1;

  private Map<String, Object> globalAttributes = Collections.emptyMap();
  private boolean configured;
  private boolean didShutdown;

  public SentryObservabilityBridge(Foundry foundry) {
    super(foundry);
  }

  @Override
  public String getPluginName() {
    return "SentryObservabilityBridge";
  }

  @UsedByFoundry
  public int configure(Dictionary payload) {
    closeActiveClient();
    configured = false;
    didShutdown = false;

    if (payload == null) {
      return BRIDGE_ERROR_FAILED;
    }

    Object globalAttributesValue = payload.get("global_attributes");
    globalAttributes = globalAttributesValue instanceof Map
        ? SentryEventMapper.copyDictionary((Map<?, ?>) globalAttributesValue)
        : Collections.emptyMap();

    if (!booleanValue(payload.get("enabled"))) {
      return BRIDGE_ERROR_OK;
    }

    String dsn = stringValue(payload.get("dsn"));
    if (dsn.isEmpty()) {
      return BRIDGE_ERROR_FAILED;
    }

    try {
      Map<?, ?> providerOptions = payload.get("provider_options") instanceof Map
          ? (Map<?, ?>) payload.get("provider_options")
          : Collections.emptyMap();
      SentryAndroid.init(
          getContext().getApplicationContext(),
          (SentryAndroidOptions options) -> {
            options.setDsn(dsn);
            options.setDebug(booleanValue(providerOptions.get("debug")));
            setIfNotEmpty(options::setEnvironment, payload.get("environment"));
            setIfNotEmpty(options::setRelease, payload.get("release"));
            setIfNotEmpty(options::setDist, payload.get("dist"));
          });
      configured = Sentry.isEnabled();
      return configured ? BRIDGE_ERROR_OK : BRIDGE_ERROR_FAILED;
    } catch (RuntimeException exception) {
      configured = false;
      return BRIDGE_ERROR_FAILED;
    }
  }

  @UsedByFoundry
  public boolean isAvailable() {
    return configured && !didShutdown && Sentry.isEnabled();
  }

  @UsedByFoundry
  public String capture(Dictionary payload) {
    if (!isAvailable()) {
      return "";
    }
    SentryEvent event = SentryEventMapper.makeEvent(payload, globalAttributes);
    return Sentry.captureEvent(event).toString();
  }

  @UsedByFoundry
  public int flush(int timeoutMsec) {
    if (!isAvailable()) {
      return BRIDGE_ERROR_FAILED;
    }
    Sentry.flush(Math.max(0, timeoutMsec));
    return BRIDGE_ERROR_OK;
  }

  @UsedByFoundry
  public void shutdown() {
    if (didShutdown) {
      return;
    }
    didShutdown = true;
    closeActiveClient();
  }

  private void closeActiveClient() {
    if (configured) {
      Sentry.close();
    }
    configured = false;
  }

  private static void setIfNotEmpty(
      java.util.function.Consumer<String> setter,
      Object value) {
    String string = stringValue(value);
    if (!string.isEmpty()) {
      setter.accept(string);
    }
  }

  private static boolean booleanValue(Object value) {
    return value instanceof Boolean && (Boolean) value;
  }

  private static String stringValue(Object value) {
    return value instanceof String ? (String) value : "";
  }
}
