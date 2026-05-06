# braid

braid is a deterministic concurrency testing library for .NET.

braid explores explicit async probe points and makes race/concurrency failures reproducible.

The v0 prototype is intentionally small. Tests fork logical workers, workers hit named probes, and braid records a seed and trace when a failure occurs.

## What It Is

- A standalone .NET testing library.
- A way to make explicit async interleavings reproducible.
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

## Failure Reporting

Failures are wrapped in `BraidRunException`. The exception includes the failing seed, iteration, and a human-readable trace:

```text
Seed: 12345
Iteration: 0
Trace:
  worker-1 forked
  worker-1 hit before-failure
```

The seed and trace are the starting point for replaying the same interleaving with a scripted schedule.

## Package shape

- `braid`
