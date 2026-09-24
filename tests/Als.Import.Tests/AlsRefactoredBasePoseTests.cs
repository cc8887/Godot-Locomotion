using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredBasePoseTests(ITestOutputHelper output)
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    private static IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> Compile(string? input=null,string? inventory=null)=>
        AlsRefactoredBasePoseCompiler.Compile(input??Read("base_pose_inputs"),inventory??Read("layering_inventory"),Read("layering_graphs"));
    [Fact]
    public void RawBaseKeysMatchNativePhysicalAndVirtualPosesAcrossAllContexts()
    {
        var resources=Compile();var oracle=JsonNode.Parse(Read("base_pose_reference"))!;
        Assert.Equal(oracle["sourceSha256"]!.GetValue<string>().ToUpperInvariant(),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Read("base_pose_inputs")))));
        Assert.Equal(new[]{37,38},resources.Keys.Order());
        var count=0;var virtuals=0;double maxP=0,maxQ=0,maxS=0;
        foreach(var resource in resources.Values)
        {
            var source=resource.Pose;var sampler=source.CreateSampler(resource.Curves);
            Assert.Equal(2,source.Data.SampledKeyCount);Assert.Equal(68,source.Data.PhysicalBoneCount);Assert.Equal(79,source.Data.LogicalBoneCount);
            Assert.Empty(resource.Curves.Names.ToArray());
            var samples=oracle["sequences"]!.AsArray().Single(r=>r!["source"]!.GetValue<string>()==source.Data.Identity.AssetPath)!["samples"]!.AsArray();
            foreach(var sample in samples)
            {
                var pose=new AlsPrecisePose[79];sampler.Sample(sample!["requestedTime"]!.GetValue<double>(),sample["shouldRetarget"]!.GetValue<bool>(),
                    sample["extractRootMotion"]!.GetValue<bool>(),sample["ignoreRootLock"]!.GetValue<bool>(),pose,[]);
                Assert.Equal(source.BoneNames.ToArray(),sample["names"]!.AsArray().Select(n=>n!.GetValue<string>()));
                Assert.Empty(sample["curves"]!.AsObject());
                for(var bone=0;bone<pose.Length;bone++)
                {
                    double[] V(string key)=>sample["pose"]![bone]![key]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
                    var p=V("position");var q=V("rotation");var s=V("scale");var actual=pose[bone];
                    var dp=Math.Sqrt((actual.Position-new AlsDoubleVector(p[0],p[1],p[2])).LengthSquared);
                    var expectedQ=new AlsQuaternion(q[0],q[1],q[2],q[3]);if(AlsQuaternion.Dot(actual.Rotation,expectedQ)<0)expectedQ*= -1;
                    var dq=new[]{Math.Abs(actual.Rotation.X-expectedQ.X),Math.Abs(actual.Rotation.Y-expectedQ.Y),
                        Math.Abs(actual.Rotation.Z-expectedQ.Z),Math.Abs(actual.Rotation.W-expectedQ.W)}.Max();
                    var ds=Math.Sqrt((actual.Scale-new AlsDoubleVector(s[0],s[1],s[2])).LengthSquared);
                    maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                    Assert.True(dp<=1e-4&&dq<=1e-6&&ds<=1e-6,$"{source.Data.Identity.AssetPath} {source.BoneNames[bone]} p={dp:R} q={dq:R} s={ds:R}");
                    count++;if(source.Data.LogicalToPhysical[bone]<0)virtuals++;
                }
            }
            var expected=new AlsPrecisePose[79];sampler.Sample(0,true,false,false,expected,[]);
            Parallel.For(0,4,_=>
            {
                var owner=resource.CreateSampler();var actual=new AlsPrecisePose[79];
                for(var frame=0;frame<120;frame++){owner.Evaluate(actual,[]);Assert.Equal(expected,actual);}
            });
        }
        Assert.Equal(3160,count);Assert.Equal(440,virtuals);
        output.WriteLine($"bones={count} virtuals={virtuals} max_position_cm={maxP:G17} max_quaternion={maxQ:G17} max_scale={maxS:G17}");
    }
    [Theory]
    [InlineData("digest")][InlineData("missing")][InlineData("foreign")][InlineData("time")][InlineData("sync")][InlineData("curve")]
    public void RejectsSourcesOrEvaluatorPoliciesThatDoNotMatchTheOriginalGraph(string change)
    {
        var input=JsonNode.Parse(Read("base_pose_inputs"))!;var inventory=JsonNode.Parse(Read("layering_inventory"))!;
        var node=inventory["blueprints"]![2]!["nodes"]!.AsArray().First(n=>n!["class"]!.GetValue<string>()=="AnimGraphNode_SequenceEvaluator")!;
        switch(change)
        {
            case "digest": input["inventorySha256"]="wrong";break;
            case "missing": input["sequences"]!.AsArray().RemoveAt(0);break;
            case "foreign": input["sequences"]![0]!["raw"]!["source"]="/Game/Other.Other";break;
            case "time":node["runtime"]!["explicitFrame"]=1;break;
            case "sync":node["runtime"]!["method"]="SyncGroup";break;
            case "curve":input["sequences"]![0]!["curves"]!["source"]="/Game/Other.Other";break;
        }
        // Preserve the binding digest for intentional metadata policy mutations.
        var metadata=change is "time" or "sync"?inventory.ToJsonString():Read("layering_inventory");
        if(change is "time" or "sync")input["inventorySha256"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata)));
        Assert.Throws<ArgumentException>(()=>Compile(input.ToJsonString(),metadata));
    }
}
