using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandVelocityOverrideTests
{
    private static readonly AlsIslandBody Body = new(AlsPrecisePose.Identity, new(1, AlsDoubleVector.One));
    private static AlsJointIsland Create(Vector3 velocity = default, bool sleep = false) => new(
        [Body, new(AlsPrecisePose.Identity, default)], [],
        [new(AlsPrecisePose.Identity, new(velocity, default)), new(AlsPrecisePose.Identity, default)],
        sleepSettings: sleep ? [new(1, .05f, 2), default] : []);

    [Fact]
    public void ReplacementPrecedesGravityAndIsAlsoContactPreVelocity()
    {
        var island = Create(new(900, 0, 0)); var probe = new Probe();
        island.Step(.25, new(0, 0, -100), contacts: probe,
            velocityOverrides: [new(0, new(new(200, 0, 0), default))]);
        Assert.Equal(new Vector3(200, 0, 0), probe.Previous);
        Assert.Equal(new Vector3(200, 0, -25), probe.Predicted);
        Assert.Equal(new AlsDoubleVector(50, 0, -6.25), island.BodyAt(0).Actor.Position);
        Assert.Equal(new Vector3(200, 0, -25), island.BodyAt(0).Velocity.Linear);
        Assert.Equal(AlsPrecisePose.Identity, island.BodyAt(1).Actor);
    }

    [Fact]
    public void FailedLimitStepPreservesVelocityAndRetryMatchesCleanStep()
    {
        var island = Create(new(900, 0, 0)); var expected = Create(new(900, 0, 0));
        var initial = island.BodyAt(0);
        var seed = new[] { initial };
        var limit = AlsRagdollSpeedLimit.Begin(true, default, seed);
        var candidate = new[] { initial };
        var candidateLimit = limit.Refresh(candidate);
        var input = new[] { new AlsIslandVelocityOverride(0, candidate[0].Velocity) };
        var probe = new Probe { ThrowOnStage = true };
        Assert.Throws<InvalidOperationException>(() => island.Step(.25, default, contacts: probe, velocityOverrides: input));
        Assert.Equal(initial, island.BodyAt(0)); Assert.Equal(8, limit.RefreshesRemaining);
        Assert.Equal(1, probe.Aborts);
        probe.ThrowOnStage = false;
        island.Step(.25, default, contacts: probe, velocityOverrides: input);
        expected.Step(.25, default, velocityOverrides: input);
        limit = candidateLimit;
        Assert.Equal(expected.BodyAt(0), island.BodyAt(0)); Assert.Equal(7, limit.RefreshesRemaining);
        // Overrides are per-step inputs, not retained commands.
        island.Step(.25, default, forces: [new(default, LinearImpulseVelocity: new(50, 0, 0)), default]);
        Assert.Equal(250, island.BodyAt(0).Velocity.Linear.X);
    }

    [Fact]
    public void InvalidSuffixDoesNotPublishAndDoesNotLeakToNextStep()
    {
        var island = Create(new(100, 0, 0)); var saved = island.BodyAt(0);
        foreach (var bad in new[] {
            new AlsIslandVelocityOverride(0, default), // Duplicate.
            new AlsIslandVelocityOverride(1, default), // Fixed environment body.
            new AlsIslandVelocityOverride(99, default) })
        {
            Assert.Throws<ArgumentException>(() => island.Step(.25, default,
                velocityOverrides: [new(0, new(new(200, 0, 0), default)), bad]));
            Assert.Equal(saved, island.BodyAt(0));
        }
        Assert.Throws<ArgumentException>(() => island.Step(.25, default,
            velocityOverrides: [new(0, new(new(float.NaN, 0, 0), default))]));
        island.StepForceFree(.25);
        Assert.Equal(25, island.BodyAt(0).Actor.Position.X);
    }

    [Fact]
    public void ChangedVelocityWakesButFailedWakeKeepsSleepHistory()
    {
        var island = Create(sleep: true); var probe = new Probe();
        for (var i = 0; i < 3; i++) island.Step(.1, default, contacts: probe);
        Assert.True(island.IsSleeping);
        var metrics = island.SleepMetricsAt(0); var state = island.BodyAt(0);
        probe.ThrowOnStage = true;
        Assert.Throws<InvalidOperationException>(() => island.Step(.1, default, contacts: probe,
            velocityOverrides: [new(0, new(new(100, 0, 0), default))]));
        Assert.True(island.IsSleeping); Assert.Equal(state, island.BodyAt(0)); Assert.Equal(metrics, island.SleepMetricsAt(0));
        probe.ThrowOnStage = false;
        island.Step(.1, default, contacts: probe, velocityOverrides: [new(0, new(new(100, 0, 0), default))]);
        Assert.False(island.IsSleeping); Assert.Equal(10, island.BodyAt(0).Actor.Position.X, 5);
    }

    private sealed class Probe : IAlsIslandContacts
    {
        public bool ThrowOnStage;
        public int Aborts;
        public Vector3 Previous, Predicted;
        public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies, double dt) => throw new InvalidOperationException("Missing step-start state.");
        public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies, double dt, ReadOnlySpan<AlsIslandBodyState> previous)
        { Previous = previous[0].Velocity.Linear; Predicted = velocities[0].Linear; }
        public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt) { }
        public void StageCommit() { if (ThrowOnStage) throw new InvalidOperationException("Injected late failure."); }
        public void Abort() => Aborts++;
    }
}
