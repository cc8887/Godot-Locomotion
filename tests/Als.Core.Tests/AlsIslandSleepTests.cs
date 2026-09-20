using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandSleepTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsIslandBody Body = new(Identity, new(1, AlsDoubleVector.One));
    private static readonly AlsSleepBodySettings Settings = new(1, .05f, 4);
    private static AlsJointIsland Create(AlsSleepBodySettings settings) => new([Body], [], [new(Identity, default)], sleepSettings: [settings]);

    [Fact]
    public void SleepsOnlyAfterStrictCounterBoundaryAndImpulseWakesWithoutResidualReplay()
    {
        var island = Create(Settings);
        for (var i = 1; i <= 4; i++) { island.StepForceFree(.1); Assert.False(island.IsSleeping); Assert.Equal(i, island.SleepCounter); }
        island.StepForceFree(.1); Assert.True(island.IsSleeping); Assert.Equal(0, island.SleepCounter);
        var asleep = island.BodyAt(0); var metrics = island.SleepMetricsAt(0);
        for (var i = 0; i < 100; i++) island.StepForceFree(.1);
        Assert.Equal(asleep, island.BodyAt(0)); Assert.Equal(metrics, island.SleepMetricsAt(0));
        island.Step(.1, default, [new(default, LinearImpulseVelocity: new(30, 0, 0))]);
        Assert.False(island.IsSleeping); Assert.Equal(30, island.BodyAt(0).Velocity.Linear.X);
        island.StepForceFree(.1); Assert.Equal(6, island.BodyAt(0).Actor.Position.X, 6);
    }
    [Fact]
    public void OneMovingOrNeverSleepingParticipantPreventsGroupSleep()
    {
        var island = new AlsJointIsland([Body, Body], [], [new(Identity, default), new(Identity, new(new(2, 0, 0), Vector3.Zero))],
            sleepSettings: [Settings, Settings]);
        for (var i = 0; i < 30; i++) island.StepForceFree(.1);
        Assert.False(island.IsSleeping); Assert.Equal(0, island.SleepCounter);
        Assert.Equal(4, island.SleepMetricsAt(0).ParticleCounter); Assert.Equal(0, island.SleepMetricsAt(1).ParticleCounter);
        foreach (var setting in new[] { new AlsSleepBodySettings(0, 0, 0), Settings with { NeverSleep = true } })
        {
            island = Create(setting); for (var i = 0; i < 30; i++) island.StepForceFree(.1);
            Assert.False(island.IsSleeping);
        }
    }
    [Fact]
    public void FailureWhileWakingDoesNotPublishMetricsOrConsumeWakeRequest()
    {
        var island = Create(Settings); var source = new ContactProbe();
        for (var i = 0; i < 5; i++) island.Step(.1, default, contacts: source);
        Assert.True(island.IsSleeping); var state = island.BodyAt(0); var metrics = island.SleepMetricsAt(0);
        island.RequestWake(); source.ThrowOnStage = true;
        Assert.Throws<InvalidOperationException>(() => island.Step(.1, default, contacts: source));
        Assert.True(island.IsSleeping); Assert.Equal(state, island.BodyAt(0)); Assert.Equal(metrics, island.SleepMetricsAt(0));
        Assert.Equal(1, source.Aborts); source.ThrowOnStage = false;
        island.Step(.1, default, contacts: source); Assert.False(island.IsSleeping); Assert.Equal(1, island.SleepCounter);
        Assert.Equal(7, source.Gathers);
    }
    [Fact]
    public void SleepingContactHistoryIsHeldAndRemovingSupportWakesGravity()
    {
        var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, Identity, 1, 1, true)); registry.Register(new(1, Identity, 1, 1));
        var contacts = new AlsWorldContacts(registry, new Floor(), new(0, 0, 0), new(1f / 60, 0, 2000));
        var island = new AlsJointIsland([Body, new(Identity, default)], [],
            [new(Identity with { Position = new(0, 0, 1) }, default), new(Identity, default)], sleepSettings: [Settings, default]);
        for (var i = 0; i < 5; i++) island.Step(1d / 60, new(0, 0, -980), contacts: contacts);
        Assert.True(island.IsSleeping); var epoch = contacts.CompletedSteps;
        for (var i = 0; i < 100; i++) island.Step(1d / 60, new(0, 0, -980), contacts: contacts);
        Assert.Equal(epoch, contacts.CompletedSteps); Assert.Equal(1, island.BodyAt(0).Actor.Position.Z, 6);
        registry.DisableBodyPair(0, 1, true); Assert.True(contacts.RequiresWake);
        island.Step(1d / 60, new(0, 0, -980), contacts: contacts);
        Assert.False(island.IsSleeping); Assert.True(island.BodyAt(0).Velocity.Linear.Z < -16);
        Assert.Equal(epoch + 1, contacts.CompletedSteps); Assert.False(contacts.RequiresWake);
    }
    [Fact]
    public void GravityChangeContactReplacementAndResetInvalidateSleep()
    {
        var island = Create(Settings);
        for (var i = 0; i < 5; i++) island.StepForceFree(.1);
        island.Step(.1, new(0, 0, -980)); Assert.False(island.IsSleeping);
        island.Reset([new(Identity, default)]); Assert.Equal(0, island.SleepCounter);
        for (var i = 0; i < 5; i++) island.StepForceFree(.1);
        island.Step(.1, default, contacts: new ContactProbe()); Assert.False(island.IsSleeping);
    }
    [Fact]
    public void SleepTransitionsAndWakeStepsDoNotAllocateAfterWarmup()
    {
        var island = Create(Settings); var inputs = new[] { new AlsBodyStepForces(default, LinearImpulseVelocity: new(20, 0, 0)) };
        var initial = new[] { new AlsIslandBodyState(Identity, default) };
        void Cycle() { island.Reset(initial); for (var j = 0; j < 6; j++) island.StepForceFree(.1); island.Step(.1, default, inputs); }
        for (var i = 0; i < 256; i++) Cycle();
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 2048; i++) Cycle();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    private sealed class Floor : IAlsContactGeometrySource
    {
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> points)
        { if (p.Position.Z >= 1) return 0; points[0] = new(-Vector3.UnitZ, Vector3.Zero, Vector3.UnitZ); return 1; }
    }
    private sealed class ContactProbe : IAlsIslandContacts
    {
        public bool ThrowOnStage; public int Gathers, Aborts;
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt) => Gathers++;
        public void SolvePosition(Span<AlsProjectionDelta> b, int i, int n) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> b, int i, int n, double dt) { }
        public void StageCommit() { if (ThrowOnStage) throw new InvalidOperationException("Injected stage failure."); }
        public void Abort() => Aborts++;
    }
}
