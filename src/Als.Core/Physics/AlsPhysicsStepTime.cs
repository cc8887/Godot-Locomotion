namespace GodotAls.Core.Physics;

// The native scene accepts float seconds; its double-precision evolution then
// uses that stored value. Convert once at the engine/world boundary so motion,
// kinematic velocities, Gather, solver iterations and sleep share the same dt.
// Pure Core solver APIs continue to use their explicitly supplied time step.
public static class AlsPhysicsStepTime
{
    public static double FromEngineSeconds(double seconds)
    {
        var stored = (float)seconds;
        if (!float.IsFinite(stored) || stored <= 0 || !float.IsFinite(1 / stored))
            throw new ArgumentOutOfRangeException(nameof(seconds), "Physics seconds must survive native float storage and inversion.");
        return stored;
    }
}
