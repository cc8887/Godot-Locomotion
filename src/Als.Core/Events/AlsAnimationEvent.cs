using System.Runtime.InteropServices;

namespace GodotAls.Core.Events;

public enum AlsAnimationEventPhase : byte
{
    Trigger,
    Begin,
    Tick,
    End,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAnimationEvent(
    int EventId,
    float AnimationTime,
    float Weight,
    AlsAnimationEventPhase Phase);
