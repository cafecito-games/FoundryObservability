# Generic Nullable Bridge Removal Design

## Summary

Foundry `v0.1.0-alpha.9` accepts widening a value of generic type parameter
`T` to the nullable form of the same parameter, `T?`. FoundryObservability
currently targets `v0.1.0-alpha.8` in CI and retains a `Variant` bridge plus an
`unsafe_call_argument` suppression in
`ObservabilityNormalizationResult.success()` for the previously rejected
conversion.

This change upgrades the repository's pinned Foundry build, replaces that
bridge with direct statically typed passage, removes every redundant
`unsafe_call_argument` suppression from shipped addon source, and adds
positive and negative analyzer contracts that preserve the exact language
boundary.

## Goals

- Pin PR and release validation to Foundry `v0.1.0-alpha.9`.
- Pass `value: T` directly to the `p_value: T?` normalization-result
  constructor parameter.
- Remove the obsolete compiler-workaround comment, `Variant` local, and
  warning suppression.
- Leave no `unsafe_call_argument` suppression in either shipped addon.
- Retain the one intentional test-only suppression that passes `null` to a
  non-null `ObservabilityEvent` parameter to verify defensive rejection.
- Prove same-parameter `T -> T?` widening on call, initializer, assignment,
  and return surfaces.
- Prove distinct-parameter `T -> U?` passage remains an analyzer error.
- Preserve dynamic dictionary, trait, native, and runtime-narrowing
  boundaries.

## Non-Goals

- Do not change public or internal function parameters from non-null to
  nullable merely to silence a negative test.
- Do not change the factory signatures of
  `ObservabilityRedactionResult.success()` or
  `ObservabilityProcessingResult.accepted()`.
- Do not redesign heterogeneous redaction traversal around typed data-transfer
  objects.
- Do not remove `unsafe_cast`, `unsafe_method_access`, or other warning
  suppressions required at dynamic boundaries.
- Do not change the semantics of invalid-state canonicalization.
- Do not broaden nullable compatibility across different type parameters.

## Existing Suppression Audit

The repository contains twelve `unsafe_call_argument` suppression entries:

- One obsolete same-parameter generic bridge in
  `ObservabilityNormalizationResult`.
- Two standalone entries in `ObservabilityValueWalker`.
- Eight entries combined with `unsafe_cast` in
  `ObservabilityRedactor`.
- One intentional negative-test entry in `observability-core.test.fs`.

An isolated lint audit removed only the `unsafe_call_argument` warning names
while retaining all `unsafe_cast` suppressions. Both Foundry
`v0.1.0-alpha.8` and `v0.1.0-alpha.9` then reported only the known
normalization bridge. The walker entries and the call-warning portions of the
redactor entries are therefore redundant independently of the nullable
compiler fix.

The remaining test entry is materially different. It deliberately passes
`null` to `normalize_event(event: ObservabilityEvent, ...)` to prove the
runtime guard rejects the invalid call before sampling time. Its suppression
documents an intentional static-contract violation in a negative test and
remains.

## Design

### Foundry Version Pin

Update the `FOUNDRY_VERSION` value in both GitHub Actions workflows from
`v0.1.0-alpha.8` to `v0.1.0-alpha.9`. Update
`scripts/test-ci-workflows` to require alpha.9 while preserving its existing
cross-workflow equality check.

Keeping PR and release workflows on the same version prevents validation from
passing against one analyzer and publishing against another.

### Direct Generic Nullable Passage

Replace:

```foundryscript
var nullable_value: Variant = value
@warning_ignore("unsafe_call_argument")
return ObservabilityNormalizationResult[T].new(
        true,
        nullable_value,
        Error.OK,
    )
```

with:

```foundryscript
return ObservabilityNormalizationResult[T].new(true, value, Error.OK)
```

The source and target are the same type-parameter identity, differing only in
nullability. Alpha.9 accepts this statically without a runtime check.

### Suppression Boundary

Shipped addon source will contain no `unsafe_call_argument` suppression:

- Delete the two standalone walker decorators.
- Remove only `"unsafe_call_argument"` from the eight combined redactor
  decorators, leaving `@warning_ignore("unsafe_cast")`.
- Delete the normalization-result decorator with the removed bridge.

The test suite will retain exactly one occurrence at the named
`test_normalizer_rejects_null_event_before_sampling_capture_time` case.

The source-contract script will enforce both sides of this boundary:

- Reject any `unsafe_call_argument` suppression under
  `addons/FoundryObservability` or `addons/FoundryObservabilitySentry`.
- Require exactly one test suppression and require it to remain attached to
  the intentional null-event call.

This is stricter and easier to review than maintaining a growing allowlist in
production code.

### Analyzer Compatibility Fixtures

`scripts/test-foundry-script` will create temporary Foundry Script fixtures
inside the materialized test addon and remove them through the script's
existing cleanup path.

The positive fixture will define a generic holder and exercise:

1. `T` passed to a function or constructor parameter of type `T?`.
2. `var widened: T? = value` for `value: T`.
3. Assignment of `value: T` to a `T?` field.
4. Direct `return value` from a function returning `T?`.

Linting the fixture with `--fail-on=warning` must succeed. The fixture will not
contain a warning suppression or `Variant` bridge.

The negative fixture will define two independent type parameters and assign or
pass `value: T` where `U?` is required. Linting must fail, and the diagnostic
must identify the incompatible `T` and `U?` types as an analyzer error.

Temporary fixtures and diagnostic logs must be removed on success, expected
failure, interruption, and unexpected failure.

### Source Contract for the Normalization Factory

Replace the contract that requires the obsolete compiler-workaround comment
with contracts that:

- Require the direct normalization constructor call using `value`.
- Reject `Variant` bridge locals in the generic factory.
- Reject a local `unsafe_call_argument` suppression.
- Reject comments describing the removed analyzer limitation.

These checks prevent the workaround from being reintroduced even if the
positive analyzer fixture later changes.

## Error Handling

- Failure to lint the positive fixture is a validation failure and prints the
  analyzer output.
- Unexpected acceptance of the distinct-parameter negative fixture is a
  validation failure.
- A negative fixture that fails for an unrelated parse/import problem is also
  a validation failure; its diagnostic must match the expected type
  incompatibility.
- Cleanup must not mask the original validation exit status.
- Existing normalization-result invalid-state canonicalization remains
  unchanged.

## Testing Strategy

Implementation follows red-green-refactor:

1. Update workflow and Foundry Script source contracts plus analyzer fixtures.
2. Run focused contracts against the current source and confirm they fail for
   the old alpha.8 pins, old bridge, and obsolete suppression inventory.
3. Update the workflows and addon sources minimally.
4. Rerun focused contracts and confirm they pass with alpha.9.
5. Run project tests to verify normalization behavior and the intentional
   null-event defensive test.
6. Run UID and package contracts to ensure source and distributable integrity.

Final validation includes:

- `scripts/test-foundry-script`
- `scripts/test-foundry-uids`
- `scripts/test-project`
- `scripts/test-package`
- `scripts/test-ci-workflows`
- `git diff --check`
- The repository's full `task test` gate

## Acceptance Mapping

- No same-parameter `Variant` bridge remains: direct factory implementation
  plus source rejection contract.
- No obsolete suppression or workaround comment remains: zero addon
  `unsafe_call_argument` contract.
- All supported conversion surfaces are exercised: positive analyzer fixture.
- `T -> U?` remains rejected: diagnostic-checked negative analyzer fixture.
- Dynamic boundaries remain intact: only redundant call-warning names are
  removed; required cast and dynamic-access suppressions remain.
- Upgraded validation passes: both workflows pin alpha.9 and all repository
  gates run against the upgraded local/CI analyzer.
