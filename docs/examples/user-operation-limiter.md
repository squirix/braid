# User operation limiter example

This example shows a per-user operation limiter that is expected to allow at most one active operation for a configured user.

Each limiter instance is constructed with one `userId` and one `limit`. The unsafe implementation still uses an in-memory dictionary internally so the read/check/write race is visible: with a limit of `1`, two workers can both read `0`, both pass the limit check, and both write `1`.

The tests are intentionally small because callers do not repeat the user and limit on every operation:

```csharp
var limiter = new UserOperationLimiter("user-1", 1);
var allowed = await limiter.TryEnterAsync(cancellationToken);
```

The braid test forces that interleaving with a typed replay schedule:

```text
worker-1 @ after-read
worker-2 @ after-read
worker-1 @ before-write
worker-2 @ before-write
```

Because the implementation is broken, the invariant fails: both workers are allowed to enter. braid reports the seed, schedule, and trace so the failure can be reproduced.

The fixed implementation protects the read/check/write sequence with a lock. Its test uses probes before and after the synchronized entry attempt because probe-controlled awaits must not be placed inside a synchronous lock block.

Run the example with:

```powershell
dotnet test examples/user-operation-limiter/Braid.Examples.UserOperationLimiter.csproj --configuration Release
```
