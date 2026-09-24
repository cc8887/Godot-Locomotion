using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRegionSlotBindingsTests
{
    private static string Read()=>MantlingHostFixture.Read("refactored_slot_inventory");
    private static string Skeleton(JsonNode data)=>data["skeletons"]!.AsObject().Single().Key;

    [Fact]
    public void OriginalAssetClosureUsesPostLocomotionAndRegionsShareLayerGroup()
    {
        var data=JsonNode.Parse(Read())!;
        Assert.Equal(9,data["montages"]!.AsArray().Count);
        Assert.All(data["montages"]!.AsArray(),m=>Assert.Equal("PostLocomotion",Assert.Single(m!["tracks"]!.AsArray())!["slot"]!.GetValue<string>()));
        var profile=AlsRefactoredRegionSlotBindings.Compile(Read(),Skeleton(data),40);
        Assert.Equal(3,profile.NativeGroupIndex);Assert.Equal(40,profile.HostGroupId);
        var names=new[]{"Head","Spine","ArmLeft","ArmRight","Pelvis","Legs","Curves"};
        var regions=names.Select((name,index)=>profile.BindSequence(index,name,3,0)).ToArray();
        Assert.All(regions,asset=>Assert.Equal(40,asset.GroupId));
        var bank=new AlsMontageRuntime([],sequences:regions.Append(new(100,AlsMontageSlot.PostLocomotion,41,3,0)).ToArray());
        var first=new AlsFrameIdentity(1,1,1);var second=new AlsFrameIdentity(2,1,1);
        bank.Begin(first,.3f);
        Assert.True(bank.PlaySequence(new(0,AlsMontageSlot.Head,1,0,.2f,.2f)));
        Assert.True(bank.PlaySequence(new(100,AlsMontageSlot.PostLocomotion,1,0,.2f,.2f)));bank.Commit(first);
        bank.Begin(second,.1f);var frozen=bank.Evaluation.ToArray();
        Assert.True(bank.PlaySequence(new(3,AlsMontageSlot.ArmRight,1,0,.2f,.2f)));
        Assert.True(Assert.Single(bank.Candidate.ToArray(),i=>i.AnimationId==0).Interrupted);
        Assert.False(Assert.Single(bank.Candidate.ToArray(),i=>i.AnimationId==100).Interrupted);
        Assert.Equal(frozen,bank.Evaluation.ToArray());
        var candidate=bank.Candidate.ToArray();bank.Discard();bank.Begin(second,.1f);
        Assert.True(bank.PlaySequence(new(3,AlsMontageSlot.ArmRight,1,0,.2f,.2f)));
        Assert.Equal(candidate,bank.Candidate.ToArray());bank.Commit(second);
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("split")][InlineData("native")]
    public void InvalidNativeRegionOwnershipIsRejected(string kind)
    {
        var data=JsonNode.Parse(Read())!;var skeleton=Skeleton(data);
        var asset=data["skeletons"]![skeleton]!;var groups=asset["groups"]!.AsArray();
        var layer=groups.Single(g=>g!["name"]!.GetValue<string>()=="Layer")!;
        if(kind=="missing")layer["slots"]!.AsArray().RemoveAt(0);
        if(kind=="duplicate")layer["slots"]!.AsArray().Add("Head");
        if(kind=="split")groups[0]!["slots"]!.AsArray().Add("Head");
        if(kind=="native")asset["nativeText"]="";
        Assert.Throws<ArgumentException>(()=>AlsRefactoredRegionSlotBindings.Compile(data.ToJsonString(),skeleton,40));
    }
}
