# braid

Deterministic concurrency testing for .NET using explicit async probe points.

braid helps make small async interleavings reproducible. Tests fork logical workers, workers stop at named probes, and braid controls which worker is released next.

## Install

```bash
dotnet add package braid
```

braid targets **.NET 10**.

For release validation and consumer smoke-test instructions, see [docs/release-process.md](docs/release-process.md).

## What braid does

- Runs ordinary async .NET code under a deterministic scheduler.
- Uses explicit probe points instead of runtime rewriting.
- Supports seeded random scheduling for reproducing failures.
- Supports typed replay schedules with `BraidSchedule` and `BraidStep`, and **text** replay schedules via `BraidSchedule.Parse` / `TryParse`.
- Reports failures with seed, iteration, schedule, trace, inner exception details, and—when a replay schedule was configured—copyable replay text plus scheduler-state diagnostics.

## What braid does not do

- It is not a `TaskScheduler` replacement.
- It does not intercept every `await`.
- It does not rewrite binaries.
- It is not a distributed-system test framework.
- It is not exhaustive model checking.

## Quick start

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
            Schedule = BraidSchedule.Replay(BraidStep.Hit("worker-1", "ready")),
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

Outside a braid run, `BraidProbe.HitAsync` completes immediately. Inside a braid run, it becomes an explicit scheduling point.

## Replay schedules

A replay schedule describes the exact worker/probe order to reproduce.

```csharp
var options = new BraidOptions
{
    Seed = 12345,
    Iterations = 1,
    Schedule = BraidSchedule.Replay(
        BraidStep.Hit("worker-1", "after-read"),
        BraidStep.Hit("worker-2", "after-read"),
        BraidStep.Hit("worker-1", "before-write"),
        BraidStep.Hit("worker-2", "before-write")),
};
```

Replay matching is ordinal and case-sensitive for both worker ids and probe names.

### Text replay schedules

Replay schedules can also be parsed from text:

```csharp
var schedule = BraidSchedule.Parse("""
hit worker-1 after-read
hit worker-2 after-read
arrive worker-1 before-write
hit worker-2 updated
release worker-1 before-write
""");
```

The text format is line based:

```text
hit <worker> <probe>
arrive <worker> <probe>
release <worker> <probe>
```

Operation names (`hit`, `arrive`, `release`) are **case-insensitive**. Worker ids and probe names stay **case-sensitive**. Empty lines and full-line `#` comments (after trimming) are ignored; **inline** `#` comments are not supported—extra tokens are rejected.

Use `BraidSchedule.TryParse` when you need a non-throwing parse attempt.

`ToReplayText()` writes the canonical lower-case format accepted by `BraidSchedule.Parse(...)` for schedules that can be represented (no whitespace inside worker or probe tokens).

```csharp
var text = schedule.ToReplayText();
var replay = BraidSchedule.Parse(text);
```

An empty typed schedule exports to an empty string; `BraidSchedule.Parse` still requires at least one step for non-empty text.

## True interleaving replay

`BraidStep.Hit(worker, probe)` releases a worker when it is blocked at that probe.

For stricter interleaving assertions, use the two-phase arrive/release flow:

- `BraidStep.Arrive(worker, probe)` waits until the worker reaches the probe and keeps it blocked.
- `BraidStep.Release(worker, probe)` releases a worker that was previously held by `Arrive`.

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

This expresses: worker-1 is already blocked at `cache-hit`, worker-2 mutates state, then worker-1 resumes.

## Failure reproduction

When a run fails, braid wraps scheduler and callback failures in `BraidRunException` where appropriate.

**When a typed replay schedule was configured**, failure reports include **canonical replay text** you can paste into `BraidSchedule.Parse(...)` for a stable regression test—unless the schedule cannot be exported to text (for example worker or probe names containing whitespace).

Reports also include **scheduler-state diagnostics** when the scheduler captured them: last matched replay step, workers waiting at probes, workers held after `Arrive`, and unused replay steps. Sections are omitted when empty.

For **random scheduling only** (no replay schedule in options), failure reports do **not** synthesize a full replay schedule or replay text; use the seed and trace to investigate, then capture a typed or text schedule once you understand the interleaving.

Failure reports always aim to include:

- seed;
- iteration;
- replay schedule (when configured);
- replay text and diagnostics as described above;
- execution trace;
- original inner exception, when present.

Use the reported seed to reproduce random scheduling behavior. Once a race is understood, prefer a typed or text replay schedule for stable regression tests.

Example report:

```text
braid run failed.
Seed: 12345
Iteration: 0
Schedule:
  1. worker-1 @ after-read
  2. worker-2 @ after-read
Replay text:
hit worker-1 after-read
hit worker-2 after-read
Last matched replay step:
  2. hit worker-2 after-read
Trace:
  1. worker-1 forked
  2. worker-2 forked
  3. worker-1 hit after-read
  4. worker-1 released at after-read
```

## Run lifecycle

- `Braid.RunAsync` awaits `JoinAsync` after the callback completes, so an explicit final `JoinAsync` is optional.
- `BraidContext` is valid only during the active `Braid.RunAsync` callback.
- A canceled `CancellationToken` passed to `Braid.RunAsync` is honored before the callback runs.
- Empty callbacks complete when no replay schedule is configured.
- Non-empty replay schedules must be fully consumed.
- `BraidSchedule.Replay()` with no steps is allowed for empty or probe-free runs.
- Nested `Braid.RunAsync` calls are not supported.
- Only one logical probe wait may be in flight per forked worker.
- Fork delegates must return a non-null `Task`.
- Probe names cannot be null, empty, or whitespace.
- Reusing one `BraidOptions` instance across independent runs is supported.
- Failure reports are scoped to the current run and iteration only.

## When to use braid

- Cache and library concurrency tests.
- CAS, TTL, and state-machine style code.
- Race reproduction after a flaky failure is understood.
- Small deterministic async scenarios with clear probe points.

## Examples

- **User operation limiter** — unsafe read/check/write interleaving vs lock-protected fix; see [examples/user-operation-limiter](examples/user-operation-limiter) and [docs/examples/user-operation-limiter.md](docs/examples/user-operation-limiter.md).
- **Cache / CAS race** — versioned cell where a stale compare-and-set must return `VersionMismatch`; uses `Arrive` / `Hit` / `Release`; see [examples/cache-cas-race](examples/cache-cas-race) and [docs/examples/cache-cas-race.md](docs/examples/cache-cas-race.md).

## Current limitations

- Explicit probes are required.
- Await interception is not automatic.
- Exhaustive search is not implemented.
- Random-run failures do not automatically include a complete replay schedule unless you configured one in `BraidOptions.Schedule`.
