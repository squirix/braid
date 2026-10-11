# Lost update example

This example turns a classic read-modify-write race into a stable replay regression. To discover the failing interleaving without writing the schedule by hand, see [explore-lost-update](explore-lost-update.md).

Two workers increment the same integer. Each worker stops at `before-read`, reads the value, stops at `before-write`, and writes `current + 1`.
Because the read happens after a probe, the schedule decides whether both workers read the same value.
The replay schedule below releases both reads before either write, so both write `1` and the final assertion, which expects `2`, fails with a `RunException`.
A sequential schedule (`worker-1` reads and writes, then `worker-2`) keeps both increments; the example tests that too.

A worker runs alone between two scheduling points, and its start is one of them: this replay token has no `start` lines,
so both workers start before its first line, in fork order. Put a probe before every shared read or write that the race needs a switch at.

## Replay token

The example uses text replay so the important interleaving is copyable:

```text
hit worker-1 before-read
hit worker-2 before-read
hit worker-1 before-write
hit worker-2 before-write
```

The test catches the failure and calls `TryGetReplayText` to prove the report contains the same token.
In a real bug fix, paste that token into a regression test and keep it next to the code that prevents the lost update.

## Run it

```bash
dotnet run examples/single-file/lost-update/lost-update.cs
```

The test passes because it expects the failing interleaving and verifies the replay token.
