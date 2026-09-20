namespace GodotAls.Core.Locomotion;

public readonly record struct AlsJumpSpeedBlend(bool Updated, float Interpolated)
{
    public float Alpha => Updated ? System.Math.Clamp(Interpolated, 0, 1) : 0;
    public float PoseAlpha => Alpha <= AlsPoseBlender.WeightThreshold ? 0 :
        Alpha >= 1 - AlsPoseBlender.WeightThreshold ? 1 : Alpha;

    public AlsJumpSpeedBlend Update(float speedMetres, float delta)
    {
        if (!float.IsFinite(speedMetres) || speedMetres < 0 || !float.IsFinite(delta) || delta < 0 || !float.IsFinite(Interpolated))
            throw new ArgumentException("Invalid Jump blend input.");
        // UE maps 200..500 cm/s without clamping, interpolates, then clamps the node alpha.
        var target = (speedMetres * 100 - 200) / 300;
        if (!float.IsFinite(target)) throw new ArgumentException("Jump speed overflow.");
        var distance = target - Interpolated;
        var value = !Updated || distance * distance < 1e-8f ? target :
            Interpolated + distance * System.Math.Clamp(delta * 5, 0, 1);
        return new(true, value);
    }
}

public readonly record struct AlsJumpBlendInputs(AlsJumpSpeedBlend Left, AlsJumpSpeedBlend Right)
{
    public AlsJumpBlendInputs Capture(in AlsGroundedMachineUpdate update, float speedMetres, float delta)
    {
        if (update.State.Kind != AlsGroundedMachineKind.Jump || !update.State.HasInitialized)
            throw new ArgumentException("Jump blend inputs require their own state machine.");
        var result = this;
        for (var i = 0; i < update.InitializationCount; i++)
            result = update.GetInitialization(i) switch { 1 => result with { Left = default },
                2 => result with { Right = default }, _ => result };
        for (var i = 0; i < update.UpdateCount; i++)
            result = update.GetUpdate(i).State switch { 1 => result with { Left = result.Left.Update(speedMetres, delta) },
                2 => result with { Right = result.Right.Update(speedMetres, delta) }, _ => result };
        return result;
    }
}
