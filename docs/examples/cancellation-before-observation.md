# Cancellation before observation example

This example models a race where cancellation must win before an operation is recorded as observed.

One worker is about to observe the operation and stops at `before-observe`. A second worker cancels the operation and stops at `cancelled`. The replay schedule releases cancellation first, then lets the observer continue. The observer checks the cancellation source before recording the operation, so the final assertion verifies the operation was not observed.

## Replay schedule

```text
hit worker-2 cancelled
hit worker-1 before-observe
```

This schedule is intentionally small: it captures only the ordering that matters for the regression.

## Run it

```bash
dotnet test examples/cancellation-before-observation/Braid.Examples.CancellationBeforeObservation.csproj
```

Use this pattern when an async operation can be cancelled before it reaches the state your production code treats as visible.
