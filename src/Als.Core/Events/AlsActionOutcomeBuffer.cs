using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Events;

[InlineArray(AlsActionOutcomeBuffer.Capacity)]
internal struct AlsActionOutcomeStorage
{
    private AlsActionOutcome _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsActionOutcomeBuffer
{
    public const int Capacity = 2;

    private AlsActionOutcomeStorage _storage;

    public int Count { get; private set; }

    public AlsActionOutcome this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _storage[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(AlsActionOutcome outcome)
    {
        if (Count >= Capacity)
        {
            return false;
        }

        _storage[Count++] = outcome;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => Count = 0;
}
