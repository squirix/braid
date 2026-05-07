namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates a synchronized per-user operation limiter.
/// </summary>
public sealed class LockedUserOperationLimiter
{
    private readonly Dictionary<string, int> activeOperations = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly int limit;
    private readonly string userId;

    /// <summary>
    /// Initializes a new instance of the <see cref="LockedUserOperationLimiter" /> class.
    /// </summary>
    /// <param name="userId">The configured user identifier.</param>
    /// <param name="limit">The maximum active operations allowed for the configured user.</param>
    public LockedUserOperationLimiter(string userId, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        this.userId = userId;
        this.limit = limit;
    }

    /// <summary>
    /// Attempts to enter an operation slot for the configured user.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> when the operation is allowed; otherwise, <see langword="false" />.</returns>
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken = default)
    {
        await BraidProbe.HitAsync("before-enter", cancellationToken);
        bool allowed;

        lock (gate)
        {
            _ = activeOperations.TryGetValue(userId, out var current);
            if (current >= limit)
            {
                allowed = false;
            }
            else
            {
                activeOperations[userId] = current + 1;
                allowed = true;
            }
        }

        await BraidProbe.HitAsync("after-enter", cancellationToken);
        return allowed;
    }

    /// <summary>
    /// Exits a previously entered operation slot for the configured user.
    /// </summary>
    public void Exit()
    {
        lock (gate)
        {
            if (!activeOperations.TryGetValue(userId, out var current))
            {
                return;
            }

            if (current <= 1)
            {
                _ = activeOperations.Remove(userId);
                return;
            }

            activeOperations[userId] = current - 1;
        }
    }
}
