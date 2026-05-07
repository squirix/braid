namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates a synchronized per-user operation limiter.
/// </summary>
public sealed class LockedUserOperationLimiter
{
    private readonly Dictionary<string, int> activeOperations = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>
    /// Attempts to enter a per-user operation slot.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="limit">The maximum active operations allowed for the user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> when the operation is allowed; otherwise, <see langword="false" />.</returns>
    public async Task<bool> TryEnterAsync(string userId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

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
    /// Exits a previously entered per-user operation slot.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    public void Exit(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

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
