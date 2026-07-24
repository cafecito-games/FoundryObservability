# Changelog

## [Unreleased] - 2026-07-23

- Bootstrapped the FoundryScript addon, consumer test project, local validation,
  pull-request CI, and semver release packaging.
- Added the provider-neutral core API with typed events, exceptions,
  configuration, null/memory providers, lifecycle handling, and defensive data
  copying.
- Included the FoundryLib logging sink in the core addon and removed the
  redundant integration addon; FoundryLib remains a required package
  dependency.
- Renamed the public namespace to `foundry.observability` and documented the
  complete API inline and in the reference guide.
- Added the optional `FoundryObservabilitySentry` sibling addon with an iOS
  Foundry-Swift alpha.2 bridge, Sentry Cocoa integration, event mapping, and
  device/simulator xcframework packaging.
