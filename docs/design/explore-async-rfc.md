# ExploreAsync RFC

**Status:** accepted for v0.6.0 (bounded hit-schedule exploration).

See also: [roadmap.md](../roadmap.md), [replay-token-workflow.md](../replay-token-workflow.md).

## Problem

`BraidRunner.RunAsync` with random scheduling can find flaky failures, but turning them into stable replay regressions still requires manual schedule construction. Users need a bounded, deterministic search that stops at the first reproducible failure and exports a replay token when possible.

## Goals

- Add `BraidRunner.ExploreAsync` as an **additive** API on top of the existing scheduler.
- Keep explicit probes only; no await interception or IL rewriting.
- Bound search with `MaxSchedules` and `MaxStepsPerSchedule`.
- Stop at the **first** test failure and surface `BraidRunException` with replay text when a typed schedule was used.
- Preserve `RunAsync` semantics and compatibility.

## Non-goals (v0.6.0)

- Automatic probe discovery without running user code.
- `Arrive` / `Release` schedule generation (hit-only interleavings in v0.6.0).
- Collecting multiple distinct failures in one run.
- Seed corpus persistence API (document convention only).

## API

```csharp
await BraidRunner.ExploreAsync(
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

`WorkerAsync` maps to `BraidContext.Fork(workerId, operation)` with stable worker ids for replay schedules. Call `JoinAsync` before post-join assertions in the explore callback.

## Strategy

1. **Discovery run** — one random `RunAsync` iteration records per-worker probe sequences from the scheduling trace (`{workerId} hit {probe}` lines).
2. **Enumeration** — bounded depth-first generation of hit schedules that preserve each worker's probe order.
3. **Replay attempts** — each generated schedule runs under `RunAsync` with `Iterations = 1`.
4. **Stop** — return when bounds are exhausted without failure; throw the first `BraidRunException` caused by a test assertion (or a discovery random failure).

Invalid schedules (scheduler mismatch) are skipped. Scheduler-only failures during replay do not stop exploration.

## Determinism

Same seed, bounds, and test callback produce the same discovery trace and the same enumeration order, so the first reported failure is stable.

## Failure artifacts

When exploration fails under a replay schedule, use `BraidRunException.TryGetReplayText` exactly as with `RunAsync`. Random-only discovery failures may not export replay text until a replay schedule reproduces the assertion.

## Seed corpus (docs convention)

Teams may persist failing `(seed, replay text)` pairs in test data or CI artifacts. No file format is mandated in v0.6.0.

## Open questions (deferred)

- Fairness-aware enumeration vs pure depth-first ordering.
- `Arrive` / `Release` exploration.
- Collecting N failures per run.
- Source-generated probe catalogs.

## Compatibility

- `RunAsync` unchanged for existing callers.
- `BraidContext.Fork(string workerId, ...)` is additive.
- `ExploreAsync` shares the scheduler core; no `BraidContext` rename.
