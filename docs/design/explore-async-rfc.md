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

1. **Discovery run** — one random `RunAsync` iteration records per-worker probe sequences from the scheduling trace (`{workerId} hit {probe}` lines).
   It starts every worker first, in fork order, so it learns the probes of every worker even when it stops early.
2. **Enumeration** — bounded depth-first generation of schedules: a start step per worker, then its hit steps in probe order, interleaved across workers.
   The first schedules start every worker before any hit, in fork order; later ones move the starts between the hits.
   With `p` probes per worker and `n` workers there are `(n(p + 1))! / ((p + 1)!)^n` schedules, of which the first `(np)! / (p!)^n` keep the fork-order start.
   A schedule with more hit steps than `MaxStepsPerSchedule` is cut there; start steps do not count.
   A schedule that begins like one that hung on a worker waiting for a parked one is not run: it would hang the same way.
3. **Replay attempts** — each generated schedule runs under `RunAsync` with `Iterations = 1`.
   After its last step, waiting workers are released in fork order, so the test runs to completion; the reported replay token includes those completion steps.
4. **Stop** — return when bounds are exhausted without failure; throw the first `RunException` caused by a test assertion, a timeout or an API misuse error (or a discovery random failure).

Invalid schedules (scheduler mismatch, an exhausted or unused script) are skipped, and so is a timeout while workers are parked at probes
(see [runtime boundaries](../runtime-boundaries.md#waiting-for-a-parked-worker)). Every other failure stops exploration, including API misuse
errors such as a fork after `JoinAsync` or two probe waits in flight on one worker, which `RunAsync` reports the same way.

## Determinism

Same seed, bounds, and test callback produce the same discovery trace and the same enumeration order, so the first reported failure is stable.

## Failure artifacts

When exploration fails under a replay schedule, use `RunException.TryGetReplayText` exactly as with `RunAsync`.
The discovery run records each release as a step, so a discovery failure that no generated schedule reproduces carries replay text too.
Its seed alone does not reproduce it through `RunAsync`: the discovery run starts every worker first, in fork order, and a random run does not.

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
