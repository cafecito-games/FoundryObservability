# Contributing

FoundryObservability is a FoundryScript addon. Keep the public API typed and
provider-neutral; provider-specific behavior belongs behind explicit addon
boundaries.

The core addon provides the provider-neutral observability API and includes
the FoundryLib `LogSink` adapter. FoundryLib owns the general-purpose logging
framework; the adapter must not make FoundryLib depend on observability.
Provider/native work belongs behind `ObservabilityProvider`.

Use `foundry.observability` for core imports and
`foundry.observability.foundrylib` for the FoundryLib adapter. The old
pre-release namespace is not supported.

## Foundry Script modeling

- Use complete `Callable[[ArgumentType], ResultType]` or
  `AsyncCallable[[ArgumentType], ResultType]`
  signatures for functional extension points.
- Use traits for injected dependencies with named operations.
- Use `null` for an absent nullable callback; do not use `Callable()` as a
  sentinel.
- Keep dynamic native calls inside the owning bridge adapter.
- Use `async` only when a function has a real suspension point.
- Match implementation subnamespace suffixes to source directories.

Before opening a pull request, run:

```sh
task test
```

Keep commits focused. Changes to tracked FoundryScript resources must include
their `.uid` companions. Do not commit generated Foundry state, installed test
packages, or files under `dist/`. Capture and flush failures must remain
non-recursive: the observability core and its sinks must not report their own
provider failures through FoundryLib logging.
