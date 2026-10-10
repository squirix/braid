# Runtime boundaries

Braid switches between workers only at the start of each worker and at explicit `Probe.HitAsync` calls. These rules keep behavior deterministic and failures understandable.

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
- The run callback starts on the thread pool, not inline in the caller and without the caller's synchronization context or task scheduler,
  in every iteration and every explored schedule. Forked workers start there too.
- `JoinAsync` cannot be called from a forked worker: the join would wait for the worker itself.
- A failed `JoinAsync` is final: a later join in the same run, including the one the run performs after the callback, throws the same exception.
  A callback that catches the failure of its join does not make the run pass. Only canceling the run token changes the outcome:
  the run then ends with `OperationCanceledException`.
- Fork delegates must return a non-null `Task`.
- The run callback must return a non-null `Task`. A null task fails the run with `RunException` (`RunFailureOrigin.UserTest`, inner
  `InvalidOperationException`), in `RunAsync` and `ExploreAsync` alike: it is found inside the run, so the failure carries the seed and the iteration.
- Probe names cannot be null, empty, or whitespace.
- A replay schedule has at least one step and must be fully consumed.

The XML documentation of `Runner.RunAsync`, `RunContext` and `ReplaySchedule.Replay` lists the exceptions for each rule.

---

## Code before the first probe

braid orders workers at their starts and at their probes. The start of a worker is a scheduling point: the worker runs from its beginning
up to its first probe, and the scheduler chooses when.

- A random run chooses the order of the starts like the order at probes, so one seed starts the workers in one order and another seed in another.
- A replay schedule starts a worker with a `start` step (`ReplayStep.Start("worker-2")`, text `start worker-2`). A schedule without start steps
  starts every worker before its first step, in fork order, as schedules did before start steps existed. A schedule with start steps starts
  only the workers it names.
- `ExploreAsync` tries the orders of the starts together with the orders at probes, also for workers that hit no probe. Its discovery run
  starts every worker first, in fork order, to learn the probes of each. The schedules that start every worker before any hit come first,
  so other start orders need a `MaxSchedules` above the number of hit orders: two workers with five probes each have 252 hit orders
  and 924 schedules in all.
- A worker that waits, before its first probe, for something another worker does before its own first probe hangs when it starts first.
  A random run then times out for the seeds that start it first; `ExploreAsync` skips those schedules after the first one that hangs.
- Between two scheduling points a worker runs alone. Code between a worker's start and its first probe is one such stretch:
  another worker runs before it or after it, never in the middle. Put a probe inside it where the race needs a switch,
  as in `examples/single-file/lost-update`.
- Forked workers start only when the run joins. A callback that waits for a forked worker before `JoinAsync` never continues:
  the run fails with `RunFailureOrigin.Timeout` once `RunOptions.Timeout` elapses, and canceling the run token ends it.
  The timeout counts from the start of the callback. A callback that blocks synchronously (for example with `Task.Wait`) is not bounded.

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
- Canceling the token passed to `Probe.HitAsync` does not wake a parked worker. The worker observes the cancellation when the scheduler
  releases it: the probe then throws `OperationCanceledException`. A worker that cancels the token and then waits for the parked worker
  to react, before its own next probe, hangs the same way. A probe hit with an already canceled token is still a scheduling point.
  To stop a run, cancel the token passed to `RunAsync` / `JoinAsync`; a probe token alone does not stop it.

---

## Workers that outlive a failed run

When a run fails or times out, braid cancels the probe waits of parked workers and waits about one second for every worker to finish.
A worker still running after that, for example one awaiting I/O or a delay that ignores cancellation, is abandoned:

- The failure message names the abandoned workers.
- Their next probe throws `OperationCanceledException`.
- Until then they keep running and can change shared state, also while `ExploreAsync` runs later schedules.
- A run canceled through its `CancellationToken` throws `OperationCanceledException` and does not list abandoned workers.

---

## What this is not

These boundaries are **not** automatic `await` interception. You choose where probes go; Braid does not rewrite IL or replace `TaskScheduler`.
