using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredCachedOverlayTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),
        p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    [Theory]
    [InlineData(AlsRefactoredCachedOverlayKind.HandsTied)]
    [InlineData(AlsRefactoredCachedOverlayKind.Injured)]
    [InlineData(AlsRefactoredCachedOverlayKind.Barrel)]
    public void ActualCachedGraphsCompileAndRejectCacheOrderSourceAndModifiedCurves(AlsRefactoredCachedOverlayKind kind)
    {
        var catalog=Catalog();var profile=AlsRefactoredCachedOverlayCompiler.Compile(catalog,kind);
        Assert.Equal(79,profile.BoneNames.Length);
        var payload=catalog.Read(AlsRefactoredCachedOverlayProfile.Blueprint(kind));
        void Reject(Action<JsonNode> change)
        {
            var root=JsonNode.Parse(payload.GetRawText())!;change(root);
            using var doc=JsonDocument.Parse(root.ToJsonString());
            Assert.Throws<ArgumentException>(()=>AlsRefactoredCachedOverlayCompiler.ValidateGraph(doc.RootElement,kind));
        }
        JsonNode Node(JsonNode root,string type)=>root["compiled"]!["nodes"]!.AsArray().First(n=>n!["class"]!.GetValue<string>()==type)!;
        Reject(r=>r["compiled"]!["orderedSavedPoseNodes"]!.AsArray().Single(n=>n!["root"]!.GetValue<string>()=="Overlay")!["compiledNodeIndices"]=new JsonArray());
        Reject(r=>Node(r,"AnimGraphNode_UseCachedPose")["runtime"]!["linkToCachingNode"]!["linkId"]=0);
        Reject(r=>Node(r,"AnimGraphNode_UseCachedPose")["runtime"]!["cachePoseName"]="Foreign");
        Reject(r=>Node(r,"AnimGraphNode_ApplyAdditive")["runtime"]!["alpha"]=.9f);
        Reject(r=>Node(r,"AnimGraphNode_ModifyCurve")["runtime"]!["curveValues"]![0]=1);
        Reject(r=>Node(r,"AlsAnimGraphNode_GameplayTagsBlend")["runtime"]!["blendTime"]![2]=.2f);
        Reject(r=>r["nativeText"]=r["nativeText"]!.GetValue<string>().Replace("SaveCachedPoseNode=\"","SaveCachedPoseNode=\"Foreign",StringComparison.Ordinal));
    }
    public static IEnumerable<object[]> Cases()
    { foreach(var kind in Enum.GetValues<AlsRefactoredCachedOverlayKind>())foreach(var hz in new[]{30,60,120})yield return [kind,hz]; }
    [Fact]
    public void CachedResultCannotCrossSourceOwnersAndFailedFrameCanBeRetried()
    {
        var catalog=Catalog();var profile=AlsRefactoredCachedOverlayCompiler.Compile(catalog,AlsRefactoredCachedOverlayKind.HandsTied);
        var bank=new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),catalog);
        AlsRefactoredSourcePlayerRuntime Player()=>new(catalog,bank,new Dictionary<string,AlsRefactoredTriangulationProfile>(),[profile.PlayerDefinition(0,0)]);
        var first=Player();var second=Player();var owner=profile.CreateRuntime(0);
        var input=new AlsRefactoredDefaultOverlayInput(.5f,.6f,.4f,1,.7f);
        owner.Prepare(0,AlsOverlayAction.GettingUp,input,.01f);first.Prepare(0,[owner.SourceInput],.01f);second.Prepare(0,[owner.SourceInput],.01f);
        owner.Evaluate(0,first);var pose=owner.Pose.ToArray();
        Assert.Throws<ArgumentException>(()=>owner.Evaluate(0,second));
        Assert.Throws<ArgumentException>(()=>owner.Commit(0));Assert.Throws<InvalidOperationException>(()=>owner.Pose.ToArray());
        Assert.Equal(default,owner.CommittedActions);Assert.Equal(default,owner.CommittedPrediction);
        owner.Cancel();first.Cancel();second.Cancel();
        Assert.Throws<ArgumentException>(()=>owner.Prepare(0,AlsOverlayAction.Rolling,input with{Walking=float.NaN},.01f));
        owner.Prepare(0,AlsOverlayAction.GettingUp,input,.01f);first.Prepare(0,[owner.SourceInput],.01f);owner.Evaluate(0,first);
        Assert.Equal(pose,owner.Pose.ToArray());first.ValidateCommit(0);owner.ValidateCommit(0);first.Commit(0);owner.Commit(0);
        Assert.Throws<InvalidOperationException>(()=>owner.Pose.ToArray());
        owner.Prepare(1,AlsOverlayAction.Default,input,.01f);first.Prepare(1,[owner.SourceInput],.01f);
        first.Commit(1);owner.Commit(1); // Full downstream coverage can skip Evaluate.
        Assert.True(first.CommittedTime(0)>.01f);
        var before=owner.CommittedPrediction;owner.Prepare(2,AlsOverlayAction.Rolling,input with{GroundPrediction=0},.01f,true);owner.Cancel();
        Assert.Equal(before,owner.CommittedPrediction);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ActualSharedCacheTicksOnceAtMaximumWeightWithoutMutatingOtherActionConsumers(AlsRefactoredCachedOverlayKind kind,int hz)
    {
        var catalog=Catalog();var profile=AlsRefactoredCachedOverlayCompiler.Compile(catalog,kind);var owner=profile.CreateRuntime(0);
        var bank=new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),catalog);
        var players=new AlsRefactoredSourcePlayerRuntime(catalog,bank,new Dictionary<string,AlsRefactoredTriangulationProfile>(),[profile.PlayerDefinition(0,0)]);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_cached_overlay_"+kind));var root=doc.RootElement;
        Assert.Equal(AlsRefactoredCachedOverlayProfile.Blueprint(kind),root.GetProperty("source").GetString());
        Assert.Equal(profile.BoneNames.ToArray(),root.GetProperty("names").EnumerateArray().Select(v=>v.GetString()!));
        foreach(var name in new[]{"refactored_animation_sources","refactored_sync_inputs"})
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))),root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==$"{hz}hz");
        var frame=0;var multi=0;var three=0;double maxP=0,maxQ=0,maxS=0,maxC=0,maxTime=0,maxWeight=0,maxPrediction=0;
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request=row.GetProperty("input");var state=request.GetProperty("poseState");float V(string n)=>state.GetProperty(n).GetSingle();
            var input=new AlsRefactoredDefaultOverlayInput(V("GaitWalkingAmount"),V("StandingAmount"),V("CrouchingAmount"),V("InAirAmount"),request.GetProperty("inAirState").GetProperty("GroundPredictionAmount").GetSingle());
            var action=AlsOverlayActionBlend.Resolve(request.GetProperty("action").GetString()!);var delta=request.GetProperty("delta").GetSingle();var reset=request.GetProperty("reset").GetBoolean();
            var before=owner.CommittedActions;var predictionBefore=owner.CommittedPrediction;
            owner.Prepare(frame,action,input,delta,reset);players.Prepare(frame,[owner.SourceInput],delta);owner.Evaluate(frame,players);
            var pose=owner.Pose.ToArray();var curves=owner.Curves.ToArray();var time=players.Players[0].Time;
            owner.Evaluate(frame,players);Assert.Equal(pose,owner.Pose.ToArray());Assert.Equal(curves,owner.Curves.ToArray());Assert.Equal(time,players.Players[0].Time);
            owner.Cancel();players.Cancel();Assert.Equal(before,owner.CommittedActions);Assert.Equal(predictionBefore,owner.CommittedPrediction);
            owner.Prepare(frame,action,input,delta,reset);players.Prepare(frame,[owner.SourceInput],delta);owner.Evaluate(frame,players);
            Assert.Equal(pose,owner.Pose.ToArray());Assert.Equal(curves,owner.Curves.ToArray());Assert.Single(players.Players.ToArray());
            var count=0;
            for(var i=0;i<4;i++)
            {
                var error=Math.Abs(owner.Weights[i]-row.GetProperty("actionWeights")[i].GetSingle());maxWeight=Math.Max(maxWeight,error);
                Assert.True(error<=2e-6,$"{kind}/{hz} frame={frame} weight={error:R}");if(owner.Weights[i]>AlsPoseBlender.WeightThreshold)count++;
            }
            if(count>1)multi++;if(count>2)three++;
            Assert.Equal(owner.CacheWeight,row.GetProperty("cacheWeight").GetSingle());
            Assert.Equal(owner.SourceInput.Weight,row.GetProperty("idleWeight").GetSingle());
            var dt=Math.Abs(players.Players[0].Time-row.GetProperty("idleTime").GetSingle());maxTime=Math.Max(maxTime,dt);Assert.True(dt<=2e-6);
            if(profile.UsesAir)
            {
                var dp=Math.Abs(owner.Prediction.Prediction-row.GetProperty("predictionAlpha").GetSingle());maxPrediction=Math.Max(maxPrediction,dp);Assert.True(dp<=1e-6);
            }
            for(var bone=0;bone<pose.Length;bone++)
            {
                var expected=row.GetProperty("pose")[bone];var actual=owner.Pose[bone];
                double[] Values(string n)=>expected.GetProperty(n).EnumerateArray().Select(v=>v.GetDouble()).ToArray();var p=Values("position");var q=Values("rotation");var s=Values("scale");
                var dp=Math.Sqrt(Math.Pow(actual.Position.X-p[0],2)+Math.Pow(actual.Position.Y-p[1],2)+Math.Pow(actual.Position.Z-p[2],2));
                var sign=actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3]<0?-1:1;
                var dq=new[]{Math.Abs(actual.Rotation.X-sign*q[0]),Math.Abs(actual.Rotation.Y-sign*q[1]),Math.Abs(actual.Rotation.Z-sign*q[2]),Math.Abs(actual.Rotation.W-sign*q[3])}.Max();
                var ds=new[]{Math.Abs(actual.Scale.X-s[0]),Math.Abs(actual.Scale.Y-s[1]),Math.Abs(actual.Scale.Z-s[2])}.Max();maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                Assert.True(dp<=1e-10&&dq<=1e-12&&ds<=1e-12,$"{kind}/{hz} frame={frame} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
            }
            var native=row.GetProperty("curves");var present=0;
            for(var c=0;c<profile.CurveNames.Length;c++)
            {
                var value=owner.Curves[c];Assert.Equal(value.Present,native.TryGetProperty(profile.CurveNames[c],out var expected));if(!value.Present)continue;
                present++;var dc=Math.Abs(value.Value-expected.GetSingle());maxC=Math.Max(maxC,dc);Assert.True(dc<=2e-6,$"{kind}/{hz} frame={frame} curve={profile.CurveNames[c]} diff={dc:R}");
            }
            Assert.Equal(present,native.EnumerateObject().Count());players.ValidateCommit(frame);owner.ValidateCommit(frame);players.Commit(frame);owner.Commit(frame++);
        }
        Assert.Equal(hz*4,frame);Assert.True(multi>0&&three>0);
        output.WriteLine($"{kind}/{hz} frames={frame} multi={multi} three={three} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxCurve={maxC:R} maxTime={maxTime:R} maxWeight={maxWeight:R} maxPrediction={maxPrediction:R}");
    }
}
