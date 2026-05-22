# Runtime boundaries

Braid controls scheduling only at explicit `BraidProbe.HitAsync` calls. These rules keep behavior deterministic and failures understandable.

See also: [replay-token-workflow.md](replay-token-workflow.md), [README.md](../README.md) (Run lifecycle).

---

## One probe wait per forked worker

Each logical worker created with `BraidContext.Fork` may have **at most one** probe wait in flight at a time.

If a worker calls `HitAsync` while already waiting at another probe, the run fails with a clear error (for example `Concurrent probe hit on the same worker is not supported.`).

**Why:** A single worker represents one sequential async flow at the scheduler level; overlapping waits would make replay matching ambiguous.

---

## Flowing child tasks on the same worker

A forked worker may start work on another thread or task (for example `Task.Run`) that shares the same logical worker id.

| Pattern | Result |
|---------|--------|
| Child hits a probe **while** the parent is still waiting at a different probe | **Rejected** — concurrent probe hit on the same worker |
| Child hits a probe **after** the parent’s probe has completed and released | **Allowed** — serialized probes on one worker |

Tests: `BraidRuntimeBoundaryTests.ProbeInsideFlowingChildTaskConcurrentWithParentFailsClearlyOrSerializes`, `ProbeInsideFlowingChildTaskAfterParentProbeCompletesSucceeds`.

---

## Outside a braid run

`BraidProbe.HitAsync` completes immediately when no `Braid.RunAsync` is active. Probes do not schedule outside a run.

---

## Other lifecycle rules

- Nested `Braid.RunAsync` calls are not supported.
- `BraidContext` is valid only during the active run callback.
- Fork delegates must return a non-null `Task`.
- Probe names cannot be null, empty, or whitespace.
- Non-empty replay schedules must be fully consumed.

Full list: README **Run lifecycle**.

---

## What this is not

These boundaries are **not** automatic `await` interception. You choose where probes go; Braid does not rewrite IL or replace `TaskScheduler`.
