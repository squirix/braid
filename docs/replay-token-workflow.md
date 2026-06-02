# Replay token workflow

A **replay token** is Braid’s canonical replay text: the same format produced by `BraidSchedule.ToReplayText()` and accepted by `BraidSchedule.Parse(...)`. There is no separate syntax.

```text
hit <worker> <probe>
arrive <worker> <probe>
release <worker> <probe>
```

**Product goal:** find the interleaving, copy the token, keep the race fixed forever.

See also: [roadmap.md](roadmap.md), [v0.4.0-roadmap.md](design/v0.4.0-roadmap.md), [README.md](../README.md).

---

## When you already have a replay token

1. Paste the token into a test:

   ```csharp
   var schedule = BraidSchedule.Parse("""
   hit worker-1 after-read
   hit worker-2 after-read
   hit worker-1 before-write
   hit worker-2 before-write
   """);

   await Braid.RunAsync(test, new BraidOptions { Schedule = schedule, Iterations = 1 }, cancellationToken);
   ```

2. Or build a typed schedule with `BraidSchedule.Replay(...)` and `BraidStep` values.

3. Run until the assertion passes or the schedule is adjusted.

---

## When a random run fails (no token yet)

Random-only runs report **seed**, **iteration**, and **trace**. They do **not** synthesize a full replay token automatically.

1. **Capture** seed and trace from `BraidRunException.ToString()` or exception properties.
2. **Re-run** with the same seed to confirm the failure (`BraidOptions.Seed`).
3. **Add probes** at async boundaries in the code under test (`BraidProbe.HitAsync`).
4. **Build** a typed or text schedule that matches the interleaving you need (use trace lines as hints).
5. **Export** canonical text with `BraidSchedule.ToReplayText()` once the schedule reproduces the bug.
6. **Regression test** using `BraidSchedule.Parse(token)` or `BraidSchedule.Replay(...)`.

---

## When a replay-scheduled run fails

If `BraidOptions.Schedule` was configured, the failure may include exportable replay text.

Prefer the API over parsing `ToString()`:

```csharp
catch (BraidRunException ex)
{
    if (ex.TryGetReplayText(out var token, out var error))
    {
        // Paste `token` into BraidSchedule.Parse(...) for a regression test.
    }
    else
    {
        // Schedule present but not text-exportable (e.g. whitespace in worker/probe names).
        // Use ex.Schedule typed steps or fix naming.
    }
}
```

In xUnit, you can also write the full report without a separate package:

```csharp
ITestOutputHelper output; // inject in test
// ...
catch (BraidRunException ex)
{
    output.WriteLine(ex.ToString());
    throw;
}
```

---

## Scheduler diagnostics

When available, `ex.SchedulerDiagnostics` includes last matched replay step, waiting workers, held workers (after `Arrive`), and unused replay steps. Use these to fix incomplete or mismatched schedules—not to replace a replay token.

---

## Honest limits

| Situation | Replay token |
|-----------|----------------|
| Typed or text schedule configured and exportable | Yes — `TryGetReplayText` or failure report |
| Random-only run | No automatic full token — build schedule manually |
| Whitespace in worker id or probe name | Token export may fail; use typed `BraidStep` list |
| Empty schedule | No token |

---

## Runtime boundaries (probe placement)

- Only **one** logical probe wait may be in flight per forked worker at a time.
- A **concurrent** probe hit on the same worker (for example from a flowing child task while the parent waits) fails with a clear error.
- A probe in a child task **after** the parent’s probe completes is allowed (serialized).

See [runtime-boundaries.md](runtime-boundaries.md) and `tests/braid.tests/BraidRuntimeBoundaryTests.cs`.
