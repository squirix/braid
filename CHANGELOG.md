# Changelog

## Unreleased

### Added

- `RunOptions.MaxTimeout`, the longest supported run timeout (about 49.7 days).
- Start steps: `ReplayStep.Start(workerId)`, `ReplayStepKind.Start` and the replay text line `start <worker>` start a worker, which then runs
  up to its first probe. A schedule without start steps starts every worker before its first step, in fork order, as before.
- `RunFailureOrigin.Timeout` for runs that timed out while no worker was parked at a probe.
- `SchedulerDiagnostics.RunningWorkers` and a **Running workers** section in failure reports, with the probe each running worker was last released at.
- `ProbeWaitDiagnostic.StartProbeName` and `NotStartedProbeName` mark workers before their first probe; waiting workers that have not started are now listed.

### Changed

- Breaking: a run that times out reports `RunFailureOrigin.Timeout` instead of `RunFailureOrigin.Scheduler`, unless a worker was running
  while others were parked: then it keeps `RunFailureOrigin.Scheduler` and says that the running worker may be waiting for a parked one.
- Breaking: forking a second worker with an id already used in the run throws `ArgumentException` from `Fork` / `WorkerAsync`;
  uncaught, `RunAsync` and `ExploreAsync` report it as `RunException` with that `ArgumentException` as the inner exception.
  Duplicate ids used to be accepted, and exploration merged their probe sequences and missed interleavings.
- Breaking: `JoinAsync` called from a forked worker throws `InvalidOperationException` at once; uncaught, `RunAsync` and `ExploreAsync`
  report it as `RunException` with `RunFailureOrigin.UserTest` and that inner exception. It used to hang until the run timed out.
- Breaking: the run callback of the first iteration, and of the `ExploreAsync` discovery run, starts on the thread pool like every later one:
  not inline in the call, and without the caller's synchronization context or task scheduler. `RunAsync` and `ExploreAsync` can return
  before the callback starts.
  A caller that blocks the only thread of its synchronization context while waiting for the run no longer hangs it.
- Breaking: the start of a worker is a scheduling point. A random run chooses the order in which workers start, so code before the first probes
  is no longer always run in fork order, and one seed gives a different run than before. `ExploreAsync` tries the orders of the starts too,
  also for workers that hit no probe: it generates more schedules, and its replay tokens begin with `start` lines. The schedules that start
  every worker first come first, so other start orders need a `MaxSchedules` above the number of hit orders. A worker that waits, before its
  first probe, for another worker's code before its first probe now times out when it starts first.
- Breaking: canceling the token passed to `Probe.HitAsync` no longer wakes a worker parked at the probe. The worker observes the cancellation
  when the scheduler releases it, and the probe then throws `OperationCanceledException`. A canceled wait used to let the worker run at the same
  time as the released one, so one seed or replay token gave different runs, and a retry with a canceled token could fail the run with
  `SemaphoreFullException`. A worker that cancels the token and waits for the parked worker before its own next probe now times out.
- Breaking: `ReplaySchedule.Replay` rejects an empty step list with `ArgumentException`. A run with an empty schedule used to pass
  until a worker hit a probe, then fail with "Scripted schedule was exhausted" while its diagnostics said no schedule was configured.
  Leave `RunOptions.Schedule` unset for a random run.
- Breaking: `RunOptions.Timeout` and `ExploreOptionsBuilder.WithTimeout` count from the start of the run callback instead of from `JoinAsync`,
  so slow setup before the join now counts toward the timeout.

### Fixed

- A timeout above `RunOptions.MaxTimeout` is rejected with `ArgumentOutOfRangeException` when options are validated. It used to fail the run as a user test failure.
- `RunException.ToString()` includes the inner exception's stack trace and nested inner exceptions, and the report's own stack trace.
- `ExploreAsync` no longer passes a test that hangs with no running worker blocked on a parked one:
  such a timeout in the discovery run or in a generated schedule fails exploration, with a replay token for a generated schedule.
- `ExploreAsync` no longer skips generated schedules that end before the test does, including schedules cut by `MaxStepsPerSchedule`:
  after the last step, waiting workers are released in fork order, so the test can run to completion, and the replay token includes the completion steps.
- `ExploreAsync` no longer passes after the discovery run failed: when no generated schedule reproduces the failure, the discovery failure is thrown.
- A worker still running after a failed run stopped waiting for it no longer gets `ObjectDisposedException` from its next probe:
  the probe throws `OperationCanceledException`, and the failure message names the abandoned workers.
- A callback that waits before `JoinAsync`, for example for a forked worker (workers start only when the run joins), no longer hangs:
  the run timeout fails it with `RunFailureOrigin.Timeout`, and canceling the run token ends it with `OperationCanceledException`.
- `ExploreAsync` no longer passes when the test misuses the API, for example forks after `JoinAsync` or hits a second probe on a worker
  whose probe wait is still in flight: it reports the same `RunException` as `RunAsync`. Only failures caused by the schedule are skipped:
  schedule mismatches and parked-worker timeouts. A `RunException` that test code creates and throws now stops exploration too.
- A failed `JoinAsync` is final: a later join in the same run throws the same exception. When the callback catches the failure of its join,
  the run reports that failure. It used to report the `OperationCanceledException` that braid itself raised in a parked worker while stopping
  the run, or a timeout that elapsed afterwards, and it could pass after the callback caught the cancellation of its own join.
- `RunContext.TraceSteps` returns the trace so far inside the run callback. It used to stay empty until the run completed.
- A discovery failure that `ExploreAsync` surfaces because no generated schedule reproduced it carries a replay token.
- `ExploreAsync` runs one schedule, not all of them, from each group that begins with the same steps and hangs on a worker waiting for a parked one.
- Nearby small seeds no longer make related first choices: with three workers, seeds 1 to 15 all released the first worker first.
  The seed is mixed before use, so every seed gives a different run than before.
- A failed run waits about one second, not two, for a worker that keeps running after braid stopped the run.
- A run canceled through its token no longer reports a schedule mismatch when a worker observed the cancellation before the join did.

### Documentation

- The lost-update and user-operation-limiter examples put a probe before the shared read, so the replay token decides the race; a sequential schedule passes.
- `docs/runtime-boundaries.md` states which code the scheduler orders: a worker runs alone between its start and its first probe, and between probes.
- The README quick start compiles: probe, join and run calls pass a `CancellationToken`; README C# snippets are compiled and checked by tests.
- `RunAsync` and `ExploreAsync` XML docs: a null callback task surfaces as `RunException` with an inner `InvalidOperationException`.
- Fixed stale references in `docs/runtime-boundaries.md`, `docs/replay-token-workflow.md` and `contributing.md` (test names, `RunException.Steps`, TUnit, SDK).

### Packaging

- Builds on GitHub Actions set `ContinuousIntegrationBuild`, so PDB paths are deterministic.

## 0.7.0

### Added

- Added `ExploreOptionsBuilder.WithTimeout(TimeSpan)` for a configurable exploration timeout (previously fixed at 10 seconds).

### Changed

- Breaking: renamed all public `Braid`-prefixed types to drop the redundant prefix, matching the `Braid` namespace (SQR0007):
  `BraidRunner` → `Runner`, `BraidContext` → `RunContext`, `BraidOptions` → `RunOptions`, `BraidRunException` → `RunException`,
  `BraidRunFailureOrigin` → `RunFailureOrigin`, `BraidSchedule` → `ReplaySchedule`, `BraidStep` → `ReplayStep`, `BraidStepKind` → `ReplayStepKind`,
  `BraidProbe` → `Probe`, `BraidProbeWaitDiagnostic` → `ProbeWaitDiagnostic`, `BraidSchedulerDiagnostics` → `SchedulerDiagnostics`,
  `BraidExploreOptions` → `ExploreOptions`, `BraidExploreOptionsBuilder` → `ExploreOptionsBuilder`, `BraidExploreContext` → `ExploreContext`.
- Converted `ExploreOptions` to a readonly record struct and extracted `RunExceptionContext` to simplify failure-report construction.
- Centralized the assembly version in `Directory.Build.props`.

### Fixed

- Applied review findings to the scheduler and explorer internals.
- Bounded schedule enumeration now also yields schedules truncated at `MaxSteps` when not all workers complete within the bound.
- `Scheduler.Dispose` now snapshots the task list under the scheduler gate and tolerates repeated disposal.
- Scripted-schedule unused-step diagnostics now distinguish the case where no workers were forked.

### CI

- Added SonarCloud and PVS-Studio analysis jobs and NDepend analysis to the pipeline.

### Packaging

- Switched the repository license to Apache 2.0.

### Documentation

- Simplified the README to essentials and updated the public roadmap for the v0.7.0 API rename.

## 0.6.0

### Added

- Added `BraidRunner.ExploreAsync` for bounded hit-schedule exploration with configurable `MaxSchedules` and `MaxStepsPerSchedule`.
- Added `BraidExploreOptions`, `BraidExploreOptionsBuilder` (`WithSeed`, `WithMaxSchedules`, `WithMaxStepsPerSchedule`, `WithTimeout`), and `BraidExploreContext.WorkerAsync`.
- Added `BraidContext.Fork(string workerId, Func<Task>)` for stable worker ids in replay schedules.
- Added `BraidRunFailureOrigin` on `BraidRunException` to distinguish scheduler failures from user test failures during exploration.
- Added `BraidContext.TraceSteps` for the scheduling trace after a completed run (distinct from `BraidRunException.Trace`).
- Added [docs/design/explore-async-rfc.md](docs/design/explore-async-rfc.md).
- Added `examples/single-file/explore-lost-update` with a walkthrough for bounded exploration and replay-token export.

### Changed

- Renamed the public runner from `Braid` to `BraidRunner` (breaking change for v0.5.x consumers).
- Migrated featured examples to .NET 10 file-based apps under `examples/single-file/` (`dotnet run --file <example>.cs`).

### Fixed

- Canceled fork tasks now preserve original cancellation details and surface consistently through `JoinAsync`.
- Exploration classifies failures via `BraidRunFailureOrigin` so user exceptions are not skipped as scheduler noise.
- Discovery probe parsing preserves consecutive identical probe hits for accurate replay schedules.
- Bounded schedule enumeration stops after `MaxSchedules` instead of exhausting the search stack.
- Fork scheduling always queues `RunForkedOperationAsync` even when shutdown is already signaled.

### Documentation

- Documented bounded exploration in the README and updated the public roadmap for v0.6.0.
- Added `contributing.md` (replaces tracked `AGENTS.md` for contributor-facing guidance).

## 0.5.0

### Added

- Added `examples/lost-update` with a walkthrough for turning a read-modify-write race into a replay-token regression.
- Added `examples/cancellation-before-observation` with a walkthrough for replaying cancellation before an operation becomes observed.

### Documentation

- Reworked the README around install, quick start, replay tokens, when-to-use guidance, and three featured examples.
- Promoted `examples/cache-cas-race` alongside the new featured examples and kept `examples/user-operation-limiter` as a supplementary pattern.

### Packaging

- Updated package metadata for the v0.5.0 product-positioning pass.

## 0.4.0

### Added

- Added `BraidRunException.TryGetReplayText` for non-throwing canonical replay-text export when a typed schedule is present.

### Fixed

- Rejected overlapping probe waits from flowing child tasks on the same logical worker instead of allowing silent completion, serialization, or deadlock.

### Documentation

- Added replay-token workflow, runtime-boundary docs, and public roadmap updates.
- README: replay token terminology, `TryGetReplayText`, product tagline; aligned [docs/roadmap.md](docs/roadmap.md) (xUnit package deferred to v0.8.0).

## 0.3.1

### Documentation

- Aligned README, roadmap, release checklist/process examples, and example walkthroughs with **.NET 10**,
  **v0.3.0** text replay schedules, **Arrive / Hit / Release** semantics, **random-only failure** limits (no synthesized full replay schedule),
  and explicit **non-goals** (`TaskScheduler` replacement, await interception, binary rewriting, exhaustive model checking).

### Fixed

- Stabilized `CancellationWhileWorkerIsHeldDoesNotDeadlock`: assertion now allows either cancellation propagation
  or normal replay completion before cancellation, matching the intent “no deadlock on teardown.”

## 0.3.0

### Added

- Added textual replay schedule parsing with `BraidSchedule.Parse(...)`.
- Added non-throwing textual replay parsing with `BraidSchedule.TryParse(...)`.
- Added canonical replay text export with `BraidSchedule.ToReplayText()`.
- Added replay text to failure reports when a typed replay schedule was configured and can be exported.
- Added scheduler-state diagnostics to failure reports (last matched replay step, waiting workers, held workers, unused replay steps) when available.
- Added `examples/cache-cas-race` with walkthrough for versioned compare-and-set under `Arrive` / `Hit` / `Release` replay.

### Documentation

- Documented text replay schedules, formatting rules, and round-trip via `ToReplayText()` / `Parse`.
- Documented failure-report replay text and scheduler diagnostics; noted limits for random-only runs.
- Linked cache/CAS example from the README.

## 0.2.1

### Fixed

- Kept package version references consistent across README and release documentation.
- Hardened replay scheduler behavior around arrive/hold/release schedules.
- Improved diagnostics for invalid or incomplete replay schedules.
- Required explicit cancellation tokens on public async entry points and examples.
- Aligned private field naming across core scheduler code and examples.

## 0.2.0

### Added

- Added typed arrive/hold/release replay steps for true interleaving assertions.

## 0.1.0

Stable release of braid.

### Added

- Deterministic explicit-probe concurrency testing for .NET with `Braid.RunAsync`.
- Fork/join orchestration through `BraidContext`.
- Probe control with `BraidProbe.HitAsync`.
- Typed replay schedules through `BraidSchedule` and `BraidStep`.
- Failure reports with seed, iteration, schedule, and trace.

### Known limitations

- Explicit probes are required.
- No automatic `await` interception.
- No `TaskScheduler` replacement.
- No exhaustive state-space search.

## 0.1.0-preview.1

Initial preview of braid.

### Added

- Explicit probe-based concurrency testing with `BraidProbe.HitAsync`.
- Fork/join run model through `Braid.RunAsync` and `BraidContext`.
- Deterministic seed-based scheduling.
- Typed replay schedules through `BraidSchedule` and `BraidStep`.
- Reproducible failure reports with seed, iteration, schedule, and trace.
- Scripted schedule failure handling.
- Cancellation and timeout handling.
- Public API contract tests and scheduler stress smoke tests.

### Known limitations

- Explicit probes are required.
- No automatic `await` interception.
- No `TaskScheduler` replacement.
- No exhaustive state-space search.
- No string schedule parser yet.
- No test-framework-specific output adapters yet.
