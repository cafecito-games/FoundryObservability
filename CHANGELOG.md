# Changelog

## [Unreleased] - 2026-07-24

- Added default-enabled provider-neutral Apple app-hang and Android ANR
  diagnostics with configurable timeouts, optional Android thread-dump
  attachment, native severity and metadata preservation, and documented device
  validation.
- Added provider-neutral structured exception frames and bounded source context,
  with source context enabled and local variables disabled unless explicitly
  opted in. Variable forwarding uses bounded, type-filtered copies.
- Mapped structured exception frames to native Sentry Cocoa and Sentry Android
  stack types while retaining the formatted `foundry.stack_trace` extra as a
  compatible fallback.
- Removed the incompatible gdtoolkit Python/requirements dependency and the
  Python build prerequisite.
- Corrected event timestamps to Unix epoch milliseconds across the core and
  Apple/Android Sentry bridges, preserved monotonic engine ticks separately,
  and made missing timestamps resolve once to capture time.
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
- Pinned Sentry Cocoa to `9.23.0`.
- Added first-class structured log delivery with default-enabled capture,
  severity filtering, timestamp-window rate limiting, global/per-record scalar
  attributes, and native Apple/Android Sentry log bridges.
- Added explicit player feedback capture with message validation, optional
  identity and event association, and dedicated Apple/Android Sentry delivery.
- Added provider-neutral counters, gauges, and distributions with validation,
  global/per-metric scalar attributes, filtering, deterministic sampling, safe
  optional provider capability detection, and native Apple/Android Sentry
  delivery.
- Added automatic engine diagnostic and output capture with independent event,
  breadcrumb, and structured-log masks; preserved source/backtrace metadata;
  deterministic duplicate, per-frame, and sliding-window limits; recursion
  protection; and native Apple/Android Sentry breadcrumb delivery.
