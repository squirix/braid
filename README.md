# braid

braid is a deterministic concurrency testing library for .NET using explicit async probe points.

braid helps small async concurrency tests make interleavings reproducible. Tests fork logical workers, workers stop at named probes, and braid controls which worker is released next.

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

## Reproducing failures

When a run fails, braid wraps the failure in `BraidRunException` and reports:

- `Seed`: the seed used by the failing iteration.
- `Iteration`: the zero-based failing iteration index.
- `Schedule`: the typed replay schedule when one was configured.
- `Trace`: the recorded worker/probe/release trace.

Use the same seed to reproduce random scheduling behavior. Once a bug is understood, prefer a typed replay schedule for stable regression tests.

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

## When to use

- Cache and library concurrency tests.
- Race reproduction after a flaky failure is understood.
- CAS, TTL, and state-machine style code.
- Small deterministic async scenarios with clear probe points.

## Current limitations

- Explicit probes are required.
- Await interception is not automatic.
- Exhaustive search is not implemented.
- String schedule parsing is not implemented yet.
