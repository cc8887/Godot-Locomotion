using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

// Captured alongside ActionRequest in the immutable frame. Zero rate selects the
// compiled policy; a positive rate is an absolute Montage_Play rate, not a multiplier.
public readonly record struct AlsMontageActionParameters(float PlayRate, bool HasTargetYaw, float TargetYawDegrees)
{
    public bool IsValidFor(AlsActionCommand command) => command == AlsActionCommand.Start
        ? float.IsFinite(PlayRate) && PlayRate >= 0 && float.IsFinite(TargetYawDegrees) && (HasTargetYaw || TargetYawDegrees == 0)
        : this == default;
}
