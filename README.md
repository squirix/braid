# braid

braid is a deterministic concurrency testing library for .NET.

braid explores explicit async probe points and makes race/concurrency failures reproducible.

The v0 prototype is intentionally small. Tests fork logical workers, workers hit named probes, and braid records a seed and trace when a failure occurs.

## What It Is

- A standalone .NET testing library.
- A way to make explicit async interleavings reproducible.
- A v0 runner built around explicit probes and typed replay schedules.
- A probe-based runner that works with ordinary xUnit tests.

## What It Is Not

- It does not replace `TaskScheduler`.
- It does not use binary rewriting.
- It is not an actor runtime.
- It is not a full Coyote replacement.

## Naming

The package and project name are lowercase `braid`. C# namespaces and public types remain PascalCase to follow .NET conventions.

## Minimal Example

```csharp
using Braid;

await Braid.RunAsync(async context =>
{
    context.Fork(async () =>
    {
        await BraidProbe.HitAsync("probe-name", cancellationToken);
    });

    await context.JoinAsync(cancellationToken);
}, cancellationToken: cancellationToken);
```

## Probe Example

```csharp
context.Fork(async () =>
{
    await BraidProbe.HitAsync("after-read", cancellationToken);
    await BraidProbe.HitAsync("before-write", cancellationToken);
});
```

Outside a braid run, `BraidProbe.HitAsync` completes immediately.

## Replay Schedule

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

    await context.JoinAsync(cancellationToken);
}, options, cancellationToken);
```

## Failure Reporting

Failures are wrapped in `BraidRunException`. braid reports the failing seed, iteration, scripted schedule (if configured), and trace:

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

This report is meant to be copyable into test logs and issue reports so a failing interleaving can be replayed quickly.

## Package shape

- `braid`
