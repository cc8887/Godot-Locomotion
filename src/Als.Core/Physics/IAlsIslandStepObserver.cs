using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Optional diagnostics only. All spans are borrowed and read-only. Exceptions
// abort the step before publication; Complete records solver output, not a
// promise that later history staging/commit will succeed.
public interface IAlsIslandStepObserver
{
    bool Enabled { get; }
    void Begin(double dt, int positionIterations, int velocityIterations, ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
        ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
        ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> jointOrder);
    void Capture(string stage, int iteration, ReadOnlySpan<AlsPrecisePose> predicted,
        ReadOnlySpan<AlsProjectionDelta> delta, ReadOnlySpan<AlsProjectionVelocity> velocity);
    void Complete();
}
