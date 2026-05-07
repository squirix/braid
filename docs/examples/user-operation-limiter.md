# User operation limiter example

This example shows a per-user operation limiter that is expected to allow at most one active operation per user.

The unsafe implementation uses an in-memory dictionary and performs a read/check/write sequence without synchronization. With a limit of `1`, two workers can both read `0`, both
pass the limit check, and both write `1`.

The braid test forces that interleaving with a typed replay schedule:

```text
worker-1 @ after-read
worker-2 @ after-read
worker-1 @ before-write
worker-2 @ before-write
```

Because the implementation is broken, the invariant fails: both workers are allowed to enter. braid reports the seed, schedule, and trace so the failure can be reproduced.

The fixed implementation protects the read/check/write sequence with a lock. Its test uses probes before and after the synchronized entry attempt because probe-controlled awaits
must not be placed inside a synchronous lock block.

Run the example with:

```powershell
dotnet test examples/user-operation-limiter/Braid.Examples.UserOperationLimiter.csproj --configuration Release
```
