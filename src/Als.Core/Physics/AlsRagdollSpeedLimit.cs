using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Native units: cm/s. Apply only to the ragdoll body prefix, never environment
// bodies. Work on candidate states and publish the returned value only when the
// owning frame commits; retrying from the old value does not consume a refresh.
public readonly record struct AlsRagdollSpeedLimit
{
    public float SpeedLimit { get; }
    public int RefreshesRemaining { get; }
    private AlsRagdollSpeedLimit(float speedLimit, int remaining)
    { SpeedLimit = speedLimit; RefreshesRemaining = remaining; }

    public static AlsRagdollSpeedLimit Begin(bool enabled, AlsDoubleVector characterVelocity,
        Span<AlsIslandBodyState> candidateBodies)
    {
        if (!enabled) return default;
        var speed = (float)System.Math.Sqrt(characterVelocity.LengthSquared);
        if (!characterVelocity.IsFinite || !float.IsFinite(speed))
            throw new ArgumentException("Ragdoll entry requires a finite character speed.");
        var state = new AlsRagdollSpeedLimit(System.Math.Max(200f, speed), 8);
        state.Clamp(candidateBodies); // Entry is additional to the eight refreshes.
        return state;
    }

    public AlsRagdollSpeedLimit Refresh(Span<AlsIslandBodyState> candidateBodies)
    {
        if (RefreshesRemaining == 0) return this;
        Clamp(candidateBodies);
        return new(SpeedLimit, RefreshesRemaining - 1);
    }

    private void Clamp(Span<AlsIslandBodyState> bodies)
    {
        // Validate the entire prefix before any candidate is changed.
        foreach (ref readonly var body in bodies)
            if (!new AlsDoubleVector(body.Velocity.Linear).IsFinite)
                throw new ArgumentException("Ragdoll body velocity must be finite.");
        // ALS squares its float setting before comparing to FVector's double
        // size squared. Normalize in double, then store Chaos float velocity.
        var squareLimit = SpeedLimit * SpeedLimit;
        for (var i = 0; i < bodies.Length; i++)
        {
            var velocity = new AlsDoubleVector(bodies[i].Velocity.Linear);
            if (velocity.LengthSquared <= squareLimit) continue;
            var normalized = velocity * (1 / System.Math.Sqrt(velocity.LengthSquared));
            bodies[i] = bodies[i] with
            { Velocity = bodies[i].Velocity with { Linear = (normalized * SpeedLimit).ToSingle() } };
        }
    }
}
