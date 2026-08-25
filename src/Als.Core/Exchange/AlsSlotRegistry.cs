namespace GodotAls.Core.Exchange;

public readonly record struct AlsSlotHandle(
    uint CharacterId,
    uint Generation);

public sealed class AlsSlotRegistry
{
    private readonly uint[] _generations;
    private readonly bool[] _occupied;

    public AlsSlotRegistry(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _generations = new uint[capacity];
        _occupied = new bool[capacity];
    }

    public AlsSlotHandle Acquire()
    {
        for (var index = 0; index < _occupied.Length; index++)
        {
            if (_occupied[index])
            {
                continue;
            }

            _occupied[index] = true;
            _generations[index] = _generations[index] == uint.MaxValue
                ? 1
                : _generations[index] + 1;

            return new AlsSlotHandle((uint)index, _generations[index]);
        }

        throw new InvalidOperationException("No ALS character slot is available.");
    }

    public bool Release(AlsSlotHandle handle)
    {
        if (!IsCurrent(handle))
        {
            return false;
        }

        _occupied[(int)handle.CharacterId] = false;
        return true;
    }

    public bool IsCurrent(AlsSlotHandle handle)
    {
        if (handle.CharacterId >= (uint)_occupied.Length)
        {
            return false;
        }

        var index = (int)handle.CharacterId;
        return _occupied[index] && _generations[index] == handle.Generation;
    }
}
