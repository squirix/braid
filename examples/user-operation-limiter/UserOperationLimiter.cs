namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates an intentionally unsafe per-user operation limiter.
/// </summary>
public sealed class UserOperationLimiter
{
    private readonly Dictionary<string, int> activeOperations = new(StringComparer.Ordinal);

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

        _ = activeOperations.TryGetValue(userId, out var current);
        await BraidProbe.HitAsync("after-read", cancellationToken);

        if (current >= limit)
        {
            return false;
        }

        await BraidProbe.HitAsync("before-write", cancellationToken);
        activeOperations[userId] = current + 1;
        return true;
    }

    /// <summary>
    /// Exits a previously entered per-user operation slot.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    public void Exit(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

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
