# User operation limiter example

This example shows a per-user operation limiter that is expected to allow at most one active operation for a configured user. It is ordinary library-style sample code (not tied to any specific product).

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

## Unsafe limiter: the test passes by expecting the bug

The unsafe implementation is **intentionally wrong**: both workers can be granted entry under that schedule. The xUnit test does **not** expect the braid run to succeed quietly. It uses `Assert.ThrowsAsync<BraidRunException>`: braid reproduces the race, the invariant breaks inside the run, and braid surfaces a `BraidRunException` with seed, schedule, and trace so you can regress the failure. A "green" test here means **"we reliably detected the broken behavior"**, not **"the limiter is correct"**.

## Locked limiter: the test passes by completing the run

The fixed implementation protects the read/check/write sequence with a lock. Its test runs the **same** replay schedule; the run completes without `BraidRunException` because only one worker can enter. Probes are placed before and after the synchronized entry attempt so probe-controlled awaits are not taken while holding a synchronous lock.

Run the example with:

```powershell
dotnet test examples/user-operation-limiter/Braid.Examples.UserOperationLimiter.csproj --configuration Release
```
