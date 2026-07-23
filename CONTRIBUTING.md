# Contributing

FoundryObservability is a FoundryScript addon. Keep the public API typed and
provider-neutral; provider-specific behavior belongs behind explicit addon
boundaries.

Before opening a pull request, run:

```sh
task test
```

Keep commits focused. Changes to tracked FoundryScript resources must include
their `.uid` companions. Do not commit generated Foundry state, installed test
packages, or files under `dist/`.
