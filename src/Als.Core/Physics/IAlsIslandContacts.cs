using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// A contact owner gathers against this step's predicted COM poses (fixed bodies
// use actor poses), then solves into the SAME per-body buffers as the joints.
// Span data is borrowed for the call only. Gather must reset all step lambdas.
// Collision detection, stable body/shape identity and anchor persistence are the
// provider's responsibility. This interface does not supply a collision world.
public interface IAlsIslandContacts
{
    // World/geometry edits must wake a suspended island before skipping queries.
    bool RequiresWake => false;
    void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt);
    // Committed states precede this tick's gravity, impulses and damping. Borrow
    // only during Gather; failed steps must never become next tick's PreV.
    void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt, ReadOnlySpan<AlsIslandBodyState> previous) =>
        Gather(predicted, velocities, bodies, dt);
    // Detection uses the stored particle actor pose, while solving uses COM.
    // Carry both from integration: converting shape locals through COM introduces
    // rounding from the float actor quaternion. Borrow actors only for this call.
    void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt, ReadOnlySpan<AlsIslandBodyState> previous,
        ReadOnlySpan<AlsPrecisePose> predictedActors) => Gather(predicted, velocities, bodies, dt, previous);
    // Called after Gather, before any iteration. A stateful provider stages the
    // contact/joint schedule and publishes it only with its successful Commit.
    void PrepareConstraintOrder(AlsJointIsland island, Span<int> jointOrder) { }
    void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount);
    void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt);
    // Stage validates every history update before any body state is published.
    // Commit must not fail after a successful stage; Abort must be idempotent
    // and nonthrowing. Legacy stateless providers need no lifecycle callbacks.
    void StageCommit() { }
    void Commit() { }
    void Abort() { }
}
