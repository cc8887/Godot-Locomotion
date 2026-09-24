using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPropOverlayPoseTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    [Theory]
    [InlineData(AlsRefactoredPropOverlayKind.Binoculars)]
    [InlineData(AlsRefactoredPropOverlayKind.Torch)]
    public void RealPoseGraphRejectsChangedAimSpaceFrameAndPitchBinding(AlsRefactoredPropOverlayKind kind)
    {
        var catalog=Catalog();var profile=AlsRefactoredPropOverlayCompiler.Compile(catalog,kind);Assert.Equal(79,profile.BoneNames.Length);
        var payload=catalog.Read(AlsRefactoredPropOverlayUpdateProfile.Blueprint(kind));
        foreach(var change in new[]{"space","frame","binding","link","curve"})
        {
            var root=JsonNode.Parse(payload.GetRawText())!;var nodes=root["compiled"]!["nodes"]!.AsArray();
            JsonNode Node(string type)=>nodes.First(n=>n!["class"]!.GetValue<string>()==type)!;
            if(change=="space")Node("AnimGraphNode_ApplyMeshSpaceAdditive")["runtime"]!["bRootSpaceAdditive"]=true;
            if(change=="frame")nodes.Single(n=>(int)n!["propertyIndex"]! == 2)!["runtime"]!["explicitFrame"]=1;
            if(change=="binding")root["nativeText"]=root["nativeText"]!.GetValue<string>().Replace("PitchAmount","YawAmount",StringComparison.Ordinal);
            if(change=="link")Node("AnimGraphNode_ApplyMeshSpaceAdditive")["runtime"]!["base"]!["linkId"]=0;
            if(change=="curve")Node("AnimGraphNode_ModifyCurve")["runtime"]!["curveValues"]![0]=2;
            using var altered=JsonDocument.Parse(root.ToJsonString());Assert.Throws<ArgumentException>(()=>AlsRefactoredPropOverlayCompiler.ValidateGraph(altered.RootElement,kind));
        }
    }
    public static IEnumerable<object[]> Cases()=>AlsRefactoredPropOverlayUpdateTests.Cases();
    [Fact]
    public void FailedPoseFrameCannotPublishAndRetryRestoresIndependentCandidate()
    {
        var catalog=Catalog();var profile=AlsRefactoredPropOverlayCompiler.Compile(catalog,AlsRefactoredPropOverlayKind.Torch);var runtime=profile.CreateRuntime(0);
        AlsRefactoredSourcePlayerRuntime Player()=>new(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),new Dictionary<string,AlsRefactoredTriangulationProfile>(),[profile.Update.PlayerDefinition(0,0)]);
        var first=Player();var second=Player();var input=new AlsRefactoredPropOverlayInput(.4f,.8f,.6f,.4f,.2f);
        runtime.Prepare(0,AlsOverlayAction.Default,true,input,.01f);first.Prepare(0,runtime.SourceInputs,.01f);second.Prepare(0,runtime.SourceInputs,.01f);runtime.Evaluate(0,first);
        var pose=runtime.Pose.ToArray();var curves=runtime.Curves.ToArray();
        Assert.Throws<ArgumentException>(()=>runtime.Evaluate(0,second));Assert.Throws<ArgumentException>(()=>runtime.Commit(0));Assert.Throws<InvalidOperationException>(()=>runtime.Pose.ToArray());
        Assert.Equal(default,runtime.CommittedState);runtime.Cancel();first.Cancel();second.Cancel();
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(0,AlsOverlayAction.Default,true,input with{Pitch=float.NaN},.01f));
        runtime.Prepare(0,AlsOverlayAction.Default,true,input,.01f);first.Prepare(0,runtime.SourceInputs,.01f);runtime.Evaluate(0,first);
        Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());runtime.ValidateCommit(0);first.ValidateCommit(0);first.Commit(0);runtime.Commit(0);
        runtime.Prepare(1,AlsOverlayAction.Default,true,input,.01f);first.Prepare(1,runtime.SourceInputs,.01f);first.Commit(1);runtime.Commit(1);
        Assert.Throws<InvalidOperationException>(()=>runtime.Pose.ToArray());Assert.Throws<ArgumentException>(()=>runtime.Prepare(1,AlsOverlayAction.Default,true,input,.01f));
        Assert.True(first.CommittedTime(0)>.01f);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalWholePropPoseAndCurvesMatchThroughActionAndAimTransitions(AlsRefactoredPropOverlayKind kind,int hz)
    {
        var catalog=Catalog();var profile=AlsRefactoredPropOverlayCompiler.Compile(catalog,kind);var runtime=profile.CreateRuntime(0);
        var players=new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),new Dictionary<string,AlsRefactoredTriangulationProfile>(),[profile.Update.PlayerDefinition(0,0)]);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_prop_overlay_"+kind));var root=doc.RootElement;
        Assert.Equal(profile.BoneNames.ToArray(),root.GetProperty("names").EnumerateArray().Select(n=>n.GetString()!));
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==$"{hz}hz");
        int frame=0;double maxP=0,maxQ=0,maxS=0,maxC=0;
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request=row.GetProperty("input");var state=request.GetProperty("poseState");float V(string n)=>state.GetProperty(n).GetSingle();
            var input=new AlsRefactoredPropOverlayInput(V("GaitWalkingAmount"),V("GaitSprintingAmount"),V("StandingAmount"),V("CrouchingAmount"),request.GetProperty("viewState").GetProperty("PitchAmount").GetSingle());
            var action=AlsOverlayActionBlend.Resolve(request.GetProperty("action").GetString()!);var aiming=request.GetProperty("rotationMode").GetString()=="Als.RotationMode.Aiming";
            var delta=request.GetProperty("delta").GetSingle();var reset=request.GetProperty("reset").GetBoolean();
            var before=runtime.CommittedState;
            runtime.Prepare(frame,action,aiming,input,delta,reset);players.Prepare(frame,runtime.SourceInputs,delta);runtime.Evaluate(frame,players);
            var pose=runtime.Pose.ToArray();var curves=runtime.Curves.ToArray();runtime.Evaluate(frame,players);Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());
            runtime.Cancel();players.Cancel();Assert.Equal(before,runtime.CommittedState);
            runtime.Prepare(frame,action,aiming,input,delta,reset);players.Prepare(frame,runtime.SourceInputs,delta);runtime.Evaluate(frame,players);
            Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());
            for(var bone=0;bone<pose.Length;bone++)
            {
                var native=row.GetProperty("pose")[bone];var actual=runtime.Pose[bone];double[] Values(string n)=>native.GetProperty(n).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var p=Values("position");var q=Values("rotation");var s=Values("scale");
                var dp=Math.Sqrt(Math.Pow(actual.Position.X-p[0],2)+Math.Pow(actual.Position.Y-p[1],2)+Math.Pow(actual.Position.Z-p[2],2));
                var sign=actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3]<0?-1:1;
                var dq=new[]{Math.Abs(actual.Rotation.X-sign*q[0]),Math.Abs(actual.Rotation.Y-sign*q[1]),Math.Abs(actual.Rotation.Z-sign*q[2]),Math.Abs(actual.Rotation.W-sign*q[3])}.Max();
                var ds=new[]{Math.Abs(actual.Scale.X-s[0]),Math.Abs(actual.Scale.Y-s[1]),Math.Abs(actual.Scale.Z-s[2])}.Max();
                maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                Assert.True(dp<=1e-10&&dq<=1e-12&&ds<=1e-12,$"{kind}/{hz} frame={frame} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
            }
            var nativeCurves=row.GetProperty("curves");var present=0;
            for(var c=0;c<curves.Length;c++)
            {
                Assert.Equal(curves[c].Present,nativeCurves.TryGetProperty(profile.CurveNames[c],out var expected));if(!curves[c].Present)continue;present++;
                var dc=Math.Abs(curves[c].Value-expected.GetSingle());maxC=Math.Max(maxC,dc);Assert.True(dc<=2e-6,$"{kind}/{hz}/{frame} curve={profile.CurveNames[c]} diff={dc:R}");
            }
            Assert.Equal(present,nativeCurves.EnumerateObject().Count());players.ValidateCommit(frame);runtime.ValidateCommit(frame);players.Commit(frame);runtime.Commit(frame++);
        }
        Assert.Equal(hz*4,frame);output.WriteLine($"{kind}/{hz} frames={frame} maxP_cm={maxP:R} maxQ={maxQ:R} maxScale={maxS:R} maxCurve={maxC:R}");
    }
}
