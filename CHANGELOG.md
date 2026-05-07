# Changelog

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
