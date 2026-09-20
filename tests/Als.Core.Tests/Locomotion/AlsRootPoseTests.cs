using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRootPoseTests
{
    private static readonly AlsRootPoseDefinition Definition = new(21, new(.4f, .5f, AlsTransitionBlend.HermiteCubic));
    private static readonly AlsLocalPose[] Normal = [AlsLocalPose.Identity];
    private static readonly AlsLocalPose[] Ragdoll = [new(new(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)];
    private static readonly AlsInertialCurve[] NormalCurves = [new(1), default, new(0)];
    private static readonly AlsInertialCurve[] RagdollCurves = [new(0), new(8), default];
    private static AlsAnimationGraphFrame Next(AlsAnimationGraphFrame previous, int frame) => previous.Next(new(frame, 3, 1), (ulong)frame);
    private static void Evaluate(AlsRootPoseRuntime root) => root.Evaluate(root.Visits(0) ? Normal : [], root.Visits(0) ? NormalCurves : [],
        root.Visits(1) ? Ragdoll : [], root.Visits(1) ? RagdollCurves : []);

    [Theory]
    [InlineData(AlsMovementStateInput.Grounded, 0)] [InlineData(AlsMovementStateInput.InAir, 0)]
    [InlineData(AlsMovementStateInput.Ragdoll, 1)] [InlineData(AlsMovementStateInput.Mantling, 0)]
    [InlineData((AlsMovementStateInput)255, 0)]
    public void ColdSelectionIsImmediateAndInitializesBothBranches(AlsMovementStateInput state, int child)
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var traversal = Next(default, 1);
        root.Prepare(state, new(traversal.Identity, .6f, 1f / 60, .3f), traversal);
        Assert.True(root.InitializeChildren); Assert.True(root.CacheChildBones);
        Assert.True(root.Visits(child)); Assert.False(root.Visits(1 - child));
        Assert.Equal(.6f, root.ChildContext(child).Weight); Assert.Equal(.3f, root.ChildContext(child).RootMotionWeight);
        Evaluate(root); Assert.Equal(child == 0 ? Normal : Ragdoll, root.Pose.ToArray());
        Assert.Equal(child == 0 ? NormalCurves : RagdollCurves, root.Curves.ToArray());
        root.Commit(traversal.Identity);
    }

    [Theory] [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void AuthoredDurationsAndInactiveContextsReachBothTargets(int hz)
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var traversal = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(traversal.Identity, .8f, 1f / hz, .2f), traversal); Evaluate(root); root.Commit(traversal.Identity);
        var frame = 1;
        foreach (var child in new[] { 1, 0 })
        {
            var duration = child == 1 ? .5f : .4f;
            var count = (int)MathF.Round(duration * hz);
            for (var i = 1; i <= count; i++)
            {
                traversal = Next(traversal, ++frame);
                root.Prepare(child == 0 ? AlsMovementStateInput.Grounded : AlsMovementStateInput.Ragdoll,
                    new(traversal.Identity, .8f, 1f / hz, .2f), traversal);
                Assert.False(root.InitializeChildren); Assert.False(root.CacheChildBones);
                var progress = System.Math.Clamp(i / (duration * hz), 0, 1); var alpha = progress * progress * (3 - 2 * progress);
                var weight = child == 0 ? root.CandidateState.FirstWeight : root.CandidateState.SecondWeight;
                Assert.InRange(MathF.Abs(weight - alpha), 0, 2e-6f);
                Assert.True(root.ChildContext(child).IsActive);
                if (root.Visits(1 - child)) Assert.False(root.ChildContext(1 - child).IsActive);
                Evaluate(root);
                var second = root.CandidateState.SecondWeight;
                Assert.InRange(MathF.Abs(root.Pose[0].Position.X - 10 * second), 0, 3e-5f);
                Assert.InRange(MathF.Abs(root.Pose[0].Rotation.LengthSquared() - 1), 0, 1e-6f);
                Assert.InRange(MathF.Abs(root.Curves[0].Value - (1 - second)), 0, 1e-6f);
                if (root.Visits(1))
                {
                    Assert.True(root.Curves[1].Present);
                    Assert.InRange(MathF.Abs(root.Curves[1].Value - 8 * second), 0, 2e-6f);
                }
                root.Commit(traversal.Identity);
            }
            Assert.InRange(child == 0 ? root.CommittedState.SecondWeight : root.CommittedState.FirstWeight, 0, AlsPoseBlender.WeightThreshold);
        }
    }

    [Fact]
    public void InterruptedBlendKeepsWeightsAndShortensNewDuration()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t); Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 2); root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, .25f), t);
        Evaluate(root); root.Commit(t.Identity); Assert.Equal(.5f, root.CommittedState.SecondWeight);
        t = Next(t, 3); root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, .1f), t);
        Assert.Equal(.75f, root.CandidateState.FirstWeight); Assert.False(root.InitializeChildren);
        Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 4); root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, .1f), t);
        Assert.Equal(1, root.CandidateState.FirstWeight); Assert.False(root.Visits(1));
    }

    [Fact]
    public void TraversalGapRetainsHistoryButExplicitInitializationResetsSelector()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t); Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 100); root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, .25f), t);
        Assert.False(root.InitializeChildren); Assert.Equal(.5f, root.CandidateState.SecondWeight); Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 101) with { Bones = t.Bones.Next(101) };
        root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, 0), t);
        Assert.False(root.InitializeChildren); Assert.True(root.CacheChildBones); Assert.Equal(.5f, root.CandidateState.SecondWeight);
        Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 102) with { Initialization = t.Initialization.Next(102) };
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t);
        Assert.True(root.InitializeChildren); Assert.Equal(1, root.CandidateState.FirstWeight);
    }

    [Fact]
    public void SameInitializationAndBoneCountersDoNotResetAnActiveBlendWhenGlobalStampChanges()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t); Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 2); root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, .25f), t);
        Evaluate(root); root.Commit(t.Identity); Assert.Equal(.5f, root.CommittedState.FirstWeight);
        t = Next(t, 3) with { Initialization = new(t.Initialization.Counter, 3), Bones = new(t.Bones.Counter, 3) };
        root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, 0), t);
        Assert.False(root.InitializeChildren); Assert.False(root.CacheChildBones);
        Assert.Equal(.5f, root.CandidateState.FirstWeight); Evaluate(root); root.Cancel();
        Assert.Equal(.5f, root.CommittedState.FirstWeight);
        root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, 0), t);
        Assert.Equal(.5f, root.CandidateState.FirstWeight); Evaluate(root); root.Commit(t.Identity);
    }

    [Fact]
    public void ZeroTimeTransitionRetainsPreviousZeroUpdateAndParentInactiveStatus()
    {
        var root = new AlsRootPoseRuntime(new(21, new(0, 0, AlsTransitionBlend.HermiteCubic)), 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, .01f), t); Evaluate(root); root.Commit(t.Identity);
        t = Next(t, 2); root.Prepare(AlsMovementStateInput.Ragdoll, new AlsPoseUpdateContext(t.Identity, .7f, .01f, .3f).AsInactive(), t);
        Assert.Equal(0, root.ZeroWeightPreviousChild); Assert.Equal(0, root.PreviousZeroContext().Weight);
        Assert.False(root.PreviousZeroContext().IsActive); Assert.False(root.ChildContext(1).IsActive);
        Assert.Equal(.3f, root.ChildContext(1).RootMotionWeight); Assert.False(root.Visits(0));
    }

    [Fact]
    public void MissingActiveSourceCannotMasqueradeAsReferenceAndRetryIsTransactional()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t); Evaluate(root); root.Commit(t.Identity);
        var state = root.CommittedState;
        t = Next(t, 2); var context = new AlsPoseUpdateContext(t.Identity, 1, .25f);
        root.Prepare(AlsMovementStateInput.Ragdoll, context, t); var candidate = root.CandidateState;
        Assert.Throws<InvalidOperationException>(() => root.Commit(t.Identity));
        Assert.Throws<ArgumentException>(() => root.Evaluate(Normal, NormalCurves, [], [])); Assert.Equal(state, root.CommittedState);
        root.Prepare(AlsMovementStateInput.Ragdoll, context, t); Assert.Equal(candidate, root.CandidateState);
        Evaluate(root); var expected = root.Pose.ToArray(); root.Cancel(); Assert.Equal(state, root.CommittedState);
        root.Prepare(AlsMovementStateInput.Ragdoll, context, t); Evaluate(root); Assert.Equal(expected, root.Pose.ToArray()); root.Commit(t.Identity);
    }

    [Fact]
    public void ForeignGenerationOrReplayedCommittedFrameCannotAdvanceRoot()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var t = Next(default, 1);
        root.Prepare(AlsMovementStateInput.Grounded, new(t.Identity, 1, 0), t); Evaluate(root); root.Commit(t.Identity);
        Assert.Throws<ArgumentException>(() => root.Prepare(AlsMovementStateInput.Ragdoll, new(t.Identity, 1, .1f), t));
        var foreign = default(AlsAnimationGraphFrame).Next(new(2, 3, 2), 2);
        Assert.Throws<ArgumentException>(() => root.Prepare(AlsMovementStateInput.Ragdoll, new(foreign.Identity, 1, .1f), foreign));
        Assert.Equal(t.Identity, root.CommittedIdentity); Assert.Equal(0, root.CommittedState.ActiveChild);
    }

    [Fact]
    public void MixedRootPoseUpdatesAllocateNothingAfterWarmup()
    {
        var root = new AlsRootPoseRuntime(Definition, 1, 3); var traversal = default(AlsAnimationGraphFrame);
        for (var frame = 1; frame <= 1000; frame++) Tick(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 3000; frame++) Tick(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Tick(int frame)
        {
            traversal = Next(traversal, frame);
            root.Prepare(frame % 20 < 10 ? AlsMovementStateInput.Grounded : AlsMovementStateInput.Ragdoll,
                new(traversal.Identity, 1, 1f / 60), traversal);
            Evaluate(root); root.Commit(traversal.Identity);
        }
    }
}
