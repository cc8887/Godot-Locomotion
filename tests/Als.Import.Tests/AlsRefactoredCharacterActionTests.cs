using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredCharacterActionTests
{
    internal sealed class Fixture
    {
        public readonly AlsRefactoredCharacterActionProfile Profile;
        public readonly AlsRefactoredWeaponNotifyProfile[] Weapons;
        public Fixture()
        {
            var standing = AlsRefactoredStandingHostTests.Data.Value; var catalog = standing.Catalog;
            Weapons = Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponNotifyProfile(catalog,
                new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, k)))).ToArray();
            var paths = standing.ActionAssets.ToArray().Select(a => standing.ActionSource(a.AnimationId))
                .Concat(Weapons.SelectMany(w => w.Bindings.ToArray()).Select(b => b.Sequence)).Distinct().OrderDescending(StringComparer.Ordinal).ToArray();
            // Deliberately non-contiguous, reordered IDs and a non-native group ID.
            Profile = new(standing, Weapons, paths.Select((p, i) => (p, id: 700 + i * 3)).ToDictionary(v => v.p, v => v.id), 20);
        }
    }
    internal static readonly Lazy<Fixture> Data = new(() => new());

    internal static AlsInertialCurve[] MapStanding(AlsRefactoredCharacterActionProfile profile, AlsRefactoredStandingHost host)
    {
        var result = new AlsInertialCurve[profile.CurveNames.Length];
        for (var c = 0; c < host.Curves.Length; c++)
            result[Array.FindIndex(profile.CurveNames.ToArray(), n => n.Equals(host.CurveNames[c], StringComparison.OrdinalIgnoreCase))] = host.Curves[c];
        return result;
    }

    [Fact]
    public void CharacterIdsPreservePoliciesAndRejectIncompleteOrAliasedBindings()
    {
        var fixture = Data.Value; var profile = fixture.Profile; var old = AlsRefactoredStandingHostTests.Data.Value;
        Assert.All(profile.Assets.ToArray(), a => { Assert.True(a.AnimationId >= 700); Assert.Equal(20, a.GroupId); });
        Assert.Equal(profile.Assets.Length, profile.AnimationIds.Count);
        foreach (var asset in old.ActionAssets)
        {
            var id = profile.AnimationIds[old.ActionSource(asset.AnimationId)];
            Assert.Contains(asset with { AnimationId = id, GroupId = 20 }, profile.Assets.ToArray());
        }
        var missing = profile.AnimationIds.ToDictionary(p => p.Key, p => p.Value); missing.Remove(missing.Keys.First());
        Assert.Throws<ArgumentException>(() => new AlsRefactoredCharacterActionProfile(old, fixture.Weapons, missing, 20));
        var alias = profile.AnimationIds.ToDictionary(p => p.Key, p => p.Value); alias[alias.Keys.First()] = alias.Values.Last();
        Assert.Throws<ArgumentException>(() => new AlsRefactoredCharacterActionProfile(old, fixture.Weapons, alias, 20));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SharedStandingAndOuterSlotRetryOnePhysicalFrameIncludingPostUpdate(int hz)
    {
        var fixture = Data.Value; var profile = fixture.Profile;
        var runtime = profile.CreateRuntime(19, 1); var clean = profile.CreateRuntime(19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var init = counter;
        var actualSources = new HashSet<string>(); var changed = 0; var overlaps = 0; var skipped = 0; var quick = 0;
        var mixer = new AlsMontageSlotPose(profile.Reference, profile.Parents, profile.CurveNames.Length);
        var samples = profile.Samples.CreateSampler(); var expectedPose = new AlsPrecisePose[79];
        var expectedCurves = new AlsInertialCurve[profile.CurveNames.Length];
        for (var f = 0; f < hz * 11; f++)
        {
            var id = new AlsFrameIdentity(f, 19, 1); var context = new AlsPoseUpdateContext(id, 1, 1f / hz).WithUpdateCounter(counter);
            var input = AlsRefactoredStandingHostTests.Input(f, hz); var evaluate = f % 13 >= 3;
            void Prepare(AlsRefactoredCharacterActionRuntime r)
            {
                r.Begin(id, context.Delta); r.Transition.Prepare(context, f == 0);
                r.Standing.Prepare(r.Transition.SourceUpdate.Context, input, init, f == 0);
                if (f % hz == hz / 2) r.QueueWeapon(fixture.Weapons[0].Bindings[0], "Als.Stance.Standing", input.Rest.Moving);
                if (evaluate)
                {
                    r.Standing.Evaluate(AlsPrecisePose.Identity);
                    // Controlled Standing result at the Grounded boundary. This
                    // does not claim that the missing standing/crouch selector ran.
                    r.Transition.Evaluate(r.Standing.Pose, MapStanding(profile, r.Standing));
                }
            }
            Prepare(runtime);
            var snapshot = runtime.Frame.Evaluations.ToArray(); var before = runtime.Candidate.ToArray();
            var pose = evaluate ? runtime.Transition.Pose.ToArray() : [];
            var curves = evaluate ? runtime.Transition.Curves.ToArray() : [];
            if (evaluate)
            {
                mixer.Evaluate(runtime.Frame, id, AlsMontageSlot.Transition, runtime.Standing.Pose,
                    MapStanding(profile, runtime.Standing), expectedPose, expectedCurves, samples);
                Assert.Equal(expectedPose, pose); Assert.Equal(expectedCurves, curves);
                runtime.Transition.Evaluate(runtime.Standing.Pose, MapStanding(profile, runtime.Standing));
                Assert.Equal(pose, runtime.Transition.Pose.ToArray()); Assert.Equal(before, runtime.Candidate.ToArray());
                if (!pose.SequenceEqual(runtime.Standing.Pose.ToArray())) changed++;
            }
            else { skipped++; Assert.Throws<InvalidOperationException>(() => runtime.Transition.Pose.ToArray()); }
            foreach (var entry in snapshot) actualSources.Add(profile.SourcePath(entry.AnimationId));
            if (snapshot.Count(e => e.Slot == AlsMontageSlot.Transition) > 1) overlaps++;
            Assert.Throws<InvalidOperationException>(() => runtime.Standing.Commit(id));
            Assert.Throws<InvalidOperationException>(() => runtime.Standing.PostUpdateActions());
            runtime.PostUpdateActions(); quick += runtime.Standing.QuickStopDispatchCount;
            Assert.Equal(snapshot, runtime.Frame.Evaluations.ToArray());
            var post = runtime.Candidate.ToArray(); var queue = runtime.QueueState;
            runtime.Discard(); Prepare(runtime); Prepare(clean);
            if (evaluate)
            {
                Assert.Equal(pose, runtime.Transition.Pose.ToArray()); Assert.Equal(curves, runtime.Transition.Curves.ToArray());
                Assert.Equal(pose, clean.Transition.Pose.ToArray()); Assert.Equal(curves, clean.Transition.Curves.ToArray());
            }
            runtime.PostUpdateActions(); clean.PostUpdateActions();
            Assert.Equal(post, runtime.Candidate.ToArray()); Assert.Equal(queue, runtime.QueueState);
            Assert.Equal(post, clean.Candidate.ToArray());
            runtime.Commit(id); clean.Commit(id);
            Assert.Equal(id, runtime.CommittedIdentity); Assert.Equal(id, runtime.Standing.CommittedIdentity);
            Assert.Throws<InvalidOperationException>(() => runtime.Commit(id));
            counter = counter.Next((ulong)f + 1);
        }
        Assert.True(changed > hz); Assert.True(overlaps > 0); Assert.True(skipped > 0); Assert.True(quick > 0);
        Assert.Contains(actualSources, p => p.Contains("A_Als_Stop_", StringComparison.Ordinal));
        Assert.Contains(actualSources, p => p.Contains("Dynamic", StringComparison.Ordinal));
    }

    [Fact]
    public void LateSlotFailureDiscardsSharedActionsAndAllowsSameFrameRetry()
    {
        var fixture = Data.Value; var r = fixture.Profile.CreateRuntime(19, 1); var id = new AlsFrameIdentity(0, 19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
        void Prepare()
        {
            r.Begin(id, context.Delta); r.Transition.Prepare(context, true);
            r.Standing.Prepare(context, AlsRefactoredStandingHostTests.Input(0, 60), counter, true);
            r.QueueWeapon(fixture.Weapons[0].Bindings[0], "Als.Stance.Standing", false); r.Standing.Evaluate(AlsPrecisePose.Identity);
        }
        Prepare();
        Assert.Throws<ArgumentException>(() => r.Transition.Evaluate(r.Standing.Pose, []));
        Assert.Throws<InvalidOperationException>(() => r.Candidate.ToArray()); Assert.Empty(r.Committed.ToArray());
        Assert.Equal(default, r.Standing.CommittedIdentity);
        Prepare(); r.Transition.Evaluate(r.Standing.Pose, MapStanding(fixture.Profile, r.Standing)); r.PostUpdateActions();
        var frozen = r.Frame.Evaluations.ToArray();
        r.PlayWeaponNotify(fixture.Weapons[0].Bindings[0], "Als.Stance.Standing", false);
        Assert.Equal(frozen, r.Frame.Evaluations.ToArray());
        Assert.Throws<ArgumentException>(() => r.Transition.Evaluate(r.Standing.Pose, MapStanding(fixture.Profile, r.Standing)));
        r.Commit(id); Assert.NotEmpty(r.Committed.ToArray());
        Assert.Throws<ArgumentException>(() => r.Begin(new(1, 20, 1), context.Delta));
        Assert.Throws<ArgumentException>(() => r.Begin(new(1, 19, 2), context.Delta));
        Assert.Throws<ArgumentException>(() => r.Begin(id, context.Delta));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void WorkerAndMainNotifyOrderUsesOneQueueAndOnlyTheCoordinatorCanPublish(bool stop)
    {
        var fixture = Data.Value; var r = fixture.Profile.CreateRuntime(19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var init = counter;
        for (var f = 0; f < 2; f++)
        {
            var id = new AlsFrameIdentity(f, 19, 1); var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
            r.Begin(id, context.Delta);
            var input = AlsRefactoredStandingHostTests.Input(0, 60) with { MovingSmooth = stop && f == 1 };
            r.Standing.Prepare(context, input, init, f == 0);
            var first = fixture.Weapons[0].Bindings[0]; var last = fixture.Weapons[^1].Bindings[1];
            r.QueueWeapon(first, "Als.Stance.Standing", false);
            var queued = r.QueueState.Play;
            r.QueueWeapon(last, "Als.Stance.Crouching", false); Assert.Equal(queued, r.QueueState.Play);
            r.QueueWeapon(last, "Als.Stance.Standing", true); Assert.Equal(queued, r.QueueState.Play);
            r.QueueWeapon(last, "Als.Stance.Standing", false);
            Assert.Equal(fixture.Profile.Weapons.Command(last), r.QueueState.Play);
            Assert.Equal(stop && f == 1, r.QueueState.Stop);
            var frozen = r.Frame.Evaluations.ToArray(); r.PostUpdateActions();
            Assert.Throws<InvalidOperationException>(() => r.QueueWeapon(first, "Als.Stance.Standing", false));
            // Missing Slot update is a late dependency failure: no graph or
            // physical action history may have been committed yet.
            Assert.Throws<InvalidOperationException>(() => r.Commit(id));
            Assert.Equal(f == 0 ? default : new AlsFrameIdentity(0, 19, 1), r.Standing.CommittedIdentity);
            Assert.False(r.QueueState.Stop);
            if (stop && f == 1) Assert.Equal(fixture.Profile.Weapons.Command(last), r.QueueState.Play);
            else Assert.Null(r.QueueState.Play);
            r.PlayWeaponNotify(first, "Als.Stance.Standing", false); Assert.Null(r.QueueState.Play);
            Assert.Equal(fixture.Profile.AnimationIds[first.Sequence], r.Candidate[^1].AnimationId);
            Assert.Equal(frozen, r.Frame.Evaluations.ToArray());
            r.Discard();
            // Retry in correct traversal order; Commit publishes the full frame.
            r.Begin(id, context.Delta); r.Transition.Prepare(context, f == 0);
            r.Standing.Prepare(context, input, init, f == 0); r.PostUpdateActions(); r.Commit(id);
            counter = counter.Next((ulong)f + 1);
        }
    }

    [Fact]
    public void CancellingSharedStandingAlsoDiscardsQueuedActionsAndOuterSlot()
    {
        var fixture = Data.Value; var r = fixture.Profile.CreateRuntime(19, 1); var id = new AlsFrameIdentity(0, 19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
        r.Begin(id, context.Delta); r.Transition.Prepare(context, true);
        r.Standing.Prepare(context, AlsRefactoredStandingHostTests.Input(0, 60), counter, true);
        r.QueueWeapon(fixture.Weapons[0].Bindings[0], "Als.Stance.Standing", false);
        r.Standing.Cancel();
        Assert.Throws<InvalidOperationException>(() => r.QueueState); Assert.Throws<InvalidOperationException>(() => r.Transition.Weights);
        r.Begin(id, context.Delta); Assert.Null(r.QueueState.Play);
        r.Transition.Prepare(context, true); r.Standing.Prepare(context, AlsRefactoredStandingHostTests.Input(0, 60), counter, true);
        r.PostUpdateActions(); r.Commit(id); Assert.Empty(r.Committed.ToArray());
    }

    [Fact]
    public void SharedContextRejectsDifferentDeltaAndInvalidProducerInvalidatesWholeFrame()
    {
        var fixture = Data.Value; var r = fixture.Profile.CreateRuntime(19, 1); var id = new AlsFrameIdentity(0, 19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
        r.Begin(id, 1f / 30);
        Assert.Throws<ArgumentException>(() => r.Standing.Prepare(context, AlsRefactoredStandingHostTests.Input(0, 60), counter, true));
        Assert.Throws<InvalidOperationException>(() => r.Frame); Assert.Empty(r.Committed.ToArray());
        r.Begin(id, context.Delta); r.Transition.Prepare(context, true);
        r.Standing.Prepare(context, AlsRefactoredStandingHostTests.Input(0, 60), counter, true);
        r.QueueWeapon(fixture.Weapons[0].Bindings[0], "Als.Stance.Standing", false);
        Assert.Throws<ArgumentException>(() => r.QueueWeapon(fixture.Weapons[0].Bindings[0] with { PlayRate = 999 }, "Als.Stance.Standing", false));
        Assert.Throws<InvalidOperationException>(() => r.Candidate.ToArray()); Assert.Empty(r.Committed.ToArray());
        Assert.Equal(default, r.Standing.CommittedIdentity);
    }

    [Theory]
    [InlineData("source")] [InlineData("destination")] [InlineData("slot")] [InlineData("always")] [InlineData("callback")]
    public void MainGraphTransitionBoundaryRejectsChangedTopologyOrPolicy(string mutation)
    {
        var payload = JsonNode.Parse(AlsRefactoredStandingHostTests.Data.Value.Catalog.Read("/ALS/ALS/Character/AB_Als.AB_Als").GetRawText())!;
        var nodes = payload["compiled"]!["nodes"]!.AsArray();
        var slot = nodes.Single(n => n!["propertyIndex"]!.GetValue<int>() == 13)!;
        if (mutation == "source") slot["runtime"]!["source"]!["linkId"] = 5;
        if (mutation == "destination") nodes.Single(n => n!["propertyIndex"]!.GetValue<int>() == 7)!["runtime"]!["inputPoses"]![0]!["linkId"] = 6;
        if (mutation == "slot") slot["runtime"]!["slotName"] = "PostLocomotion";
        if (mutation == "always") slot["authoredProperties"]!["Node"]!["bAlwaysUpdateSourcePose"] = true;
        if (mutation == "callback") slot["runtime"]!["updateFunction"]!["functionName"] = "Unexpected";
        using var document = JsonDocument.Parse(payload.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredTransitionSlotGraph.Validate(document.RootElement));
    }
}
