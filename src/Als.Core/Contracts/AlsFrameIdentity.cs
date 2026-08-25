using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFrameIdentity
{
    public AlsFrameIdentity(long frameId, uint characterId, uint slotGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameId);
        ArgumentOutOfRangeException.ThrowIfZero(slotGeneration);

        FrameId = frameId;
        CharacterId = characterId;
        SlotGeneration = slotGeneration;
    }

    public long FrameId { get; }

    public uint CharacterId { get; }

    public uint SlotGeneration { get; }
}
