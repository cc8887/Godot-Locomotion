using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original Grounded pose closure; action IDs remain owned by the character profile.</summary>
public sealed class AlsRefactoredGroundedHostProfile
{
    public const string RollSource = "/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll";
    public AlsRefactoredGroundedResources Machine { get; }
    internal readonly AlsPoseCacheDefinition Caches;
    internal readonly AlsPrecisePose[] Roll;
    internal readonly AlsInertialCurve[] RollCurves;
    internal readonly string[] RollCurveNames;
    internal readonly AlsSequenceMontageCommand RollToStanding, RollToCrouching;
    internal readonly float StopDuration;
    private readonly string[] _curves;
    public ReadOnlySpan<string> CurveNames => _curves;

    internal AlsRefactoredGroundedHostProfile(AlsRefactoredStandingHostProfile standing, string machines)
    {
        var catalog=standing.Catalog;Machine=new(machines,catalog);
        var payload=catalog.Read(AlsRefactoredGroundedResources.Source);var native=payload.GetProperty("nativeText").GetString()!;
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("propertyIndex").GetInt32());
        string Text(JsonElement n,string key)=>n.GetProperty(key).GetString()!;
        void Link(int id,string field,int target)
        {
            Expect(nodes[id].GetProperty("runtime").GetProperty(field),new{linkId=target,sourceLinkId=id});
            var node=nodes[id];var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,AlsRefactoredGroundedResources.Source,Text(node,"graph")),true);
            var outer=graph.Named(Text(node,"path").Split('.')[^1]);
            // Cached and linked-input links are UObject references, not pose pins.
            if(field is "linkToCachingNode")return;
            var pin=field switch{"sourcePose"=>"SourcePose","source"=>"Source","result"=>"Result","pose"=>"Pose",_=>throw new ArgumentException("Unknown Grounded link.")};
            var (other,output)=graph.Follow(outer,pin);
            Require(output.Output&&Text(nodes[target],"path")==Text(node,"graph")+"."+other.Name,"Grounded editor source differs.");
        }
        foreach(var (id,field,target) in new (int,string,int)[]{(0,"result",44),(44,"source",5),(5,"source",42),(42,"sourcePose",43),(43,"source",41),
            (40,"result",38),(38,"source",39),(39,"linkToCachingNode",3),(37,"result",35),(35,"source",36),(36,"linkToCachingNode",4),
            (34,"result",33),(32,"result",31),(30,"result",27),(27,"source",28),(28,"sourcePose",29),(3,"pose",1),(4,"pose",2)})Link(id,field,target);
        Expect(nodes[43].GetProperty("runtime"),new{defaultBlendProfile="",filteredCurves=Array.Empty<string>(),filteredBones=Array.Empty<string>(),
            bResetOnBecomingRelevant=true,bForwardRequestsThroughSkippedCachedPoseNodes=true});
        Expect(nodes[42].GetProperty("runtime"),new{curveNames=new[]{"PoseGrounded","FootLeftIk","FootRightIk"},curveValues=new[]{1,1,1},alpha=1,applyMode="Blend"});
        Expect(nodes[28].GetProperty("runtime"),new{curveNames=new[]{"FootLeftLock","FootRightLock"},curveValues=new[]{1,1},alpha=1,applyMode="Blend"});
        foreach(var (id,name) in new[]{(1,"Standing Input"),(2,"Crouching Input")})Expect(nodes[id].GetProperty("runtime"),new{name,graph="AnimGraph",bIsOutputLinked=true});
        foreach(var (id,function,site) in new[]{(44,"InitializeGrounded","OnBecomeRelevant"),(5,"RefreshGrounded","OnUpdate"),
            (38,"ResetGroundedEntryMode","OnBecomeRelevant"),(35,"ResetGroundedEntryMode","OnBecomeRelevant"),(27,"ResetGroundedEntryMode","OnBecomeRelevant")})
        {
            var node=nodes[id];Expect(node.GetProperty("runtime"),new{callSite=site});Expect(node.GetProperty("authoredProperties").GetProperty("Node"),new{callSite=site});
            var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,AlsRefactoredGroundedResources.Source,Text(node,"graph")),true);
            var outer=graph.Named(Text(node,"path").Split('.')[^1]);
            var innerName=Regex.Match(outer.Body,"(?m)^      InnerGraph=\"/Script/Engine.EdGraph'([^']+)'\"").Groups[1].Value;
            Require(innerName!="","Missing Grounded callback graph.");
            var inner=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,AlsRefactoredGroundedResources.Source,Text(node,"path")+"."+innerName),true);
            var call=inner.Nodes.Single();inner.Self(call);
            Require(call.Kind=="K2Node_CallFunction"&&call.Member==function&&call.Pins.Values.All(p=>p.Links==""),"Grounded callback differs.");
        }
        foreach(var id in new[]{33,31})
        {
            var node=nodes[id];var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,AlsRefactoredGroundedResources.Source,Text(node,"graph")),true);
            var outer=graph.Named(Text(node,"path").Split('.')[^1]);
            Require(outer.Pins.Values.All(p=>p.Output||p.Links=="")&&!outer.Body.Contains("bIsBound=True",StringComparison.Ordinal),"Dynamic Grounded player binding.");
            Require(float.Parse(graph.Literal(outer,"PlayRate"),CultureInfo.InvariantCulture)==1.2f,"Grounded authored rate pin differs.");
            Expect(node.GetProperty("runtime"),new{playRate=(double)1.2f});
            Expect(node.GetProperty("authoredProperties").GetProperty("Node"),new{playRate=1});
            foreach(var policy in new[]{node.GetProperty("runtime"),node.GetProperty("authoredProperties").GetProperty("Node")})
            {
                Expect(policy,new{sequence=Machine.Players[id==33?0:1].Source,playRateBasis=1,startPosition=0,bLoopAnimation=false,
                    method="DoNotSync",groupName="None",bIgnoreForRelevancyTest=false,bStartFromMatchingPose=false,bOverridePositionWhenJoiningSyncGroupAsLeader=false});
                Clamp(policy.GetProperty("playRateScaleBiasClampConstants"));
            }
        }
        Expect(nodes[29].GetProperty("runtime"),new{sequence=RollSource,bUseExplicitFrame=true,explicitFrame=45,bShouldLoop=true,bTeleportToExplicitTime=true,method="DoNotSync",groupName="None",reinitializationBehavior="ExplicitTime"});
        var roll=catalog.CompileAbsolutePoseWithCurves(RollSource);Roll=new AlsPrecisePose[roll.Pose.BoneNames.Length];RollCurveNames=roll.Curves.Names.ToArray();RollCurves=new AlsInertialCurve[RollCurveNames.Length];
        var time=(float)(45d*roll.Pose.Data.FrameRateDenominator/roll.Pose.Data.FrameRateNumerator);
        roll.Pose.CreateSampler(roll.Curves).Sample(time,true,false,false,Roll,RollCurves);
        var curves=Machine.Players.ToArray().SelectMany(p=>catalog.CompileAbsolutePoseWithCurves(p.Source).Curves.Names.ToArray());
        _curves=curves.Concat(RollCurveNames).Concat(["PoseGrounded","FootLeftIk","FootRightIk","FootLeftLock","FootRightLock"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var order=payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes")[0].GetProperty("compiledNodeIndices").EnumerateArray()
            .Select(i=>nodes.Values.Single(n=>n.GetProperty("compiledNodeIndex").GetInt32()==i.GetInt32()).GetProperty("propertyIndex").GetInt32()).ToArray();
        Require(order.SequenceEqual(new[]{3,4}),"Grounded cache order differs.");Caches=new(45,order,[new(39,3),new(36,4)]);
        AlsYawOffsetCompiler.Graph Function(string name)=>new(AlsNativeNestedGraph.Extract(native,AlsRefactoredGroundedResources.Source,AlsRefactoredGroundedResources.Source+":"+name),true);
        AlsYawOffsetCompiler.Node Call(AlsYawOffsetCompiler.Graph graph,string name)
        {
            var call=graph.Nodes.Single(n=>n.Kind=="K2Node_CallFunction"&&n.Member==name);graph.Function(call,name,"ALS.AlsAnimationInstance");
            var (parent,pin)=graph.Follow(call,"self");graph.Self(parent);Require(parent.Member=="GetParent"&&pin.Name=="ReturnValue","Foreign Grounded callback owner.");
            var entry=graph.Nodes.Single(n=>n.Kind=="K2Node_FunctionEntry");graph.Links(call,"execute",(entry,"then"));
            Require(graph.Nodes.Count()==3&&call.Pins.Values.Single(p=>p.Name=="then").Links=="","Grounded function closure differs.");return call;
        }
        var rollGraph=Function("PlayRollToGroundedTransitionAnimation");var rollCall=Call(rollGraph,"PlayTransitionRightAnimation");
        float Number(string key)=>float.Parse(rollGraph.Literal(rollCall,key),CultureInfo.InvariantCulture);
        Require(rollGraph.Literal(rollCall,"bFromStandingIdleOnly")=="false","Grounded Roll transition idle gate differs.");
        AlsSequenceMontageCommand Command(int side)=>new(standing.QuickStop.Assets[side].AnimationId,AlsMontageSlot.Transition,Number("PlayRate"),Number("StartTime"),Number("BlendInDuration"),Number("BlendOutDuration"));
        RollToStanding=Command(1);RollToCrouching=Command(3);
        var stopGraph=Function("StopTransitionAndTurnInPlaceAnimations");var stopCall=Call(stopGraph,"StopTransitionAndTurnInPlaceAnimations");
        StopDuration=float.Parse(stopGraph.Literal(stopCall,"BlendOutDuration"),CultureInfo.InvariantCulture);
    }
    private static void Require(bool ok,string message){if(!ok)throw new ArgumentException(message);}
}
