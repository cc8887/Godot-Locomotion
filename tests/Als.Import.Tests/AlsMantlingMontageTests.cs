using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingMontageTests
{
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+name+".json"));
    private static AlsMantlingMontageProfile Compile()=>AlsMantlingMontageCompiler.Compile(Read("refactored_mantle_animation_inputs"),
        Read("refactored_mantle_root_tracks"),Read("refactored_mantle_curves"));

    [Fact]
    public void RealMontagesUseSharedClockRateScaleAndFullPoseCurveSlot()
    {
        var profile=Compile();Assert.Equal(6,profile.Definitions.Count);Assert.Equal("Locomotion",profile.GroupName);
        foreach(var definition in profile.Definitions.Values)
        {
            var asset=definition.Asset;var bank=profile.CreateRuntime();var sampler=profile.CreatePoseSource();
            Assert.Equal(AlsMontageSlot.PostLocomotion,asset.Slot);Assert.Equal(2,asset.GroupId);
            Assert.False(asset.RootMotionEnabled);Assert.Equal(.2f,asset.Lifecycle.BlendInSeconds);
            Assert.Equal(AlsActionBlendOption.HermiteCubic,asset.Lifecycle.BlendInOption);
            Assert.Equal(definition.Path.Contains("_High.")?AlsActionBlendOption.Cubic:AlsActionBlendOption.HermiteCubic,asset.Lifecycle.BlendOutOption);
            var source=profile.Poses[definition.SequencePath];var slot=new AlsMontageSlotPose(source.ReferencePose,source.Parents,2);
            var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[2];
            var expectedPose=new AlsPrecisePose[79];var expectedCurves=new AlsInertialCurve[2];
            var idle=source.ReferencePose.ToArray();var idleCurves=new AlsInertialCurve[2];
            var full=0;var partial=0;var done=false;
            for(var frame=1;frame<=240;frame++)
            {
                var identity=new AlsFrameIdentity(frame,1,1);bank.Begin(identity,1f/60);
                if(frame==1){Assert.True(bank.PlayAction(asset.ActionDefinitionId,1));Assert.Empty(bank.Evaluation.ToArray());}
                if(frame==2)Assert.Equal(asset.RateScale/60,bank.Candidate[0].Position,6);
                Assert.Equal(0,bank.CandidateRootMotionInstance);
                slot.Evaluate(bank.Frame,identity,AlsMontageSlot.PostLocomotion,idle,idleCurves,pose,curves,sampler);
                var weights=bank.Frame.SlotWeights(AlsMontageSlot.PostLocomotion);
                if(weights.SlotNodeWeight==1&&weights.SourceWeight==0)
                {
                    full++;sampler.Sample(bank.Evaluation[0],expectedPose,expectedCurves);
                    for(var bone=0;bone<pose.Length;bone++)
                    {
                        Assert.True((pose[bone].Position-expectedPose[bone].Position).LengthSquared<1e-16);
                        Assert.True(Math.Abs(1-Math.Abs(AlsQuaternion.Dot(pose[bone].Rotation.Normalized(),expectedPose[bone].Rotation.Normalized())))<1e-12);
                    }
                    Assert.Equal(expectedCurves,curves);
                }
                else if(weights.SlotNodeWeight>0)partial++;
                foreach(var bone in pose)bone.Validate();
                if(frame>1&&bank.Candidate.IsEmpty)done=true;
                bank.Commit(identity);
            }
            Assert.True(full>30&&partial>5&&done);Assert.Equal(idle,pose);Assert.Equal(idleCurves,curves);
        }
    }

    [Fact]
    public void ReplacementAndDiscardRetainPhysicalInstanceIdentity()
    {
        var profile=Compile();var assets=profile.Definitions.Values.Select(d=>d.Asset).ToArray();var bank=profile.CreateRuntime();
        bank.Begin(new(1,1,1),.1f);bank.PlayAction(assets[0].ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),.1f);bank.Commit(new(2,1,1));var before=bank.Committed.ToArray();
        bank.Begin(new(3,1,1),.1f);bank.PlayAction(assets[1].ActionDefinitionId,1);
        Assert.Equal(2,bank.Candidate.Length);Assert.NotEqual(bank.Candidate[0].InstanceId,bank.Candidate[1].InstanceId);
        bank.Discard();Assert.Equal(before,bank.Committed.ToArray());
        bank.Begin(new(3,1,1),.1f);bank.PlayAction(assets[1].ActionDefinitionId,1);bank.Commit(new(3,1,1));
        Assert.Equal(2,bank.Committed.Length);Assert.True(bank.Committed[0].Interrupted);
    }

    [Theory]
    [InlineData("slot")][InlineData("section")][InlineData("blend-profile")][InlineData("rate")][InlineData("custom-curve")]
    public void RejectsUnboundNativeMontageSemantics(string mutation)
    {
        var json=JsonNode.Parse(Read("refactored_mantle_animation_inputs"))!;var row=json["montages"]![0]!;
        switch(mutation)
        {
            case "slot":row["slot"]="BaseLayer";break;
            case "section":row["nativeText"]=row["nativeText"]!.GetValue<string>().Replace("SectionName=\"Default\"","SectionName=\"Other\"");break;
            case "blend-profile":row["nativeText"]=row["nativeText"]!.GetValue<string>()+"\n   BlendProfileIn=Other\n";break;
            case "rate":row["rateScale"]=0;break;
            case "custom-curve":row["blendIn"]!["customCurve"]="/Game/Curve.Curve";break;
        }
        var text=json.ToJsonString();var curves=JsonNode.Parse(Read("refactored_mantle_curves"))!;
        curves["animationInputsSha256"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        Assert.Throws<ArgumentException>(()=>AlsMantlingMontageCompiler.Compile(text,Read("refactored_mantle_root_tracks"),curves.ToJsonString()));
    }
}
