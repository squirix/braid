# Changelog

## Unreleased

## 0.2.1

### Fixed

- Kept package version references consistent across README and release documentation.
- Hardened replay scheduler behavior around arrive/hold/release schedules.
- Improved diagnostics for invalid or incomplete replay schedules.

## 0.2.0

### Added

- Added typed arrive/hold/release replay steps for true interleaving assertions.

## 0.1.0

Stable release of braid.

### Added

* Deterministic explicit-probe concurrency testing for .NET with `Braid.RunAsync`.
* Fork/join orchestration through `BraidContext`.
* Probe control with `BraidProbe.HitAsync`.
* Typed replay schedules through `BraidSchedule` and `BraidStep`.
* Failure reports with seed, iteration, schedule, and trace.

### Known limitations

* Explicit probes are required.
* No automatic `await` interception.
* No `TaskScheduler` replacement.
* No exhaustive state-space search.

## 0.1.0-preview.1

Initial preview of braid.

### Added

* Explicit probe-based concurrency testing with `BraidProbe.HitAsync`.
* Fork/join run model through `Braid.RunAsync` and `BraidContext`.
* Deterministic seed-based scheduling.
* Typed replay schedules through `BraidSchedule` and `BraidStep`.
* Reproducible failure reports with seed, iteration, schedule, and trace.
* Scripted schedule failure handling.
* Cancellation and timeout handling.
* Public API contract tests and scheduler stress smoke tests.

### Known limitations

* Explicit probes are required.
* No automatic `await` interception.
* No `TaskScheduler` replacement.
* No exhaustive state-space search.
* No string schedule parser yet.
* No test-framework-specific output adapters yet.
