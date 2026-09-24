using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredHeadNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void OriginalHeadGraphDrivesCallbacksAndFinalMeshAdditivePose(int hz)
    {
        string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
        var inputs=Read("head_inputs");var graphs=Read("layering_graphs");var inventory=Read("layering_inventory");
        var profile=AlsRefactoredHeadGraphCompiler.Compile(graphs,inventory,inputs);
        var owner=profile.CreateRuntime(1,1);
        var bases=AlsRefactoredBasePoseCompiler.Compile(Read("base_pose_inputs"),inventory,graphs);
        var basis=new AlsPrecisePose[79];bases[37].CreateSampler().Evaluate(basis,[]);
        var pose=new AlsPrecisePose[79];AlsInertialCurve[] curves=new AlsInertialCurve[1];
        AlsInertialCurve[] sourceCurves=[new(.37f)];
        using var document=JsonDocument.Parse(Read("head_graph_trace"));var root=document.RootElement;
        Assert.True(root.GetProperty("headGraph").GetBoolean());
        Assert.Equal("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C",root.GetProperty("graphClass").GetString());
        Assert.Equal("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose",root.GetProperty("baseSequence").GetString());
        foreach(var name in new[]{"layering_graphs","layering_inventory","base_pose_inputs"})
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Read(name)))),
                root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs))),root.GetProperty("inputsSha256").GetString()!.ToUpperInvariant());
        Assert.Equal(bases[37].Pose.BoneNames.ToArray(),root.GetProperty("names").EnumerateArray().Select(n=>n.GetString()!));
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==$"{hz}hz");
        var view=AlsRefactoredViewState.Initial;var spine=AlsRefactoredSpineState.Initial;
        var graph=default(AlsAnimationGraphFrame);var frame=0;var initials=0;var hidden=0;
        double maxP=0,maxQ=0,maxS=0,maxHead=0;
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input=row.GetProperty("request").GetProperty("input").Deserialize<AlsRefactoredViewInput>();
            var identity=new AlsFrameIdentity(++frame,1,1);graph=graph.Next(identity,(ulong)frame);
            (view,spine)=AlsRefactoredViewModel.RefreshView(input,view,spine);
            owner.Prepare(graph,input,view);var candidate=owner.Candidate;
            if(candidate.Initialized)initials++;if(!candidate.HeadUpdated)hidden++;
            // Callback flags in the request are deliberately not consumed: native
            // graph relevance and C# traversal independently drive the callbacks.
            var head=candidate.Head;var expectedHead=row.GetProperty("head").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            Assert.Equal(expectedHead[0]!=0,head.InitializationRequired);Assert.Equal(expectedHead[1]!=0,head.SwitchingSides);
            double[] actualHead=[head.Pitch,head.Yaw,head.YawVelocity,head.YawAmount];
            for(var i=0;i<4;i++)
            {
                var error=Math.Abs(actualHead[i]-expectedHead[i+2]);maxHead=Math.Max(maxHead,error);
                Assert.True(error<=(i==3?2e-6:1e-3),$"{hz}Hz frame={frame} Head[{i}] error={error:R}");
            }
            owner.Evaluate(basis,sourceCurves,pose,curves);
            for(var bone=0;bone<pose.Length;bone++)
            {
                var expected=row.GetProperty("pose")[bone];
                double[] V(string name)=>expected.GetProperty(name).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var p=V("position");var q=V("rotation");var s=V("scale");var a=pose[bone];
                var dp=Math.Sqrt(Math.Pow(a.Position.X-p[0],2)+Math.Pow(a.Position.Y-p[1],2)+Math.Pow(a.Position.Z-p[2],2));
                var sign=a.Rotation.X*q[0]+a.Rotation.Y*q[1]+a.Rotation.Z*q[2]+a.Rotation.W*q[3]<0?-1:1;
                var dq=new[]{Math.Abs(a.Rotation.X-sign*q[0]),Math.Abs(a.Rotation.Y-sign*q[1]),Math.Abs(a.Rotation.Z-sign*q[2]),Math.Abs(a.Rotation.W-sign*q[3])}.Max();
                var ds=new[]{Math.Abs(a.Scale.X-s[0]),Math.Abs(a.Scale.Y-s[1]),Math.Abs(a.Scale.Z-s[2])}.Max();
                maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                Assert.True(dp<=1e-3&&dq<=1e-5&&ds<=1e-5,$"{hz}Hz frame={frame} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
            }
            var nativeCurves=row.GetProperty("curves");Assert.Single(nativeCurves.EnumerateObject());
            Assert.Equal(nativeCurves.GetProperty("HeadProbe").GetSingle(),curves[0].Value);Assert.True(curves[0].Present);
            owner.Commit(identity);
        }
        Assert.Equal(hz*5,frame);Assert.Equal(2,initials);Assert.Equal(hz/2,hidden);
        output.WriteLine($"{hz}Hz frames={frame} bones={frame*79} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxHead={maxHead:R}");
    }
}
