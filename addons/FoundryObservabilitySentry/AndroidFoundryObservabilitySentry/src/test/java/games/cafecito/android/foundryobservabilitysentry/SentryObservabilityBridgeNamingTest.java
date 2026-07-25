package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertTrue;

import java.io.BufferedReader;
import java.io.FileReader;
import java.io.IOException;
import java.io.StringWriter;
import org.junit.Test;

public class SentryObservabilityBridgeNamingTest {
  @Test
  public void manifestUsesTheSharedBridgeName() throws IOException {
    StringWriter contents = new StringWriter();
    try (BufferedReader reader = new BufferedReader(
        new FileReader("src/main/AndroidManifest.xml"))) {
      String line;
      while ((line = reader.readLine()) != null) {
        contents.append(line).append('\n');
      }
    }
    String source = contents.toString();

    assertTrue(source.contains("org.foundryengine.plugin.v2.SentryObservabilityBridge"));
    assertTrue(source.contains(
        "games.cafecito.android.foundryobservabilitysentry.SentryObservabilityBridge"));
  }
}
