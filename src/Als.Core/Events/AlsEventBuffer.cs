using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Events;

[InlineArray(AlsEventBuffer.Capacity)]
internal struct AlsEventStorage
{
    private AlsAnimationEvent _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsEventBuffer
{
    public const int Capacity = 16;

    private AlsEventStorage _storage;

    public int Count { get; private set; }

    public AlsAnimationEvent this[int index]
    {
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

    public bool TryAdd(AlsAnimationEvent animationEvent)
    {
        if (Count >= Capacity)
        {
            return false;
        }

        _storage[Count++] = animationEvent;
        return true;
    }

    public void Clear() => Count = 0;
}
