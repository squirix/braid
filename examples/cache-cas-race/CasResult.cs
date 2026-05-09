namespace Braid.Examples.CacheCasRace;

/// <summary>
/// Describes the outcome of a compare-and-set attempt on a versioned cell.
/// </summary>
public enum CasResult
{
    /// <summary>
    /// The value was updated successfully.
    /// </summary>
    Ok,

    /// <summary>
    /// The expected version did not match the current version.
    /// </summary>
    VersionMismatch,
}
