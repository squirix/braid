namespace Braid.Examples.CacheCasRace;

/// <summary>
/// Represents a value paired with a monotonic version for optimistic concurrency.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Value">The stored value.</param>
/// <param name="Version">The version observed or written.</param>
public sealed record VersionedEntry<T>(T Value, long Version);
