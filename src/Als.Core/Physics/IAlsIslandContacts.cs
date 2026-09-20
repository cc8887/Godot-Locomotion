using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// A contact owner gathers against this step's predicted COM poses (fixed bodies
// use actor poses), then solves into the SAME per-body buffers as the joints.
// Span data is borrowed for the call only. Gather must reset all step lambdas.
// Collision detection, stable body/shape identity and anchor persistence are the
// provider's responsibility. This interface does not supply a collision world.
public interface IAlsIslandContacts
{
    void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt);
    void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount);
    void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt);
}
