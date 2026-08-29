using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct AlsFootPlacementReleaseSignals(
    byte LeftPlatformRemoved,
    int LeftPlatformId,
    long LeftColliderId,
    byte RightPlatformRemoved,
    int RightPlatformId,
    long RightColliderId)
{
    public static AlsFootPlacementReleaseSignals CreateDefault() =>
        new(0, -1, -1, 0, -1, -1);
}
