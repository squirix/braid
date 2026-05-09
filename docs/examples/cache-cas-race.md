# Cache compare-and-set race example

This example is a minimal **versioned cell**: each read returns a value and a monotonic **version**. A **compare-and-set (CAS)** write succeeds only if the caller’s expected version still matches the cell. If another writer bumps the version first, CAS returns **`VersionMismatch`** instead of silently overwriting.

## Why this matters

Stale reads are dangerous for caches and configuration cells: a worker may read `version = 1`, pause, then another worker updates the entry to `version = 2`. If the first worker writes without rechecking, it can **clobber** the newer data. CAS ties the write to the version observed at read time so the library can reject stale updates.

## Why stress tests miss this

The bad interleaving requires worker 1 to block **after** the read but **before** CAS while worker 2 runs a full update. That window is narrow; under normal scheduling the race may almost never appear. Braid makes the ordering **explicit** with probes and a **replay schedule**.

## How braid forces the interleaving

We use a three-step replay schedule:

```csharp
BraidSchedule.Replay(
    BraidStep.Arrive("worker-1", "before-cas"),
    BraidStep.Hit("worker-2", "updated"),
    BraidStep.Release("worker-1", "before-cas"))
```

The same schedule as text:

```text
arrive worker-1 before-cas
hit worker-2 updated
release worker-1 before-cas
```

Meaning:

1. **`Arrive`** — Wait until worker 1 blocks at probe `before-cas` (after `GetAsync`, before `CompareAndSetAsync`), and **keep it held** there.
2. **`Hit`** — Release worker 2 when it reaches probe `updated` (after `SetAsync` has already bumped the version).
3. **`Release`** — Release worker 1 so it continues with CAS using the **old** expected version from step 1.

So worker 2’s update happens **while worker 1 is held** at `before-cas`. When worker 1 resumes, the cell is already at **version 2**; CAS with expected version **1** correctly returns **`VersionMismatch`**.

## Running the example tests

```powershell
dotnet test examples/cache-cas-race/Braid.Examples.CacheCasRace.csproj --configuration Release
```

## Implementation note

The sample `VersionedCell<T>` uses a simple **`lock`** for clarity. The point of the example is **deterministic interleaving** around public `async` APIs and probes, not lock-free algorithms.
