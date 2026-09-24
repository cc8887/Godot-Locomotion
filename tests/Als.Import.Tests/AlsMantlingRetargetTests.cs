using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingRetargetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ActualMantleRetargetPassMatchesNativePoseContexts(bool fbxMeters)
    {
        JsonNode Read(string file)=>JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+file+".json")))!;
        var source=Read("refactored_mantle_animation_inputs");var oracle=Read("refactored_mantle_pose_reference");
        Assert.Equal(oracle["sourceSha256"]!.GetValue<string>().ToUpperInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(
                Path.Combine(RepositoryRoot.Find(),"assets/config/refactored_mantle_animation_inputs.json")))));
        AlsPrecisePose Pose(JsonNode node)
        {
            double[] V(string key)=>node[key]!.AsArray().Select(x=>x!.GetValue<double>()).ToArray();
            var p=V("position");var q=V("rotation");var s=V("scale");
            return fbxMeters ? new(new(p[0]*.01,-p[1]*.01,p[2]*.01),new(-q[0],q[1],-q[2],q[3]),new(s[0],s[1],s[2]))
                : new(new(p[0],p[1],p[2]),new(q[0],q[1],q[2],q[3]),new(s[0],s[1],s[2]));
        }
        var skeleton=source["skeletons"]!.AsObject().First().Value!["metadata"]!;
        var names=skeleton["logicalBoneNames"]!.AsArray().Select(v=>v!.GetValue<string>()).ToArray();
        var mapping=skeleton["logicalToPhysical"]!.AsArray().Select(v=>v!.GetValue<int>()).ToArray();
        var modes=skeleton["translationRetargetModes"]!.AsArray().Select(v=>v!.GetValue<string>() switch
        { "Animation"=>0,"Skeleton"=>1,"AnimationScaled"=>2,"AnimationRelative"=>3,"OrientAndScale"=>4,_=>throw new Exception() }).ToArray();
        var target=skeleton["referencePose"]!.AsArray().Select(v=>Pose(v!)).ToArray();
        var count=0;double maxPosition=0,maxQuaternionComponent=0,maxScale=0;
        foreach(var asset in source["sequences"]!.AsArray())
        {
            var raw=asset!["raw"]!;var policy=asset["evaluation"]!;
            var present=raw["tracks"]!.AsArray().Select(t=>t!["bone"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var model=new AlsPrecisePoseRetargetModel(mapping,modes,names.Select(present.Contains).ToArray(),target,
                policy["retargetTransforms"]!.AsArray().Select(p=>Pose(p!)).ToArray(),fbxMeters?100:1);
            var samples=oracle["sequences"]!.AsArray().Single(a=>a!["source"]!.GetValue<string>()==raw["source"]!.GetValue<string>())!["samples"]!.AsArray();
            foreach(var item in samples)
            {
                var sample=item!;var time=sample["requestedTime"]!.GetValue<double>();
                Assert.Equal(names,sample["names"]!.AsArray().Select(n=>n!.GetValue<string>()));
                var original=samples.Single(s=>s!["context"]!.GetValue<string>()=="raw"&&s["requestedTime"]!.GetValue<double>()==time)!;
                var actual=original["pose"]!.AsArray().Select(p=>Pose(p!)).ToArray();
                var context=sample["context"]!.GetValue<string>();model.Apply(actual,context!="raw");
                if(context is "asset_root_lock" or "extract_root_lock") actual[0]=target[0];
                for(var bone=0;bone<actual.Length;bone++)
                {
                    var expected=Pose(sample["pose"]![bone]!);var result=actual[bone];
                    var position=System.Math.Sqrt((result.Position-expected.Position).LengthSquared)*(fbxMeters?100:1);
                    var sign=AlsQuaternion.Dot(result.Rotation,expected.Rotation)<0 ? -1:1;
                    var a=result.Rotation;var b=expected.Rotation*sign;
                    var q=new[]{System.Math.Abs(a.X-b.X),System.Math.Abs(a.Y-b.Y),System.Math.Abs(a.Z-b.Z),System.Math.Abs(a.W-b.W)}.Max();
                    var scale=System.Math.Sqrt((result.Scale-expected.Scale).LengthSquared);
                    maxPosition=System.Math.Max(maxPosition,position);maxQuaternionComponent=System.Math.Max(maxQuaternionComponent,q);maxScale=System.Math.Max(maxScale,scale);
                    Assert.True(position<=1e-4,$"{context} {names[bone]} position={position}");
                    Assert.True(q<=1e-6,$"{context} {names[bone]} quaternion={q}");Assert.True(scale<=1e-6);count++;
                }
            }
        }
        Assert.Equal(4740,count);output.WriteLine($"fbx={fbxMeters} bones={count} position_cm={maxPosition:G17} quaternion_component={maxQuaternionComponent:G17} scale={maxScale:G17}");
    }
}
