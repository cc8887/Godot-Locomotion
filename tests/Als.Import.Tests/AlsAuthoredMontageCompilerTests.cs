using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAuthoredMontageCompilerTests
{
    [Fact]
    public void CanonicalRollAndTurnsUseNativeDistinctGroupsAndOneOwner()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(),set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"),set,locomotion);
        var profile = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"),set);
        var actions = AlsAuthoredMontageCompiler.Compile(Read("v4_action_montage_inputs.json"),Read("v4_turn_montage_inputs.json"),set,profile,pose.SkeletonId);
        var turns = AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"),set,pose);
        var roll = Assert.Single(actions);
        Assert.Equal(AlsMontageSlot.BaseLayer,roll.Slot); Assert.Equal(3,roll.GroupId); Assert.True(roll.RootMotionEnabled);
        Assert.All(turns,t => Assert.Equal(1,t.GroupId)); Assert.Equal(.1f,roll.Lifecycle.BlendInSeconds);
        var owner = new AlsMontageRuntime(turns,actions);
        owner.Begin(new(1,1,1),.05f); owner.PlayAction(roll.ActionDefinitionId,1);
        owner.Play(new(turns[0].AnimationId,turns[0].Slot,1,0,.2f,.2f,1,0));
        Assert.Equal(2,owner.Candidate.Length); Assert.All(owner.Candidate.ToArray(),i => Assert.False(i.Interrupted));
        owner.Commit(new(1,1,1)); owner.Begin(new(2,1,1),.05f);
        Assert.Equal(2,owner.Evaluation.Length);
        Assert.Equal(.5f,owner.SlotWeights(AlsMontageSlot.BaseLayer).SlotNodeWeight,6);
        Assert.True(owner.SlotWeights(AlsTurnSlot.Standing).SlotNodeWeight > 0);
        var requests = AlsAuthoredMontageCompiler.CompileRequests(profile,actions);
        var policy = Assert.Single(requests); var configured = Assert.Single(profile.Actions);
        Assert.Equal(configured.PlayRate,policy.PlayRate); Assert.Equal(configured.BlendSeconds,policy.CancelBlendSeconds);
        Assert.Equal(configured.Interruptible,policy.Interruptible);
        var bank = new AlsMontageRuntime(turns,actions); var runtime = new AlsMontageActionRuntime(bank,requests);
        runtime.Begin(new(1,1,1),.05f);
        runtime.ApplyRequest(new(1,AlsActionCommand.Start,configured.DefinitionId,configured.StartSectionId,configured.Priority,1));
        runtime.Complete(); Assert.Equal(AlsActionResultCode.Accepted,runtime.Outcomes[0].ResultCode);
        Assert.Equal(roll.AnimationId,bank.Candidate[0].AnimationId); runtime.Commit(new(1,1,1));
        Assert.Throws<ArgumentException>(()=>AlsAuthoredMontageCompiler.CompileRequests(profile,[]));
        Assert.Throws<ArgumentException>(()=>AlsAuthoredMontageCompiler.CompileRequests(profile,[roll with {Duration=3}]));
        Assert.Throws<ArgumentException>(()=>AlsAuthoredMontageCompiler.CompileRequests(profile,[roll with {MontageId=999}]));
    }

    [Theory]
    [InlineData("rateScale",0)] [InlineData("blendInMode",1)] [InlineData("blendOutMode",1)]
    [InlineData("inOption",0)] [InlineData("outOption",0)] [InlineData("slot",0)] [InlineData("length",3)]
    public void RejectsUnsupportedOrStaleNativeNumericFields(string field,int value) =>
        Reject(a => a[field] = value);

    [Theory]
    [InlineData("blendProfiles")] [InlineData("customBlendCurves")]
    public void RejectsUnsupportedNativeBlendFeatures(string field) => Reject(a => a[field] = true);

    [Fact] public void RejectsStaleRootMotionProvenance() => Reject(a => a["hasRootMotion"] = false);
    [Fact] public void RejectsWrongNativeGroup() => Reject(a => a["group"] = "Grounded Group");

    private static void Reject(Action<JsonObject> mutate)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"),set);
        var root = JsonNode.Parse(Read("v4_action_montage_inputs.json"))!.AsObject(); mutate(root["assets"]![0]!.AsObject());
        var skeleton = set.Animations[profile.SegmentBindings[0].AnimationId].SkeletonId;
        Assert.Throws<ArgumentException>(() => AlsAuthoredMontageCompiler.Compile(root.ToJsonString(),Read("v4_turn_montage_inputs.json"),set,profile,skeleton));
    }
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets","config",name));
}
