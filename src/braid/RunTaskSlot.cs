namespace Braid;

internal static class RunTaskSlot
{
    private static readonly AsyncLocal<RunTask?> Slot = new();

    internal static RunTask? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }

    internal static void Clear() => Slot.Value = null;
}
