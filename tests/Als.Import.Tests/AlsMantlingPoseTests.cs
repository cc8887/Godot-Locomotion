using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingPoseTests(ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+name+".json"));
    private static IReadOnlyDictionary<string,AlsMantlingPoseSource> Compile(string? json=null)=>
        AlsMantlingPoseCompiler.Compile(json??Read("refactored_mantle_animation_inputs"),Read("refactored_mantle_root_tracks"));

    [Fact]
    public void OriginalKeysProduceAllNativeMantlePoseContexts()
    {
        var sources=Compile();var oracle=JsonNode.Parse(Read("refactored_mantle_pose_reference"))!;
        Assert.Equal(oracle["sourceSha256"]!.GetValue<string>().ToUpperInvariant(),Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config/refactored_mantle_animation_inputs.json")))));
        var count=0;var virtualCount=0;double maxP=0,maxQ=0,maxS=0;
        foreach(var asset in oracle["sequences"]!.AsArray())
        {
            var path=asset!["source"]!.GetValue<string>();var source=sources[path];var sampler=source.CreateSampler();
            Assert.Equal("/ALS/ALS/Character/SK_Als.SK_Als",source.SkeletonPath);
            Assert.Equal(79,source.Data.LogicalBoneCount);Assert.Equal(68,source.Data.PhysicalBoneCount);
            var pose=new AlsPrecisePose[79];var retry=new AlsPrecisePose[79];
            foreach(var sample in asset["samples"]!.AsArray())
            {
                Assert.Equal(source.BoneNames.ToArray(),sample!["names"]!.AsArray().Select(n=>n!.GetValue<string>()));
                void Sample(AlsPrecisePose[] destination)=>sampler.Sample(sample["requestedTime"]!.GetValue<double>(),
                    sample["shouldRetarget"]!.GetValue<bool>(),sample["extractRootMotion"]!.GetValue<bool>(),
                    sample["ignoreRootLock"]!.GetValue<bool>(),destination);
                Sample(pose);Sample(retry);Assert.Equal(pose,retry);
                for(var bone=0;bone<pose.Length;bone++)
                {
                    double[] V(string key)=>sample["pose"]![bone]![key]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
                    var p=V("position");var q=V("rotation");var s=V("scale");var actual=pose[bone];
                    var position=Math.Sqrt((actual.Position-new AlsDoubleVector(p[0],p[1],p[2])).LengthSquared);
                    var rotation=new AlsQuaternion(q[0],q[1],q[2],q[3]);var sign=AlsQuaternion.Dot(actual.Rotation,rotation)<0?-1:1;
                    var a=actual.Rotation;var b=rotation*sign;
                    var quaternion=new[]{Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y),Math.Abs(a.Z-b.Z),Math.Abs(a.W-b.W)}.Max();
                    var scale=Math.Sqrt((actual.Scale-new AlsDoubleVector(s[0],s[1],s[2])).LengthSquared);
                    maxP=Math.Max(maxP,position);maxQ=Math.Max(maxQ,quaternion);maxS=Math.Max(maxS,scale);
                    Assert.True(position<=1e-4&&quaternion<=1e-6&&scale<=1e-6,
                        $"{path} {sample["context"]} time={sample["requestedTime"]} bone={source.BoneNames[bone]} p={position:R} q={quaternion:R} s={scale:R}");
                    count++;if(source.Data.LogicalToPhysical[bone]<0)virtualCount++;
                }
            }
        }
        Assert.Equal(4740,count);Assert.Equal(660,virtualCount);
        output.WriteLine($"bones={count} virtuals={virtualCount} position_cm={maxP:G17} quaternion_component={maxQ:G17} scale={maxS:G17}");
    }

    [Theory]
    [InlineData("root-hash")][InlineData("missing-source")][InlineData("duplicate-source")]
    [InlineData("foreign-skeleton")][InlineData("parent")][InlineData("track")]
    [InlineData("root-channel")][InlineData("partial-channel")][InlineData("additive")]
    [InlineData("retarget-reference")][InlineData("missing-montage")][InlineData("montage-segment")]
    public void RejectsBrokenPoseMotionClosure(string mutation)
    {
        var root=JsonNode.Parse(Read("refactored_mantle_animation_inputs"))!;
        var sequence=root["sequences"]![0]!;var raw=sequence["raw"]!;var policy=sequence["evaluation"]!;
        var skeleton=root["skeletons"]!.AsObject().First().Value!["metadata"]!;
        switch(mutation)
        {
            case "root-hash":root["rootBindingsSha256"]=new string('0',64);break;
            case "missing-source":root["sequences"]!.AsArray().RemoveAt(0);break;
            case "duplicate-source":root["sequences"]!.AsArray().Add(sequence.DeepClone());break;
            case "foreign-skeleton":raw["skeletonSource"]="/Game/V4.V4";break;
            case "parent":skeleton["rawParents"]![1]=2;break;
            case "track":raw["tracks"]![1]!["bone"]="root";break;
            case "root-channel":raw["tracks"]![0]!["positions"]![0]![0]=999;break;
            case "partial-channel":raw["tracks"]![1]!["positions"]!.AsArray().RemoveAt(0);break;
            case "additive":policy["additiveType"]="AAT_LocalSpaceBase";break;
            case "retarget-reference":policy["retargetTransforms"]![1]!["position"]![0]=999;break;
            case "missing-montage":root["montages"]!.AsArray().RemoveAt(0);break;
            case "montage-segment":root["montages"]![0]!["segments"]![0]!["animationStart"]=.4;break;
        }
        Assert.ThrowsAny<ArgumentException>(()=>Compile(root.ToJsonString()));
    }

    [Fact]
    public void SeparateOwnersReplaySharedKeysWithoutDependingOnAccessOrder()
    {
        var sources=Compile().Values.ToArray();var results=new AlsPrecisePose[sources.Length*4][][];
        Parallel.For(0,results.Length,i=>
        {
            var source=sources[i/4];var sampler=source.CreateSampler();results[i]=new AlsPrecisePose[31][];
            for(var step=30;step>=0;step--)
            {
                var pose=new AlsPrecisePose[79];sampler.Sample(source.Data.PlayLength*step/30,true,false,true,pose);
                results[i][step]=pose;
            }
        });
        for(var i=0;i<sources.Length;i++)
        {
            var sampler=sources[i].CreateSampler();var pose=new AlsPrecisePose[79];
            for(var step=0;step<=30;step++)
            {
                sampler.Sample(sources[i].Data.PlayLength*step/30,true,false,true,pose);
                for(var owner=0;owner<4;owner++)Assert.Equal(results[i*4+owner][step],pose);
            }
        }
    }

    [Fact]
    public void AuthoredNonRootKeyChangesOutputWithoutAnySampledReferenceInput()
    {
        var root=JsonNode.Parse(Read("refactored_mantle_animation_inputs"))!;
        var original=Compile();var sequence=root["sequences"]![0]!;var path=sequence["raw"]!["source"]!.GetValue<string>();
        var track=sequence["raw"]!["tracks"]!.AsArray().First(t=>t!["bone"]!.GetValue<string>()=="pelvis")!;
        track["positions"]![0]![0]=track["positions"]![0]![0]!.GetValue<double>()+10;
        var changed=Compile(root.ToJsonString());var before=new AlsPrecisePose[79];var after=new AlsPrecisePose[79];
        original[path].CreateSampler().Sample(0,false,false,true,before);
        changed[path].CreateSampler().Sample(0,false,false,true,after);
        var pelvis=Array.IndexOf(original[path].BoneNames.ToArray(),"pelvis");
        Assert.InRange(after[pelvis].Position.X-before[pelvis].Position.X,9.99999,10.00001);
    }
}
