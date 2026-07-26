package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotSame;
import static org.junit.Assert.assertNull;

import io.sentry.Attachment;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.Test;

public class SentryAttachmentMapperTest {
  @Test
  public void mapsAbsoluteFileAttachmentAndPreservesMetadata() {
    Attachment attachment = SentryAttachmentMapper.map(
        Map.of(
            "filename", "game.log",
            "content_type", "text/plain",
            "category", "event.attachment",
            "path", "/tmp/game.log"));

    assertEquals("/tmp/game.log", attachment.getPathname());
    assertNull(attachment.getBytes());
    assertEquals("game.log", attachment.getFilename());
    assertEquals("text/plain", attachment.getContentType());
    assertEquals("event.attachment", attachment.getAttachmentType());
  }

  @Test
  public void mapsByteAttachmentAndCopiesSourceBytes() {
    byte[] source = {1, 2, 3};
    Attachment attachment = SentryAttachmentMapper.map(
        Map.of(
            "filename", "view.json",
            "content_type", "application/json",
            "category", "event.view_hierarchy",
            "bytes", source));

    source[0] = 9;

    assertNull(attachment.getPathname());
    assertNotSame(source, attachment.getBytes());
    assertArrayEquals(new byte[] {1, 2, 3}, attachment.getBytes());
    assertEquals("view.json", attachment.getFilename());
    assertEquals("application/json", attachment.getContentType());
    assertEquals("event.view_hierarchy", attachment.getAttachmentType());
  }

  @Test
  public void emptyContentTypeNormalizesToNull() {
    Attachment attachment = SentryAttachmentMapper.map(
        Map.of(
            "filename", "empty-type.bin",
            "content_type", "",
            "category", "event.attachment",
            "bytes", new byte[] {1}));

    assertNull(attachment.getContentType());
  }

  @Test
  public void preservesEveryNonemptyFilenameWithoutAdditionalPolicy() {
    for (String filename : List.of(".", "..", "dir/file.log", "dir\\file.log", " ")) {
      Attachment attachment = SentryAttachmentMapper.map(
          Map.of(
              "filename", filename,
              "category", "event.attachment",
              "bytes", new byte[] {1}));

      assertEquals(filename, attachment.getFilename());
    }
  }

  @Test
  public void rejectsMalformedAttachmentPayloads() {
    List<Map<String, Object>> malformed = List.of(
        Map.of(),
        Map.of("filename", "", "category", "event.attachment", "path", "/tmp/a"),
        Map.of("filename", "a", "category", "", "path", "/tmp/a"),
        Map.of("filename", "a", "category", "other", "path", "/tmp/a"),
        Map.of("filename", "a", "category", "event.attachment", "path", "relative/a"),
        Map.of("filename", "a", "category", "event.attachment", "path", ""),
        Map.of(
            "filename", "a",
            "category", "event.attachment",
            "path", "/tmp/a",
            "bytes", new byte[] {1}),
        Map.of("filename", "a", "category", "event.attachment", "bytes", new Object[] {1, 2}),
        Map.of("filename", "a", "category", "event.attachment"),
        Map.of(
            "filename", "a",
            "category", "event.attachment",
            "path", "/tmp/a",
            "content_type", 12),
        Map.of("filename", 12, "category", "event.attachment", "path", "/tmp/a"),
        Map.of("filename", "a", "category", 12, "path", "/tmp/a"));
    Map<String, Object> nullContentType = new LinkedHashMap<>();
    nullContentType.put("filename", "a");
    nullContentType.put("category", "event.attachment");
    nullContentType.put("path", "/tmp/a");
    nullContentType.put("content_type", null);

    for (Map<String, Object> payload : malformed) {
      assertNull("Accepted " + payload, SentryAttachmentMapper.map(payload));
    }
    assertNull(SentryAttachmentMapper.map(nullContentType));
    assertNull(SentryAttachmentMapper.map(null));
  }
}
