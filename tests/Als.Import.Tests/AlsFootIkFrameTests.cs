using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed partial class AlsFootIkControllerTests
{
    [Theory]
    [InlineData(0f)] [InlineData(.7f)]
    public void FbxFramePreservesNativePropertiesAndWorldControls(float yaw)
    {
        var model=AlsFootIkInputCompiler.Compile(Read());
        string[] curves=["Enable_FootIK_L","Enable_FootIK_R","FootLock_L","FootLock_R"];
        var canonical=new AlsFootIkFrameRuntime(model,Compile(),Bones,Parents,curves);
        var fbx=new AlsFootIkFrameRuntime(model,Compile(),Bones,Parents,curves,AlsFootIkPoseSpace.Fbx);
        var basis=AlsFootIkCoordinates.FbxToGodotRotation;
        var inverse=Quaternion.Conjugate(basis);
        var source=Pose(); var fbxSource=source.Select(ToFbx).ToArray();
        var world=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
        var previous=default(AlsFrameIdentity);
        for(var frame=1;frame<=3;frame++)
        {
            var input=Scene(frame,previous);
            input=input with {FootIk=input.FootIk with
                {ComponentToWorld=AlsLocalPose.Identity with {Rotation=world},MovementVelocity=new(.3f,0,-.5f)}};
            var other=input with {FootIk=input.FootIk with
            {
                ComponentToWorld=input.FootIk.ComponentToWorld with {Rotation=world*basis},
                LeftComponent=ToFbx(input.FootIk.LeftComponent),RightComponent=ToFbx(input.FootIk.RightComponent)
            }};
            canonical.Prepare(input,AlsMovementStateInput.Grounded); fbx.Prepare(other,AlsMovementStateInput.Grounded);
            var a=canonical.CandidateState; var b=fbx.CandidateState;
            Near(a.LeftLock.Location,b.LeftLock.Location,2e-5); Near(a.RightLock.Location,b.RightLock.Location,2e-5);
            Near(a.LeftOffset.Location,b.LeftOffset.Location,2e-5); Near(a.RightOffset.Location,b.RightOffset.Location,2e-5);
            Near(a.Pelvis.Offset,b.Pelvis.Offset,2e-5);
            Assert.Equal(a.LeftLock.Alpha,b.LeftLock.Alpha); Assert.Equal(a.RightLock.Alpha,b.RightLock.Alpha);
            AlsInertialCurve[] values=[new(1),new(.8f),new(1),new(.6f)];
            canonical.Evaluate(source,values); fbx.Evaluate(fbxSource,values);
            for(var bone=0;bone<Bones.Length;bone++)
            {
                var expected=ToFbx(canonical.Pose[bone]); var actual=fbx.Pose[bone];
                Assert.True(Vector3.Distance(expected.Position,actual.Position)<2e-5f,$"FBX position {Bones[bone]}");
                Assert.True(MathF.Min((expected.Rotation-actual.Rotation).Length(),(expected.Rotation+actual.Rotation).Length())<2e-5f,
                    $"FBX rotation {Bones[bone]}");
            }
            canonical.CompleteFinalOutput(input.Identity,canonical.Curves); fbx.CompleteFinalOutput(input.Identity,fbx.Curves);
            canonical.Commit(input.Identity); fbx.Commit(input.Identity); previous=input.Identity;
        }
        AlsLocalPose ToFbx(AlsLocalPose p)=>p with
            {Position=Vector3.Transform(p.Position,inverse),Rotation=inverse*p.Rotation*basis};
    }
    private static readonly string[] FootCurves = ["Enable_FootIK_L", "Enable_FootIK_R", "FootLock_L", "FootLock_R"];
    private static AlsFootIkFrameRuntime NewFrame() => new(AlsFootIkInputCompiler.Compile(Read()), Compile(), Bones, Parents, FootCurves);
    private static AlsFrameInput Scene(int frame, AlsFrameIdentity previous = default, float delta = 1f / 60) =>
        AlsFrameInput.CreateDefault(new(frame, 11, 2), delta) with
        {
            Floor = new(1, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            FootIk = new(1, previous, AlsLocalPose.Identity,
                AlsLocalPose.Identity with { Position = new(-.2f, .135f, 0) },
                AlsLocalPose.Identity with { Position = new(.2f, .135f, 0) },
                Vector3.Zero, Quaternion.Identity, Vector3.Zero, delta),
            LeftFootHit = AlsFootHit.Invalid with { Valid = 1, Walkable = 1, Position = new(-.2f, .1f, 0) },
            RightFootHit = AlsFootHit.Invalid with { Valid = 1, Walkable = 1, Position = new(.2f, .05f, 0) },
        };

    [Fact]
    public void FootFrameReadsCommittedCurvesAndCommitsPropertiesWithPose()
    {
        var runtime = NewFrame(); var input = Scene(1); var source = Pose();
        runtime.Prepare(input, AlsMovementStateInput.Grounded);
        runtime.Evaluate(source, [new(1), new(1), new(1), new(0)]);
        Assert.Equal(0, runtime.EvaluatedControls); Assert.Equal(source, runtime.Pose.ToArray());
        Assert.Equal(default, runtime.CommittedIdentity); Assert.Equal(0, runtime.CommittedState.LeftLock.Alpha);
        runtime.CompleteFinalOutput(input.Identity, runtime.Curves); runtime.Commit(input.Identity);
        input = Scene(2, input.Identity);
        runtime.Prepare(input, AlsMovementStateInput.Grounded);
        Assert.Equal(1, runtime.CandidateState.LeftLock.Alpha);
        Assert.Equal(2.5, runtime.CandidateState.LeftOffset.Location.Z, 5);
        Assert.Equal(1.25, runtime.CandidateState.RightOffset.Location.Z, 5);
        // This frame's now-zero curves cannot undo its already-prepared controls.
        runtime.Evaluate(source, [new(0), new(0), new(0), new(0)]);
        Assert.True(runtime.EvaluatedControls > 0);
        Assert.Equal(0, runtime.CommittedState.LeftLock.Alpha);
        var candidate = runtime.CandidateState; runtime.CompleteFinalOutput(input.Identity, runtime.Curves); runtime.Commit(input.Identity);
        Assert.Equal(candidate, runtime.CommittedState);
        input = Scene(3, input.Identity); runtime.Prepare(input, AlsMovementStateInput.Grounded);
        Assert.Equal(Vector3.Zero, runtime.CandidateState.LeftOffset.Location.ToSingle());
        // Native SetFootLocking preserves lock alpha/location while enable <= 0.
        Assert.Equal(candidate.LeftLock, runtime.CandidateState.LeftLock);
        runtime.Cancel();
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FootFrameUsesMovementVelocityAndWorldDeltaInsteadOfVisualVelocityAndAnimationDelta(int hz)
    {
        var runtime = NewFrame(); var first = Scene(1);
        runtime.Prepare(first, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), [new(1), new(1), new(1), new(1)]);
        runtime.CompleteFinalOutput(first.Identity, runtime.Curves); runtime.Commit(first.Identity);
        var frame = Scene(2, first.Identity, 1f / hz);
        frame = frame with { ActualVelocity = new(100, 100, 100), FootIk = frame.FootIk with { MovementVelocity = new(0, 0, -3), WorldDelta = .02f } };
        runtime.Prepare(frame, AlsMovementStateInput.Grounded);
        Near(new(-300 * (double).02f, -.2f * 100.0, .135f * 100.0), runtime.CandidateState.LeftLock.Location, 1e-7);
        var expected = 10.0 * Math.Clamp((1f / hz) * 15, 0, 1);
        Assert.Equal(expected, runtime.CandidateState.LeftOffset.Location.Z, 5);
        runtime.Cancel();
    }
    [Theory]
    [InlineData("uncaptured")] [InlineData("stale")] [InlineData("generation")] [InlineData("character")] [InlineData("gap")]
    public void FootFrameRejectsForeignOrUncapturedSceneHistory(string mutation)
    {
        var runtime = NewFrame(); var first = Scene(1); runtime.Prepare(first, AlsMovementStateInput.Grounded);
        runtime.Evaluate(Pose(), [new(1), new(1), new(1), new(1)]);
        runtime.CompleteFinalOutput(first.Identity, runtime.Curves); runtime.Commit(first.Identity);
        var next = Scene(2, first.Identity);
        next = mutation switch
        {
            "uncaptured" => next with { FootIk = next.FootIk with { Captured = 0 } },
            "stale" => next with { FootIk = next.FootIk with { PoseIdentity = default } },
            "generation" => next with { Identity = new(2, 11, 3) },
            "character" => next with { Identity = new(2, 12, 2) },
            _ => next with { Identity = new(3, 11, 2) },
        };
        Assert.Throws<ArgumentException>(() => runtime.Prepare(next, AlsMovementStateInput.Grounded));
        Assert.Equal(first.Identity, runtime.CommittedIdentity);
    }
    [Fact]
    public void FootFrameDiscardAfterEvaluationRestoresPropertyAndCurveHistoryForRetry()
    {
        var runtime = NewFrame(); var first = Scene(1);
        runtime.Prepare(first, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), [new(1), new(1), new(1), new(1)]);
        runtime.CompleteFinalOutput(first.Identity, runtime.Curves); runtime.Commit(first.Identity);
        var input = Scene(2, first.Identity); var committed = runtime.CommittedState;
        runtime.Prepare(input, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), [default, default, default, default]);
        var properties = runtime.CandidateState; var pose = runtime.Pose.ToArray(); runtime.Cancel();
        Assert.Equal(committed, runtime.CommittedState); Assert.Equal(first.Identity, runtime.CommittedIdentity);
        runtime.Prepare(input, AlsMovementStateInput.Grounded); Assert.Equal(properties, runtime.CandidateState);
        var bad = Pose(); bad[4] = bad[4] with { Position = new(float.NaN) };
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(bad, [default, default, default, default]));
        runtime.Prepare(input, AlsMovementStateInput.Grounded); runtime.Evaluate(Pose(), [default, default, default, default]);
        Assert.Equal(pose, runtime.Pose.ToArray()); Assert.Equal(properties, runtime.CandidateState);
        runtime.CompleteFinalOutput(input.Identity, runtime.Curves); runtime.Commit(input.Identity);
    }
    [Fact]
    public void FootFrameIsAirResetAndNormalReentryUseTheSameCommittedPropertyOwner()
    {
        var runtime = NewFrame(); var first = Scene(1); var p = Pose();
        runtime.Prepare(first, AlsMovementStateInput.Grounded); runtime.Evaluate(p, [new(1), new(1), new(0), new(0)]);
        runtime.CompleteFinalOutput(first.Identity, runtime.Curves); runtime.Commit(first.Identity);
        var second = Scene(2, first.Identity); runtime.Prepare(second, AlsMovementStateInput.Grounded);
        runtime.Evaluate(p, [new(1), new(1), new(0), new(0)]);
        runtime.CompleteFinalOutput(second.Identity, runtime.Curves); runtime.Commit(second.Identity);
        var before = runtime.CommittedState;
        var air = Scene(3, second.Identity) with { Floor = first.Floor with { IsGrounded = 0 } };
        runtime.Prepare(air, AlsMovementStateInput.InAir);
        Assert.True(runtime.CandidateState.LeftOffset.Location.Z < before.LeftOffset.Location.Z);
        Assert.Equal(before.RightOffset, runtime.CandidateState.RightOffset); // Original asymmetric reset.
        runtime.Evaluate(p, [new(1), new(1), new(0), new(0)]);
        runtime.CompleteFinalOutput(air.Identity, runtime.Curves); runtime.Commit(air.Identity);
        var landed = Scene(4, air.Identity); runtime.Prepare(landed, AlsMovementStateInput.Grounded);
        Assert.True(runtime.CandidateState.LeftOffset.Location.Z > runtime.CommittedState.LeftOffset.Location.Z);
        runtime.Cancel();
    }
    [Theory]
    [InlineData(0, 1, 0, 90, 0, -90, 0)]
    [InlineData(1, 0, 0, 90, 90, 0, 0)]
    [InlineData(0, 0, 1, 90, 0, 0, -90)]
    public void FootQuaternionConversionUsesNativeAxesAndSingularityPolicy(float x, float y, float z, float angle, double pitch, double yaw, double roll)
    {
        var q = Quaternion.CreateFromAxisAngle(new(x, y, z), angle * MathF.PI / 180);
        var r = AlsFootIkCoordinates.Rotation(q);
        Assert.Equal(pitch, r.Pitch, 4); Assert.Equal(yaw, r.Yaw, 4); Assert.Equal(roll, r.Roll, 4);
    }
    [Fact]
    public void FullFootFramePropertyAndControlLoopDoesNotAllocateAfterWarmup()
    {
        var runtime = NewFrame(); var pose = Pose(); AlsInertialCurve[] curves = [new(.8f), new(.6f), new(1), new(.4f)];
        for (var i = 1; i <= 1000; i++) Step(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1001; i <= 3000; i++) Step(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Step(int frame)
        {
            var input = Scene(frame, runtime.CommittedIdentity); runtime.Prepare(input, AlsMovementStateInput.Grounded);
            runtime.Evaluate(pose, curves); runtime.CompleteFinalOutput(input.Identity, runtime.Curves); runtime.Commit(input.Identity);
        }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void HiddenFootPropertiesConsumeAlternateRootCurvesWhilePoseHistoryStaysUnvisited(int hz)
    {
        var runtime = NewFrame(); var lastPose = default(AlsFrameIdentity);
        var source = Pose(); var changed = 0; var resumed = 0;
        AlsInertialCurve[] ordinary = [new(0), new(0), new(0), new(0)];
        AlsInertialCurve[] final = [new(1), new(1), new(1), new(1)];
        for (var frame = 1; frame <= hz; frame++)
        {
            var hidden = frame <= hz / 2;
            var priorId = runtime.CommittedIdentity; var priorState = runtime.CommittedState;
            var input = Scene(frame, priorId, 1f / hz);
            var state = (frame % 4) switch { 0 => AlsMovementStateInput.Ragdoll, 1 => AlsMovementStateInput.Mantling,
                2 => AlsMovementStateInput.InAir, _ => AlsMovementStateInput.Grounded };
            input = input with { FootIk = input.FootIk with { MovementVelocity = new(0, 0, -.7f) } };
            Prepare(); var expected = runtime.CandidateState;
            if (frame > 1)
            {
                Assert.Equal(1, expected.LeftLock.Alpha);
                if (state == AlsMovementStateInput.Ragdoll)
                {
                    Assert.Equal(priorState.LeftOffset, expected.LeftOffset);
                    Assert.Equal(priorState.RightOffset, expected.RightOffset);
                    Assert.Equal(priorState.Pelvis, expected.Pelvis);
                }
                if (expected != priorState) changed++;
            }
            if (hidden)
            {
                Assert.Equal(0, runtime.EvaluatedControls);
                Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(source, ordinary));
                Assert.Throws<InvalidOperationException>(() => { _ = runtime.Pose.Length; });
                Assert.Throws<InvalidOperationException>(() => { _ = runtime.Curves.Length; });
            }
            else if (frame == hz / 2 + 1) { Assert.True(runtime.EvaluatedControls > 0); resumed++; }
            runtime.CompleteFinalOutput(input.Identity, final); runtime.Cancel();
            Assert.Equal(priorId, runtime.CommittedIdentity); Assert.Equal(priorState, runtime.CommittedState);
            Assert.Equal(lastPose, runtime.CommittedPoseIdentity);
            Prepare(); Assert.Equal(expected, runtime.CandidateState);
            runtime.CompleteFinalOutput(input.Identity, final); runtime.Commit(input.Identity);
            if (!hidden) lastPose = input.Identity;
            Assert.Equal(lastPose, runtime.CommittedPoseIdentity); Assert.Equal(input.Identity, runtime.CommittedIdentity);
            void Prepare()
            {
                runtime.PrepareGlobal(input, state);
                Assert.Throws<InvalidOperationException>(() => runtime.ValidateCommit(input.Identity));
                runtime.PrepareGraph(!hidden);
                if (!hidden) runtime.Evaluate(source, ordinary);
                Assert.Throws<InvalidOperationException>(() => runtime.Commit(input.Identity));
            }
        }
        Assert.True(changed > hz / 2); Assert.Equal(1, resumed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FinalFootFeedbackRejectsWrongStageIdentityAndNonfiniteOutputWithoutLeaking(bool hidden)
    {
        var runtime = NewFrame(); var input = Scene(1); var source = Pose();
        AlsInertialCurve[] ordinary = [new(0), new(0), new(0), new(0)];
        AlsInertialCurve[] final = [new(1), new(1), new(1), new(1)];
        runtime.PrepareGlobal(input, AlsMovementStateInput.Grounded);
        Assert.Throws<InvalidOperationException>(() => runtime.CompleteFinalOutput(input.Identity, final));
        runtime.PrepareGraph(!hidden);
        if (!hidden)
        {
            Assert.Throws<InvalidOperationException>(() => runtime.CompleteFinalOutput(input.Identity, final));
            runtime.Evaluate(source, ordinary);
        }
        Assert.Throws<InvalidOperationException>(() => runtime.CompleteFinalOutput(new(1, 12, 2), final));
        var bad = final.ToArray(); bad[^1] = new(float.NaN);
        Assert.Throws<ArgumentException>(() => runtime.CompleteFinalOutput(input.Identity, bad));
        Assert.Equal(default, runtime.CommittedIdentity);
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(input.Identity));
        runtime.PrepareGlobal(input, AlsMovementStateInput.Grounded); runtime.PrepareGraph(!hidden);
        if (!hidden) runtime.Evaluate(source, ordinary);
        runtime.CompleteFinalOutput(input.Identity, final);
        Assert.Throws<InvalidOperationException>(() => runtime.CompleteFinalOutput(input.Identity, ordinary));
        runtime.Commit(input.Identity);
        runtime.PrepareGlobal(Scene(2, input.Identity), AlsMovementStateInput.Grounded);
        Assert.Equal(1, runtime.CandidateState.LeftLock.Alpha);
        runtime.Cancel();
    }
}
