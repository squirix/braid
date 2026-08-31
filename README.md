# braid

Deterministic concurrency testing for .NET libraries using explicit async probe
points and replay tokens.

**Find the interleaving. Copy the replay token. Keep the race fixed forever.**

Tests fork logical workers, workers stop at named probes, and braid controls
which worker is released next. When a race is understood, keep the
reproducing interleaving as a copyable replay token.

[![Powered by NDepend](docs/assets/powered-by-ndepend.png)](https://www.ndepend.com/)

## Install

```bash
dotnet add package braid
```

braid targets **.NET 10**.

## Quick start

```csharp
using Braid;

var workerCompleted = false;
var options = new BraidOptions
{
    Iterations = 1,
    Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "ready")),
};

await BraidRunner.RunAsync(
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
```

Outside a braid run, `BraidProbe.HitAsync` completes immediately. Inside a
braid run, it becomes an explicit scheduling point.

Don't know the failing interleaving yet? Try bounded exploration:

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
        Assert.Equal(expected, observed);
    },
    cancellationToken);
```

When a run fails, use `BraidRunException.TryGetReplayText` to export a replay token
for a stable regression test.

## Replay schedules

A replay schedule describes the exact worker/probe order to reproduce.

```csharp
var options = new BraidOptions
{
    Iterations = 1,
    Schedule = BraidSchedule.Replay(
        BraidStep.Hit("worker-1", "after-read"),
        BraidStep.Hit("worker-2", "after-read"),
        BraidStep.Hit("worker-1", "before-write"),
        BraidStep.Hit("worker-2", "before-write")),
};
```

Schedules can also be parsed from text:
`BraidSchedule.Parse("hit worker-1 after-read\nhit worker-2 after-read")`.
The parsed text is the same format braid emits as a replay token on failure.

For stricter two-phase interleaving control, use `BraidStep.Arrive` / `BraidStep.Release`
instead of `BraidStep.Hit`.

## When to use braid

- cache, CAS, TTL, and state-machine library tests;
- race reproduction after a flaky failure is understood;
- regression tests where a specific interleaving should stay fixed;
- small async scenarios where explicit probes are acceptable.

Braid is not a `TaskScheduler` replacement, does not rewrite binaries, and is
not a distributed-system test framework.

## Learn more

- [Replay token workflow](docs/replay-token-workflow.md)
- [Roadmap](docs/roadmap.md)
- [Runtime boundaries](docs/runtime-boundaries.md)
- [Release process](docs/release-process.md)
- Contributing: [contributing.md](contributing.md)

## SAST Tools

[PVS-Studio](https://pvs-studio.com/pvs-studio/?utm_source=website&utm_medium=github&utm_campaign=open_source) - static
code analyzer for Enterprise (C, C++, C#, Go, and Java) and Web (JS and TS) development.
