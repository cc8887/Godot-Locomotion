using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStandingRestTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog = new(MantlingHostFixture.Read("refactored_animation_sources"),p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        public readonly AlsRefactoredStandingRestGraph Graph;
        public readonly AlsRefactoredStandingRestPose Pose;
        public Fixture()
        {
            Graph = new(Catalog);
            Pose = new(Catalog,Graph,new(MantlingHostFixture.Read("refactored_skeleton_curves"),Catalog),["SlotOnly"]);
        }
    }
    private static readonly Lazy<Fixture> Data = new(() => new());

    [Theory]
    [InlineData("link")] [InlineData("slot")] [InlineData("alwaysUpdate")] [InlineData("scale")]
    [InlineData("binding")] [InlineData("frame")] [InlineData("lock")] [InlineData("callback")]
    public void RestGraphRejectsChangedNativePolicies(string change)
    {
        var f = Data.Value;
        Assert.Equal(new[] {57,59,58},f.Graph.IdleCallbacks.ToArray().Select(c => c.PropertyIndex));
        Assert.Equal(new[] {false,true,false},f.Graph.IdleCallbacks.ToArray().Select(c => c.OnBecomeRelevant));
        Assert.False(f.Graph.AlwaysUpdateSlotSource);
        var json = JsonNode.Parse(f.Catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)).GetRawText())!;
        JsonNode Node(int id) => json["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == id)!;
        switch (change)
        {
            case "link": Node(62)["runtime"]!["sourcePose"]!["linkId"] = 63; break;
            case "slot": Node(60)["runtime"]!["slotName"] = "TurnInPlaceCrouching"; break;
            case "alwaysUpdate": Node(60)["runtime"]!["bAlwaysUpdateSourcePose"] = true; break;
            case "scale": Node(13)["runtime"]!["applyMode"] = "Blend"; break;
            case "binding": json["nativeText"] = json["nativeText"]!.GetValue<string>().Replace("\"TurnInPlaceState\",\"PlayRate\"","\"RotateInPlaceState\",\"PlayRate\"",StringComparison.Ordinal); break;
            case "frame": Node(61)["runtime"]!["explicitFrame"] = 1; break;
            case "lock": Node(63)["runtime"]!["curveValues"]![0] = 0; break;
            case "callback": Node(59)["runtime"]!["callSite"] = "OnUpdate"; break;
        }
        using var document = JsonDocument.Parse(json.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredStandingRestGraph.Compile(document.RootElement));
    }

    [Fact]
    public void IdleHasFixedOriginalPoseAndPreservesSlotChangesBeforeYawScaling()
    {
        var f = Data.Value; var profile = f.Pose;
        var pose = new AlsPrecisePose[profile.BoneNames.Length]; var curves = new AlsInertialCurve[profile.CurveNames.Length];
        profile.SampleIdleSource(pose,curves);
        var expected = new AlsPrecisePose[pose.Length];
        var asset = f.Catalog.CompileAbsolutePoseWithCurves(AlsRefactoredStandingRestGraph.IdleSequence);
        asset.Pose.CreateSampler(asset.Curves).Sample(0,true,false,false,expected,new AlsInertialCurve[asset.Curves.Names.Length]);
        Assert.Equal(79,pose.Length); Assert.Equal(expected,pose);
        foreach (var n in new[] {"FootLeftLock","FootRightLock","AllowTransitions"}) Assert.Equal(new AlsInertialCurve(1),curves[Index(n)]);
        // Represents an evaluated Slot, not a simulated montage acceptance test.
        curves[Index("FootLeftLock")] = new(.125f); curves[Index("FootRightLock")] = default;
        curves[Index("AllowTransitions")] = new(.25f); curves[Index("SlotOnly")] = new(.7f);
        var yaw = Index("RotationYawSpeed"); curves[yaw] = new(-73.125f);
        profile.FinishIdleSlot(pose,curves,1.35f,pose,curves);
        Assert.Equal(expected,pose);
        Assert.Equal(new AlsInertialCurve(.125f),curves[Index("FootLeftLock")]); Assert.False(curves[Index("FootRightLock")].Present);
        Assert.Equal(new AlsInertialCurve(.25f),curves[Index("AllowTransitions")]); Assert.Equal(new AlsInertialCurve(.7f),curves[Index("SlotOnly")]);
        Assert.Equal(new AlsInertialCurve(-73.125f+(-73.125f*1.35f-(-73.125f))),curves[yaw]);
        curves[yaw] = default; profile.FinishIdleSlot(pose,curves,2,pose,curves); Assert.Equal(new AlsInertialCurve(0),curves[yaw]);
        var before = curves.ToArray();
        Assert.Throws<ArgumentException>(() => profile.FinishIdleSlot(pose,curves,float.NaN,pose,curves)); Assert.Equal(before,curves);
        pose[0] = default; var output = expected.ToArray();
        Assert.Throws<ArgumentException>(() => profile.FinishIdleSlot(pose,curves,1,output,curves)); Assert.Equal(expected,output);
        profile.SampleIdleSource(pose,curves); Assert.Equal(expected,pose); Assert.Equal(new AlsInertialCurve(1),curves[Index("FootLeftLock")]);
        int Index(string name) => Array.IndexOf(profile.CurveNames.ToArray(),name);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RotationUsesActualIndependentClocksAndRetryPreservesPose(int hz)
    {
        var f = Data.Value; var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),f.Catalog);
        // Prefix identity ensures neither side assumes it owns player zero.
        var definitions = new[] {new AlsRefactoredSourcePlayerDefinition(0,AlsRefactoredStandingRestGraph.IdleSequence,-1)}.Concat(f.Graph.RotatePlayers.Bind(1));
        var players = new AlsRefactoredSourcePlayerRuntime(f.Catalog,bank,new Dictionary<string,AlsRefactoredTriangulationProfile>(),definitions);
        var sampler = f.Pose.BindRotate(players,1); var bones = f.Pose.BoneNames.Length;
        var pose = new AlsPrecisePose[bones]; var curves = new AlsInertialCurve[f.Pose.CurveNames.Length];
        var expected = new AlsPrecisePose[bones];
        var sources = f.Graph.RotatePlayers.Players.ToArray().Select(p => f.Catalog.CompileAbsolutePoseWithCurves(p.Source)).ToArray();
        var sourceSamplers = sources.Select(s => s.Pose.CreateSampler(s.Curves)).ToArray();
        var raw = sources.Select(s => new AlsInertialCurve[s.Curves.Names.Length]).ToArray();
        for (var frame = 0; frame < hz*4; frame++)
        {
            var phase = frame/hz; var rate = phase == 3 ? -.75f : 1.35f;
            var left = phase < 2; var right = phase >= 1 && phase != 3;
            AlsRefactoredSourcePlayerInput[] ticks = [f.Graph.RotatePlayers.Input(1,0,rate,left,right,.4f,frame == hz*3),f.Graph.RotatePlayers.Input(1,1,rate,left,right,.6f,frame == hz*3)];
            players.Prepare(frame,ticks,1f/hz);
            Assert.Throws<InvalidOperationException>(() => sampler.Sample(frame,true,rate,pose,curves));
            var saved = new AlsPrecisePose[2][]; var savedCurves = new AlsInertialCurve[2][];
            for (var side = 0; side < 2; side++)
            {
                players.Evaluate(frame,1+side); sampler.Sample(frame,side == 0,rate,pose,curves);
                var history = players.Players.ToArray().Single(p => p.PlayerId == 1+side);
                sourceSamplers[side].Sample(history.Time,true,false,false,expected,raw[side]); Assert.Equal(expected,pose);
                for (var c = 0; c < curves.Length; c++)
                {
                    var name = f.Pose.CurveNames[c]; var index = Array.IndexOf(sources[side].Curves.Names.ToArray(),name);
                    var value = index < 0 ? default : raw[side][index];
                    if (name == "RotationYawSpeed") { var v = value.Present ? value.Value : 0; value = new(v+(v*rate-v)); }
                    Assert.Equal(value,curves[c]);
                }
                saved[side] = pose.ToArray(); savedCurves[side] = curves.ToArray();
            }
            players.Cancel(); players.Prepare(frame,ticks,1f/hz);
            for (var side = 0; side < 2; side++)
            {
                players.Evaluate(frame,1+side); sampler.Sample(frame,side == 0,rate,pose,curves);
                Assert.Equal(saved[side],pose); Assert.Equal(savedCurves[side],curves);
            }
            Assert.Throws<ArgumentException>(() => sampler.Sample(frame+1,true,rate,pose,curves));
            players.Commit(frame);
        }
    }
}
