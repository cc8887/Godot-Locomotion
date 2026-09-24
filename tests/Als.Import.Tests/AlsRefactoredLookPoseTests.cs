using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLookPoseTests(ITestOutputHelper log)
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    [Fact]
    public void OriginalLookEvaluatorIndependentlyMatchesNativeWeightsAndPoses()
    {
        var resource=AlsRefactoredLookPoseCompiler.Compile(Read("head_inputs"),Read("layering_graphs"));
        var sampler=resource.CreateSampler();var pose=new AlsPrecisePose[79];
        var rows=JsonNode.Parse(Read("head_pose_reference"))!["poses"]!.AsArray();
        double maxP=0,maxQ=0,maxS=0;var count=0;
        foreach(var row in rows)
        {
            var samples=row!["samples"]!.AsArray().Select(s=>new AlsAimGridVertex(s!["index"]!.GetValue<int>(),s["weight"]!.GetValue<float>())).ToArray();
            var weights=new AlsAimGridVertex[2];var pitch=row["pitch"]!.GetValue<float>();
            var countWeights=AlsRefactoredLookBlendSpace.Evaluate(pitch,weights);
            Assert.Equal(samples,weights.Take(countWeights).ToArray());
            sampler.Evaluate(pitch,row["normalizedTime"]!.GetValue<float>(),pose);
            Assert.Equal(resource.BoneNames.ToArray(),row["names"]!.AsArray().Select(n=>n!.GetValue<string>()));
            for(var bone=0;bone<79;bone++)
            {
                double[] V(string field)=>row["pose"]![bone]![field]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
                var p=V("position");var q=V("rotation");var s=V("scale");var actual=pose[bone];
                var dp=Math.Sqrt((actual.Position-new AlsDoubleVector(p[0],p[1],p[2])).LengthSquared);
                var rotation=new AlsQuaternion(q[0],q[1],q[2],q[3]);if(AlsQuaternion.Dot(actual.Rotation,rotation)<0)rotation*= -1;
                var dq=new[]{Math.Abs(actual.Rotation.X-rotation.X),Math.Abs(actual.Rotation.Y-rotation.Y),Math.Abs(actual.Rotation.Z-rotation.Z),Math.Abs(actual.Rotation.W-rotation.W)}.Max();
                var ds=Math.Sqrt((actual.Scale-new AlsDoubleVector(s[0],s[1],s[2])).LengthSquared);
                maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                Assert.True(dp<=1e-4&&dq<=1e-6&&ds<=1e-6,$"pitch={row["pitch"]} time={row["normalizedTime"]} bone={resource.BoneNames[bone]} p={dp:R} q={dq:R} s={ds:R}");
            }
            count++;
        }
        Assert.Equal(35,count);log.WriteLine($"poses={count} bones={count*79} maxPcm={maxP:R} maxQ={maxQ:R} maxS={maxS:R}");
    }
    [Fact]
    public void DisabledRootLockMetadataDoesNotChangeRawAdditivePoseAndOwnersAreIndependent()
    {
        var json=Read("head_inputs");var changed=JsonNode.Parse(json)!;
        foreach(var row in changed["sequences"]!.AsArray().Take(3))row!["evaluation"]!["rootLockFirstFrame"]!["scale"]=new JsonArray(0,0,0);
        var source=AlsRefactoredLookPoseCompiler.Compile(json,Read("layering_graphs"));
        var other=AlsRefactoredLookPoseCompiler.Compile(changed.ToJsonString(),Read("layering_graphs"));
        Parallel.For(0,4,_=>
        {
            var a=source.CreateSampler();var b=other.CreateSampler();var x=new AlsPrecisePose[79];var y=new AlsPrecisePose[79];
            for(var sample=0;sample<3;sample++)for(var key=0;key<=30;key++)
            {
                a.SampleSequence(sample,key/30f,x);b.SampleSequence(sample,key/30f,y);Assert.Equal(x,y);
            }
            Assert.Throws<ArgumentException>(()=>a.SampleBlend([new(0,.5f)],0,x));
            a.SampleBlend([new(0,.5f),new(1,.5f)],.25f,x);
            b.SampleBlend([new(0,.5f),new(1,.5f)],.25f,y);Assert.Equal(x,y);
        });
    }
    [Theory]
    [InlineData("base")][InlineData("root")][InlineData("duration")][InlineData("additive")][InlineData("curves")]
    [InlineData("grid")][InlineData("segments")][InlineData("axis")][InlineData("nativeSample")]
    public void UnsupportedLookPolicyIsRejected(string mutation)
    {
        var data=JsonNode.Parse(Read("head_inputs"))!;var policy=data["sequences"]![0]!["evaluation"]!;
        if(mutation=="base")policy["baseFrame"]=1;
        if(mutation=="root")policy["forceRootLock"]=true;
        if(mutation=="duration")policy["sequencePlayLength"]=2;
        if(mutation=="additive")policy["additiveType"]="AAT_LocalSpaceBase";
        if(mutation=="curves")policy["floatCurveCount"]=1;
        var native=data["blendSpace"]!["nativeText"]!.GetValue<string>();
        if(mutation=="grid")data["blendSpace"]!["nativeText"]=native+"\n   bInterpolateUsingGrid=True\n";
        if(mutation=="segments")data["blendSpace"]!["nativeText"]=native.Replace("Vertices[1]=0.500000","Vertices[1]=0.600000",StringComparison.Ordinal);
        if(mutation=="axis")data["blendSpace"]!["nativeText"]=native.Replace("Min=-90.000000","Min=-100.000000",StringComparison.Ordinal);
        if(mutation=="nativeSample")data["blendSpace"]!["nativeText"]=native.Replace("A_Als_Look_Up","A_Als_Look_Down",StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(()=>AlsRefactoredLookPoseCompiler.Compile(data.ToJsonString(),Read("layering_graphs")));
    }
}
