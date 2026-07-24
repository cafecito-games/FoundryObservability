# Contributing

FoundryObservability is a FoundryScript addon. Keep the public API typed and
provider-neutral; provider-specific behavior belongs behind explicit addon
boundaries.

The core addon provides the provider-neutral observability API and includes
the FoundryLib `LogSink` adapter. FoundryLib owns the general-purpose logging
framework; the adapter must not make FoundryLib depend on observability.
Provider/native work belongs behind `ObservabilityProvider`.

Before opening a pull request, run:

```sh
task test
```

Keep commits focused. Changes to tracked FoundryScript resources must include
their `.uid` companions. Do not commit generated Foundry state, installed test
packages, or files under `dist/`. Capture and flush failures must remain
non-recursive: the observability core and its sinks must not report their own
provider failures through FoundryLib logging.
