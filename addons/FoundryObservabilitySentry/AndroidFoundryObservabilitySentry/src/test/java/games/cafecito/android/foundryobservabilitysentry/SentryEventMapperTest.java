package games.cafecito.android.foundryobservabilitysentry;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import io.sentry.Scope;
import io.sentry.SentryEvent;
import io.sentry.SentryLevel;
import io.sentry.SentryOptions;
import io.sentry.protocol.SentryException;
import io.sentry.protocol.SentryStackFrame;
import java.lang.reflect.Field;
import java.lang.reflect.Modifier;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TimeZone;
import org.junit.Test;

public class SentryEventMapperTest {
  @Test
  public void mapsFoundryLevels() {
    assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(10));
    assertEquals(SentryLevel.DEBUG, SentryEventMapper.sentryLevel(20));
    assertEquals(SentryLevel.INFO, SentryEventMapper.sentryLevel(30));
    assertEquals(SentryLevel.WARNING, SentryEventMapper.sentryLevel(40));
    assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(50));
    assertEquals(SentryLevel.FATAL, SentryEventMapper.sentryLevel(60));
    assertEquals(SentryLevel.ERROR, SentryEventMapper.sentryLevel(999));
  }

  @Test
  public void convertsUnixMillisecondsWithoutTimezoneDependence() {
    TimeZone original = TimeZone.getDefault();
    try {
      TimeZone.setDefault(TimeZone.getTimeZone("GMT+09:00"));
      Date result = SentryEventMapper.sentryDate(1612325106123L);
      assertEquals(1612325106123L, result.getTime());
    } finally {
      TimeZone.setDefault(original);
    }
  }

  @Test
  public void mergesAttributesAndWritesReservedMetadataLast() {
    Map<String, Object> global = new HashMap<>();
    global.put("shared", "global");
    global.put("build", 42);
    Map<String, Object> event = new HashMap<>();
    event.put("shared", "event");
    event.put("foundry.kind", "caller-value");
    Map<String, Object> exception = new HashMap<>();
    exception.put("type_name", "InvalidState");
    exception.put("message", "boom");
    exception.put("stack_trace", "trace");

    Map<String, Object> extras = SentryEventMapper.mergedExtras(
        global, event, "exception", "game", 1612325106123L, 4567L, exception);

    assertEquals("event", extras.get("shared"));
    assertEquals(42L, extras.get("build"));
    assertEquals("exception", extras.get("foundry.kind"));
    assertEquals("game", extras.get("foundry.source"));
    assertEquals(1612325106123L, extras.get("foundry.timestamp_msec"));
    assertEquals(4567L, extras.get("foundry.engine_ticks_msec"));
    assertEquals("InvalidState", extras.get("foundry.exception_type"));
    assertEquals("trace", extras.get("foundry.stack_trace"));
  }

  @Test
  public void unavailableEngineTicksRemoveCallerControlledReservedMetadata() {
    Map<String, Object> callerAttributes = Map.of("foundry.engine_ticks_msec", 999L);

    Map<String, Object> extras = SentryEventMapper.mergedExtras(
        callerAttributes,
        callerAttributes,
        "message",
        "game",
        1612325106123L,
        -1L,
        null);

    assertFalse(extras.containsKey("foundry.engine_ticks_msec"));
  }

  @Test
  public void buildsMessageAndExceptionEvent() {
    Map<String, Object> payload = new HashMap<>();
    payload.put("kind", "exception");
    payload.put("level", 50);
    payload.put("message", "boom");
    payload.put("source", "game");
    payload.put("timestamp_msec", 1612325106123L);
    payload.put("engine_ticks_msec", 4567L);
    payload.put("attributes", Map.of("screen", "title"));
    payload.put("exception", Map.of(
        "type_name", "InvalidState",
        "message", "boom",
        "stack_trace", "trace",
        "attributes", Map.of()));

    SentryEvent result = SentryEventMapper.makeEvent(payload, Map.of("build", 42));

    assertEquals("boom", result.getMessage().getFormatted());
    assertEquals(SentryLevel.ERROR, result.getLevel());
    assertEquals("game", result.getLogger());
    assertEquals(1612325106123L, result.getTimestamp().getTime());
    assertEquals("title", result.getExtras().get("screen"));
    assertEquals(4567L, result.getExtras().get("foundry.engine_ticks_msec"));
    assertEquals(1, result.getExceptions().size());
    assertEquals("InvalidState", result.getExceptions().get(0).getType());
    assertEquals("boom", result.getExceptions().get(0).getValue());
  }

  @Test
  public void omitsUnsupportedNestedValues() {
    Map<String, Object> input = new HashMap<>();
    input.put("supported", "yes");
    input.put("unsupported", new Object());

    Map<String, Object> result = SentryEventMapper.copyDictionary(input);

    assertEquals("yes", result.get("supported"));
    assertTrue(!result.containsKey("unsupported"));
  }

  @SuppressWarnings("unchecked")
  @Test
  public void sanitizesAndAppliesRuntimeContexts() {
    Map<String, Object> cycle = new HashMap<>();
    cycle.put("self", cycle);
    Map<String, Object> nested = new HashMap<>();
    nested.put("kept", 1);
    nested.put("infinite", Double.POSITIVE_INFINITY);
    Map<String, Object> engine = new HashMap<>();
    engine.put("version", "4.5");
    engine.put("debug_build", true);
    engine.put("nested", nested);
    engine.put("unsupported", new Object());
    engine.put("cycle", cycle);
    Map<Object, Object> input = new HashMap<>();
    input.put("foundry_engine", engine);
    input.put("", Map.of("invalid", true));
    input.put(7, Map.of("invalid", true));
    input.put("empty", Map.of());

    Map<String, Map<String, Object>> contexts = SentryEventMapper.contexts(input);

    assertEquals("4.5", contexts.get("foundry_engine").get("version"));
    assertEquals(true, contexts.get("foundry_engine").get("debug_build"));
    Map<String, Object> sanitizedNested =
        (Map<String, Object>) contexts.get("foundry_engine").get("nested");
    assertEquals(1, sanitizedNested.get("kept"));
    assertFalse(sanitizedNested.containsKey("infinite"));
    assertFalse(contexts.get("foundry_engine").containsKey("unsupported"));
    assertTrue(((Map<?, ?>) contexts.get("foundry_engine").get("cycle")).isEmpty());
    assertFalse(contexts.containsKey(""));
    assertTrue(contexts.containsKey("empty"));

    Scope scope = new Scope(new SentryOptions());
    AndroidSentrySdkDriver.applyContexts(scope, contexts);
    Map<String, Object> applied =
        (Map<String, Object>) scope.getContexts().get("foundry_engine");
    assertEquals("4.5", applied.get("version"));
  }

  @SuppressWarnings("unchecked")
  @Test
  public void scopePayloadIsImmutableAndAppliesTagsContextsAndExplicitUserFields() {
    Map<String, Object> nested = new HashMap<>();
    nested.put("id", 7);
    nested.put("teams", new ArrayList<>(List.of("red", "blue")));
    nested.put("nullable", null);
    nested.put("positions", new ArrayList<>(Arrays.asList("red", null, "blue")));
    Map<String, Object> contexts = new HashMap<>();
    contexts.put("match", nested);
    contexts.put("empty", new HashMap<>());
    Map<String, Object> tags = new HashMap<>();
    tags.put("region", "iad");
    tags.put("invalid", 7);
    Map<String, Object> user = new HashMap<>();
    user.put("id", "player-7");
    user.put("display_name", "Mina");
    user.put("contact_email", "mina@example.com");
    user.put("ip_address", "127.0.0.1");
    Map<String, Object> input = new HashMap<>();
    input.put("tags", tags);
    input.put("contexts", contexts);
    input.put("user", user);

    SentryEventMapper.ScopePayload payload = SentryEventMapper.scopePayload(input);
    tags.put("region", "mutated");
    nested.put("id", 99);
    ((List<Object>) nested.get("teams")).set(0, "mutated");
    user.put("id", "mutated");

    assertEquals(Map.of("region", "iad"), payload.tags);
    assertEquals(7, payload.contexts.get("match").get("id"));
    assertEquals(List.of("red", "blue"), payload.contexts.get("match").get("teams"));
    assertTrue(payload.contexts.get("match").containsKey("nullable"));
    assertNull(payload.contexts.get("match").get("nullable"));
    assertEquals(
        Arrays.asList("red", null, "blue"),
        payload.contexts.get("match").get("positions"));
    assertEquals(Collections.emptyMap(), payload.contexts.get("empty"));
    assertEquals(
        Map.of(
            "id", "player-7",
            "display_name", "Mina",
            "contact_email", "mina@example.com"),
        payload.user);

    Scope scope = new Scope(new SentryOptions());
    SentryEventMapper.applyScope(scope, payload);

    assertEquals("iad", scope.getTags().get("region"));
    assertEquals(7, ((Map<?, ?>) scope.getContexts().get("match")).get("id"));
    assertEquals(Collections.emptyMap(), scope.getContexts().get("empty"));
    assertEquals("player-7", scope.getUser().getId());
    assertEquals("Mina", scope.getUser().getUsername());
    assertEquals("mina@example.com", scope.getUser().getEmail());
    assertNull(scope.getUser().getIpAddress());
    assertNull(scope.getUser().getName());

    expectUnsupported(() -> payload.tags.put("other", "value"));
    expectUnsupported(() -> payload.contexts.get("match").put("other", true));
    expectUnsupported(
        () -> ((List<Object>) payload.contexts.get("match").get("teams")).add("green"));
  }

  @Test
  public void scopePayloadReadOnlyFieldsAreFinal() throws Exception {
    for (String name : List.of("tags", "contexts", "user")) {
      Field field = SentryEventMapper.ScopePayload.class.getDeclaredField(name);
      assertTrue(Modifier.isFinal(field.getModifiers()));
    }
  }

  @SuppressWarnings("unchecked")
  @Test
  public void captureScopeAppliesRuntimeThenLocalOverridesWithoutMutatingGlobalScope() {
    Scope global = new Scope(new SentryOptions());
    global.setTag("region", "global");
    global.setTag("preserved", "yes");
    global.setContexts("match", Map.of("id", 1, "team", "global"));
    global.setContexts("unrelated", Map.of("value", true));
    io.sentry.protocol.User globalUser = new io.sentry.protocol.User();
    globalUser.setId("global-player");
    global.setUser(globalUser);

    io.sentry.IScope captureScope = global.clone();
    SentryEventMapper.ScopePayload local = SentryEventMapper.scopePayload(Map.of(
        "tags", Map.of("region", "local"),
        "contexts", Map.of("match", Map.of()),
        "user", Map.of("id", "local-player")));

    AndroidSentrySdkDriver.applyCaptureScope(
        captureScope,
        Map.of(
            "match", Map.of("id", 2, "team", "runtime"),
            "runtime", Map.of("refreshed", true)),
        local);

    assertEquals("local", captureScope.getTags().get("region"));
    assertEquals("yes", captureScope.getTags().get("preserved"));
    assertEquals(Collections.emptyMap(), captureScope.getContexts().get("match"));
    assertEquals(
        Map.of("refreshed", true),
        captureScope.getContexts().get("runtime"));
    assertEquals(
        Map.of("value", true),
        captureScope.getContexts().get("unrelated"));
    assertEquals("local-player", captureScope.getUser().getId());

    assertEquals("global", global.getTags().get("region"));
    assertEquals(
        Map.of("id", 1, "team", "global"),
        (Map<String, Object>) global.getContexts().get("match"));
    assertFalse(global.getContexts().containsKey("runtime"));
    assertEquals("global-player", global.getUser().getId());

    io.sentry.IScope laterCapture = global.clone();
    assertEquals("global", laterCapture.getTags().get("region"));
    assertEquals("global-player", laterCapture.getUser().getId());
  }

  @Test
  public void mapsMetricPayloadAndScalarAttributes() {
    Map<String, Object> attributes = new HashMap<>();
    attributes.put("string", "value");
    attributes.put("bool", true);
    attributes.put("int", 42);
    attributes.put("long", 43L);
    attributes.put("float", 1.5F);
    attributes.put("double", 2.5D);
    attributes.put("nested", Map.of("unsupported", true));

    SentryEventMapper.MetricPayload result = SentryEventMapper.metricPayload(Map.of(
        "type", 2,
        "name", "request.duration",
        "value", 12.5D,
        "unit", "millisecond",
        "attributes", attributes));

    assertNotNull(result);
    assertEquals(2, result.type);
    assertEquals("request.duration", result.name);
    assertEquals(12.5D, result.value, 0.0D);
    assertEquals("millisecond", result.unit);
    assertEquals("value", result.attributes.get("string"));
    assertEquals(true, result.attributes.get("bool"));
    assertEquals(42L, result.attributes.get("int"));
    assertEquals(43L, result.attributes.get("long"));
    assertEquals(1.5D, (Double) result.attributes.get("float"), 0.0D);
    assertEquals(2.5D, (Double) result.attributes.get("double"), 0.0D);
    assertFalse(result.attributes.containsKey("nested"));
  }

  @Test
  public void rejectsInvalidMetricPayloads() {
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 9, "name", "metric", "value", 1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 0, "name", "counter", "value", -1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 0, "name", "counter", "value", 1.5D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 1, "name", "", "value", 1.0D)));
    assertNull(SentryEventMapper.metricPayload(Map.of(
        "type", 1, "name", "metric", "value", Double.NaN)));
  }

  @Test
  public void mapsStructuredExceptionFramesInProviderOrder() {
    Map<Object, Object> variables = new HashMap<>();
    variables.put("damage", 10);
    variables.put("critical", false);
    variables.put(7, "ignored");
    Map<String, Object> firstFrame = new HashMap<>();
    firstFrame.put("file", "res://Player.fs");
    firstFrame.put("function", "Player.attack");
    firstFrame.put("line", 24L);
    firstFrame.put("language", "fsharp");
    firstFrame.put("in_app", true);
    firstFrame.put("context_line", "let damage = 10");
    firstFrame.put("pre_context", new Object[] {"let weapon = sword", 2, "let target = goblin"});
    firstFrame.put("post_context", Arrays.asList("applyDamage target damage", false));
    firstFrame.put("variables", variables);
    Map<String, Object> secondFrame = new HashMap<>();
    secondFrame.put("file", "res://Combat.fs");
    secondFrame.put("function", "Combat.resolve");
    secondFrame.put("line", new BigDecimal("8"));
    secondFrame.put("language", "fsharp");
    secondFrame.put("in_app", false);

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "legacy formatted stack",
        "frames", new Object[] {firstFrame, secondFrame}));

    assertEquals("legacy formatted stack", result.getExtras().get("foundry.stack_trace"));
    SentryException exception = result.getExceptions().get(0);
    assertEquals("InvalidState", exception.getType());
    assertEquals("bad state", exception.getValue());
    assertNotNull(exception.getStacktrace());
    List<SentryStackFrame> frames = exception.getStacktrace().getFrames();
    assertEquals(2, frames.size());

    SentryStackFrame first = frames.get(0);
    assertEquals("res://Player.fs", first.getFilename());
    assertEquals("Player.attack", first.getFunction());
    assertEquals(Integer.valueOf(24), first.getLineno());
    assertEquals("fsharp", first.getPlatform());
    assertEquals(Boolean.TRUE, first.isInApp());
    assertEquals("let damage = 10", first.getContextLine());
    assertEquals(Arrays.asList("let weapon = sword", "let target = goblin"), first.getPreContext());
    assertEquals(List.of("applyDamage target damage"), first.getPostContext());
    assertEquals(10, first.getVars().get("damage"));
    assertEquals(false, first.getVars().get("critical"));
    assertFalse(first.getVars().containsKey("7"));
    assertFalse(first.getVars() == (Object) variables);

    SentryStackFrame second = frames.get(1);
    assertEquals("res://Combat.fs", second.getFilename());
    assertEquals("Combat.resolve", second.getFunction());
    assertEquals(Integer.valueOf(8), second.getLineno());
    assertEquals("fsharp", second.getPlatform());
    assertEquals(Boolean.FALSE, second.isInApp());
  }

  @Test
  public void keepsFramesOnlyExceptionData() {
    SentryEvent result = eventWithException(Map.of(
        "stack_trace", "legacy formatted stack",
        "frames", List.of(Map.of("file", "res://frames-only.fs"))));

    assertEquals("legacy formatted stack", result.getExtras().get("foundry.stack_trace"));
    assertNotNull(result.getExceptions());
    assertEquals(1, result.getExceptions().size());
    SentryException exception = result.getExceptions().get(0);
    assertNotNull(exception.getStacktrace());
    assertEquals(1, exception.getStacktrace().getFrames().size());
    assertEquals("res://frames-only.fs", exception.getStacktrace().getFrames().get(0).getFilename());
  }

  @Test
  public void skipsMalformedFramesAndKeepsUsefulPartialFrames() {
    List<Object> frames = new ArrayList<>();
    frames.add("not a frame");
    frames.add(Map.of(
        "file", 42,
        "function", false,
        "language", 99,
        "line", -1,
        "in_app", "yes",
        "context_line", "context without identity",
        "pre_context", new Object[] {"valid", 2, false},
        "post_context", Arrays.asList(true, 4),
        "variables", List.of("not", "a map")));
    frames.add(Map.of(
        "file", "",
        "function", "",
        "language", "",
        "line", 0,
        "context_line", "context with empty identity",
        "variables", Map.of("value", 1)));
    frames.add(Map.of("context_line", "context only", "pre_context", List.of("nearby")));
    frames.add(Map.of("variables", Map.of("value", 1)));
    frames.add(Map.of("in_app", false));
    frames.add(Map.of("line", 1.5D));
    frames.add(Map.of("line", new BigDecimal("1.0000000000000000000000000001")));
    frames.add(Map.of("line", new BigDecimal("2147483646.0000000001")));
    frames.add(Map.of("line", Double.NaN));
    frames.add(Map.of("line", Double.POSITIVE_INFINITY));
    frames.add(Map.of("line", Long.MAX_VALUE));
    frames.add(Map.of("line", true));
    frames.add(Map.of("line", "1"));
    frames.add(Map.of(
        "file", "res://partial.fs",
        "function", "",
        "language", "",
        "line", -1,
        "context_line", "",
        "pre_context", new String[] {"discarded"},
        "post_context", new String[] {"also discarded"},
        "variables", Map.of()));
    frames.add(Map.of(
        "file", "res://no-context.fs",
        "context_line", 123,
        "pre_context", new Object[] {"discarded"},
        "post_context", List.of("also discarded")));
    frames.add(Map.of("file", "res://missing-in-app.fs"));
    frames.add(Map.of("file", "res://wrong-in-app.fs", "in_app", 1));

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "legacy formatted stack",
        "frames", frames));

    assertEquals("legacy formatted stack", result.getExtras().get("foundry.stack_trace"));
    List<SentryStackFrame> mapped = result.getExceptions().get(0).getStacktrace().getFrames();
    assertEquals(4, mapped.size());
    SentryStackFrame partial = mapped.get(0);
    assertEquals("res://partial.fs", partial.getFilename());
    assertNull(partial.getFunction());
    assertNull(partial.getLineno());
    assertNull(partial.getPlatform());
    assertEquals(Boolean.TRUE, partial.isInApp());
    assertNull(partial.getContextLine());
    assertNull(partial.getPreContext());
    assertNull(partial.getPostContext());
    assertNull(partial.getVars());

    SentryStackFrame withoutContext = mapped.get(1);
    assertEquals("res://no-context.fs", withoutContext.getFilename());
    assertNull(withoutContext.getContextLine());
    assertNull(withoutContext.getPreContext());
    assertNull(withoutContext.getPostContext());
    assertEquals(Boolean.TRUE, withoutContext.isInApp());

    assertEquals("res://missing-in-app.fs", mapped.get(2).getFilename());
    assertEquals(Boolean.TRUE, mapped.get(2).isInApp());
    assertEquals("res://wrong-in-app.fs", mapped.get(3).getFilename());
    assertEquals(Boolean.TRUE, mapped.get(3).isInApp());
  }

  @Test
  public void sanitizesNestedFrameVariablesWithoutAliasingOrCycles() {
    Map<Object, Object> nested = new HashMap<>();
    nested.put("kept", "nested value");
    nested.put(7, "discarded key");
    nested.put("unsupported", new Object());
    nested.put("nan", Double.NaN);
    List<Object> list = new ArrayList<>();
    list.add("list value");
    list.add(2L);
    list.add(Double.NEGATIVE_INFINITY);
    list.add(new Object());
    Object[] array = new Object[] {"array value", 3, Float.POSITIVE_INFINITY};
    Map<String, Object> cycle = new HashMap<>();
    cycle.put("kept", "cycle value");
    cycle.put("self", cycle);
    Map<Object, Object> variables = new HashMap<>();
    variables.put("nested", nested);
    variables.put("list", list);
    variables.put("array", array);
    variables.put("cycle", cycle);
    variables.put(8, "discarded top-level key");

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "frames", List.of(Map.of("file", "res://variables.fs", "variables", variables))));

    Map<String, Object> sanitized = result.getExceptions().get(0).getStacktrace().getFrames().get(0).getVars();
    assertEquals(Map.of("kept", "nested value"), sanitized.get("nested"));
    assertEquals(Arrays.asList("list value", 2L), sanitized.get("list"));
    assertEquals(Arrays.asList("array value", 3), sanitized.get("array"));
    assertEquals(Map.of("kept", "cycle value"), sanitized.get("cycle"));
    assertFalse(sanitized.containsKey("8"));
    assertFalse(sanitized == (Object) variables);

    nested.put("kept", "mutated nested value");
    list.set(0, "mutated list value");
    array[0] = "mutated array value";
    cycle.put("kept", "mutated cycle value");

    assertEquals(Map.of("kept", "nested value"), sanitized.get("nested"));
    assertEquals(Arrays.asList("list value", 2L), sanitized.get("list"));
    assertEquals(Arrays.asList("array value", 3), sanitized.get("array"));
    assertEquals(Map.of("kept", "cycle value"), sanitized.get("cycle"));
  }

  @SuppressWarnings("unchecked")
  @Test
  public void copiesRepeatedAcyclicVariableContainersIndependently() {
    Map<String, Object> sharedMap = new HashMap<>();
    sharedMap.put("value", "map value");
    List<Object> sharedList = new ArrayList<>(List.of("list value"));
    Object[] sharedArray = new Object[] {"array value"};
    Map<String, Object> variables = new HashMap<>();
    variables.put("map_one", sharedMap);
    variables.put("map_two", sharedMap);
    variables.put("list_one", sharedList);
    variables.put("list_two", sharedList);
    variables.put("array_one", sharedArray);
    variables.put("array_two", sharedArray);

    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "frames", List.of(Map.of("file", "res://shared.fs", "variables", variables))));

    Map<String, Object> sanitized = result.getExceptions().get(0).getStacktrace().getFrames().get(0).getVars();
    Map<String, Object> firstMap = (Map<String, Object>) sanitized.get("map_one");
    Map<String, Object> secondMap = (Map<String, Object>) sanitized.get("map_two");
    List<Object> firstList = (List<Object>) sanitized.get("list_one");
    List<Object> secondList = (List<Object>) sanitized.get("list_two");
    List<Object> firstArray = (List<Object>) sanitized.get("array_one");
    List<Object> secondArray = (List<Object>) sanitized.get("array_two");
    assertEquals(Map.of("value", "map value"), firstMap);
    assertEquals(Map.of("value", "map value"), secondMap);
    assertEquals(List.of("list value"), firstList);
    assertEquals(List.of("list value"), secondList);
    assertEquals(List.of("array value"), firstArray);
    assertEquals(List.of("array value"), secondArray);
    assertFalse(firstMap == secondMap);
    assertFalse(firstList == secondList);
    assertFalse(firstArray == secondArray);

    sharedMap.put("value", "mutated source map");
    sharedList.set(0, "mutated source list");
    sharedArray[0] = "mutated source array";
    assertEquals(Map.of("value", "map value"), firstMap);
    assertEquals(Map.of("value", "map value"), secondMap);
    assertEquals(List.of("list value"), firstList);
    assertEquals(List.of("list value"), secondList);
    assertEquals(List.of("array value"), firstArray);
    assertEquals(List.of("array value"), secondArray);

    firstMap.put("value", "mutated first map");
    firstList.set(0, "mutated first list");
    firstArray.set(0, "mutated first array copy");
    assertEquals(Map.of("value", "map value"), secondMap);
    assertEquals(List.of("list value"), secondList);
    assertEquals(List.of("array value"), secondArray);
  }

  @Test
  public void boundsFrameVariableContainerDepthAndItemCount() {
    Map<String, Object> variables = new HashMap<>();
    for (int index = 0; index < 257; index++) {
      variables.put("item" + index, index);
    }

    SentryEvent itemResult = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "frames", List.of(Map.of("file", "res://items.fs", "variables", variables))));

    assertEquals(256, itemResult.getExceptions().get(0).getStacktrace().getFrames().get(0).getVars().size());

    Map<String, Object> depthVariables = new HashMap<>();
    Map<String, Object> current = depthVariables;
    for (int depth = 0; depth < 9; depth++) {
      Map<String, Object> child = new HashMap<>();
      current.put("child", child);
      current = child;
    }
    current.put("leaf", "discarded");

    SentryEvent depthResult = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "frames", List.of(Map.of("file", "res://depth.fs", "variables", depthVariables))));

    Map<?, ?> sanitized = depthResult.getExceptions().get(0).getStacktrace().getFrames().get(0).getVars();
    for (int depth = 0; depth < 8; depth++) {
      assertTrue(sanitized.get("child") instanceof Map);
      sanitized = (Map<?, ?>) sanitized.get("child");
    }
    assertFalse(sanitized.containsKey("child"));
  }

  @Test
  public void leavesStringOnlyExceptionsWithoutStructuredStacktrace() {
    SentryEvent result = eventWithException(Map.of(
        "type_name", "InvalidState",
        "message", "bad state",
        "stack_trace", "at Player.attack()"));

    assertEquals("at Player.attack()", result.getExtras().get("foundry.stack_trace"));
    assertNull(result.getExceptions().get(0).getStacktrace());
  }

  private static SentryEvent eventWithException(Map<String, Object> exception) {
    Map<String, Object> payload = new HashMap<>();
    payload.put("kind", "exception");
    payload.put("level", 50);
    payload.put("message", "boom");
    payload.put("source", "combat");
    payload.put("timestamp_msec", 1612325106123L);
    payload.put("exception", exception);
    return SentryEventMapper.makeEvent(payload, Map.of());
  }

  private static void expectUnsupported(Runnable mutation) {
    try {
      mutation.run();
    } catch (UnsupportedOperationException expected) {
      return;
    }
    throw new AssertionError("scope payload should be immutable");
  }
}
