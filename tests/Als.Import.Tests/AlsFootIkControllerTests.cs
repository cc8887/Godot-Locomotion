using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed partial class AlsFootIkControllerTests
{
    private static readonly string[] Bones = ["root", "pelvis", "thigh_l", "calf_l", "foot_l", "toe_l",
        "thigh_r", "calf_r", "foot_r", "toe_r", "ik_foot_root", "ik_foot_l", "ik_foot_r",
        "VB ik_foot_l_Offset", "VB ik_foot_r_Offset", "VB ik_knee_target_l", "VB ik_knee_target_r"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 4, 1, 6, 7, 8, 0, 10, 10, 11, 12, 2, 6];

    [Fact]
    public void CompilesOriginalNineControlChainWithNativeDefaults()
    {
        var d = Compile();
        Assert.Equal(new(20, 30, 0), d.LeftKneeOffset); Assert.Equal(new(-20, -30, 0), d.RightKneeOffset);
        Assert.Equal(1, d.StartStretchRatio); Assert.Equal(1.5, d.MaxStretchScale);
    }
    [Theory]
    [InlineData("stretch")] [InlineData("space")] [InlineData("target")] [InlineData("curve")]
    [InlineData("order")] [InlineData("knee")] [InlineData("rotation")] [InlineData("alphaPolicy")]
    public void RejectsChangedGraphBeforeRuntime(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(":Foot IK"))!;
        var text = graph["nativeText"]!.GetValue<string>();
        var modified = mutation switch
        {
            "stretch" => text.Replace("MaxStretchScale=1.500000", "MaxStretchScale=1.200000"),
            "space" => text.Replace("TranslationSpace=BCS_WorldSpace", "TranslationSpace=BCS_ComponentSpace"),
            "target" => text.Replace("BoneName=\"VB ik_foot_l_Offset\"", "BoneName=\"ik_foot_l\""),
            "curve" => text.Replace("DefaultValue=\"Enable_FootIK_L\"", "DefaultValue=\"Enable_FootIK_R\""),
            "order" => text.Replace("LinkedTo=(AnimGraphNode_ModifyBone_3 ", "LinkedTo=(AnimGraphNode_ModifyBone_2 "),
            "knee" => text.Replace("X=20.000000,Y=30.000000", "X=-20.000000,Y=30.000000"),
            "rotation" => text.Replace("bTakeRotationFromEffectorSpace=True", "bTakeRotationFromEffectorSpace=False"),
            _ => text.Replace("AlphaBoolBlend=(BlendOption=Linear)", "AlphaScaleBias=(Scale=0.5),AlphaBoolBlend=(BlendOption=Linear)")
        };
        Assert.NotEqual(text, modified); graph["nativeText"] = modified;
        Assert.ThrowsAny<Exception>(() => AlsFootIkCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void NativeStretchOracleMatchesAllReachThresholdsAndDegenerateBendPlanes()
    {
        var root = Native(); var rows = root["rows"]!.AsArray(); Assert.Equal(96, rows.Count);
        foreach (var row in rows)
        {
            var r = At(V(row!["root"]!)); var j = At(V(row["joint"]!)); var e = At(V(row["end"]!));
            AlsTwoBoneIk.Solve(ref r, ref j, ref e, V(row["pole"]!), V(row["target"]!), row["allow"]!.GetValue<bool>(),
                row["start"]!.GetValue<double>(), row["maximum"]!.GetValue<double>());
            Near(V(row["resultJoint"]!), j.Position, 1e-8); Near(V(row["resultEnd"]!), e.Position, 1e-8);
            Assert.Equal(AlsDoubleVector.One, r.Scale); Assert.Equal(AlsDoubleVector.One, j.Scale);
        }
    }
    [Fact]
    public void FullLockRotationsMatchNativeQuaternionConversionIncludingWinding()
    {
        var rows = Native()["rotations"]!.AsArray(); Assert.Equal(7, rows.Count);
        foreach (var row in rows)
        {
            var v = V(row!["rotation"]!); var q = row["quaternion"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
            var p = default(AlsFootIkPropertyState) with { LeftLock = new(1, new(0, -20, 0), new(v.X, v.Y, v.Z)) };
            var runtime = New(); runtime.Prepare(Input(1, p)); runtime.Evaluate(Pose(), [default, default]);
            var actual = Components(runtime.Pose)[11].Rotation; var expected = new AlsQuaternion(-q[1], -q[2], q[0], q[3]);
            Assert.True(Math.Abs(AlsQuaternion.Dot(actual, expected)) > 1 - 1e-7);
        }
    }
    [Fact]
    public void LockFeedsVirtualTargetBeforeLegSolveAndFootTakesTargetRotation()
    {
        var p = default(AlsFootIkPropertyState) with { LeftLock = new(1, new(0, -20, 10), new(0, 30, 0)) };
        var runtime = New(); runtime.Prepare(Input(1, p, 1, 0)); runtime.Evaluate(Pose(), [new(0), default]);
        var c = Components(runtime.Pose);
        Near(new(-.2, .1, 0), c[11].Position); Near(c[11].Position, c[13].Position);
        Near(c[13].Position, c[4].Position);
        Assert.True(Math.Abs(AlsQuaternion.Dot(c[4].Rotation, c[13].Rotation)) > 1 - 1e-7);
        Assert.Equal(4, runtime.EvaluatedControls);
        Near(new(.2, 0, 0), c[8].Position);
    }
    [Fact]
    public void PelvisOffsetIsWorldVectorWithRotatedAndScaledComponent()
    {
        var world = new AlsPrecisePose(new(7, 3, -4), new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, .9f)), new(2, 2, 2));
        var p = default(AlsFootIkPropertyState) with { Pelvis = new(1, new(10, 20, -10)) };
        var source = Pose(); var before = AlsPrecisePose.Compose(Components(source)[1], world);
        var runtime = New(); runtime.Prepare(Input(1, p) with { ComponentToWorld = world }); runtime.Evaluate(source, [default, default]);
        var after = AlsPrecisePose.Compose(Components(runtime.Pose)[1], world);
        Near(new(.2, -.1, -.1), after.Position - before.Position); Assert.Equal(1, runtime.EvaluatedControls);
    }
    [Fact]
    public void PelvisMovesChainBeforeSolveAndLegUsesAuthoredStretchInsteadOfClampingAtRestLength()
    {
        var p = default(AlsFootIkPropertyState) with { Pelvis = new(1, new(0, 0, 20)) };
        var runtime = New(); runtime.Prepare(Input(1, p, 1, 1)); runtime.Evaluate(Pose(), [default, default]);
        var c = Components(runtime.Pose);
        Near(new(-.2, 0, 0), c[4].Position); Near(new(.2, 0, 0), c[8].Position);
        Assert.Equal(.6, Math.Sqrt((c[3].Position - c[2].Position).LengthSquared), 6);
        Assert.Equal(.6, Math.Sqrt((c[4].Position - c[3].Position).LengthSquared), 6);
        Assert.Equal(7, runtime.EvaluatedControls);
    }
    [Fact]
    public void UpdateAlphasAreIndependentFromEvaluationCurvesAndPreserveCurvePresence()
    {
        var p = default(AlsFootIkPropertyState) with { LeftOffset = new(new(30, 0, 20), default) };
        var source = Pose(); var runtime = New();
        runtime.Prepare(Input(1, p)); runtime.Evaluate(source, [new(1), default]);
        Assert.Equal(source, runtime.Pose.ToArray()); Assert.Equal(0, runtime.EvaluatedControls);
        Assert.True(runtime.Curves[0].Present); Assert.False(runtime.Curves[1].Present); runtime.Cancel();
        runtime.Prepare(Input(1, p, .5f, 0)); runtime.Evaluate(source, [new(0), default]);
        var c = Components(runtime.Pose);
        Near(new(-.2, .1, -.15), c[13].Position);
        Assert.True((c[4].Position - c[13].Position).LengthSquared > 1e-4, "IK must blend the solved local chain, not just its effector.");
        Assert.Equal(.5, Math.Sqrt((c[3].Position - c[2].Position).LengthSquared), 6);
        Assert.Equal(.5, Math.Sqrt((c[4].Position - c[3].Position).LengthSquared), 6);
        Assert.Equal(new AlsInertialCurve(0), runtime.Curves[0]);
    }
    [Fact]
    public void KneeOffsetsAreInBoneSpaceAndAppliedAfterPelvis()
    {
        var pose = Pose(); pose[15] = pose[15] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) };
        var p = default(AlsFootIkPropertyState) with { Pelvis = new(1, new(0, 0, -10)) };
        var runtime = New(); runtime.Prepare(Input(1, p, 1, 0)); runtime.Evaluate(pose, [default, default]);
        // Knee target is a thigh child and moves again when IK rotates that
        // parent. Its LOCAL translation retains the authored bone-space delta.
        var delta = new AlsDoubleVector(.3, 0, -.2).Rotate(new(pose[15].Rotation));
        Near(new AlsDoubleVector(pose[15].Position) + delta, new(runtime.Pose[15].Position));
    }
    [Fact]
    public void LateDiscardAndBadEvaluationDoNotCommitAndRetryReproducesFullChain()
    {
        var p = new AlsFootIkPropertyState(new(1, new(0, -20, 0), default), new(.6, new(0, 20, 0), default),
            new(new(15, 0, 5), new(10, 0, 2)), new(new(-10, 0, 10), new(-3, 0, 8)), new(.7, new(2, 3, -4)));
        var runtime = New(); var input = Input(1, p, .8f, .6f); var source = Pose();
        runtime.Prepare(input); runtime.Evaluate(source, [default, new(.4f)]); var expected = runtime.Pose.ToArray();
        Assert.Equal(9, runtime.EvaluatedControls); runtime.Cancel();
        runtime.Prepare(input); var bad = (AlsLocalPose[])source.Clone(); bad[16] = bad[16] with { Position = new(float.NaN) };
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(bad, [default, new(.4f)]));
        Assert.Equal(default, runtime.CommittedIdentity); Assert.Throws<InvalidOperationException>(() => runtime.Commit(input.Identity));
        runtime.Prepare(input); runtime.Evaluate(source, [default, new(.4f)]); Assert.Equal(expected, runtime.Pose.ToArray());
        runtime.Commit(input.Identity); Assert.Equal(input.Identity, runtime.CommittedIdentity);
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(input));
    }
    [Fact]
    public void FullNineControlEvaluationDoesNotAllocateAfterWarmup()
    {
        var p = new AlsFootIkPropertyState(new(.3, new(0, -20, 0), default), new(.6, new(0, 20, 0), default),
            new(new(15, 0, 5), new(10, 0, 2)), new(new(-10, 0, 10), new(-3, 0, 8)), new(.7, new(2, 3, -4)));
        var runtime = New(); var source = Pose(); var curves = new AlsInertialCurve[2];
        for (var frame = 1; frame <= 1000; frame++) Step(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 3000; frame++) Step(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Step(int frame) { var input = Input(frame, p, .8f, .6f); runtime.Prepare(input); runtime.Evaluate(source, curves); runtime.Commit(input.Identity); }
    }
    private static AlsLocalPose[] Pose()
    {
        var p = Enumerable.Repeat(AlsLocalPose.Identity, Bones.Length).ToArray();
        p[1] = p[1] with { Position = new(0, 1, 0) };
        p[2] = p[2] with { Position = new(-.2f, 0, 0) }; p[6] = p[6] with { Position = new(.2f, 0, 0) };
        foreach (var i in new[] { 3, 4, 7, 8 }) p[i] = p[i] with { Position = new(0, -.5f, 0) };
        p[5] = p[5] with { Position = new(0, 0, -.1f) }; p[9] = p[9] with { Position = new(0, 0, -.1f) };
        p[11] = p[11] with { Position = new(-.2f, 0, 0) }; p[12] = p[12] with { Position = new(.2f, 0, 0) };
        p[15] = p[15] with { Position = new(0, -.4f, -1) }; p[16] = p[16] with { Position = new(0, -.4f, -1) };
        return p;
    }
    private static AlsPrecisePose[] Components(ReadOnlySpan<AlsLocalPose> pose)
    {
        var result = new AlsPrecisePose[pose.Length];
        for (var i = 0; i < pose.Length; i++) result[i] = Parents[i] < 0 ? new(pose[i]) : AlsPrecisePose.Compose(new(pose[i]), result[Parents[i]]);
        return result;
    }
    private static AlsFootIkControlInput Input(int frame, AlsFootIkPropertyState p, float left = 0, float right = 0) =>
        new(new(frame, 11, 2), p, left, right, AlsPrecisePose.Identity);
    private static AlsPrecisePose At(AlsDoubleVector v) => AlsPrecisePose.Identity with { Position = v };
    private static AlsDoubleVector V(JsonNode n) => new(n[0]!.GetValue<double>(), n[1]!.GetValue<double>(), n[2]!.GetValue<double>());
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual, double tolerance = 1e-6) =>
        Assert.True((expected - actual).LengthSquared <= tolerance * tolerance, $"Expected {expected}; got {actual}.");
    private static AlsFootIkRuntime New() => new(Compile(), Bones, Parents, 2);
    private static AlsFootIkDefinition Compile() => AlsFootIkCompiler.Compile(Read());
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_foot_ik_inputs.json"));
    private static JsonNode Native() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "tests/Als.Import.Tests/Fixtures/FootIk/native_controller_math.json")))!;
}
