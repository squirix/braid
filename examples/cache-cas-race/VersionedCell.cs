namespace Braid.Examples.CacheCasRace;

/// <summary>
/// A minimal in-memory cell with versioned reads and compare-and-set writes.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class VersionedCell<T>
    where T : notnull
{
    private readonly Lock _gate = new();
    private VersionedEntry<T> _entry;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionedCell{T}"/> class with version <c>1</c>.
    /// </summary>
    /// <param name="initialValue">The initial value.</param>
    public VersionedCell(T initialValue)
    {
        _entry = new VersionedEntry<T>(initialValue, 1);
    }

    /// <summary>
    /// Reads the current entry under a lock.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current entry snapshot.</returns>
    public Task<VersionedEntry<T>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_entry);
        }
    }

    /// <summary>
    /// Unconditionally sets a new value and increments the version.
    /// </summary>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task SetAsync(T value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entry = new VersionedEntry<T>(value, _entry.Version + 1);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the value only when the current version matches <paramref name="expectedVersion"/>.
    /// </summary>
    /// <param name="expectedVersion">The version observed by the caller.</param>
    /// <param name="value">The new value to write when the version matches.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The CAS outcome.</returns>
    public Task<CasResult> CompareAndSetAsync(long expectedVersion, T value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_entry.Version != expectedVersion)
            {
                return Task.FromResult(CasResult.VersionMismatch);
            }

            _entry = new VersionedEntry<T>(value, _entry.Version + 1);
            return Task.FromResult(CasResult.Ok);
        }
    }
}
