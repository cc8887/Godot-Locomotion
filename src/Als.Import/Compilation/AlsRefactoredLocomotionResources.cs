using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredLocomotionEdge(int From, int To, int RuleCompiledIndex, int RulePropertyIndex,
    AlsRefactoredLocomotionRule Rule, bool Automatic, float TriggerTime, float Seconds,
    bool Inertialization, bool QuickFeet, int StartNotify);
public readonly record struct AlsRefactoredLocomotionTimingPlayer(int State, int PropertyIndex, string Source,
    float Length, bool Loop, string Group, float StartPosition, float Rate);
public sealed class AlsRefactoredLocomotionState
{
    private readonly int[] _players;
    public string Name { get; }
    public int RootPropertyIndex { get; }
    public int ExitStart { get; }
    public int ExitCount { get; }
    public bool AlwaysResetOnEntry { get; }
    public string ExitFunction { get; }
    public AlsRefactoredLocomotionRule? EntryRule { get; }
    public ReadOnlySpan<int> PlayerPropertyIndices => _players;
    internal AlsRefactoredLocomotionState(string name, int root, int start, int count, bool reset,
        string exit, AlsRefactoredLocomotionRule? entry, int[] players)
    { Name=name; RootPropertyIndex=root; ExitStart=start; ExitCount=count; AlwaysResetOnEntry=reset; ExitFunction=exit; EntryRule=entry; _players=players; }
}

/// <summary>Verified original Locomotion or nested Jump machine. Both preserve
/// baked priorities and source identities; this resource does not evaluate poses.</summary>
public sealed class AlsRefactoredLocomotionResources
{
    public const string Source = "/ALS/ALS/Character/AnimationInstances/AB_Als_Locomotion.AB_Als_Locomotion";
    public const string QuickFeetPath = "/ALS/ALS/Character/SK_Als.SK_Als:QuickFeetBlend";
    private readonly AlsRefactoredLocomotionState[] _states;
    private readonly AlsRefactoredLocomotionEdge[] _edges;
    private readonly AlsRefactoredLocomotionTimingPlayer[] _players;
    public bool Jump { get; }
    public string CatalogDigest { get; }
    public int MachinePropertyIndex => Jump ? 64 : 83;
    public int InertiaPropertyIndex => Jump ? 39 : 4;
    public ReadOnlySpan<AlsRefactoredLocomotionState> States => _states;
    public ReadOnlySpan<AlsRefactoredLocomotionEdge> Edges => _edges;
    public ReadOnlySpan<AlsRefactoredLocomotionTimingPlayer> TimingPlayers => _players;
    public AlsOverlayBoneProfile? QuickFeet { get; }

    public AlsRefactoredLocomotionResources(string json, AlsRefactoredAnimationCatalog catalog, bool jump = false)
    {
        Jump=jump; CatalogDigest=catalog.IndexDigest;
        using var document=JsonDocument.Parse(json); var root=document.RootElement;
        Expect(root,new { schemaVersion=1 });
        Require(Text(root,"catalogSha256").Equals(CatalogDigest,StringComparison.OrdinalIgnoreCase),"Foreign Locomotion catalog.");
        var export=root.GetProperty("graphs").EnumerateArray().Single(g=>Text(g,"source")==Source);
        var baked=export.GetProperty("bakedMachines").EnumerateArray().Single(m=>Int(m,"machineIndex")== (jump?1:0));
        var machineName=jump?"Jump States":"Locomotion States"; Expect(baked,new { machineName, initialState=0 });
        var payload=catalog.Read(Source); var native=Text(payload,"nativeText");
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>Int(n,"propertyIndex"));
        var compiled=nodes.Values.ToDictionary(n=>Int(n,"compiledNodeIndex"));
        var machine=nodes[MachinePropertyIndex]; Expect(machine,new { @class="AnimGraphNode_StateMachine" });
        var policy=new { maxTransitionsPerFrame=3,maxTransitionsRequests=32,bSkipFirstUpdateTransition=true,
            bReinitializeOnBecomingRelevant=true,bCreateNotifyMetaData=true,bAllowConduitEntryStates=jump };
        Expect(machine.GetProperty("runtime"),policy); Expect(machine.GetProperty("runtime"),new { stateMachineIndexInClass=jump?1:0 });
        Expect(machine.GetProperty("authoredProperties").GetProperty("Node"),policy);
        // The nested template is index 0; the compiled Jump machine is index 1.
        Expect(machine.GetProperty("authoredProperties").GetProperty("Node"),new { stateMachineIndexInClass=0 });
        var machinePath=Text(machine,"path")+"."+machineName;
        var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,Source,machinePath),true);
        var editor=export.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var states=baked.GetProperty("states").EnumerateArray().ToArray();
        var edges=baked.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names=jump?["Entry Conduit","Left Foot","Right Foot","Loop","Flail"]:["Grounded","Fall","Jump","Land","Land Movement","Land Conduct"];
        int[] roots=jump?[-1,63,59,55,53]:[82,80,65,35,29,-1];
        int[] starts=jump?[0,2,3,4,5]:[0,2,4,6,11,14],counts=jump?[2,1,1,1,0]:[2,2,2,5,3,2];
        int[] from=jump?[0,0,1,2,3]:[0,0,1,1,2,2,3,3,3,3,3,4,4,4,5,5];
        int[] to=jump?[2,1,3,3,4]:[2,1,5,0,5,0,0,0,4,2,1,0,2,1,4,3];
        int[][] players=jump?[[],[62,61],[58,57],[54],[52]]:[[],[78,77,76,72,71,68],[43,42,38,62,61,58,57,54,52],[34,33],[28,27],[]];
        Require(states.Length==names.Length&&edges.Length==from.Length,"Locomotion state closure differs.");
        _states=new AlsRefactoredLocomotionState[states.Length]; _edges=new AlsRefactoredLocomotionEdge[edges.Length];
        var paths=new string[states.Length]; var delegates=new int[edges.Length]; var timing=new List<AlsRefactoredLocomotionTimingPlayer>();
        for(var s=0;s<states.Length;s++)
        {
            var conduit=s==(jump?0:5); var reset=!jump&&s==2; var state=states[s];
            Expect(state,new { stateName=names[s],startNotify=-1,endNotify=-1,fullyBlendedNotify=-1,bAlwaysResetOnEntry=reset,
                bIsAConduit=conduit,entryRuleNodeIndex=conduit?(jump?32:62):-1,layerNodeIndices=Array.Empty<int>() });
            Require(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p=>Int(compiled[p.GetInt32()],"propertyIndex")).SequenceEqual(players[s]),"Original state player order differs.");
            AlsRefactoredLocomotionRule? entry=null; var exit="None";
            if(conduit)
            {
                Require(Int(state,"stateRootNodeIndex")==-1,"Conduit has executable pose.");
                var node=compiled[jump?32:62]; var path=Text(node,"graph"); paths[s]=path[..path.LastIndexOf('.')];
                entry=AlsRefactoredLocomotionRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native,Source,path));
                Require(entry.Kind==(jump?AlsRefactoredLocomotionPredicate.True:AlsRefactoredLocomotionPredicate.Grounded),"Conduit predicate differs.");
            }
            else
            {
                var node=compiled[Int(state,"stateRootNodeIndex")]; Require(Int(node,"propertyIndex")==roots[s],"Locomotion root identity differs.");
                var path=Text(node,"graph"); paths[s]=path[..path.LastIndexOf('.')]; exit=!jump&&s==0?"StopTransitionAndTurnInPlaceAnimations":"None";
                Expect(node.GetProperty("runtime"),new { stateIndex=s,name=names[s],stateEntryFunction=new {className="None",functionName="None"},
                    stateExitFunction=new {className="None",functionName=exit},stateFullyBlendedInFunction=new {className="None",functionName="None"},
                    stateFullyBlendedOutFunction=new {className="None",functionName="None"} });
                Expect(editor.Single(n=>Text(n,"path")==paths[s]).GetProperty("properties"),new {BoundGraph=path,bAlwaysResetOnEntry=reset});
            }
            _states[s]=new(names[s],roots[s],starts[s],counts[s],reset,exit,entry,players[s]);
            var exits=state.GetProperty("transitions").EnumerateArray().ToArray(); Require(exits.Length==counts[s],"Locomotion exit count differs.");
            for(var i=0;i<exits.Length;i++)
            {
                var e=starts[s]+i; var automatic=jump?e is 2 or 3:e is 7 or 11;
                Expect(exits[i],new {transitionIndex=e,customResultNodeIndex=-1,bDesiredTransitionReturnValue=true,bAutomaticRemainingTimeRule=automatic,
                    automaticRuleTriggerTime=automatic&&(jump||e==7)?0:-1,syncGroupNameToRequireValidMarkersRule="None",poseEvaluatorLinks=Array.Empty<int>()});
                delegates[e]=Int(exits[i],"canTakeDelegateIndex");
            }
            // Automatic transitions query only these states, in their original
            // baked player order (strict maximum cached weight, first tie wins).
            if(jump?s is 1 or 2:s is 3 or 4)
                foreach(var id in players[s])
                {
                    var node=nodes[id]; Expect(node,new {@class="AnimGraphNode_SequencePlayer"}); var runtime=node.GetProperty("runtime");
                    var shortName=id switch {62=>"Jump_Walk_Left",61=>"Jump_Run_Left",58=>"Jump_Walk_Right",57=>"Jump_Run_Right",
                        34=>"Land_Heavy",33=>"Land_Light",28=>"Land_Light_Additive",27=>"Land_Heavy_Additive",_=>throw new ArgumentException("Foreign timing source.")};
                    var group=jump?"Jump":"Land"; var clip="/ALS/ALS/Animations/Air/"+group+"/A_Als_"+shortName+".A_Als_"+shortName;
                    var start=id is 62 or 58?.12f:0; var rate=id==28?1.75f:id==27?1.5f:1;
                    Expect(runtime,new {sequence=clip,bIgnoreForRelevancyTest=false,bLoopAnimation=false,method="SyncGroup",groupName=group,
                        groupRole="CanBeLeader",bOverridePositionWhenJoiningSyncGroupAsLeader=false,playRateBasis=1,playRate=(double)rate,startPosition=(double)start});
                    timing.Add(new(s,id,clip,catalog.Read(clip).GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle(),false,group,start,rate));
                }
        }
        _players=timing.ToArray();
        var aliases=graph.Nodes.Where(n=>n.Kind=="AnimStateAliasNode").ToDictionary(n=>n.Name,n=>
        {
            Require(!n.Body.Contains("bGlobalAlias=True",StringComparison.Ordinal),"Unexpected global Locomotion alias.");
            var line=n.Body.Split('\n').Single(l=>l.TrimStart().StartsWith("AliasedStateNodes=",StringComparison.Ordinal));
            var members=Regex.Matches(line,"AnimStateNode'([^']+)'").Select(m=>Source[..(Source.LastIndexOf('.')+1)]+m.Groups[1].Value).ToArray();
            Require(members.Length is 2 or 3&&members.Distinct().Count()==members.Length&&members.All(paths.Contains),"Foreign Locomotion alias members.");
            return members.Select(p=>Array.IndexOf(paths,p)).ToArray();
        });
        Require(aliases.Count==(jump?0:2),"Locomotion alias closure differs.");
        var consumed=new Dictionary<string,HashSet<int>>(); var previousPriority=0;
        for(var e=0;e<edges.Length;e++)
        {
            var automatic=jump?e is 2 or 3:e is 7 or 11; var inertia=jump?e is 2 or 3:e is 0 or 1 or 9 or 10 or 12 or 13 or 14 or 15;
            var seconds=jump?(e==4?1:.2f):e switch {0 or 9 or 12 or 14 or 15=>.1f,1 or 10 or 13=>.6f,7=>.8f,11=>.3f,_=>.2f};
            var quick=!jump&&e==7; var blend=quick?"Cubic":"HermiteCubic"; var logic=inertia?"TLT_Inertialization":"TLT_StandardBlend";
            var notify=quick?0:-1;
            Expect(edges[e],new {previousState=from[e],nextState=to[e],customCurve="",blendProfile=quick?QuickFeetPath:"",minTimeBeforeReentry=-1,
                startNotify=notify,endNotify=-1,interruptNotify=-1,blendMode=blend,logicType=logic,bAllowInertializationForSelfTransitions=false});
            Require(edges[e].GetProperty("crossfadeDuration").GetSingle()==seconds&&delegates[e]==(jump?33:63)+e,"Locomotion duration/delegate order differs.");
            var debugRule=compiled.TryGetValue(delegates[e],out var debugNode)?AlsRefactoredLocomotionRuleCompiler.Compile(
                AlsNativeNestedGraph.Extract(native,Source,Text(debugNode,"graph")),automatic):null;
            bool IncludesSource(AlsYawOffsetCompiler.Node n)
            {
                var input=n.Pins.Values.Single(p=>p.Name=="In").Links.Split(' ')[0];
                return input==paths[from[e]].Split('.')[^1]||aliases.TryGetValue(input,out var members)&&members.Contains(from[e]);
            }
            var candidates=graph.Nodes.Where(n=>n.Kind=="AnimStateTransitionNode"&&IncludesSource(n)&&
                n.Pins.Values.Single(p=>p.Name=="Out").Links.StartsWith(paths[to[e]].Split('.')[^1]+" ",StringComparison.Ordinal));
            var matches=0;
            foreach(var candidate in candidates)
            {
                var authored=editor.Single(n=>Text(n,"path")==machinePath+"."+candidate.Name).GetProperty("properties");
                if(authored.GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean()!=automatic)continue;
                var expression=AlsRefactoredLocomotionRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native,Source,Text(authored,"BoundGraph")),automatic);
                if(debugRule is not null&&expression.Signature!=debugRule.Signature)continue;
                Expect(authored,new {BlendMode=blend,LogicType=logic,CustomBlendCurve="",CustomTransitionGraph="",bDisabled=false,MinTimeBeforeReentry=-1,
                    BlendProfileWrapper=new {bIsSkeletonBlendProfile=true,blendProfileProvider="None",blendProfile=quick?QuickFeetPath:""}});
                Require(authored.GetProperty("CrossfadeDuration").GetSingle()==seconds,"Authored Locomotion duration differs.");
                var ruleNode=nodes.Values.Single(n=>Text(n,"class")=="AnimGraphNode_TransitionResult"&&Text(n,"graph")==Text(authored,"BoundGraph"));
                if(e==starts[from[e]])previousPriority=0;
                var priority=Int(authored,"PriorityOrder"); Require(priority>=previousPriority,"Locomotion outgoing priority differs."); previousPriority=priority;
                var notifyName=Regex.Match(candidate.Body,"(?m)^      TransitionStart=\\([^\\r\\n]*NotifyName=\"([^\"]+)\"").Groups[1].Value;
                Require(notifyName==(quick?"LandToGrounded":""),"Locomotion transition notification differs.");
                if(!consumed.TryGetValue(candidate.Name,out var sources))consumed[candidate.Name]=sources=[];
                Require(sources.Add(from[e]),"Repeated Locomotion alias expansion.");
                _edges[e]=new(from[e],to[e],delegates[e],Int(ruleNode,"propertyIndex"),expression,automatic,
                    automatic&&(jump||e==7)?0:-1,seconds,inertia,quick,notify); matches++;
            }
            Require(matches==1,"Missing/ambiguous original Locomotion edge: "+e);
        }
        foreach(var node in graph.Nodes.Where(n=>n.Kind=="AnimStateTransitionNode"))
        {
            var input=node.Pins.Values.Single(p=>p.Name=="In").Links.Split(' ')[0];
            Require(consumed.TryGetValue(node.Name,out var sources)&&sources.Count==(aliases.TryGetValue(input,out var members)?members.Length:1),"Incomplete Locomotion authored transition expansion.");
        }
        if(!jump)QuickFeet=AlsRefactoredCrouchingResources.CompileQuickFeet(export,catalog);
    }
    private static string Text(JsonElement e,string field)=>e.GetProperty(field).GetString()!;
    private static int Int(JsonElement e,string field)=>e.GetProperty(field).GetInt32();
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
