using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredBoxOverlayTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    [Fact]
    public void ActualBoxGraphCompilesAndActionCurveAndBindingMutationsFail()
    {
        var catalog = Catalog(); var profile = AlsRefactoredBoxOverlayCompiler.Compile(catalog);
        Assert.Equal(79, profile.BoneNames.Length); Assert.Equal(new(.5f, .2f, 0, .2f), profile.BlendTimes);
        void Reject(int index, string field, JsonNode value)
        {
            var root = JsonNode.Parse(catalog.Read(AlsRefactoredBoxOverlayProfile.Blueprint).GetRawText())!;
            var node = root["compiled"]!["nodes"]!.AsArray().Single(n => (int)n!["propertyIndex"]! == index)!;
            node["runtime"]![field] = value;
            using var doc = JsonDocument.Parse(root.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredBoxOverlayCompiler.ValidateGraph(doc.RootElement));
        }
        Reject(13, "blendTime", JsonNode.Parse("[0.5,0.2,0.1,0.2]")!);
        Reject(13, "childUpateMode", JsonValue.Create("ResetChildOnActivate")!);
        Reject(8, "curveValues", JsonNode.Parse("[1,1]")!);
        Reject(6, "sourcePose", JsonNode.Parse("{\"linkId\":12,\"sourceLinkId\":6}")!);
        Reject(9, "groupName", JsonValue.Create("None")!);
        Reject(12, "explicitFrame", JsonValue.Create(1)!);
        var changed = JsonNode.Parse(catalog.Read(AlsRefactoredBoxOverlayProfile.Blueprint).GetRawText())!;
        changed["nativeText"] = changed["nativeText"]!.GetValue<string>().Replace("\"GetParent\",\"LocomotionAction\"", "\"GetParent\",\"OverlayMode\"", StringComparison.Ordinal);
        using var wrongBinding = JsonDocument.Parse(changed.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredBoxOverlayCompiler.ValidateGraph(wrongBinding.RootElement));
    }
    [Fact]
    public void ExactTagsInstantPreviousUpdateAndInvalidCandidateAreHandled()
    {
        Assert.Equal(AlsOverlayAction.Default, AlsOverlayActionBlend.Resolve("Als.LocomotionAction.Mantling.High"));
        Assert.Equal(AlsOverlayAction.Default, AlsOverlayActionBlend.Resolve(""));
        Assert.Equal(AlsOverlayAction.Mantling, AlsOverlayActionBlend.Resolve("Als.LocomotionAction.Mantling"));
        var times = new Vector4(.5f, .2f, 0, .2f);
        var first = AlsOverlayActionBlend.Advance(default, AlsOverlayAction.Default, .01f, times);
        var instant = AlsOverlayActionBlend.Advance(first.State, AlsOverlayAction.GettingUp, .01f, times);
        Assert.Equal(0, instant.ZeroWeightPrevious); Assert.Equal(new(0, 0, 1, 0), instant.State.Weights);
        var initialGetup = AlsOverlayActionBlend.Advance(default, AlsOverlayAction.GettingUp, 0, times);
        Assert.Equal(-1, initialGetup.ZeroWeightPrevious); Assert.Equal(instant.State.Weights, initialGetup.State.Weights);
        Assert.Throws<ArgumentException>(() => AlsOverlayActionBlend.Advance(first.State, AlsOverlayAction.Rolling, float.NaN, times));
        Assert.Equal(new(1, 0, 0, 0), first.State.Weights);
    }
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void NativeBoxActionsPreserveInterruptedBlendsCurvesClocksAndFrameTransactions(int hz)
    {
        var catalog = Catalog(); var profile = AlsRefactoredBoxOverlayCompiler.Compile(catalog);
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank, new Dictionary<string, AlsRefactoredTriangulationProfile>(), [profile.PlayerDefinition(0, 0)]);
        var overlay = profile.CreateRuntime(0);
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_box_overlay_trace")); var root = doc.RootElement;
        Assert.Equal(AlsRefactoredBoxOverlayProfile.Blueprint, root.GetProperty("source").GetString());
        Assert.Equal(profile.BoneNames.ToArray(), root.GetProperty("names").EnumerateArray().Select(v => v.GetString()!));
        foreach (var name in new[] { "refactored_animation_sources", "refactored_sync_inputs" })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))), root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == $"{hz}hz");
        var frame = 0; var zeroTicks = 0; var hidden = 0;
        double maxW = 0, maxT = 0, maxP = 0, maxQ = 0, maxS = 0, maxC = 0;
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var state = input.GetProperty("poseState");
            var action = AlsOverlayActionBlend.Resolve(input.GetProperty("action").GetString()!);
            var walking = state.GetProperty("GaitWalkingAmount").GetSingle(); var standing = state.GetProperty("StandingAmount").GetSingle(); var crouching = state.GetProperty("CrouchingAmount").GetSingle();
            var delta = input.GetProperty("delta").GetSingle(); var reset = input.GetProperty("reset").GetBoolean();
            var before = overlay.CommittedState;
            overlay.Prepare(frame, action, walking, standing, crouching, delta, reset);
            players.Prepare(frame, overlay.SourceInputs, delta); overlay.Evaluate(frame, players);
            var poses = overlay.Pose.ToArray(); var curves = overlay.Curves.ToArray(); var weights = overlay.Weights;
            overlay.Cancel(); players.Cancel(); Assert.Equal(before, overlay.CommittedState);
            Assert.Throws<InvalidOperationException>(() => overlay.Pose.ToArray());
            overlay.Prepare(frame, action, walking, standing, crouching, delta, reset);
            players.Prepare(frame, overlay.SourceInputs, delta); overlay.Evaluate(frame, players);
            Assert.Equal(poses, overlay.Pose.ToArray()); Assert.Equal(curves, overlay.Curves.ToArray()); Assert.Equal(weights, overlay.Weights);
            for (var i = 0; i < 4; i++)
            {
                var dw = Math.Abs(weights[i] - row.GetProperty("actionWeights")[i].GetSingle()); maxW = Math.Max(maxW, dw);
                Assert.True(dw <= 2e-6, $"{hz}Hz frame={frame} weight[{i}]={dw:R}");
            }
            if (!overlay.SourceInputs.IsEmpty)
            {
                var dt = Math.Abs(players.Players[0].Time - row.GetProperty("idleTime").GetSingle()); maxT = Math.Max(maxT, dt);
                Assert.True(dt <= 2e-6, $"{hz}Hz frame={frame} time={dt:R}");
                Assert.Equal(overlay.SourceInputs[0].Weight, row.GetProperty("idleWeight").GetSingle());
                if (overlay.SourceInputs[0].Weight == 0) zeroTicks++;
            }
            else hidden++;
            for (var bone = 0; bone < poses.Length; bone++)
            {
                var expected = row.GetProperty("pose")[bone]; var actual = overlay.Pose[bone];
                double[] V(string name) => expected.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var p = V("position"); var q = V("rotation"); var s = V("scale");
                var dp = Math.Sqrt(Math.Pow(actual.Position.X-p[0],2)+Math.Pow(actual.Position.Y-p[1],2)+Math.Pow(actual.Position.Z-p[2],2));
                var sign = actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3] < 0 ? -1 : 1;
                var dq = new[] { Math.Abs(actual.Rotation.X-sign*q[0]), Math.Abs(actual.Rotation.Y-sign*q[1]), Math.Abs(actual.Rotation.Z-sign*q[2]), Math.Abs(actual.Rotation.W-sign*q[3]) }.Max();
                var ds = new[] { Math.Abs(actual.Scale.X-s[0]), Math.Abs(actual.Scale.Y-s[1]), Math.Abs(actual.Scale.Z-s[2]) }.Max();
                maxP=Math.Max(maxP,dp); maxQ=Math.Max(maxQ,dq); maxS=Math.Max(maxS,ds);
                Assert.True(dp<=1e-10 && dq<=1e-12 && ds<=1e-12, $"{hz}Hz frame={frame} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
            }
            var native = row.GetProperty("curves"); var present = 0;
            for (var c = 0; c < profile.CurveNames.Length; c++)
            {
                var curve = overlay.Curves[c]; Assert.Equal(curve.Present, native.TryGetProperty(profile.CurveNames[c],out var value));
                if (!curve.Present) continue;
                present++; var dc=Math.Abs(curve.Value-value.GetSingle()); maxC=Math.Max(maxC,dc);
                Assert.True(dc<=2e-6,$"{hz}Hz frame={frame} curve={profile.CurveNames[c]} difference={dc:R}");
            }
            Assert.Equal(present,native.EnumerateObject().Count());
            players.ValidateCommit(frame); overlay.ValidateCommit(frame); players.Commit(frame); overlay.Commit(frame++);
        }
        Assert.Equal(hz*4,frame); Assert.True(hidden>0); Assert.True(zeroTicks>0);
        output.WriteLine($"{hz}Hz frames={frame} hidden={hidden} zeroTicks={zeroTicks} maxW={maxW:R} maxTime={maxT:R} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxCurve={maxC:R}");
    }
    [Fact]
    public void HiddenBranchRequiresMatchingSourceFrameAndCancelledResetDoesNotLeak()
    {
        var catalog=Catalog(); var profile=AlsRefactoredBoxOverlayCompiler.Compile(catalog);
        var bank=new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),catalog);
        var players=new AlsRefactoredSourcePlayerRuntime(catalog,bank,new Dictionary<string,AlsRefactoredTriangulationProfile>(),[profile.PlayerDefinition(0,0)]);
        var overlay=profile.CreateRuntime(0);
        overlay.Prepare(0,AlsOverlayAction.GettingUp,0,1,0,.01f,true); Assert.Empty(overlay.SourceInputs.ToArray());
        players.Prepare(1,[],.01f);
        Assert.Throws<ArgumentException>(()=>overlay.Evaluate(0,players));
        Assert.Throws<ArgumentException>(()=>overlay.Commit(0));
        Assert.Equal(default,overlay.CommittedState); overlay.Cancel(); players.Cancel();
        overlay.Prepare(0,AlsOverlayAction.Default,0,1,0,.01f);
        Assert.False(overlay.SourceInputs[0].Reinitialize);
        players.Prepare(0,overlay.SourceInputs,.01f); players.ValidateCommit(0); overlay.ValidateCommit(0);
        players.Commit(0); overlay.Commit(0); // Update-only graph frame.
        overlay.Prepare(1,AlsOverlayAction.GettingUp,0,1,0,.01f,true);
        players.Prepare(1,overlay.SourceInputs,.01f); players.Commit(1); overlay.Commit(1);
        overlay.Prepare(2,AlsOverlayAction.Default,0,1,0,.01f);
        Assert.True(overlay.SourceInputs[0].Reinitialize);
        overlay.Cancel(); overlay.Prepare(2,AlsOverlayAction.Default,0,1,0,.01f);
        Assert.True(overlay.SourceInputs[0].Reinitialize); // Pending reset survives a discarded reactivation.
        players.Prepare(2,overlay.SourceInputs,.01f); overlay.Evaluate(2,players);
        Assert.Equal(.01f,players.Players[0].Time);
        players.Commit(2); overlay.Commit(2);
    }
}
