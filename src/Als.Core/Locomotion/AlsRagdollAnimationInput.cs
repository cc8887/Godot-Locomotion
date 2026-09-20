namespace GodotAls.Core.Locomotion;

public sealed record AlsRagdollAnimationInput(string VelocityBone, double InputMinimum, double InputMaximum,
    double OutputMinimum, double OutputMaximum)
{
    // Native GetPhysicsLinearVelocity on the owning mesh, in UE centimeters/s.
    // Do not replace it with actor movement velocity or horizontal speed.
    public double FlailRate(AlsDoubleVector physicsVelocity)
    {
        if (!physicsVelocity.IsFinite || !double.IsFinite(physicsVelocity.LengthSquared) ||
            !double.IsFinite(InputMinimum) || !double.IsFinite(InputMaximum) || InputMaximum <= InputMinimum ||
            !double.IsFinite(OutputMinimum) || !double.IsFinite(OutputMaximum))
            throw new ArgumentException("Invalid Ragdoll velocity or mapping.");
        var alpha = System.Math.Clamp((System.Math.Sqrt(physicsVelocity.LengthSquared) - InputMinimum) / (InputMaximum - InputMinimum), 0, 1);
        return OutputMinimum + alpha * (OutputMaximum - OutputMinimum);
    }
}
