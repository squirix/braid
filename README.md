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

// Inside a test method: cancellationToken is the test framework's token (or CancellationToken.None).
var workerCompleted = false;
var options = new RunOptions
{
    Iterations = 1,
    Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
};

await Runner.RunAsync(
    async context =>
    {
        context.Fork(async () =>
        {
            await Probe.HitAsync("ready", cancellationToken);
            workerCompleted = true;
        });

        await context.JoinAsync(cancellationToken);
    },
    options,
    cancellationToken);

// workerCompleted is true here.
```

Outside a braid run, `Probe.HitAsync` completes immediately. Inside a
braid run, it becomes an explicit scheduling point.

Don't know the failing interleaving yet? Try bounded exploration:

```csharp
await Runner.ExploreAsync(
    static options => options
        .WithSeed(123)
        .WithMaxSchedules(1_000)
        .WithMaxStepsPerSchedule(100),
    async braid =>
    {
        await braid.WorkerAsync("reader", ReaderAsync);
        await braid.WorkerAsync("writer", WriterAsync);

        await braid.JoinAsync(cancellationToken);
        if (observed != expected)
            throw new InvalidOperationException($"Observed {observed}, expected {expected}.");
    },
    cancellationToken);
```

`ReaderAsync` and `WriterAsync` are your worker methods, and they call `Probe.HitAsync` at the points braid should interleave.
When a run fails, use `RunException.TryGetReplayText` to export a replay token
for a stable regression test.

## Replay schedules

A replay schedule describes the exact worker/probe order to reproduce.

```csharp
var options = new RunOptions
{
    Iterations = 1,
    Schedule = ReplaySchedule.Replay(
        ReplayStep.Hit("worker-1", "before-read"),
        ReplayStep.Hit("worker-2", "before-read"),
        ReplayStep.Hit("worker-1", "before-write"),
        ReplayStep.Hit("worker-2", "before-write")),
};
```

Schedules can also be parsed from text:
`ReplaySchedule.Parse("hit worker-1 before-read\nhit worker-2 before-read")`.
The parsed text is the same format braid emits as a replay token on failure.

For stricter two-phase interleaving control, use `ReplayStep.Arrive` / `ReplayStep.Release`
instead of `ReplayStep.Hit`.

A schedule can also say which worker starts first, with `ReplayStep.Start("worker-2")` or the text line `start worker-2`.
A schedule without start steps starts every worker before its first step, in fork order.

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
