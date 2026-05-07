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

## Run lifecycle

- `Braid.RunAsync` always awaits `JoinAsync` after your callback returns, so an explicit `JoinAsync` at the end of the callback is optional.
- `BraidContext` is only valid during the active `Braid.RunAsync` callback/run; using it after the run completes fails clearly.
- A canceled `CancellationToken` passed to `Braid.RunAsync` is honored before the callback runs (and before options validation).
- An empty callback with no forks completes when no replay schedule is configured; if a schedule is provided, every step must be consumed or the run fails with unused-step reporting.
- An explicitly empty replay schedule (`BraidSchedule.Replay()`) is allowed for empty or probe-free runs.
- Replay matching is ordinal and case-sensitive for both worker ids and probe names.
- Nested `Braid.RunAsync` calls are not supported in v0; starting a second run while a scheduler scope is active throws `InvalidOperationException`.
- Only one logical probe wait may be in flight per forked worker. Concurrent `HitAsync` calls on the same worker fail with a clear `BraidRunException`. A child `Task.Run` may call `HitAsync` after the parent probe completes; overlapping parent/child probes are rejected.
- Fork delegates must return a non-null `Task`; `null` is treated as an invalid fork result. Probe names may contain diagnostic punctuation, but cannot be null/empty/whitespace.
- Failure reports snapshot trace and schedule; mutating caller arrays after a failure does not change `BraidRunException` contents.
- Reusing one `BraidOptions` instance (including replay schedules) across independent runs is supported.
- Failure reports are scoped to the current run and iteration only.

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

## Real-world example: per-user operation limiter

A per-user operation limiter is supposed to allow at most one active operation for a configured user. The example limiter stores one `userId` and one `limit`, but still uses a dictionary internally so the unsafe read/check/write sequence is visible. Two workers can both observe `0` and both enter.

braid can force that interleaving through explicit probes such as `after-read` and `before-write`. The tests call `TryEnterAsync(cancellationToken)`, and the failure report includes the seed, schedule, and trace needed to reproduce the race.

See [examples/user-operation-limiter](examples/user-operation-limiter/) for the unsafe limiter, the locked implementation, and deterministic tests.

## Current limitations

- Explicit probes are required.
- Await interception is not automatic.
- Exhaustive search is not implemented.
- String schedule parsing is not implemented yet.

## Package status

braid is currently a preview package candidate. v0.1 is explicit-probe-based, and package publication is manual for now.

There is no stable API compatibility promise yet, but the v0.1 public surface is intentionally small.
