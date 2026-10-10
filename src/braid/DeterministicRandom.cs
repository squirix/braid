namespace Braid;

internal sealed class DeterministicRandom
{
    private uint _state;

    internal DeterministicRandom(int seed)
    {
        Seed = seed;
        _state = Scramble(uint.CreateTruncating(seed));

        if (_state == 0)
            _state = 0x9E3779B9;
    }

    /// <summary>Gets the seed the sequence started from.</summary>
    internal int Seed { get; }

    internal int NextInt32(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);

        var value = NextUInt32();
        var modulus = uint.CreateTruncating(exclusiveMax);
        return int.CreateTruncating(value % modulus);
    }

    /// <summary>
    /// Mixes the seed over all bits. Without it the first value of a small seed is a multiple of that seed,
    /// so nearby small seeds make related first choices: with three workers, seeds 1 to 15 all chose the first one.
    /// </summary>
    /// <param name="seed">The seed.</param>
    /// <returns>The initial state.</returns>
    private static uint Scramble(uint seed)
    {
        unchecked
        {
            var value = seed + 0x9E3779B9;
            value ^= value >> 16;
            value *= 0x85EBCA6B;
            value ^= value >> 13;
            value *= 0xC2B2AE35;
            value ^= value >> 16;
            return value;
        }
    }

    private uint NextUInt32()
    {
        var value = _state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _state = value;
        return value;
    }
}
