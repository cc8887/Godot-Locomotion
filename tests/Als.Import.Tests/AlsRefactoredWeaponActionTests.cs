using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponActionTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow, 3, 8, 0)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded, 3, 7, 3)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded, 3, 7, 2)]
    [InlineData(AlsRefactoredWeaponKind.Rifle, 11, 7, 9)]
    public void OriginalActionBranchesPreserveFramesCurvesAndRejectChangedPolicies(AlsRefactoredWeaponKind kind, int mantle, int getup, int roll)
    {
        var catalog = Catalog(); var source = new AlsRefactoredWeaponSourceProfile(catalog, new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind)));
        var profile = new AlsRefactoredWeaponActionProfile(catalog, source);
        Assert.Equal(new Vector4(.3f,.3f,0,.3f), profile.BlendTimes);
        var payload = catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(kind));
        var definition = AlsRefactoredWeaponActionProfile.Compile(payload, source);
        Assert.Equal(new[]{mantle,getup,roll}, definition.Branches.Select(b=>b.Frame));
        var arm = kind == AlsRefactoredWeaponKind.Bow ? "LayerArmLeft" : "LayerArmRight";
        Assert.Equal(new[]{arm}, definition.Branches[1].Names); Assert.Equal(new[]{3f}, definition.Branches[1].Values);
        if(kind == AlsRefactoredWeaponKind.Bow)
        { Assert.Equal(new[]{"LayerArmLeftAdditive"}, definition.Branches[0].Names); Assert.Equal(new[]{.5f}, definition.Branches[0].Values); }
        else Assert.Empty(definition.Branches[0].Names);
        if(kind == AlsRefactoredWeaponKind.Rifle)
        { Assert.Equal(new[]{"LayerArmLeft","LayerArmLeftAdditive","LayerArmRight","LayerArmRightAdditive"},definition.Branches[2].Names); Assert.Equal(new[]{3f,0,3,0},definition.Branches[2].Values); }
        else { Assert.Equal(new[]{arm},definition.Branches[2].Names); Assert.Equal(new[]{3f},definition.Branches[2].Values); }
        var raw = catalog.CompileAbsolutePoseWithCurves($"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}_Poses.A_Als_{kind}_Poses");
        for(var action=0;action<3;action++)
        {
            var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[raw.Curves.Names.Length];var frame=definition.Branches[action].Frame;
            raw.Pose.CreateSampler(raw.Curves).Sample((float)((double)frame*raw.Pose.Data.FrameRateDenominator/raw.Pose.Data.FrameRateNumerator),true,false,false,pose,curves);
            Assert.Equal(pose,profile.Poses[action]);
            for(var c=0;c<profile.CurveNames.Length;c++)
            {
                var name=profile.CurveNames[c];var modify=Array.IndexOf(definition.Branches[action].Names,name);
                var rawIndex=Array.IndexOf(raw.Curves.Names.ToArray(),name);var expected=rawIndex<0?default:curves[rawIndex];
                if(modify>=0)expected=AlsStandingCycleCurves.ModifyBlend(expected,definition.Branches[action].Values[modify],1);
                Assert.Equal(expected,profile.Curves[action][c]);
            }
        }
        foreach(var change in new[]{"time","tag","curve","frame","link","binding","entry"})
        {
            var root=JsonNode.Parse(payload.GetRawText())!;var nodes=root["compiled"]!["nodes"]!.AsArray().Where(n=>n!["graph"]!.GetValue<string>().EndsWith(":Overlay",StringComparison.Ordinal)).ToArray();
            JsonNode Node(string kindName)=>nodes.First(n=>n!["class"]!.GetValue<string>()==kindName)!;
            var action=Node("AlsAnimGraphNode_GameplayTagsBlend")["runtime"]!;
            if(change=="time")action["blendTime"]![0]=.2;
            if(change=="tag")action["tags"]![0]!["tagName"]="Als.Other";
            if(change=="curve")Node("AnimGraphNode_ModifyCurve")["runtime"]!["curveValues"]![0]=7;
            if(change=="frame")Node("AnimGraphNode_SequenceEvaluator")["runtime"]!["explicitFrame"]=99;
            if(change=="link")action["blendPose"]![1]!["linkId"]=0;
            if(change=="binding")root["nativeText"]=root["nativeText"]!.GetValue<string>().Replace("\"LocomotionAction\"","\"RotationMode\"",StringComparison.Ordinal);
            if(change=="entry")root["compiled"]!["nodes"]!.AsArray().Single(n=>n!["class"]!.GetValue<string>()=="AnimGraphNode_LinkedAnimLayer")!["runtime"]!["layer"]="Other";
            using var changed=JsonDocument.Parse(root.ToJsonString());Assert.Throws<ArgumentException>(()=>AlsRefactoredWeaponActionProfile.Compile(changed.RootElement,source));
        }
    }
    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    public void HiddenMachineClocksStopAndReentryResetsWithAtomicRetry(AlsRefactoredWeaponKind kind)
    {
        var catalog=Catalog();var source=new AlsRefactoredWeaponSourceProfile(catalog,new(catalog,new(MantlingHostFixture.Read("refactored_weapon_machines"),catalog,kind)));
        var profile=new AlsRefactoredWeaponActionProfile(catalog,source);var runtime=new AlsRefactoredWeaponOverlayRuntime(profile,0);
        var players=new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),new Dictionary<string,AlsRefactoredTriangulationProfile>(),source.Players.Bind(0,new Dictionary<string,int>{["Secondary Motion"]=0,["Movement"]=1}));
        var rules=new AlsRefactoredWeaponRuleInput("Als.RotationMode.Aiming","Als.Gait.Running","Als.LocomotionMode.Grounded",false,false);
        var input=new AlsRefactoredWeaponPoseInput(.4f,1,0,.7f,.3f,0,0,0,new(1,0,0,0));
        var hidden=0;var reentries=0;var full=new HashSet<int>();
        for(var frame=0;frame<48;frame++)
        {
            var action=frame<4?0:frame<12?1:frame<20?2:frame<28?3:frame<36?0:frame<40?2:0;
            var tag=action==0?"":"Als.LocomotionAction."+((AlsOverlayAction)action);var reset=frame==37;
            var before=runtime.CommittedActions;var clocks=Enumerable.Range(0,source.Players.Players.Length).Select(players.CommittedTime).ToArray();
            runtime.Prepare(frame,tag,rules,input,.65f,.1f,reset);players.Prepare(frame,runtime.SourceInputs,.1f);runtime.Evaluate(frame,players);
            var pose=runtime.Pose.ToArray();var curves=runtime.Curves.ToArray();var weights=runtime.CandidateActions.Weights;
            if(!runtime.MachineUpdated){hidden++;Assert.Empty(runtime.SourceInputs.ToArray());}
            else if(runtime.Machine.Candidate.Reinitialized && frame>0)reentries++;
            for(var i=1;i<4;i++)if(weights[i]==1){full.Add(i);Assert.Equal(profile.Poses[i-1],pose);Assert.Equal(profile.Curves[i-1],curves);}
            var active=runtime.MachineUpdated;
            runtime.Cancel();players.Cancel();Assert.Equal(before,runtime.CommittedActions);Assert.Throws<InvalidOperationException>(()=>runtime.Pose.ToArray());
            runtime.Prepare(frame,tag,rules,input,.65f,.1f,reset);players.Prepare(frame,runtime.SourceInputs,.1f);runtime.Evaluate(frame,players);
            Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());
            runtime.ValidateCommit(frame);players.ValidateCommit(frame);runtime.Commit(frame);players.Commit(frame);
            if(!active)Assert.Equal(clocks,Enumerable.Range(0,source.Players.Players.Length).Select(players.CommittedTime));
        }
        Assert.True(hidden>10);Assert.True(reentries>=2);Assert.Equal(3,full.Count);
        var foreign=new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),new Dictionary<string,AlsRefactoredTriangulationProfile>(),source.Players.Bind(0,new Dictionary<string,int>{["Secondary Motion"]=0,["Movement"]=1}));
        runtime.Prepare(48,"",rules,input,.65f,.1f);players.Prepare(48,runtime.SourceInputs,.1f);foreign.Prepare(48,runtime.SourceInputs,.1f);
        runtime.Evaluate(48,players);var saved=runtime.Pose.ToArray();
        Assert.Throws<ArgumentException>(()=>runtime.Evaluate(48,foreign));
        Assert.Throws<ArgumentException>(()=>runtime.Commit(48));Assert.Throws<InvalidOperationException>(()=>runtime.Pose.ToArray());
        runtime.Cancel();players.Cancel();foreign.Cancel();
        runtime.Prepare(48,"",rules,input,.65f,.1f);players.Prepare(48,runtime.SourceInputs,.1f);runtime.Evaluate(48,players);Assert.Equal(saved,runtime.Pose.ToArray());
        runtime.Commit(48);players.Commit(48);
        runtime.Prepare(49,"",rules,input,.65f,.1f);players.Prepare(49,runtime.SourceInputs,.1f);
        // A downstream full-weight montage may skip the entire pose evaluation.
        runtime.ValidateCommit(49);players.ValidateCommit(49);runtime.Commit(49);players.Commit(49);
        Assert.Throws<InvalidOperationException>(()=>runtime.Pose.ToArray());
    }
}
