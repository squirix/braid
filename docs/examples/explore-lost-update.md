# Explore lost update example

This example complements [lost-update](lost-update.md). There you start from a known replay token. Here you start from workers and probe names only — exploration finds a failing interleaving for you.

## Scenario

Two workers (`reader` and `writer`) perform the same read-modify-write on a shared integer. Each worker hits `after-read` then `before-write`. The final assertion expects `2`, but a classic lost update leaves `1` when both workers read the same value before either writes.

## Exploration

`BraidRunner.ExploreAsync` runs one discovery pass to learn each worker's probe sequence, then tries bounded hit-schedule permutations until the assertion fails:

```csharp
await BraidRunner.ExploreAsync(
    options => options
        .WithSeed(12_345)
        .WithMaxSchedules(100)
        .WithMaxStepsPerSchedule(10),
    async braid =>
    {
        await braid.WorkerAsync("reader", ReaderAsync);
        await braid.WorkerAsync("writer", WriterAsync);
        await braid.JoinAsync(cancellationToken);
        Assert.Equal(2, value);
    },
    cancellationToken);
```

`WorkerAsync` registers stable worker ids so generated replay text uses `reader` / `writer` instead of anonymous `worker-1` / `worker-2`.

## Replay token

On failure, call `BraidRunException.TryGetReplayText` and parse the token:

```csharp
var schedule = BraidSchedule.Parse(replayText);
```

The example replays that schedule with `BraidRunner.RunAsync` and `BraidContext.Fork("reader", ...)` / `Fork("writer", ...)` to prove the token reproduces the same failure.

## Run it

```bash
dotnet run examples/single-file/explore-lost-update/explore-lost-update.cs
```

The test passes because it expects exploration to fail with a replayable token, then confirms `RunAsync` fails again under that schedule.

## When to use exploration vs a fixed token

- Use **explore** when you know the workers and probes but not the failing order (for example after a random `RunAsync` flake).
- Use a **fixed token** (see [lost-update](lost-update.md)) once you have the schedule and want a fast, stable regression.
