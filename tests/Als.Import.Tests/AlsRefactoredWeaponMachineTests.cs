using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponMachineTests
{
    private static readonly Lazy<AlsRefactoredWeaponMachineProfile[]> Profiles = new(() =>
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        return Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponMachineProfile(catalog,
            new AlsRefactoredWeaponMachineResources(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, k))).ToArray();
    });
    private static AlsRefactoredWeaponRuleInput Input(bool aim = false, bool allowed = false, bool moving = false, bool sprint = false, bool air = false) =>
        new(aim ? "Als.RotationMode.Aiming" : "Als.RotationMode.ViewDirection", sprint ? "Als.Gait.Sprinting" : "Als.Gait.Running",
            air ? "Als.LocomotionMode.InAir" : "Als.LocomotionMode.Grounded", moving, allowed);
    private static AlsOverlayMachineUpdate Tick(AlsRefactoredWeaponMachineRuntime runtime, long frame, AlsRefactoredWeaponRuleInput input, float delta)
    { runtime.Prepare(frame, input, delta); var next = runtime.Candidate; runtime.Commit(frame); return next; }

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void FirstFrameDelayBoundaryThreeStepPriorityAndQuickFeetUseOriginalResources(AlsRefactoredWeaponKind kind)
    {
        var profile = Profiles.Value[(int)kind]; var runtime = profile.CreateRuntime();
        var first = Tick(runtime, 0, Input(aim: true), .1f);
        Assert.Equal(1, first.State.CurrentState); Assert.Equal(2, first.TransitionCount);
        Assert.Equal(0, first.State.Transitions.Count); Assert.Equal(0, first.NotifyCount);
        Assert.Equal(3, first.EntryCount); Assert.Equal(.1f, first.State.ElapsedSeconds);
        var ready = Tick(runtime, 1, Input(), 3);
        Assert.Equal(2, ready.State.CurrentState); Assert.Equal(3, ready.State.ElapsedSeconds);
        runtime.Prepare(2, Input(allowed: true, moving: true), .1f);
        var relax = runtime.Candidate;
        Assert.Equal(0, relax.State.CurrentState); Assert.Equal(2, relax.GetTransition(0).Path.Edge);
        Assert.Equal(new AlsOverlayTransitionNotify(1, 2), relax.GetNotify(0));
        var bones = profile.Resources.QuickFeet.BoneNames.ToArray();
        Assert.True(runtime.CandidateBoneWeight(0, Array.IndexOf(bones, "foot_l")) > runtime.CandidateBoneWeight(0, Array.IndexOf(bones, "pelvis")));
        runtime.Cancel();
        // At the exact same committed 3-second boundary all three transitions can fire.
        runtime.Prepare(2, Input(aim: true, allowed: true, moving: true), .01f);
        var chain = runtime.Candidate;
        Assert.Equal(new[] { 2, 0, 4 }, Enumerable.Range(0, chain.TransitionCount).Select(i => chain.GetTransition(i).Path.Edge));
        Assert.Equal(new[] { 1, 0 }, Enumerable.Range(0, chain.NotifyCount).Select(i => chain.GetNotify(i).GeneratedIndex));
        Assert.Equal(1, chain.State.CurrentState); Assert.Equal(.01f, chain.State.ElapsedSeconds);
        runtime.Cancel();
        runtime.Prepare(2, Input(moving: true), .1f);
        Assert.Equal(3, runtime.Candidate.GetTransition(0).Path.Edge); Assert.Equal(0, runtime.Candidate.NotifyCount);
        Assert.Equal(runtime.CandidateBoneWeight(0, Array.IndexOf(bones, "foot_l")), runtime.CandidateBoneWeight(0, Array.IndexOf(bones, "pelvis")));
        runtime.Cancel();
        runtime.Prepare(2, Input(sprint: true), 0);
        Assert.Equal(5, runtime.Candidate.GetTransition(0).Path.Edge);
        Assert.Equal(kind == AlsRefactoredWeaponKind.Bow ? .4f : .2f, runtime.Candidate.GetTransition(0).Duration);
    }

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void InterruptedStateReentryAndDiscardRetainClocksCurvesAndLocalNotifyIdentity(AlsRefactoredWeaponKind kind)
    {
        var profile = Profiles.Value[(int)kind];
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var runtime = profile.CreateRuntime(); Tick(runtime, 0, Input(), 1f / hz);
            var aim = Tick(runtime, 1, Input(aim: true), 1f / hz);
            Assert.Equal(2, aim.State.Transitions.Count); Assert.Equal(new AlsOverlayTransitionNotify(0, 0), aim.GetNotify(0));
            runtime.Prepare(2, Input(), 1f / hz);
            var interrupted = runtime.Candidate;
            Assert.Equal(3, interrupted.State.Transitions.Count); Assert.False(interrupted.GetEntry(0).Initialize);
            Assert.Equal(1, interrupted.State.GetActivePath(2).Edge);
            Assert.Throws<ArgumentException>(() => runtime.Commit(3));
            runtime.Cancel(); Assert.Equal(aim.State.ElapsedSeconds, runtime.CommittedState.ElapsedSeconds);
            runtime.Prepare(2, Input(), 1f / hz); Equivalent(interrupted, runtime.Candidate); runtime.Commit(2);
            for (var frame = 3; frame < 303; frame++)
            {
                var input = Input(aim: frame % 47 < 19, moving: frame % 23 < 5, allowed: frame % 31 < 5,
                    sprint: frame % 41 < 5, air: frame % 61 < 5);
                runtime.Prepare(frame, input, 1f / hz);
                var candidate = runtime.Candidate; runtime.Cancel();
                runtime.Prepare(frame, input, 1f / hz); Equivalent(candidate, runtime.Candidate);
                Assert.InRange(candidate.TransitionCount, 0, 3);
                var updates = Enumerable.Range(0, candidate.UpdateCount).Select(i => candidate.GetUpdate(i)).ToArray();
                Assert.Equal(updates.Length, updates.Select(u => u.State).Distinct().Count());
                Assert.All(updates, u => Assert.InRange(u.Weight, 0, 1));
                for (var bone = 0; bone < 79; bone++)
                    Assert.InRange(Enumerable.Range(0, 3).Sum(s => runtime.CandidateBoneWeight(s, bone)), .999999f, 1.000001f);
                runtime.Commit(frame);
            }
            Assert.Throws<ArgumentException>(() => runtime.Prepare(302, Input(), 0));
        }
    }

    [Fact]
    public void RelevanceUsesTraversalRatherThanFrameGapsAndZeroWeightStillUpdates()
    {
        var runtime = Profiles.Value[0].CreateRuntime(); var counter = new AlsGraphTraversalCounter(short.MaxValue, 1);
        runtime.Prepare(0, Input(aim: true), .1f, updateCounter: counter); runtime.Commit(0);
        counter = counter.Next(2);
        runtime.Prepare(100, Input(aim: true), .1f, updateCounter: counter);
        Assert.False(runtime.Candidate.Reinitialized); Assert.Equal(.2f, runtime.Candidate.State.ElapsedSeconds); runtime.Commit(100);
        runtime.Prepare(101, Input(), 0, weight: 0, inactive: true, updateCounter: counter.Next(3));
        Assert.True(runtime.Candidate.UpdateCount > 0);
        for (var i = 0; i < runtime.Candidate.UpdateCount; i++)
        { Assert.Equal(0, runtime.Candidate.GetUpdate(i).Weight); Assert.True(runtime.Candidate.GetUpdate(i).Inactive); }
        runtime.Cancel();
        runtime.Prepare(101, Input(aim: true), .1f, updateCounter: counter.Next(3).Next(4));
        Assert.True(runtime.Candidate.Reinitialized); Assert.Equal(0, runtime.Candidate.NotifyCount); runtime.Commit(101);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(102, Input(), .1f));
        runtime.Prepare(102, Input(), .1f, reinitialize: true);
        Assert.True(runtime.Candidate.Reinitialized); Assert.Equal(0, runtime.Candidate.State.CurrentState);
    }

    [Fact]
    public void LegacyInputCannotSilentlyDriveRefactoredRules()
    {
        var profile = Profiles.Value[0];
        var machine = new AlsOverlayStateMachine(profile.Definition, profile.Resources.Curves, profile.Resources.QuickFeet);
        Assert.Throws<ArgumentException>(() => machine.Update(default, default, 1, .1f, 0));
        Assert.Throws<ArgumentException>(() => new AlsOverlayStateMachine(profile.Definition, [], profile.Resources.QuickFeet));
        var edges = profile.Definition.Edges.ToArray(); edges[0] = edges[0] with { RefactoredRule = null };
        Assert.Throws<ArgumentException>(() => new AlsOverlayMachineDefinition(AlsOverlayMachineKind.Bow, 9, 0, 0, 3, true,
            profile.Definition.States.ToArray(), edges));
    }

    private static void Equivalent(AlsOverlayMachineUpdate a, AlsOverlayMachineUpdate b)
    {
        Assert.Equal(a.State.CurrentState, b.State.CurrentState); Assert.Equal(a.State.ElapsedSeconds, b.State.ElapsedSeconds);
        Assert.Equal(a.State.Transitions.Count, b.State.Transitions.Count); Assert.Equal(a.Reinitialized, b.Reinitialized);
        for (var i = 0; i < a.State.Transitions.Count; i++)
        { Assert.Equal(a.State.Transitions.GetTransition(i), b.State.Transitions.GetTransition(i)); Assert.Equal(a.State.GetActivePath(i), b.State.GetActivePath(i)); }
        Assert.Equal(a.EntryCount, b.EntryCount); for (var i = 0; i < a.EntryCount; i++) Assert.Equal(a.GetEntry(i), b.GetEntry(i));
        Assert.Equal(a.TransitionCount, b.TransitionCount); for (var i = 0; i < a.TransitionCount; i++) Assert.Equal(a.GetTransition(i), b.GetTransition(i));
        Assert.Equal(a.UpdateCount, b.UpdateCount); for (var i = 0; i < a.UpdateCount; i++) Assert.Equal(a.GetUpdate(i), b.GetUpdate(i));
        Assert.Equal(a.NotifyCount, b.NotifyCount); for (var i = 0; i < a.NotifyCount; i++) Assert.Equal(a.GetNotify(i), b.GetNotify(i));
    }
}
