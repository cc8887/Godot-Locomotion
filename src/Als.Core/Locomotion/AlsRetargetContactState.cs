namespace GodotAls.Core.Locomotion;

// Caller-owned value state. The character commits or discards it with its pose;
// default resets it for a model generation, interruption, or ragdoll transition.
// Release duration is a presentation policy, not a physical or native ALS claim.
public readonly record struct AlsRetargetContactState
{
    public bool Initialized { get; init; }
    public AlsDoubleVector RawCorrection { get; init; }
    public AlsDoubleVector RealizedCorrection { get; init; }
    public AlsDoubleVector Velocity { get; init; }
    public double ContactWeight { get; init; }
    public bool Releasing { get; init; }
    public AlsDoubleVector ReleaseStartCorrection { get; init; }
    public AlsDoubleVector ReleaseVelocity { get; init; }
    public float ReleaseElapsedSeconds { get; init; }
    public float ReleaseDurationSeconds { get; init; }

    internal void Validate(double budget, int contactCount)
    {
        if (!RawCorrection.IsFinite || !RealizedCorrection.IsFinite || !Velocity.IsFinite ||
            !ReleaseStartCorrection.IsFinite || !ReleaseVelocity.IsFinite || !double.IsFinite(ContactWeight) ||
            ContactWeight < 0 || ContactWeight > contactCount || !float.IsFinite(ReleaseElapsedSeconds) ||
            !float.IsFinite(ReleaseDurationSeconds) || ReleaseElapsedSeconds < 0 || ReleaseDurationSeconds < 0 ||
            Releasing && (ReleaseDurationSeconds <= 0 || ReleaseElapsedSeconds > ReleaseDurationSeconds) ||
            RawCorrection.LengthSquared > (budget+1e-8)*(budget+1e-8) ||
            RealizedCorrection.LengthSquared > (budget+1e-8)*(budget+1e-8) ||
            ReleaseStartCorrection.LengthSquared > (budget+1e-8)*(budget+1e-8) ||
            !Initialized && this != default)
            throw new ArgumentException("Invalid committed retarget contact state.");
    }

    internal static AlsRetargetContactState Preference(in AlsRetargetContactState previous,
        AlsDoubleVector raw, double contactWeight, float delta, float duration)
    {
        var releasing = previous.Initialized && duration > 0 &&
            (previous.Releasing || contactWeight < previous.ContactWeight-1e-6) &&
            contactWeight <= previous.ContactWeight+1e-6;
        var start = previous.Releasing ? previous.ReleaseStartCorrection : previous.RealizedCorrection;
        var velocity = previous.Releasing ? previous.ReleaseVelocity : previous.Velocity;
        var length = previous.Releasing ? previous.ReleaseDurationSeconds : duration;
        var elapsed = previous.Releasing ? previous.ReleaseElapsedSeconds+delta : delta;
        releasing &= elapsed < length-1e-7f;
        var desired = raw;
        if (releasing)
        {
            // Blend from the realized release origin towards the current raw
            // preference. Its changing input is faded in by the same envelope,
            // so a subsequent support-curve step cannot instantly drop the body.
            desired += new AlsDoubleVector(
                AlsInertialDecay.Evaluate((float)(start.X-raw.X),(float)velocity.X,elapsed,length),
                AlsInertialDecay.Evaluate((float)(start.Y-raw.Y),(float)velocity.Y,elapsed,length),
                AlsInertialDecay.Evaluate((float)(start.Z-raw.Z),(float)velocity.Z,elapsed,length));
        }
        return new()
        {
            Initialized = true, RawCorrection = raw, RealizedCorrection = desired,
            ContactWeight = contactWeight, Releasing = releasing,
            ReleaseStartCorrection = releasing ? start : default,
            ReleaseVelocity = releasing ? velocity : default,
            ReleaseElapsedSeconds = releasing ? elapsed : 0,
            ReleaseDurationSeconds = releasing ? length : 0
        };
    }
}
