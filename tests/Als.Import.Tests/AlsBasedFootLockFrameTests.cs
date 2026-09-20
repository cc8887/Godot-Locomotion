using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed partial class AlsFootIkControllerTests
{
    private static AlsFootIkFrameRuntime BasedFrame(AlsFootIkPoseSpace space = AlsFootIkPoseSpace.Godot) =>
        new(AlsFootIkInputCompiler.Compile(Read()), Compile(), Bones, Parents, FootCurves, space,
            space == AlsFootIkPoseSpace.Fbx ? Pose().Select(BasedFbx).ToArray() : Pose());
    private static AlsLocalPose BasedFbx(AlsLocalPose pose)
    {
        var basis = AlsFootIkCoordinates.FbxToGodotRotation; var inverse = Quaternion.Conjugate(basis);
        return pose with { Position = Vector3.Transform(pose.Position, inverse), Rotation = inverse * pose.Rotation * basis };
    }

    [Fact]
    public void DiagnosticTracePublishesOnlyTheCommittedFootTransaction()
    {
        var runtime = new AlsFootIkFrameRuntime(AlsFootIkInputCompiler.Compile(Read()), Compile(), Bones,
            Parents, FootCurves, AlsFootIkPoseSpace.Godot, Pose(), captureBasedTrace: true);
        AlsInertialCurve[] curves = [new(1), new(1), new(1), new(1)];
        var first = Scene(1, runtime.CommittedIdentity);
        runtime.Prepare(first, AlsMovementStateInput.Grounded);
        runtime.Evaluate(Pose(), curves);
        Assert.Null(runtime.CommittedBasedTrace);
        runtime.CompleteFinalOutput(first.Identity, curves); runtime.Commit(first.Identity);
        var committed = Assert.IsType<AlsBasedFootLockFrameTrace>(runtime.CommittedBasedTrace);
        Assert.Equal(first.Identity, committed.Identity);
        Assert.Equal(runtime.CommittedBased.Left, committed.Left.Result);

        var next = Scene(2, runtime.CommittedIdentity);
        runtime.Prepare(next, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), curves);
        runtime.CompleteFinalOutput(next.Identity, curves);
        Assert.Same(committed, runtime.CommittedBasedTrace);
        runtime.Cancel();
        Assert.Same(committed, runtime.CommittedBasedTrace);
        runtime.Prepare(next, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), curves);
        runtime.CompleteFinalOutput(next.Identity, curves); runtime.Commit(next.Identity);
        Assert.Equal(next.Identity, runtime.CommittedBasedTrace!.Identity);
        Assert.Equal(committed.Left.Result, runtime.CommittedBasedTrace.Left.Previous);
        Assert.Equal(1, runtime.CommittedBasedTrace.Left.Input.LockAmount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BasedTargetsFollowMixedRootWithoutFeedingBackOrdinaryLockedBones(bool fbx)
    {
        var runtime = BasedFrame(fbx ? AlsFootIkPoseSpace.Fbx : AlsFootIkPoseSpace.Godot);
        AlsInertialCurve[] curves = [new(1), new(1), new(0), new(0)];
        var ordinary = Pose();
        // A final parent blend can change the ordinary branch's target and
        // pelvis even though Foot IK was visited. Both are next-frame sockets.
        var alternate = Pose();
        alternate[11] = alternate[11] with { Position = new(-.15f, .1f, -.25f) };
        alternate[12] = alternate[12] with { Position = new(.15f, .08f, -.2f) };
        alternate[1] = alternate[1] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .4f) };
        alternate[10] = alternate[10] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f) };
        var finalPose = Pose(); // Simulates target bones rewritten by ordinary foot locking.
        finalPose[11] = finalPose[11] with { Position = new(4, 5, 6) };
        if (fbx) { ordinary = ordinary.Select(BasedFbx).ToArray(); alternate = alternate.Select(BasedFbx).ToArray(); }
        var first = Scene(1, runtime.CommittedIdentity);
        var before = runtime.CommittedBased;
        Prepare();
        var computedFinal = runtime.CandidateBased.Left.FinalComponent;
        var candidate = runtime.CandidateBased;
        var yaw = 2 * Math.Atan2(.75 * Math.Sin(.3), .25 + .75 * Math.Cos(.3));
        var c = Math.Cos(yaw); var s = Math.Sin(yaw);
        Near(new(18.75*c - 16.25*s, -16.25*c - 18.75*s, 7.5), candidate.LeftTarget.Position, .00001);
        Near(new(15*c + 16.25*s, 16.25*c - 15*s, 6), candidate.RightTarget.Position, .00001);
        var pelvis = new AlsQuaternion(0, 0, -.75 * Math.Sin(.2), .25 + .75 * Math.Cos(.2)).Normalized();
        Assert.True(Math.Abs(AlsQuaternion.Dot(candidate.PelvisRotation,
            pelvis)) > .999999);
        runtime.Cancel(); Assert.Equal(before, runtime.CommittedBased);
        Prepare(); Assert.Equal(candidate, runtime.CandidateBased);
        runtime.Commit(first.Identity);
        Assert.Equal(computedFinal, runtime.CommittedBased.Left.FinalComponent);
        runtime.Prepare(Scene(2, runtime.CommittedIdentity), AlsMovementStateInput.Grounded);
        Near(candidate.LeftTarget.Position, runtime.CandidateBased.Left.FinalComponent.Position, .00001);
        runtime.Cancel();
        void Prepare()
        {
            runtime.Prepare(first, AlsMovementStateInput.Grounded); runtime.Evaluate(ordinary, curves);
            runtime.CompleteFinalOutput(first.Identity, curves, finalPose, ordinary, alternate, .25f);
        }
    }

    [Theory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    public void BasedFootFrameTransportsActualControllerTargetsInBothBoneBases(int hz, bool fbx)
    {
        var runtime = BasedFrame(fbx ? AlsFootIkPoseSpace.Fbx : AlsFootIkPoseSpace.Godot);
        AlsInertialCurve[] curves = [new(1), new(1), new(1), new(1)];
        AlsDoubleVector? initial = null;
        for (var frame = 1; frame <= hz; frame++)
        {
            var t = (frame - 1f) / hz;
            var basis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f * t);
            var platform = Matrix4x4.CreateFromQuaternion(basis); platform.Translation = new(t, 0, 0);
            var input = Scene(frame, runtime.CommittedIdentity, 1f / hz);
            input = input with { Floor = input.Floor with { PlatformId = 7, ColliderId = 0x100000007, PlatformTransform = platform },
                LeftFootHit = AlsFootHit.Invalid, RightFootHit = AlsFootHit.Invalid,
                FootIk = input.FootIk with { ComponentToWorld = AlsLocalPose.Identity with { Position = new(t, 0, 0),
                    Rotation = fbx ? AlsFootIkCoordinates.FbxToGodotRotation : Quaternion.Identity },
                    // IK-after feedback is deliberately unrelated: this adapter
                    // must use the separately committed pre-controller targets.
                    LeftComponent = AlsLocalPose.Identity with { Position = new(5, 5, 5) } } };
            runtime.Prepare(input, AlsMovementStateInput.Grounded);
            var candidate = runtime.CandidateBased;
            var source = Pose(); source[11] = source[11] with { Position = new(-.2f + .01f * MathF.Sin(t * 30), 0, 0) };
            if (fbx) source = source.Select(BasedFbx).ToArray();
            runtime.Evaluate(source, curves);
            if (frame > 1)
            {
                initial ??= candidate.Left.BaseLock.Position;
                Near(initial.Value, candidate.Left.BaseLock.Position, 1e-5);
                Assert.Equal(0x100000007ul, candidate.Left.BaseIdentity);
                var final = Components(runtime.Pose)[11];
                var world = AlsPrecisePose.Compose(final, new(input.FootIk.ComponentToWorld));
                Near(candidate.Left.WorldLock.Position, new(-world.Position.Z * 100, world.Position.X * 100, world.Position.Y * 100), .0001);
            }
            runtime.CompleteFinalOutput(input.Identity, curves); runtime.Commit(input.Identity);
        }
    }

    [Fact]
    public void BasedFootTargetsAndFinalRemainSeparateWhenCurveDisablesLock()
    {
        var runtime = BasedFrame(); AlsInertialCurve[] curves = [new(1), new(1), new(0), new(0)];
        for (var frame = 1; frame <= 8; frame++)
        {
            var input = Scene(frame, runtime.CommittedIdentity) with { LeftFootHit = AlsFootHit.Invalid, RightFootHit = AlsFootHit.Invalid };
            var previousTarget = runtime.CommittedBased.LeftTarget;
            var source = Pose(); source[11] = source[11] with { Position = new(-.2f, .01f * frame, 0) };
            runtime.Prepare(input, AlsMovementStateInput.Grounded);
            Near(previousTarget.Position, runtime.CandidateBased.Left.FinalComponent.Position, 1e-5);
            runtime.Evaluate(source, curves);
            var finalBone = Components(runtime.Pose)[11];
            Near(previousTarget.Position, AlsFootIkCoordinates.ToNative(finalBone.Position.ToSingle()), 1e-5);
            runtime.CompleteFinalOutput(input.Identity, curves); runtime.Commit(input.Identity);
            Assert.Equal(0, runtime.CommittedBased.Left.Amount);
            Assert.Equal(frame, runtime.CommittedBased.LeftTarget.Position.Z, 5);
            Assert.NotEqual(runtime.CommittedBased.LeftTarget.Position, runtime.CommittedBased.Left.FinalComponent.Position);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BasedFootHistoryTeleportAndHiddenRootAreOneCancelableTransaction(bool hidden)
    {
        var runtime = BasedFrame(); var pose = Pose(); AlsInertialCurve[] curves = [new(1), new(1), new(1), new(1)];
        for (var frame = 1; frame <= 3; frame++)
        {
            var input = Scene(frame, runtime.CommittedIdentity);
            runtime.Prepare(input, AlsMovementStateInput.Grounded); runtime.Evaluate(pose, curves);
            runtime.CompleteFinalOutput(input.Identity, curves); runtime.Commit(input.Identity);
        }
        var committed = runtime.CommittedBased;
        var next = Scene(4, runtime.CommittedIdentity);
        next = next with { FootIk = next.FootIk with { TeleportSequence = 1,
            ComponentToWorld = AlsLocalPose.Identity with { Position = new(4, 0, 0) } } };
        Prepare();
        var candidate = runtime.CandidateBased;
        Assert.Equal(0, candidate.SecondsSinceTeleport);
        Assert.Equal(1ul, candidate.TeleportSequence);
        Near(committed.Left.ComponentLock.Position + new AlsDoubleVector(0, 400, 0), candidate.Left.WorldLock.Position, .0001);
        var bad = curves.ToArray(); bad[0] = new(float.NaN);
        Assert.Throws<ArgumentException>(() => runtime.CompleteFinalOutput(next.Identity, bad, pose));
        Assert.Equal(committed, runtime.CommittedBased); Assert.Equal(committed, runtime.CandidateBased);
        Prepare(); Assert.Equal(candidate, runtime.CandidateBased);
        runtime.CompleteFinalOutput(next.Identity, curves, pose); runtime.Cancel();
        Assert.Equal(committed, runtime.CommittedBased);
        Prepare(); runtime.CompleteFinalOutput(next.Identity, curves, pose); runtime.Commit(next.Identity);
        Assert.Equal(candidate.Left, runtime.CommittedBased.Left);
        Assert.Equal(next.Identity, runtime.CommittedIdentity);
        void Prepare()
        {
            runtime.PrepareGlobal(next, AlsMovementStateInput.Grounded); runtime.PrepareGraph(!hidden);
            if (!hidden) runtime.Evaluate(pose, curves);
        }
    }

    [Fact]
    public void BasedFootReleaseFinalIsNotBlendedTwiceByV4Controllers()
    {
        var runtime = BasedFrame(); AlsInertialCurve[] curves = [new(1), new(1), new(1), new(1)];
        for (var frame = 1; frame <= 4; frame++)
        {
            var source = Pose(); source[11] = source[11] with { Position = new(-.2f, 0, -.03f * frame) };
            var input = Scene(frame, runtime.CommittedIdentity) with { LeftFootHit = AlsFootHit.Invalid, RightFootHit = AlsFootHit.Invalid };
            runtime.Prepare(input, AlsMovementStateInput.Grounded); runtime.Evaluate(source, curves);
            runtime.CompleteFinalOutput(input.Identity, frame == 3 ? [new(1), new(1), new(.5f), new(.5f)] : curves);
            if (frame == 4)
            {
                Assert.Equal(.5f, runtime.CandidateBased.Left.Amount);
                var actual = AlsFootIkCoordinates.ToNative(Components(runtime.Pose)[11].Position.ToSingle());
                Near(runtime.CandidateBased.Left.FinalComponent.Position, actual, 1e-5);
            }
            runtime.Commit(input.Identity);
        }
    }

    [Fact]
    public void BasedFootFrameDoesNotAllocatePerUpdateAfterWarmup()
    {
        var runtime = BasedFrame(); var pose = Pose(); AlsInertialCurve[] curves = [new(1), new(1), new(1), new(1)];
        for (var i = 1; i <= 1000; i++) Step(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1001; i <= 2000; i++) Step(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Step(int frame)
        {
            var input = Scene(frame, runtime.CommittedIdentity); runtime.Prepare(input, AlsMovementStateInput.Grounded);
            runtime.Evaluate(pose, curves); runtime.CompleteFinalOutput(input.Identity, curves); runtime.Commit(input.Identity);
        }
    }
}
