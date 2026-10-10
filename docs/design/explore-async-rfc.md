# ExploreAsync RFC

**Status:** accepted for v0.6.0 (bounded hit-schedule exploration).

See also: [roadmap.md](../roadmap.md), [replay-token-workflow.md](../replay-token-workflow.md).

## Problem

`Runner.RunAsync` with random scheduling can find flaky failures, but turning them into stable replay regressions still requires manual schedule construction.
Users need a bounded, deterministic search that stops at the first reproducible failure and exports a replay token when possible.

## Goals

- Add `Runner.ExploreAsync` as an **additive** API on top of the existing scheduler.
- Keep explicit probes only; no await interception or IL rewriting.
- Bound search with `MaxSchedules` and `MaxStepsPerSchedule`.
- Stop at the **first** test failure and surface `RunException` with replay text when a typed schedule was used.
- Preserve `RunAsync` semantics and compatibility.

## Non-goals (v0.6.0)

- Automatic probe discovery without running user code.
- `Arrive` / `Release` schedule generation (hit-only interleavings in v0.6.0).
- Collecting multiple distinct failures in one run.
- Seed corpus persistence API (document convention only).

## API

```csharp
await Runner.ExploreAsync(
    options => options
        .WithSeed(123)
        .WithMaxSchedules(1_000)
        .WithMaxStepsPerSchedule(100),
    async braid =>
    {
        await braid.WorkerAsync("reader", ReaderAsync);
        await braid.WorkerAsync("writer", WriterAsync);
        await braid.JoinAsync(cancellationToken);
    },
    cancellationToken);
```

`WorkerAsync` maps to `RunContext.Fork(workerId, operation)` with stable worker ids for replay schedules. Call `JoinAsync` before post-join assertions in the explore callback.

## Strategy

Exploration is a depth-first search over the choices of real runs. A choice is a point where no worker runs and at least one waits,
before its start or at a probe.

1. **First run** — at every choice the run releases the first waiting worker in a fixed order: workers that have not started, in fork order,
   then started workers by worker id. So the first schedule starts every worker before any probe is passed.
   The run records which steps it could take at every choice.
2. **Next runs** — each run replays the steps of the path so far up to the last choice that still has an untried step, takes that step,
   and chooses in the fixed order from then on, recording its choices again.
   Every replayed step was taken by an earlier run, so a schedule fits the test also when the order of the workers changes which probes
   a worker hits: a retry loop, a conflict branch, an early exit.
3. **Bounds** — `MaxSchedules` limits the number of runs. Only the choices up to the first `MaxStepsPerSchedule` hit steps of a run are explored;
   start steps do not count. A run makes its later choices in the fixed order, so the test runs to completion, and the reported replay token
   includes those steps.
   A test whose probes do not depend on the order has `(n(p + 1))! / ((p + 1)!)^n` schedules for `n` workers with `p` probes each,
   of which the first `(np)! / (p!)^n` start every worker first.
4. **Hangs** — a run that times out while a worker runs and others are parked
   (see [runtime boundaries](../runtime-boundaries.md#waiting-for-a-parked-worker)) is skipped. Its choices end at the hang,
   so no other schedule that begins the same way is run. When every run hangs this way, exploration fails with the first of those timeouts.
5. **Stop** — return when every choice was tried or `MaxSchedules` runs were made without failure; throw the first `RunException` caused by
   a test assertion, a timeout or an API misuse error, such as a fork after `JoinAsync` or two probe waits in flight on one worker,
   which `RunAsync` reports the same way.

The test must take the same steps whenever its workers are released in the same order. When a step that an earlier run took cannot be taken
again, exploration fails with "The test did not repeat under the same schedule"; the inner exception names the step.

## Determinism

Same bounds and test callback give the same order of schedules, so the first reported failure is stable.
The seed does not change the order; it is only reported with a failure.

## Failure artifacts

When exploration fails, use `RunException.TryGetReplayText` exactly as with `RunAsync`. The replay text holds every step of the failing run.

## Seed corpus (docs convention)

Teams may persist failing `(seed, replay text)` pairs in test data or CI artifacts. No file format is mandated in v0.6.0.

## Open questions (deferred)

- Fairness-aware enumeration vs pure depth-first ordering.
- `Arrive` / `Release` exploration.
- Collecting N failures per run.
- Source-generated probe catalogs.

## Compatibility

- `RunAsync` unchanged for existing callers.
- `RunContext.Fork(string workerId, ...)` is additive.
- `ExploreAsync` shares the scheduler core; no `RunContext` rename.
