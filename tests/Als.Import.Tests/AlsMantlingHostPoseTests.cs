using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingHostPoseTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Lazy<AlsRawAnimationSkeletonDefinition> Host=new(()=>
    {
        var root=RepositoryRoot.Find();var set=P3RepositoryFixtures.LoadAnimationSet();var json=MantlingHostFixture.Read("v4_movement_source_inputs");
        using var document=JsonDocument.Parse(json);var request=document.RootElement.GetProperty("request");
        var roots=request.GetProperty("rootAssets").EnumerateArray().Select(a=>set.Animations.Single(s=>s.StableId==a.GetProperty("assetId").GetString()).Id).ToArray();
        return AlsRawAnimationSourceCompiler.Compile(json,set,request.GetProperty("bindingDigest").GetString()!,request.GetProperty("players").GetInt32(),
            request.GetProperty("samples").GetInt32(),roots,file=>File.ReadAllBytes(Path.Combine(root,"assets/config",file))).GetSkeleton(set.Animations[roots[0]].SkeletonId);
    });
    private static AlsMantlingMontageProfile Profile()=>MantlingHostFixture.Bind(AlsMantlingMontageCompiler.Compile(
        MantlingHostFixture.Read("refactored_mantle_animation_inputs"),MantlingHostFixture.Read("refactored_mantle_root_tracks"),
        MantlingHostFixture.Read("refactored_mantle_curves"))).Profile;
    private static readonly string[] Curves=["UnusedHostCurve","PoseGrounded","PoseStanding","FootLeftLock"];
    [Fact]
    public void MontageOwnedCurvesMatchNativeEvaluationAndSurviveHostSlotComposition()
    {
        var profile=Profile();var host=Host.Value;var json=MantlingHostFixture.Read("refactored_mantle_montage_curves");
        var owned=AlsMantlingCurveCompiler.CompileMontages(json,MantlingHostFixture.Read("refactored_mantle_animation_inputs"));
        using var reference=JsonDocument.Parse(MantlingHostFixture.Read("refactored_mantle_montage_curve_reference"));
        Assert.Equal(reference.RootElement.GetProperty("curvesSha256").GetString()!.ToUpperInvariant(),Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
        var names=Curves.Concat(owned.Values.SelectMany(c=>c.Names.ToArray())).Distinct(StringComparer.Ordinal).ToArray();
        var sampler=new AlsMantlingHostPoseProfile(profile,host,names,owned).CreatePoseSource();
        var pose=new AlsPrecisePose[host.LogicalBoneCount];var curves=new AlsInertialCurve[names.Length];
        var samples=0;var values=0;var partial=0;float maxError=0;
        foreach(var row in reference.RootElement.GetProperty("montages").EnumerateArray())
        {
            var path=row.GetProperty("source").GetString()!;var asset=profile.Definitions[path].Asset;
            foreach(var sample in row.GetProperty("samples").EnumerateArray())
            {
                sampler.Sample(new(1,asset.AnimationId,asset.Slot,sample.GetProperty("timeSeconds").GetSingle(),1,asset.ActionDefinitionId),pose,curves);
                foreach(var curve in sample.GetProperty("curves").EnumerateObject())
                {
                    var actual=curves[Array.IndexOf(names,curve.Name)];var expected=curve.Value.GetSingle();
                    Assert.True(actual.Present);Assert.True(MathF.Abs(expected-actual.Value)<=2e-6,$"{path} {curve.Name}: {expected:R} != {actual.Value:R}");
                    maxError=MathF.Max(maxError,MathF.Abs(expected-actual.Value));
                    values++;if(expected!=0&&expected!=1&&expected!=-1)partial++;
                }
                Assert.False(curves[0].Present);samples++;
            }
            var bank=profile.CreateRuntime();var slot=new AlsMontageSlotPose(host.PreciseReferencePose,host.LogicalParents,names.Length);
            var sourceCurves=Enumerable.Repeat(new AlsInertialCurve(.3f),names.Length).ToArray();
            var mixed=new AlsInertialCurve[names.Length];var mixedPose=new AlsPrecisePose[pose.Length];
            for(var frame=1;frame<=240;frame++)
            {
                var identity=new AlsFrameIdentity(frame,1,1);bank.Begin(identity,1f/60);
                if(frame==1)bank.PlayAction(asset.ActionDefinitionId,1);
                slot.Evaluate(bank.Frame,identity,asset.Slot,host.PreciseReferencePose,sourceCurves,mixedPose,mixed,sampler);
                if(bank.Evaluation.Length>0)
                {
                    var entry=bank.Evaluation[0];sampler.Sample(entry,pose,curves);var sourceWeight=bank.SlotWeights(asset.Slot).SourceWeight;
                    foreach(var name in owned[path].Names)
                    {
                        var i=Array.IndexOf(names,name);Assert.True(mixed[i].Present);
                        Assert.True(MathF.Abs(mixed[i].Value-(curves[i].Value*entry.Weight+.3f*sourceWeight))<2e-6);
                    }
                }
                bank.Commit(identity);
            }
        }
        Assert.Equal(726,samples);Assert.Equal(7502,values);Assert.True(partial>100);
        output.WriteLine($"montages=6 samples={samples} values={values} partial={partial} max_error={maxError:R}");
        var good=profile.Definitions.Values.First().Asset;var saved=curves.ToArray();
        Assert.Throws<ArgumentException>(()=>sampler.Sample(new(1,good.AnimationId,good.Slot,.4f,1,int.MaxValue),pose,curves));
        Assert.Equal(saved,curves);
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostPoseProfile(profile,host,Curves,owned));
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostPoseProfile(profile,host,names,owned.Skip(1).ToDictionary(p=>p.Key,p=>p.Value)));
    }
    [Theory]
    [InlineData("digest")][InlineData("curve")][InlineData("montage")]
    public void MontageCurveCompilerRejectsIncompleteOrForeignResources(string mutation)
    {
        var json=System.Text.Json.Nodes.JsonNode.Parse(MantlingHostFixture.Read("refactored_mantle_montage_curves"))!;
        if(mutation=="digest")json["animationInputsSha256"]=new string('0',64);
        else if(mutation=="curve")json["montages"]![0]!["curves"]!.AsArray().RemoveAt(0);
        else json["montages"]!.AsArray().RemoveAt(0);
        Assert.Throws<ArgumentException>(()=>AlsMantlingCurveCompiler.CompileMontages(json.ToJsonString(),MantlingHostFixture.Read("refactored_mantle_animation_inputs")));
    }
    [Fact]
    public void PhysicalBonesPreserveNativeSamplesAndCurvesKeepPresenceInReorderedHostLayout()
    {
        var profile=Profile();var host=Host.Value;var adapted=new AlsMantlingHostPoseProfile(profile,host,Curves).CreatePoseSource();
        var native=profile.CreatePoseSource();var nativePose=new AlsPrecisePose[79];var pose=new AlsPrecisePose[host.LogicalBoneCount];
        var originalCurves=new AlsInertialCurve[2];var curves=new AlsInertialCurve[Curves.Length];
        foreach(var definition in profile.Definitions.Values)
        for(var frame=0;frame<=60;frame++)
        {
            var entry=new AlsMontageEvaluation(1,definition.Asset.AnimationId,definition.Asset.Slot,definition.Asset.Duration*frame/60,1,definition.Asset.ActionDefinitionId);
            native.Sample(entry,nativePose,originalCurves);adapted.Sample(entry,pose,curves);
            var source=profile.Poses[definition.SequencePath];
            for(var physical=0;physical<host.PhysicalBoneCount;physical++)
            {
                var logical=host.PhysicalToLogical[physical];var name=host.LogicalBoneNames[logical];
                var original=Array.FindIndex(source.BoneNames.ToArray(),n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
                Assert.Equal(AlsMantlingHostPoseProfile.ToFbx(nativePose[original]),pose[logical]);
            }
            for(var i=0;i<2;i++)Assert.Equal(originalCurves[i],curves[Array.IndexOf(Curves,profile.Curves[definition.SequencePath].Names[i])]);
            Assert.Equal(default,curves[0]);Assert.Equal(default,curves[3]);
        }
    }
    [Fact]
    public void VirtualBonesUseHostDefinitionsAtBothRawKeysBeforeInterpolation()
    {
        var profile=Profile();var host=Host.Value;var adapted=new AlsMantlingHostPoseProfile(profile,host,Curves).CreatePoseSource();
        var pose=new AlsPrecisePose[host.LogicalBoneCount];var curves=new AlsInertialCurve[Curves.Length];var checkedBones=0;
        foreach(var definition in profile.Definitions.Values.GroupBy(d=>d.SequencePath).Select(g=>g.First()))
        {
            var source=profile.Poses[definition.SequencePath];var data=source.Data;
            var originalNames=source.BoneNames.ToArray();
            for(var frame=0;frame<29;frame++)
            {
                var time=(float)(data.PlayLength*(frame+.37)/30);var keys=AlsRawSequencePoseSampler.SelectKeys(data,time);
                adapted.Sample(new(1,definition.Asset.AnimationId,definition.Asset.Slot,time,1,definition.Asset.ActionDefinitionId),pose,curves);
                foreach(var vb in host.VirtualBones)
                {
                    var expected=AlsPrecisePose.BlendTransform(AtKey(keys.FirstKey,vb),AtKey(keys.SecondKey,vb),keys.Alpha);
                    var converted=AlsMantlingHostPoseProfile.ToFbx(expected);var actual=pose[vb.Bone];
                    Assert.True((actual.Position-converted.Position).LengthSquared<1e-24);
                    Assert.True(System.Math.Abs(AlsQuaternion.Dot(actual.Rotation,converted.Rotation)-1)<1e-10);checkedBones++;
                }
                AlsPrecisePose AtKey(int key,AlsLogicalVirtualBone vb)
                {
                    var components=new AlsPrecisePose[source.Data.LogicalBoneCount];
                    // Build only the real physical hierarchy from authored binary32 keys.
                    for(var logical=0;logical<components.Length;logical++)
                    {
                        var physical=data.LogicalToPhysical[logical];if(physical<0)continue;
                        var local=new AlsPrecisePose(data.GetPhysicalKey(key)[physical]);var parent=source.Parents[logical];
                        components[logical]=parent<0?local:AlsPrecisePose.Compose(local,components[parent]).Normalized();
                    }
                    var rawSource=vb.Source;
                    foreach(var other in host.VirtualBones)if(other.Bone==vb.Source){rawSource=other.Target;break;}
                    var a=Array.FindIndex(originalNames,n=>n.Equals(host.LogicalBoneNames[rawSource],StringComparison.OrdinalIgnoreCase));
                    var b=Array.FindIndex(originalNames,n=>n.Equals(host.LogicalBoneNames[vb.Target],StringComparison.OrdinalIgnoreCase));
                    Assert.True(a>=0&&b>=0);return AlsPrecisePose.Relative(components[b],components[a]);
                }
            }
        }
        Assert.Equal(957,checkedBones);
    }
    [Fact]
    public void IndependentOwnersMatchSerialAndBadOutputDoesNotPartiallyWrite()
    {
        var profile=Profile();var layout=new AlsMantlingHostPoseProfile(profile,Host.Value,Curves);var asset=profile.Definitions.Values.First().Asset;
        var entry=new AlsMontageEvaluation(1,asset.AnimationId,asset.Slot,.743f,1,asset.ActionDefinitionId);
        var expected=new AlsPrecisePose[layout.BoneCount];var expectedCurves=new AlsInertialCurve[Curves.Length];layout.CreatePoseSource().Sample(entry,expected,expectedCurves);
        Parallel.For(0,4,_=>
        {
            var sampler=layout.CreatePoseSource();var pose=new AlsPrecisePose[layout.BoneCount];var curves=new AlsInertialCurve[Curves.Length];
            for(var i=0;i<100;i++)sampler.Sample(entry,pose,curves);Assert.Equal(expected,pose);Assert.Equal(expectedCurves,curves);
            Assert.Throws<ArgumentException>(()=>sampler.Sample(entry with {AnimationId=int.MaxValue},pose,curves));Assert.Equal(expected,pose);Assert.Equal(expectedCurves,curves);
        });
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostPoseProfile(profile,Host.Value,["PoseStanding"]));
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostPoseProfile(profile,Host.Value,["PoseStanding","posestanding","PoseGrounded"]));
    }
    [Fact]
    public void SharedPostLocomotionSlotBlendsAdaptedPoseAndReturnsTheOriginalHostSource()
    {
        var profile=Profile();var host=Host.Value;var sampler=new AlsMantlingHostPoseProfile(profile,host,Curves).CreatePoseSource();
        var slot=new AlsMontageSlotPose(host.PreciseReferencePose,host.LogicalParents,Curves.Length);
        var source=host.PreciseReferencePose.ToArray();var sourceCurves=new AlsInertialCurve[]{new(5),new(.2f),new(.3f),new(.4f)};
        var pose=new AlsPrecisePose[source.Length];var curves=new AlsInertialCurve[Curves.Length];
        var sample=new AlsPrecisePose[source.Length];var sampleCurves=new AlsInertialCurve[Curves.Length];
        foreach(var definition in profile.Definitions.Values)
        {
            var bank=profile.CreateRuntime();var partial=0;var full=0;
            for(var frame=1;frame<=240;frame++)
            {
                var identity=new AlsFrameIdentity(frame,1,1);bank.Begin(identity,1f/60);
                if(frame==1)bank.PlayAction(definition.Asset.ActionDefinitionId,1);
                slot.Evaluate(bank.Frame,identity,AlsMontageSlot.PostLocomotion,source,sourceCurves,pose,curves,sampler);
                var weights=bank.SlotWeights(AlsMontageSlot.PostLocomotion);
                if(bank.Evaluation.Length>0)
                {
                    var entry=bank.Evaluation[0];sampler.Sample(entry,sample,sampleCurves);
                    foreach(var logical in host.PhysicalToLogical)
                    {
                        var expected=sample[logical].Position*entry.Weight+source[logical].Position*weights.SourceWeight;
                        Assert.True((expected-pose[logical].Position).LengthSquared<1e-20);
                    }
                    if(weights.SourceWeight==0){full++;Assert.Equal(default,curves[0]);Assert.Equal(default,curves[3]);}
                    else partial++;
                }
                foreach(var bone in pose)bone.Validate();bank.Commit(identity);
            }
            Assert.True(partial>5&&full>20);Assert.Equal(source,pose);Assert.Equal(sourceCurves,curves);
        }
    }
}
