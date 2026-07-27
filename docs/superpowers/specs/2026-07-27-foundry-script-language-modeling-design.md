# Foundry Script Language Modeling Design

## Context

FoundryObservability is still under heavy development and has no compatibility
obligation to released consumers. Its current Foundry Script implementation grew
feature by feature and preserves several seams that no longer represent the
domain:

- clocks, process frames, and caller ownership are injected as untyped
  `Callable` values;
- processor and filter callbacks are genuine functional extension points but
  discard Foundry Script's callable signatures;
- the automatic logger retains an unused positional frame-supplier parameter;
- processing, redaction, limiter, provider-session, and attachment outcomes are
  communicated through dictionaries with magic keys;
- Sentry collectors accept `Object` probes and invoke them by string;
- the optional native Sentry bridge is dynamically called throughout the
  provider instead of behind one checked adapter;
- `ObservabilityConfig` has accumulated a large positional constructor and
  unrelated settings;
- bounded structured-value traversal is duplicated across scopes, startup
  options, stack-frame variables, and redaction;
- `FoundryObservability`, `ObservabilityProcessingPipeline`, and
  `ObservabilityRedactor` each coordinate too many responsibilities.

Foundry Script supports traits, generics, typed callables, nullable types, named
enums, namespaces/imports, final declarations, and first-class async functions.
This design applies those features where they strengthen the model. It does not
use language features cosmetically.

The existing Foundry Script contract passes. Local validation prints expected
dynamic-library warnings when the optional Sentry framework cannot find the
local FoundrySwift framework, but Foundry Script analysis reports no
diagnostics.

## Goals

- Replace object-shaped callable and dynamic-object seams with traits.
- Give every genuine callback its complete Foundry Script signature.
- Replace internal dictionary protocols with immutable typed models.
- Decompose configuration into coherent immutable value objects.
- Make runtime-dependent behavior deterministic through one injected runtime.
- Isolate unavoidable dynamic native calls at the cross-language boundary.
- Split oversized coordinators into units with one clear responsibility.
- Organize implementation-only units into focused namespaces and matching
  source directories.
- Preserve all intentional capture, processing, concurrency, and provider
  semantics while allowing a completely breaking source migration.
- Keep structured payload dictionaries where dictionaries are the domain or
  wire representation.

## Non-goals

- Backward-compatible constructors, aliases, overloads, or deprecation shims.
- Changing Apple or Android native payload formats solely for source cleanup.
- Turning every dictionary payload into a class. Attributes, contexts,
  provider options, redaction trees, and bridge payloads are intentionally
  dynamic structured data.
- Making synchronous functions `async` without a real suspension point.
- Moving native work to another thread. Foundry Script `async` is cooperative
  and does not provide implicit thread scheduling.
- Changing capture ordering or making automatic logger callbacks eventually
  consistent.
- Introducing remote configuration or runtime mutation of processor arrays.

## Approved approach

Perform a typed domain refactor across the core and Sentry addons. The public
facade remains synchronous and provider-neutral. Traits model runtime services,
Sentry sources, and the native bridge. Exact typed callables model event, log,
and metric transformations. Generic result classes and named enums replace
dictionary protocols. Configuration becomes an immutable aggregate of focused
sub-configurations.

This is one hard migration. First-party source, tests, documentation, packaging
contracts, and examples move together. Historical design and plan documents
remain historical records and are not rewritten.

## Namespace and source organization

The public root remains:

```text
foundry.observability
```

It contains the facade API, public events and value objects, root
configuration, provider traits, and provider-neutral capability traits.

Implementation-focused namespaces are:

```text
foundry.observability.runtime
foundry.observability.processing
foundry.observability.sentry
foundry.observability.foundrylib
```

The `runtime` and `processing` namespace suffixes have matching directories
under the core addon. The Sentry addon treats its root as
`foundry.observability.sentry`; any deeper namespace suffix must match a deeper
directory. The existing FoundryLib adapter remains in its matching
`foundrylib` directory.

Imports are explicit across namespace boundaries. Public root types do not move
into internal namespaces merely to shorten large files.

## Runtime abstraction

Add a public trait:

```foundryscript
trait_name ObservabilityRuntime

abstract func monotonic_time_msec() -> int
abstract func unix_time_msec() -> int
abstract func process_frame() -> int
abstract func caller_id() -> int
abstract func main_thread_id() -> int
```

`SystemObservabilityRuntime` is the production implementation. It delegates to
`Time`, `Engine`, and `OS` and normalizes Unix time to integer milliseconds.

`FoundryObservability._init()` accepts an optional runtime. A null constructor
argument selects a new `SystemObservabilityRuntime`, which is required because
the engine constructs the autoload without arguments. The facade then passes
that one non-null instance to its provider session, processing pipeline,
automatic logger, and Sentry collaborators that require core runtime facts.
Tests inject a mutable `FakeObservabilityRuntime` that conforms to the trait.

Consequences:

- `_processing_clock`, `_processing_frame`, `_clock`, `_frame`, and `_owner`
  callable fields are removed;
- direct core calls to `Time.get_ticks_msec()`,
  `Time.get_unix_time_from_system()`, `Engine.get_process_frames()`, and
  `OS.get_thread_caller_id()` move into the system runtime;
- `AutomaticObservabilityLogger` accepts only the service, automatic-capture
  configuration, and runtime;
- the unused automatic-logger frame-supplier parameter is deleted;
- invalid `Callable()` defaults and callable return-type checking disappear
  from runtime access.

The runtime is required and non-null after facade composition. Collaborator
constructors reject null instead of silently selecting their own clocks or
ownership sources.

## Typed callable extension points

Processors and filters remain callables because callers are expected to supply
functions or lambdas rather than stateful service objects.

The exact types are:

```foundryscript
Array[Callable[[ObservabilityEvent], ObservabilityEvent?]]
Array[Callable[[ObservabilityMetric], ObservabilityMetric?]]
Callable[[ObservabilityMetric], bool]?
```

Event and log processors use separate arrays of the same callable type. A
processor returns an immutable replacement or `null` to drop the signal. A
metric filter returns `bool`. The absence of a metric filter is `null`, not an
invalid callable sentinel.

All fields, constructor parameters, local snapshots, loops, accessors, and
documentation use the complete callable signatures. There is no bare
first-party `Callable` or `Array[Callable]` in either addon. Unavoidable
reflection APIs that accept or return an engine-level bare callable stay
confined to an adapter and are documented as such.

Configuration validates that every supplied callable is non-null, valid, and
has the required signature. Foundry Script's analyzer provides the primary
shape check; runtime validity checks defend values received through dynamic
construction.

## Named enums

Replace related `StringName` state families with named enums:

- `ObservabilitySignal`: `EVENT`, `LOG`, `METRIC`, `STATE`;
- `ObservabilityProcessingOutcome`: `ACCEPTED`, `DROPPED`, `FAILED`;
- `ObservabilityProcessingReason`: `NONE`, `PROCESSOR`, `SAMPLED`,
  `RATE_LIMITED`, `RECURSIVE`, `INVALID_PROCESSOR_RESULT`,
  `REDACTION_FAILED`, `INVALID_PAYLOAD`, `PROVIDER_REJECTED`, and
  `STALE_GENERATION`;
- `ObservabilityLimitKind`: `NONE`, `PER_FRAME`, `REPEATED`, `WINDOW`, and
  `LEGACY_LOG_WINDOW`.

Public diagnostics expose these enum types. Documentation provides their stable
names for logging or serialization. Payload kind/source/category fields remain
`StringName` because they are open vocabularies, not closed state machines.

## Typed processing models

### ObservabilityAdmissionDecision

An immutable final value containing:

- `accepted: bool`;
- `reason: ObservabilityProcessingReason`;
- `limit_kind: ObservabilityLimitKind`.

`ObservabilitySignalLimiter.admit()` returns this type. Accepted and dropped
factory functions establish valid field combinations. Callers no longer index
`accepted`, `reason`, or `limit_kind` dictionary keys.

### ObservabilityRedactionResult[T]

An immutable generic final value containing:

- `valid: bool`;
- `value: T?`;
- `failed_rule_index: int`;
- `error: int`.

Success requires a non-null value for current public redaction operations.
Failure carries no value, uses `-1` when no rule is responsible, and reports
`Error.ERR_INVALID_DATA`.

Typed redactor entry points return specializations such as:

```foundryscript
ObservabilityRedactionResult[ObservabilityEvent]
ObservabilityRedactionResult[ObservabilityMetric]
ObservabilityRedactionResult[Dictionary]
ObservabilityRedactionResult[ObservabilityUser]
ObservabilityRedactionResult[ObservabilityBreadcrumb]
ObservabilityRedactionResult[ObservabilityAttachment]
```

Internal recursive structured-value traversal uses its own internal result
model because its legitimate value type is `Variant`.

### ObservabilityProcessingResult[T]

An immutable generic final value containing:

- `outcome: ObservabilityProcessingOutcome`;
- `signal: ObservabilitySignal`;
- `value: T?`;
- `operation_token: int`;
- `reason: ObservabilityProcessingReason`;
- `processor_index: int`;
- `redaction_rule_index: int`;
- `limit_kind: ObservabilityLimitKind`;
- `error: int`.

Only an accepted result contains a value and nonnegative operation token.
Dropped and failed results contain no value or provider token. Factory
functions enforce these invariants.

`process_event()` returns
`ObservabilityProcessingResult[ObservabilityEvent]`.
`process_metric()` returns
`ObservabilityProcessingResult[ObservabilityMetric]`.

### ObservabilityProcessingLease[T]

An internal immutable generic value replacing the processing snapshot
dictionary. It contains:

- configuration generation;
- operation token;
- owner ID;
- signal enum;
- typed processor snapshot;
- redactor;
- signal limiter and its mutex;
- shared runtime.

Event/log and metric leases preserve their exact processor types. Lease
creation and release remain centralized in the processing pipeline.

### Provider-session models

`ObservabilityProviderSession` replaces provider-state dictionaries with typed
internal values:

- `ObservabilityProviderSnapshot` pins provider, configuration, generation, and
  enabled state;
- `ObservabilityProviderCall` pins one in-flight provider invocation and its
  generation;
- a typed result records whether beginning a call succeeded and why it did not.

These values do not expose mutable session internals. The session owns the
mutex, provider-call count, configuration-in-progress state, shutdown request,
generation, active provider, and null-provider fallback.

## Configuration decomposition

`ObservabilityConfig` becomes an immutable aggregate. It retains only
cross-cutting provider/deployment data:

- enabled;
- environment;
- release;
- distribution;
- global scalar attributes;
- provider options;
- the focused sub-configurations below.

Every nested configuration is immutable, validates itself, and returns
defensive copies of mutable collections.

### ObservabilityProcessingConfig

Owns:

- log and metric enablement;
- log minimum level;
- event, log, and metric sample rates;
- event, log, and metric typed processor arrays;
- nullable typed metric filter;
- event, log, and metric signal limits;
- redaction policy;
- any legacy fixed log-window behavior that remains intentionally distinct.

### ObservabilityAutomaticCaptureConfig

Owns:

- automatic capture enablement;
- event, breadcrumb, and log masks;
- invisible-message and prefix-filter policy;
- any logger-specific routing settings.

Admission limits do not live here because all event admission is owned by the
processing configuration and applies consistently to automatic and manual
events.

### ObservabilityAttachmentConfig

Owns:

- maximum attachment bytes;
- game-log attachment enablement;
- screenshot attachment enablement;
- scene-tree attachment enablement.

### ObservabilityStackTraceConfig

Owns:

- source-context enablement;
- local-variable enablement;
- bounded source-context and variable settings that are actually configurable.

Hard safety ceilings remain constants in the responsible implementation.

### ObservabilityMobileDiagnosticsConfig

Owns:

- Apple application-hang enablement and timeout;
- Android ANR enablement and timeout;
- Android thread-dump attachment enablement.

### Construction and validation

The root constructor accepts named sub-configuration values. It has no giant
positional parameter list. Call sites construct only the focused configuration
they need and rely on each focused type's documented defaults.

Startup settings parse project data into candidate sub-configurations and
produce a root configuration only after every candidate is valid. Provider
configuration receives an immutable snapshot. Failed validation cannot mutate
the active session.

There are no compatibility fields that duplicate a setting owned by a focused
configuration.

## Facade decomposition

`FoundryObservability` remains the autoload and implementation of
`FoundryObservabilityApi`. It owns:

- public API argument orchestration;
- startup entry points and startup status;
- composition of the runtime, normalizer, pipeline, and provider session;
- automatic logger registration lifecycle;
- translation of collaborator results into `last_error` and public diagnostics.

It delegates the following concerns.

### ObservabilityNormalizer

Owns:

- capture-time resolution from `ObservabilityRuntime`;
- event/log normalization;
- exception and stack-frame normalization;
- feedback validation;
- metric name, unit, value, and attribute validation;
- construction of counter, gauge, and distribution values.

Normalization returns typed values/results and never dispatches providers or
mutates session state.

### ObservabilityProviderSession

Owns:

- active provider and immutable configuration;
- provider configuration transactions;
- generation pinning;
- in-flight provider calls;
- replacement and rollback;
- flush and shutdown coordination;
- null-provider restoration.

No session lock is held while calling provider code.

### ObservabilityProcessingPipeline

Owns:

- redaction;
- typed processor chains;
- sampling and admission;
- recursive-processing protection;
- processing-operation tokens;
- payload-free processing diagnostics.

It does not normalize public API inputs, call providers, or own automatic
logger registration.

### AutomaticObservabilityLogger

Owns only translation from engine logger callbacks into normalized
provider-neutral inputs. It uses the injected runtime and the focused automatic
capture configuration. The shared pipeline remains the sole admission owner.

## Shared bounded structured-value traversal

Introduce an internal processing utility for the mechanics duplicated across
redaction, scopes, startup provider options, and stack-frame variables:

- depth accounting;
- total-item budgets;
- dictionary and array recursion;
- active-container cycle detection;
- defensive reconstruction;
- stable rejection on unsupported values.

Domain-specific policies remain separate. Redaction decides path matching and
replacement. Scope normalization decides its allowed scalar/container types.
Startup provider options enforce their own bounds. Stack-frame variables apply
their privacy and truncation policy.

The traversal utility therefore exposes a typed policy/visitor trait rather
than embedding one universal allowlist. Its visit decision is a typed immutable
value, not a dictionary. Structured leaves remain `Variant` because the
traversed payload is intentionally heterogeneous.

This extraction must preserve every existing safety ceiling and cycle-handling
rule. It is not permission to weaken a stricter caller to the broadest policy.

## Sentry trait boundaries

### SentryRuntimeContextSource

Replace the runtime-context collector's `Object` probe with a trait. It returns
typed stable and volatile runtime snapshots rather than unrelated dictionaries.
Snapshots contain explicit fields for application, engine, device, display,
GPU, runtime, memory/storage, privacy, and orientation data.

`SystemSentryRuntimeContextSource` reads engine APIs. Test fakes conform to the
trait directly. `SentryRuntimeContextCollector` converts snapshots into Sentry
context dictionaries only at the Sentry payload boundary.

### SentryAttachmentSource

Replace the attachment collector's `Object` probe with a trait exposing exact
typed methods:

- main-thread and headless state;
- drawn-frame index;
- main scene tree;
- screenshot PNG bytes;
- game-log path.

`SystemSentryAttachmentSource` is the production implementation. The collector
depends on the trait and never calls methods by string.

### SentryNativeBridge

Add a trait representing the provider operations the Foundry Script Sentry
provider needs: lifecycle/version checks, configuration, availability,
capture, scope, breadcrumbs, feedback, metrics, attachments, flush, and
shutdown.

`DynamicSentryNativeBridgeAdapter` wraps the optional native extension object.
It is the only first-party Foundry Script unit allowed to use `has_method`,
`call`, dynamic property access, or engine-level untyped bridge results for
native integration. It validates and converts every result before returning a
typed trait value.

The provider accepts a `SentryNativeBridge?`. Normal startup constructs the
dynamic adapter after resolving the optional extension. Tests inject fakes that
conform to the trait. Optional native method families are represented by typed
capability/status results in the adapter instead of repeated provider-side
method-name probes.

### SentryAttachmentCollection

Replace attachment collection dictionaries with an immutable value containing:

- `Array[Dictionary]` native attachment payloads;
- `Array[ObservabilityAttachmentFailure]` isolated failures.

Attachment payload dictionaries remain intentional because they are immediate
native wire values. The collection defensively copies both arrays and every
payload dictionary.

## Capture data flow

One event or metric capture follows this sequence:

1. The facade snapshots capture time from `ObservabilityRuntime` once.
2. `ObservabilityNormalizer` validates and constructs the canonical immutable
   value.
3. `ObservabilityProviderSession` returns a pinned provider call or a typed
   no-op/failure.
4. `ObservabilityProcessingPipeline` reserves a typed processing lease.
5. The pipeline performs first-pass redaction.
6. The typed processor chain replaces or drops the value.
7. The pipeline performs second-pass redaction and final validation.
8. The signal limiter returns an `ObservabilityAdmissionDecision`.
9. An accepted processing result carries the immutable value and operation
   token to the facade.
10. The facade dispatches through the pinned provider or optional provider
    capability.
11. The pipeline records the provider result only for the matching token and
    generation.
12. The provider call and processing lease are released through one balanced
    path each.

State operations such as contexts, users, breadcrumbs, and attachments use
typed redaction results and provider calls but do not pretend their distinct
return values are event-processing results.

## Concurrency and reentrancy

The current guarantees remain requirements:

- no state mutex is held while invoking a user processor, provider, or native
  bridge;
- configuration is prepared and validated before commit;
- provider replacement commits one generation atomically;
- failed replacement preserves the active generation;
- a capture pins its provider and configuration generation;
- a stale processing or provider result cannot update a newer generation;
- processing recursion is keyed by `ObservabilityRuntime.caller_id()`;
- automatic logger recursion remains distinct from ordinary processor
  recursion;
- shutdown requests made during provider/configuration calls complete after
  the in-flight call exits;
- shutdown remains idempotent;
- pending provider-result tokens remain bounded.

Typed leases make ownership explicit but do not introduce destructors or rely
on garbage collection for release. Every begin operation has one structured
finish path, and tests cover early returns.

## Error and diagnostic semantics

Expected filtering, processor-requested drops, sampling, and rate limiting are
successful no-ops. They update the processing diagnostic but do not turn
`last_error` into a provider failure.

Invalid public input, invalid processor results, redaction failures, stale
generations, configuration failures, and provider failures carry explicit
engine `Error` values. The facade remains responsible for mapping collaborator
results into `last_error`.

`ObservabilityProcessingDiagnostic` becomes an immutable typed snapshot using
the signal, outcome, reason, and limit enums. It remains payload-free and does
not retain messages, attributes, user data, filenames, processor identities,
or redaction patterns.

Foundry Script callable failures are not catchable exceptions. A failed
processor therefore remains fail-closed. Its typed result path records
`INVALID_PROCESSOR_RESULT` without attempting to log the sensitive payload.

## Async decision

No existing first-party operation has a Foundry Script suspension point:

- engine logger callbacks are synchronous;
- capture ordering is synchronous;
- native bridge capture and flush methods are synchronous;
- startup must finish before later autoloads capture;
- `_exit_tree()` cannot rely on an unobserved coroutine completing.

Consequently, capture, configuration, flush, shutdown, startup, attachment
collection, and provider traits remain synchronous. Marking them `async` would
change return types and ordering without moving work off-thread.

When a provider later has a real awaitable operation, it should be introduced
as a separate async capability trait with `abstract async func`, an explicit
facade method that callers must `await`, and an `AsyncCallable` only if the
extension point is genuinely functional. That future capability is outside
this refactor.

## Hard migration

The migration deletes rather than deprecates:

- untyped runtime callables;
- invalid callable sentinels;
- the automatic logger's unused frame-supplier parameter;
- giant positional configuration construction;
- dynamic Sentry probe calls;
- provider-side native bridge method probing;
- dictionary-key processing/admission/redaction/session protocols;
- duplicate compatibility fields and helper paths.

All first-party call sites use named arguments for constructors that still have
multiple same-typed parameters. Examples and tests demonstrate the intended
new API rather than compatibility syntax.

Every new Foundry Script source receives a stable UID. Moves preserve existing
UIDs where the logical resource remains the same. Packaging and copy scripts
include the reorganized directories.

## Testing strategy

Implementation proceeds test-first in vertical slices.

### Runtime tests

- system runtime maps each engine source to the documented unit;
- fake runtime deterministically controls Unix time, monotonic time, frame,
  caller, and main-thread identity;
- facade, pipeline, and automatic logger share the injected runtime;
- no core runtime source bypasses the trait.

### Callable tests

- exact event/log and metric processor lambdas compile and execute;
- nullable metric filters use `null`;
- wrong parameter or return types are rejected by Foundry Script analysis
  fixtures or source contracts;
- processor arrays retain order and defensive isolation;
- no first-party addon source contains a bare callable declaration.

### Typed model tests

- accepted, dropped, and failed factories establish valid invariants;
- model fields cannot be reassigned;
- mutable payloads and collections are defensively copied;
- limiter, redactor, pipeline, and provider session no longer require magic
  dictionary keys;
- named enums distinguish closed state families.

### Configuration tests

- every focused configuration has documented defaults;
- invalid focused candidates fail before root construction/commit;
- nested mutable data is isolated;
- root configuration composes focused values without duplicate ownership;
- startup settings produce the same effective defaults through the new model;
- no positional compatibility constructor remains.

### Processing and session regression tests

Port and retain coverage for:

- event/log/metric processor ordering, replacement, and drop;
- two-pass redaction;
- deterministic sampling;
- per-frame, repeated, sliding-window, and legacy log limits;
- recursive calls and recursive processing failures;
- invalid processor results;
- provider rejection diagnostics;
- pending-token bounds;
- stale configuration generations;
- reconfiguration from processors/providers;
- shutdown requested during configuration or capture;
- balanced release on every early return;
- automatic capture routing and recursion.

Assertions use typed accessors and enums rather than dictionary keys and string
constants.

### Sentry tests

- system sources map engine state to typed snapshots;
- collectors consume source traits without dynamic calls;
- the dynamic bridge adapter validates every optional method/result family;
- the provider consumes only the bridge trait;
- native payload dictionaries remain byte-for-byte/schema-compatible where
  behavior is unchanged;
- attachment collections isolate payloads and failures;
- missing optional native integrations fail closed as before.

### Structured-value traversal tests

- depth, item, and active-container bounds are enforced;
- cycles fail closed or truncate exactly according to each caller's existing
  contract;
- each domain policy retains its specific allowed-value rules;
- defensive copies do not alias caller containers;
- redaction paths and rule indices remain stable.

### Source and packaging contracts

Add checks for:

- no bare `Callable` or `Array[Callable]` in first-party addon source;
- no `Callable()` sentinel configuration;
- no unused compatibility constructor parameters;
- no dynamic probe calls;
- `Object.call()`/`has_method()` for the native bridge appears only in the
  adapter;
- namespace suffixes match implementation directories;
- required imports are present;
- all new sources have stable UIDs;
- reorganized source is included in packaged addons;
- public documentation contains the exact new signatures.

## Verification

Fresh completion evidence must include:

- the focused Foundry Script tests for every TDD slice;
- the full Foundry Script analysis and consumer test suite;
- source-contract checks;
- UID validation;
- package validation;
- FoundryLib adapter tests;
- Sentry Foundry Script tests;
- available Apple and Android bridge contract/unit suites;
- documentation/source signature checks;
- `git diff --check`.

Optional native-framework load warnings are acceptable only when the command
still reports zero Foundry Script diagnostics and exits successfully. Any
unexpected analyzer diagnostic, warning promoted by the project, test failure,
or packaging omission blocks completion.

## Documentation

Update:

- `README.md` quick starts and configuration examples;
- `docs/API.md` namespace imports, traits, exact callable signatures, focused
  configurations, typed results, and migration examples;
- `BUILD.md` if validation commands or source layout change;
- `CONTRIBUTING.md` with the first-party typed-callable and dynamic-boundary
  rules;
- `CHANGELOG.md` with the intentional breaking refactor;
- source documentation comments for every public trait, enum, configuration,
  result, and facade method.

Documentation must explicitly state why ingestion remains synchronous and when
an async capability would be appropriate.

## Acceptance criteria

- Core time, frame, and caller behavior is supplied through
  `ObservabilityRuntime`.
- Automatic logger construction has no unused or compatibility-only parameter.
- Every first-party functional extension point has a complete callable
  signature and uses `null` for absence.
- Processing, redaction, admission, provider-session, and attachment collection
  outcomes use typed immutable models.
- Closed outcome/reason/signal/limit families use named enums.
- The root configuration aggregates focused immutable configurations and has
  no giant positional constructor.
- Sentry runtime and attachment collectors depend on traits.
- Dynamic native Sentry calls are isolated in one adapter.
- Oversized facade/pipeline/redactor responsibilities are decomposed as
  specified.
- Shared structured-value traversal removes duplicated mechanics without
  weakening caller-specific policies.
- No synchronous API is marked `async` without a suspension point.
- Tests and documentation use only the new API.
- Full analysis, test, UID, packaging, and available native validation commands
  pass.
