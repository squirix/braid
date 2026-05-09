namespace Braid.Examples.UserOperationLimiter;

/// <summary>
/// Demonstrates a synchronized per-user operation limiter.
/// </summary>
public sealed class LockedUserOperationLimiter
{
    private readonly Dictionary<string, int> _activeOperations = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly int _limit;
    private readonly string _userId;

    /// <summary>
    /// Initializes a new instance of the <see cref="LockedUserOperationLimiter" /> class.
    /// </summary>
    /// <param name="userId">The configured user identifier.</param>
    /// <param name="limit">The maximum active operations allowed for the configured user.</param>
    public LockedUserOperationLimiter(string userId, int limit)
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
        await BraidProbe.HitAsync("before-enter", cancellationToken);
        bool allowed;

        lock (_gate)
        {
            _ = _activeOperations.TryGetValue(_userId, out var current);
            if (current >= _limit)
            {
                allowed = false;
            }
            else
            {
                _activeOperations[_userId] = current + 1;
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
        lock (_gate)
        {
            if (!_activeOperations.TryGetValue(_userId, out var current))
            {
                return;
            }

            if (current <= 1)
            {
                _ = _activeOperations.Remove(_userId);
                return;
            }

            _activeOperations[_userId] = current - 1;
        }
    }
}
