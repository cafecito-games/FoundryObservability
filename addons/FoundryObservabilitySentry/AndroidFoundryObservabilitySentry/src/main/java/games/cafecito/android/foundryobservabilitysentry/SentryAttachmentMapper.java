package games.cafecito.android.foundryobservabilitysentry;

import io.sentry.Attachment;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;

final class SentryAttachmentMapper {
  private static final String EVENT_ATTACHMENT = "event.attachment";
  private static final String VIEW_HIERARCHY = "event.view_hierarchy";

  private SentryAttachmentMapper() {}

  static Attachment map(Map<String, Object> payload) {
    if (payload == null || payload.isEmpty()) {
      return null;
    }

    Object filenameValue = payload.get("filename");
    if (!(filenameValue instanceof String) || ((String) filenameValue).isEmpty()) {
      return null;
    }
    String filename = (String) filenameValue;

    Object categoryValue = payload.get("category");
    if (!(categoryValue instanceof String)) {
      return null;
    }
    String category = (String) categoryValue;
    if (!EVENT_ATTACHMENT.equals(category) && !VIEW_HIERARCHY.equals(category)) {
      return null;
    }

    String contentType = null;
    if (payload.containsKey("content_type")) {
      Object contentTypeValue = payload.get("content_type");
      if (!(contentTypeValue instanceof String)) {
        return null;
      }
      String candidate = (String) contentTypeValue;
      contentType = candidate.isEmpty() ? null : candidate;
    }

    boolean hasPath = payload.containsKey("path");
    boolean hasBytes = payload.containsKey("bytes");
    if (hasPath == hasBytes) {
      return null;
    }

    if (hasPath) {
      Object pathValue = payload.get("path");
      if (!(pathValue instanceof String)
          || ((String) pathValue).isEmpty()
          || !((String) pathValue).startsWith("/")) {
        return null;
      }
      return new Attachment(
          (String) pathValue,
          filename,
          contentType,
          category,
          false);
    }

    Object bytesValue = payload.get("bytes");
    if (!(bytesValue instanceof byte[])) {
      return null;
    }
    byte[] source = (byte[]) bytesValue;
    return new Attachment(
        Arrays.copyOf(source, source.length),
        filename,
        contentType,
        category,
        false);
  }

  static List<Attachment> mapAll(Object payloads) {
    Object[] values;
    if (payloads instanceof Object[]) {
      values = (Object[]) payloads;
    } else if (payloads instanceof List<?>) {
      values = ((List<?>) payloads).toArray();
    } else {
      return null;
    }

    List<Attachment> attachments = new ArrayList<>(values.length);
    for (Object value : values) {
      if (!(value instanceof Map<?, ?>)) {
        return null;
      }
      Attachment attachment = map(stringKeyedPayload((Map<?, ?>) value));
      if (attachment == null) {
        return null;
      }
      attachments.add(attachment);
    }
    return attachments;
  }

  private static Map<String, Object> stringKeyedPayload(Map<?, ?> payload) {
    for (Object key : payload.keySet()) {
      if (!(key instanceof String)) {
        return null;
      }
    }
    @SuppressWarnings("unchecked")
    Map<String, Object> stringKeyed = (Map<String, Object>) payload;
    return stringKeyed;
  }
}
