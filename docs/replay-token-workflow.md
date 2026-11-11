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

Use `#` at the start of a line or after whitespace following a complete step to add a comment.
A `#` in a worker id or probe name is part of that identifier. `ToReplayText()` emits only steps.

**Product goal:** find the interleaving, copy the token, keep the race fixed forever.

See also: [roadmap.md](roadmap.md), [v0.4.0-roadmap.md](design/v0.4.0-roadmap.md), [README.md](../README.md).

---

## When you already have a replay token

1. Paste the token into a test:

   ```csharp
   var schedule = ReplaySchedule.Parse("""
   hit worker-1 before-read
   hit worker-2 before-read  # both workers read before either writes
   hit worker-1 before-write
   hit worker-2 before-write
   """);

   await Runner.RunAsync(test, new RunOptions { Schedule = schedule, Iterations = 1 }, cancellationToken);
   ```

2. Or build a typed schedule with `ReplaySchedule.Replay(...)` and `ReplayStep` values.

3. Run until the assertion passes or the schedule is adjusted.

---

## Export the decisions that led to a failure

`RunException.TryGetExecutedReplayText` exports the steps taken up to a failure in a random, scripted, or exploration run.
It includes implicit worker starts and omits the unused tail of a configured schedule.

```csharp
catch (RunException ex)
{
    if (ex.TryGetExecutedReplayText(out var token, out _))
        output.WriteLine(token);

    throw;
}
```

Paste the text into `ReplaySchedule.Parse(...)` and replay it with the same callback. For names containing whitespace,
pass `ex.Context.ExecutedSteps` to `ReplaySchedule.Replay(...)`. If the list is empty, the run made no scheduling decision.
Use the seed and trace to reproduce the callback failure. For a script mismatch, use `TryGetReplayText` to recover
the original script, including the step that could not be matched.

---

## Scheduler diagnostics

When available, `ex.SchedulerDiagnostics` includes last matched replay step, waiting workers, held workers (after `Arrive`), and unused replay steps.
Use these to fix incomplete or mismatched schedules—not to replace a replay token.

---

## Honest limits

| Situation | Replay token |
| ----------- | ---------------- |
| Typed or text schedule configured and exportable | Yes — `TryGetReplayText` or failure report |
| Random run with recorded steps | `TryGetExecutedReplayText` exports the steps taken before failure |
| Whitespace in worker id or probe name | Token export may fail; use typed `ReplayStep` list |
| No executed steps | Use the seed and trace to reproduce the failure |

---

## Runtime boundaries (probe placement)

- Only **one** logical probe wait may be in flight per forked worker at a time.
- A **concurrent** probe hit on the same worker (for example from a flowing child task while the parent waits) fails with a clear error.
- A task that a worker starts may hit a probe only while the worker does nothing but wait for that task.

See [runtime-boundaries.md](runtime-boundaries.md) and `tests/braid.tests/BraidProbeConcurrencyBoundaryTests.cs`.
