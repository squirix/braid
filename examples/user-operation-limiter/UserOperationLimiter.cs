namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates an intentionally unsafe per-user operation limiter.
/// </summary>
public sealed class UserOperationLimiter
{
    private readonly Dictionary<string, int> _activeOperations = new(StringComparer.Ordinal);
    private readonly int _limit;
    private readonly string _userId;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserOperationLimiter" /> class.
    /// </summary>
    /// <param name="userId">The configured user identifier.</param>
    /// <param name="limit">The maximum active operations allowed for the configured user.</param>
    public UserOperationLimiter(string userId, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        _userId = userId;
        _limit = limit;
    }

    /// <summary>
    /// Attempts to enter an operation slot for the configured user.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> when the operation is allowed; otherwise, <see langword="false" />.</returns>
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        _ = _activeOperations.TryGetValue(_userId, out var current);
        await BraidProbe.HitAsync("after-read", cancellationToken);

        if (current >= _limit)
        {
            return false;
        }

        await BraidProbe.HitAsync("before-write", cancellationToken);
        _activeOperations[_userId] = current + 1;
        return true;
    }
}
