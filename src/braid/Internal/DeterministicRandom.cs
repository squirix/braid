namespace Braid.Internal;

internal sealed class DeterministicRandom
{
    private uint _state;

    internal DeterministicRandom(int seed)
    {
        _state = unchecked((uint)seed);

        if (_state == 0)
        {
            _state = 0x9E3779B9;
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
        var value = _state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _state = value;
        return value;
    }
}
