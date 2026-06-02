# Lost update example

This example turns a classic read-modify-write race into a stable replay regression.

Two workers read the same integer value, both stop at `after-read`, then both stop again at `before-write`. The replay schedule releases them so each worker writes `current + 1` from the same original value. The final assertion expects `2`, so the reproduced interleaving fails with a `BraidRunException`.

## Replay token

The example uses text replay so the important interleaving is copyable:

```text
hit worker-1 after-read
hit worker-2 after-read
hit worker-1 before-write
hit worker-2 before-write
```

The test catches the failure and calls `TryGetReplayText` to prove the report contains the same token. In a real bug fix, paste that token into a regression test and keep it next to the code that prevents the lost update.

## Run it

```bash
dotnet test examples/lost-update/Braid.Examples.LostUpdate.csproj
```

The test passes because it expects the failing interleaving and verifies the replay token.
