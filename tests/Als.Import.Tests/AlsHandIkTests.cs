using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsHandIkTests
{
    private static readonly string[] Bones = ["root", "upper_l", "lower_l", "hand_l", "finger_l", "upper_r", "lower_r", "hand_r", "finger_r", "VB RHS_ik_hand_l", "VB LHS_ik_hand_r"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 7, 3];

    [Fact]
    public void CompilesOriginalLeftThenRightTargets()
    {
        var definition = Compile();
        Assert.Equal(new AlsHandIkNode(26,"hand_l","VB RHS_ik_hand_l","Enable_HandIK_L"), definition.Nodes[0]);
        Assert.Equal(new AlsHandIkNode(24,"hand_r","VB LHS_ik_hand_r","Enable_HandIK_R"), definition.Nodes[1]);
    }
    [Theory]
    [InlineData("space")]
    [InlineData("rotation")]
    [InlineData("stretch")]
    [InlineData("offset")]
    [InlineData("target")]
    [InlineData("twist")]
    public void RejectsChangedHandControlSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var p = root["compiledNodeInventory"]!.AsArray().Single(n => n!["path"]!.GetValue<string>()
            .EndsWith(":AnimGraph.AnimGraphNode_TwoBoneIK_5",StringComparison.Ordinal))!["properties"]!["Node"]!;
        switch (mutation)
        {
            case "space": p["jointTargetLocationSpace"] = "BCS_BoneSpace"; break;
            case "rotation": p["bTakeRotationFromEffectorSpace"] = false; break;
            case "stretch": p["bAllowStretching"] = true; break;
            case "offset": p["effectorLocation"]!["x"] = 1; break;
            case "target": p["effectorTarget"]!["boneReference"]!["boneName"] = "hand_r"; break;
            case "twist": p["bAllowTwist"] = false; break;
        }
        Assert.ThrowsAny<Exception>(() => AlsHandIkCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void SolverReachesGoalWithOriginalBendPlaneAndLengths()
    {
        var r = At(0,0,0); var j = At(1,0,0); var e = At(2,0,0);
        AlsTwoBoneIk.Solve(ref r,ref j,ref e,new(0,1,0),new(1,1,0));
        Near(new(0,1,0),j.Position); Near(new(1,1,0),e.Position);
        Near(new(0,1,0),new AlsDoubleVector(1,0,0).Rotate(r.Rotation));
        Assert.Equal(1,(j.Position-r.Position).LengthSquared,10); Assert.Equal(1,(e.Position-j.Position).LengthSquared,10);
    }
    [Theory]
    [InlineData(5)] [InlineData(-5)]
    public void UnreachableGoalExtendsWithoutStretching(double x)
    {
        var r = At(0,0,0); var j = At(1,0,0); var e = At(2,0,0);
        AlsTwoBoneIk.Solve(ref r,ref j,ref e,new(0,1,0),new(x,0,0));
        Near(new(Math.Sign(x),0,0),j.Position); Near(new(2*Math.Sign(x),0,0),e.Position);
        Near(new(Math.Sign(x),0,0),new AlsDoubleVector(1,0,0).Rotate(r.Rotation));
    }
    [Fact]
    public void CollinearNativeZDirectionUsesNativeFallbackBendAxis()
    {
        var r = At(0,0,0); var j = At(0,0,1); var e = At(0,0,2);
        AlsTwoBoneIk.Solve(ref r,ref j,ref e,new(0,0,1),new(0,0,1));
        Near(new(0,-Math.Sqrt(.75),.5),j.Position); Near(new(0,0,1),e.Position);
    }

    [Fact]
    public void SecondHandUsesTargetMovedByFirstHandAndCopiesEffectorRotation()
    {
        var runtime = New(); var source = Pose();
        runtime.Prepare(Input(1,1,1)); runtime.Evaluate(source,[new(0),default]);
        var component = Components(runtime.Pose);
        Near(new(-.5,1,0),component[3].Position);
        Near(new(.5,1,0),component[7].Position);
        Assert.True((component[7].Position-new AlsDoubleVector(1,0,0)).LengthSquared>.1,"Right hand used its pre-left target.");
        Assert.True(Math.Abs(AlsQuaternion.Dot(component[3].Rotation,AlsQuaternion.Identity))>1-1e-8);
        Assert.True(Math.Abs(AlsQuaternion.Dot(component[7].Rotation,AlsQuaternion.Identity))>1-1e-8);
        Assert.Equal(new AlsInertialCurve(0),runtime.Curves[0]); Assert.False(runtime.Curves[1].Present);
    }
    [Fact]
    public void PartialControlBlendsLocalChainsInsteadOfLerpingEffectorPositions()
    {
        var runtime = New(); var source = Pose();
        runtime.Prepare(Input(1,.5,0)); runtime.Evaluate(source,[default,default]);
        var component = Components(runtime.Pose);
        Assert.True((component[3].Position-new AlsDoubleVector(-.25,.5,0)).LengthSquared>1e-4);
        Assert.True(Math.Abs((component[2].Position-component[1].Position).LengthSquared-1)<1e-6);
        Assert.True(Math.Abs((component[3].Position-component[2].Position).LengthSquared-1)<1e-6);
    }
    [Fact]
    public void HiddenControlsPreservePoseAndFailedCandidateCanRetry()
    {
        var runtime = New(); var source = Pose();
        runtime.Prepare(Input(1,0,0)); runtime.Evaluate(source,[default,new(1)]);
        Assert.Equal(source,runtime.Pose.ToArray()); Assert.Equal(0,runtime.EvaluatedHands); runtime.Commit(new(1,11,2));
        runtime.Prepare(Input(2,1,1)); runtime.Evaluate(source,[default,new(1)]);
        var expected = runtime.Pose.ToArray(); runtime.Cancel();
        runtime.Prepare(Input(2,1,1)); var bad = (AlsLocalPose[])source.Clone(); bad[3] = bad[3] with { Position = new(float.NaN,0,0) };
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(bad,[default,new(1)]));
        Assert.Equal(new AlsFrameIdentity(1,11,2),runtime.CommittedIdentity);
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(new(2,11,2)));
        runtime.Prepare(Input(2,1,1)); runtime.Evaluate(source,[default,new(1)]); Assert.Equal(expected,runtime.Pose.ToArray());
    }
    [Fact]
    public void SequentialPartialHandControlsDoNotAllocateAfterWarmup()
    {
        var runtime = New(); var source = Pose(); var curves = new AlsInertialCurve[2];
        for (var frame=1;frame<=1000;frame++) Step(frame);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var frame=1001;frame<=3000;frame++) Step(frame);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-bytes);
        void Step(int frame) { var input=Input(frame,.4,.7); runtime.Prepare(input); runtime.Evaluate(source,curves); runtime.Commit(input.Identity); }
    }

    [Fact]
    public void HiddenCommitHoldsNodeInputsAndLastPoseWhileCancelledCandidatesCannotChangeThem()
    {
        var runtime=New(); var source=Pose();
        runtime.Prepare(Input(1,.4,.7)); runtime.Evaluate(source,[default,default]); runtime.Commit(new(1,11,2));
        runtime.Prepare(Input(2,1,0)); runtime.Evaluate(source,[default,default]); runtime.Cancel();
        Assert.Equal(.4f,runtime.LeftAlpha); Assert.Equal(.7f,runtime.RightAlpha);
        runtime.Prepare(Input(2,0,1),updateSource:false);
        Assert.Equal(.4f,runtime.LeftAlpha); Assert.Equal(.7f,runtime.RightAlpha); Assert.Equal(0,runtime.EvaluatedHands);
        Assert.Throws<InvalidOperationException>(()=>runtime.Evaluate(source,[default,default]));
        Assert.Throws<InvalidOperationException>(()=>{_=runtime.Pose.Length;});
        runtime.ValidateCommit(new(2,11,2)); runtime.Commit(new(2,11,2));
        Assert.Equal(new(2,11,2),runtime.CommittedIdentity); Assert.Equal(new(1,11,2),runtime.CommittedPoseIdentity);
        runtime.Prepare(Input(3,1,1)); runtime.Evaluate(source,[default,default]);
        var expected=runtime.Pose.ToArray(); runtime.Cancel();
        Assert.Equal(.4f,runtime.LeftAlpha); Assert.Equal(new(2,11,2),runtime.CommittedIdentity);
        runtime.Prepare(Input(3,1,1)); runtime.Evaluate(source,[default,default]);
        Assert.Equal(expected,runtime.Pose.ToArray()); runtime.Commit(new(3,11,2));
        Assert.Equal(runtime.CommittedIdentity,runtime.CommittedPoseIdentity);
    }

    private static AlsLocalPose[] Pose()
    {
        var pose = Enumerable.Repeat(AlsLocalPose.Identity,Bones.Length).ToArray();
        pose[1] = pose[1] with {Position=new(-2,0,0)}; pose[2] = pose[2] with {Position=new(1,0,0)}; pose[3] = pose[3] with {Position=new(1,0,0)};
        pose[4] = pose[4] with {Position=new(.2f,0,0)};
        pose[5] = pose[5] with {Position=new(2,0,0)}; pose[6] = pose[6] with {Position=new(-1,0,0)}; pose[7] = pose[7] with {Position=new(-1,0,0)};
        pose[8] = pose[8] with {Position=new(-.2f,0,0)};
        pose[9] = pose[9] with {Position=new(-.5f,1,0)}; pose[10] = pose[10] with {Position=new(1,0,0)};
        return pose;
    }
    private static AlsPrecisePose[] Components(ReadOnlySpan<AlsLocalPose> pose)
    {
        var result = new AlsPrecisePose[pose.Length];
        for(var i=0;i<pose.Length;i++) result[i]=Parents[i]<0 ? new(pose[i]) : AlsPrecisePose.Compose(new(pose[i]),result[Parents[i]]);
        return result;
    }
    private static AlsLayeringInput Input(int frame,double left,double right) => default(AlsLayeringInput) with {Identity=new(frame,11,2),LeftHandIk=left,RightHandIk=right};
    private static AlsPrecisePose At(double x,double y,double z) => AlsPrecisePose.Identity with {Position=new(x,y,z)};
    private static void Near(AlsDoubleVector expected,AlsDoubleVector actual) => Assert.True((expected-actual).LengthSquared<1e-12,$"Expected {expected}; got {actual}.");
    private static AlsHandIkRuntime New() => new(Compile(),Bones,Parents,2,1);
    private static AlsHandIkDefinition Compile() => AlsHandIkCompiler.Compile(Read());
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/v4_layering_inputs.json"));
}
