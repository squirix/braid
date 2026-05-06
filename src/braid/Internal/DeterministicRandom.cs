namespace Braid.Internal;

internal sealed class DeterministicRandom
{
    private uint state;

    internal DeterministicRandom(int seed)
    {
        state = unchecked((uint)seed);

        if (state == 0)
        {
            state = 0x9E3779B9;
        }
    }

    internal int NextInt32(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);

        var value = NextUInt32();
        return (int)(value % (uint)exclusiveMax);
    }

    private uint NextUInt32()
    {
        var value = state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        state = value;
        return value;
    }
}
