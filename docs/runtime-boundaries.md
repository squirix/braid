# Runtime boundaries

Braid controls scheduling only at explicit `Probe.HitAsync` calls. These rules keep behavior deterministic and failures understandable.

See also: [replay-token-workflow.md](replay-token-workflow.md), [README.md](../README.md).

---

## One probe wait per forked worker

Each logical worker created with `RunContext.Fork` may have **at most one** probe wait in flight at a time.

If a worker calls `HitAsync` while already waiting at another probe, the run fails with a clear error (for example `Concurrent probe hit on the same worker is not supported.`).

**Why:** A single worker represents one sequential async flow at the scheduler level; overlapping waits would make replay matching ambiguous.

---

## Flowing child tasks on the same worker

A forked worker may start work on another thread or task (for example `Task.Run`) that shares the same logical worker id.

| Pattern | Result |
| ------- | ------ |
| Child hits a probe **while** the parent is still waiting at a different probe | **Rejected** — concurrent probe hit on the same worker |
| Child hits a probe **after** the parent’s probe has completed and released | **Allowed** — serialized probes on one worker |

Tests: `BraidProbeConcurrencyBoundaryTests.ProbeInsideFlowingFailsOrSerializes`, `ProbeInsideFlowingAfterParentSucceeds`.

---

## Outside a braid run

`Probe.HitAsync` completes immediately when no `Runner.RunAsync` is active. Probes do not schedule outside a run.

---

## Other lifecycle rules

- Nested `Runner.RunAsync` calls are not supported.
- `RunContext` is valid only during the active run callback.
- Fork delegates must return a non-null `Task`.
- Probe names cannot be null, empty, or whitespace.
- Non-empty replay schedules must be fully consumed.

The XML documentation of `Runner.RunAsync` and `RunContext` lists the exceptions for each rule.

---

## Waiting for a parked worker

braid runs one released worker at a time and keeps the others parked before their start or at their probes until the released worker
reaches its next probe or completes. A released worker that waits for a parked worker before its next probe (for example for a lock that
the parked worker holds across a probe) never continues, and the run times out. Real threads would not hang there.

- A timeout while a worker runs and others are parked is reported with `RunFailureOrigin.Scheduler`. The failure report lists the running
  worker under **Running workers** and the parked ones under **Waiting workers**; `(start)` and `(not started)` mark workers before their first probe.
- `ExploreAsync` skips such schedules, including a discovery run that timed out this way, so exploration can still complete without failure.
- A timeout with no running worker blocked on a parked one is reported with `RunFailureOrigin.Timeout` and fails `ExploreAsync`.
- A deadlock between two or more workers looks the same as such a hang, so `ExploreAsync` does not report it yet.
- Do not hold a lock that another worker waits for across a probe.

---

## What this is not

These boundaries are **not** automatic `await` interception. You choose where probes go; Braid does not rewrite IL or replace `TaskScheduler`.
