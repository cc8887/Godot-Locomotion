namespace GodotAls.Core.Actions;

public readonly record struct AlsMontagePositionOverride(long InstanceId, float Position);

/// <summary>Immutable physics-to-animation handoff. Time belongs to the root
/// motion source; the physical montage is sought before its ordinary Advance.</summary>
public readonly record struct AlsMantlingFrame(bool Active, bool Started, bool Interrupted,
    int DefinitionId, float Time, float StartTime, float Rate, float Height, AlsMantlingType Type)
{
    public void Validate()
    {
        if (Started && !Active || Active && (Interrupted || DefinitionId < 0 || !float.IsFinite(Time) || Time < 0 ||
            !float.IsFinite(StartTime) || StartTime < 0 || !float.IsFinite(Rate) || Rate <= 0 ||
            !float.IsFinite(Height) || Height <= 0 || Type is < AlsMantlingType.High or > AlsMantlingType.InAir))
            throw new ArgumentException("Invalid mantle physics snapshot.");
    }
    public float PositionBeforeAdvance(float delta)
    {
        Validate();
        if (!Active || !float.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid mantle advancement.");
        return MathF.Max(0, StartTime + (Time + delta) * Rate - delta);
    }
}
