# braid

braid is a deterministic concurrency testing library for .NET using explicit async probe points.

braid helps small async concurrency tests make interleavings reproducible. Tests fork logical workers, workers stop at named probes, and braid controls which worker is released next.

## Install from NuGet

braid targets **.NET 10** and is published on NuGet as `0.1.0`.

```bash
dotnet add package braid --version 0.1.0
```

For a one-off consumer check, see [Consumer smoke test](https://github.com/squirix/braid/blob/main/docs/release-process.md#consumer-smoke-test) in the release process doc.

## What braid is

- Explicit-probe-based concurrency testing for ordinary .NET code.
- Deterministic seed mode for replaying random scheduling choices.
- Typed replay schedules with `BraidSchedule` and `BraidStep`.
- Failure reports with seed, iteration, schedule, and trace details.

## What braid is not

- Not a `TaskScheduler` replacement.
- Not binary rewriting or automatic await interception.
- Not a Coyote replacement.
- Not distributed-system testing.
- Not exhaustive model checking yet.

## Quick start (xUnit)

After adding the package, a minimal test can fork one worker, hit a probe under a typed replay schedule, join, and assert the worker finished:

```csharp
using Braid;
using Xunit;

public sealed class BraidQuickStartTests
{
    [Fact]
    public async Task Fork_probe_join_completes_under_replay()
    {
        var workerCompleted = false;
        var options = new BraidOptions
        {
            Iterations = 1,
            Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
        };

        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("ready");
                    workerCompleted = true;
                });

                await context.JoinAsync();
            },
            options);

        Assert.True(workerCompleted);
    }
}
```

For a larger pattern (unsafe vs fixed limiter, failure assertions), see the [user operation limiter example](https://github.com/squirix/braid/tree/main/examples/user-operation-limiter) and [example walkthrough](https://github.com/squirix/braid/blob/main/docs/examples/user-operation-limiter.md).

## Minimal example

```csharp
using Braid;

await Braid.RunAsync(async context =>
{
    context.Fork(async () =>
    {
        await BraidProbe.HitAsync("ready", cancellationToken);
        // Exercise concurrent code here.
    });

    context.Fork(async () =>
    {
        await BraidProbe.HitAsync("ready", cancellationToken);
        // Exercise concurrent code here.
    });

    await context.JoinAsync(cancellationToken);
}, cancellationToken: cancellationToken);
```

Outside a braid run, `BraidProbe.HitAsync` completes immediately. Inside a run, it is an explicit scheduling point.

## Run lifecycle

- `Braid.RunAsync` always awaits `JoinAsync` after your callback's task completes, so an explicit `JoinAsync` at the end of the callback is optional.
- `BraidContext` is only valid during the active `Braid.RunAsync` callback/run; using it after the run completes fails clearly.
- A canceled `CancellationToken` passed to `Braid.RunAsync` is honored before the callback runs (and before options validation).
- An empty callback with no forks completes when no replay schedule is configured; if a non-empty schedule is provided, every step must be consumed or the run fails with unused-step reporting.
- An explicitly empty replay schedule (`BraidSchedule.Replay()` with no steps) is allowed for empty or probe-free runs.
- Replay matching is ordinal and case-sensitive for both worker ids and probe names.
- Nested `Braid.RunAsync` calls are not supported in v0; starting a second run while a scheduler scope is active throws `InvalidOperationException`.
- Only one logical probe wait may be in flight per forked worker. Concurrent `HitAsync` calls on the same worker fail with a clear `BraidRunException` when the scheduler detects them; serialized child-task probes after the parent probe completes are allowed when the current runtime accepts that pattern. Overlapping parent/child probes are rejected.
- Fork delegates must return a non-null `Task`; `null` is treated as an invalid callback result. Probe names may contain diagnostic punctuation, but cannot be null, empty, or whitespace.
- Callback faults and scheduler-detected failures are surfaced as `BraidRunException` (or cancellation) as described below; mutating caller arrays after a failure does not change captured report data.
- Reusing one `BraidOptions` instance (including replay schedules) across independent runs is supported.
- Failure reports are scoped to the current run and iteration only.

## Reproducing failures

When a run fails, braid wraps many failures in `BraidRunException` and reports:

- `Seed`: the seed used by the failing iteration (use the same seed to reproduce random scheduling for that iteration).
- `Iteration`: the zero-based failing iteration index.
- `Schedule`: the typed replay schedule when one was configured (empty when only random scheduling was used).
- `Trace`: the recorded worker/probe/release trace.
- `InnerException`: the original fault when braid wraps an underlying exception; `ToString()` on `BraidRunException` appends inner details when present.

Use the same seed to reproduce random scheduling behavior. Once a race is understood, prefer a typed replay schedule for stable regression tests instead of relying on random exploration.

```csharp
var options = new BraidOptions
{
    Seed = 12345,
    Iterations = 1,
    Schedule = BraidSchedule.Replay(
        new BraidStep("worker-1", "after-read"),
        new BraidStep("worker-2", "after-read"),
        new BraidStep("worker-1", "before-write"),
        new BraidStep("worker-2", "before-write")),
};

await Braid.RunAsync(async context =>
{
    context.Fork(async () =>
    {
        await BraidProbe.HitAsync("after-read", cancellationToken);
        await BraidProbe.HitAsync("before-write", cancellationToken);
    });

    context.Fork(async () =>
    {
        await BraidProbe.HitAsync("after-read", cancellationToken);
        await BraidProbe.HitAsync("before-write", cancellationToken);
    });

    await context.JoinAsync(cancellationToken);
}, options, cancellationToken);
```

Example report:

```text
braid run failed.
Seed: 12345
Iteration: 0
Schedule:
  1. worker-1 @ after-read
  2. worker-2 @ after-read
Trace:
  1. worker-1 forked
  2. worker-2 forked
  3. worker-1 hit after-read
  4. worker-1 released at after-read
```

## True interleaving replay (arrive/hold/release)

`BraidStep(worker, probe)` (or `BraidStep.Hit`) keeps the original behavior: release a worker when it is blocked at that probe.

For stricter interleaving assertions, replay also supports a two-phase probe flow:

- `BraidStep.Arrive(worker, probe)` waits until the worker reaches the probe and keeps it blocked.
- `BraidStep.Release(worker, probe)` releases a worker that was previously held by `Arrive`.

This lets you express "worker-1 is already blocked at probe A, then worker-2 mutates, then worker-1 resumes" without adding extra probes:

```csharp
var options = new BraidOptions
{
    Iterations = 1,
    Schedule = BraidSchedule.Replay(
        BraidStep.Arrive("worker-1", "cache-hit"),
        BraidStep.Hit("worker-2", "mutation-done"),
        BraidStep.Release("worker-1", "cache-hit")),
};
```

Semantic difference:

- `release worker at probe`: worker is released as soon as that schedule step is matched.
- `wait until arrived and hold`: worker arrival is asserted first, but execution stays blocked.
- `true interleaving test`: a competing step runs while the first worker is provably held.

## When to use

- Cache and library concurrency tests.
- Race reproduction after a flaky failure is understood.
- CAS, TTL, and state-machine style code.
- Small deterministic async scenarios with clear probe points.

## Real-world example: per-user operation limiter

A per-user operation limiter is supposed to allow at most one active operation for a configured user. The example limiter stores one `userId` and one `limit`, but still uses a dictionary internally so the unsafe read/check/write sequence is visible. Two workers can both observe `0` and both enter.

braid can force that interleaving through explicit probes such as `after-read` and `before-write`. The tests call `TryEnterAsync(cancellationToken)`, and the failure report includes the seed, schedule, and trace needed to reproduce the race.

This example is generic domain code (not tied to any product beyond the MIT sample); see [examples/user-operation-limiter](https://github.com/squirix/braid/tree/main/examples/user-operation-limiter) on GitHub.

## Current limitations

- Explicit probes are required.
- Await interception is not automatic.
- Exhaustive search is not implemented.
- String schedule parsing is not implemented yet.

## Package status

braid `0.1.0` is published on NuGet. Minor releases may still evolve the public surface, but `0.1.x` updates are intended to stay source-compatible when practical.

Manual release steps for maintainers: [docs/release-process.md](https://github.com/squirix/braid/blob/main/docs/release-process.md).
