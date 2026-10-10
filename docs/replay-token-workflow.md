# Replay token workflow

A **replay token** is Braid’s canonical replay text: the same format produced by `ReplaySchedule.ToReplayText()` and accepted by `ReplaySchedule.Parse(...)`. There is no separate syntax.

```text
start <worker>
hit <worker> <probe>
arrive <worker> <probe>
release <worker> <probe>
```

`start` runs a worker from its beginning up to its first probe. A token without `start` lines starts every worker before its first line,
in fork order; a token with them starts only the workers it names.

**Product goal:** find the interleaving, copy the token, keep the race fixed forever.

See also: [roadmap.md](roadmap.md), [v0.4.0-roadmap.md](design/v0.4.0-roadmap.md), [README.md](../README.md).

---

## When you already have a replay token

1. Paste the token into a test:

   ```csharp
   var schedule = ReplaySchedule.Parse("""
   hit worker-1 before-read
   hit worker-2 before-read
   hit worker-1 before-write
   hit worker-2 before-write
   """);

   await Runner.RunAsync(test, new RunOptions { Schedule = schedule, Iterations = 1 }, cancellationToken);
   ```

2. Or build a typed schedule with `ReplaySchedule.Replay(...)` and `ReplayStep` values.

3. Run until the assertion passes or the schedule is adjusted.

---

## When a random run fails (no token yet)

Random-only runs report **seed**, **iteration**, and **trace**. They do **not** synthesize a full replay token automatically.

1. **Capture** seed and trace from `RunException.ToString()` or exception properties.
2. **Re-run** with the same seed to confirm the failure (`RunOptions.Seed`).
3. **Add probes** at async boundaries in the code under test (`Probe.HitAsync`).
4. **Build** a typed or text schedule that matches the interleaving you need (use trace lines as hints).
   Write a `start` line for each `<worker> released` trace line, in that order: a token without `start` lines starts the workers in fork order.
5. **Export** canonical text with `ReplaySchedule.ToReplayText()` once the schedule reproduces the bug.
6. **Regression test** using `ReplaySchedule.Parse(token)` or `ReplaySchedule.Replay(...)`.

---

## When a replay-scheduled run fails

If `RunOptions.Schedule` was configured, the failure may include exportable replay text.

Prefer the API over parsing `ToString()`:

```csharp
catch (RunException ex)
{
    if (ex.TryGetReplayText(out var token, out var error))
    {
        // Paste `token` into ReplaySchedule.Parse(...) for a regression test.
    }
    else
    {
        // `error` set: the schedule is not text-exportable (e.g. whitespace in worker/probe names).
        // Use ex.Steps typed steps or fix naming.
        // `error` null: a random run failed. ex.Steps is empty and is not a schedule.
    }
}
```

In xUnit, you can also write the full report without a separate package:

```csharp
ITestOutputHelper output; // inject in test
// ...
catch (RunException ex)
{
    output.WriteLine(ex.ToString());
    throw;
}
```

---

## Scheduler diagnostics

When available, `ex.SchedulerDiagnostics` includes last matched replay step, waiting workers, held workers (after `Arrive`), and unused replay steps.
Use these to fix incomplete or mismatched schedules—not to replace a replay token.

---

## Honest limits

| Situation | Replay token |
| ----------- | ---------------- |
| Typed or text schedule configured and exportable | Yes — `TryGetReplayText` or failure report |
| Random-only run | No automatic full token — build schedule manually |
| Whitespace in worker id or probe name | Token export may fail; use typed `ReplayStep` list |
| No steps, for example `RunException.Steps` of a random run | Not a schedule: `ReplaySchedule.Replay` throws `ArgumentException` |

---

## Runtime boundaries (probe placement)

- Only **one** logical probe wait may be in flight per forked worker at a time.
- A **concurrent** probe hit on the same worker (for example from a flowing child task while the parent waits) fails with a clear error.
- A task that a worker starts may hit a probe only while the worker does nothing but wait for that task.

See [runtime-boundaries.md](runtime-boundaries.md) and `tests/braid.tests/BraidProbeConcurrencyBoundaryTests.cs`.
