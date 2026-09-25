using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredWeaponKind { Bow, PistolOneHanded, PistolTwoHanded, Rifle }
public readonly record struct AlsRefactoredWeaponEdge(int From, int To, int RuleNode, float Seconds,
    AlsTransitionBlend Blend, int Curve, bool QuickFeet, int StartNotify);
public sealed class AlsRefactoredWeaponStateResources
{
    private readonly int[] _exits, _players;
    public string Name { get; }
    public int RootNode { get; }
    public ReadOnlySpan<int> Exits => _exits;
    public ReadOnlySpan<int> Players => _players;
    internal AlsRefactoredWeaponStateResources(string name,int root,int[] exits,int[] players)
    { Name=name;RootNode=root;_exits=exits;_players=players; }
}

/// <summary>Original baked machine resources. Transition predicates and execution
/// are intentionally not inferred from endpoints or the V4 machine rules.</summary>
public sealed class AlsRefactoredWeaponMachineResources
{
    private readonly AlsRefactoredWeaponStateResources[] _states;
    private readonly AlsRefactoredWeaponEdge[] _edges;
    private readonly AlsMovementInputCurve[] _curves;
    public AlsRefactoredWeaponKind Kind { get; }
    public string CatalogDigest { get; }
    public int CompiledNode { get; }
    public ReadOnlySpan<AlsRefactoredWeaponStateResources> States => _states;
    public ReadOnlySpan<AlsRefactoredWeaponEdge> Edges => _edges;
    public ReadOnlySpan<AlsMovementInputCurve> Curves => _curves;
    public AlsOverlayBoneProfile QuickFeet { get; }
    public static string Blueprint(AlsRefactoredWeaponKind kind)=>$"/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_{kind}.AB_Als_{kind}";
    public AlsRefactoredWeaponMachineResources(string json,AlsRefactoredAnimationCatalog catalog,AlsRefactoredWeaponKind kind)
    {
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        Expect(root,new{schemaVersion=1});
        if(!string.Equals(root.GetProperty("catalogSha256").GetString(),catalog.IndexDigest,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Foreign weapon catalog.");
        var weapons=root.GetProperty("weapons").EnumerateArray().ToArray();
        if(!weapons.Select(w=>Text(w,"source")).Order().SequenceEqual(Enum.GetValues<AlsRefactoredWeaponKind>().Select(Blueprint).Order()))throw new ArgumentException("Incomplete weapon inventory.");
        var source=Blueprint(kind);var item=weapons.Single(w=>Text(w,"source")==source);var payload=catalog.Read(source);
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>Int(n,"compiledNodeIndex"));
        var node=nodes.Values.Single(n=>Text(n,"class")=="AnimGraphNode_StateMachine");
        foreach(var policy in new[]{node.GetProperty("runtime"),node.GetProperty("authoredProperties").GetProperty("Node")})
            Expect(policy,new{stateMachineIndexInClass=0,maxTransitionsPerFrame=3,maxTransitionsRequests=32,bSkipFirstUpdateTransition=true,bReinitializeOnBecomingRelevant=true,bCreateNotifyMetaData=true,bAllowConduitEntryStates=false});
        CompiledNode=Int(node,"compiledNodeIndex");Kind=kind;CatalogDigest=catalog.IndexDigest;
        var machine=item.GetProperty("bakedMachines").EnumerateArray().Single();
        Expect(machine,new{machineIndex=0,initialState=0,machineName=kind==AlsRefactoredWeaponKind.Bow?"Bow States":kind==AlsRefactoredWeaponKind.Rifle?"Rifle States":"Pistol States"});
        var states=machine.GetProperty("states").EnumerateArray().ToArray();var edges=machine.GetProperty("transitions").EnumerateArray().ToArray();
        if(!states.Select(s=>Text(s,"stateName")).SequenceEqual(new[]{"Relaxed","Aiming","Ready"})||edges.Length!=6)throw new ArgumentException("Weapon state closure differs.");
        _states=new AlsRefactoredWeaponStateResources[3];_edges=new AlsRefactoredWeaponEdge[6];
        int[][] exits=[[0],[1],[2,3,4,5]];var delegates=new int[6];
        var editor=item.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var authoredMachine=editor.Single(n=>Text(n,"class")=="AnimGraphNode_StateMachine");
        if(Text(authoredMachine,"path")!=Text(node,"path"))throw new ArgumentException("Weapon machine identity differs.");
        for(var s=0;s<3;s++)
        {
            var state=states[s];Expect(state,new{startNotify=-1,endNotify=-1,fullyBlendedNotify=-1,entryRuleNodeIndex=-1,bAlwaysResetOnEntry=false,bIsAConduit=false});
            if(state.GetProperty("layerNodeIndices").GetArrayLength()!=0)throw new ArgumentException("Unsupported weapon linked state.");
            var id=Int(state,"stateRootNodeIndex");var authored=editor.Single(n=>Text(n,"class")=="AnimStateNode"&&Text(n.GetProperty("properties"),"BoundGraph").EndsWith("."+Text(state,"stateName"),StringComparison.Ordinal));
            Expect(authored.GetProperty("properties"),new{bAlwaysResetOnEntry=false});
            if(Text(nodes[id],"class")!="AnimGraphNode_StateResult"||Text(nodes[id],"graph")!=Text(authored.GetProperty("properties"),"BoundGraph"))throw new ArgumentException("Weapon root binding differs.");
            var outgoing=state.GetProperty("transitions").EnumerateArray().ToArray();
            if(!outgoing.Select(e=>Int(e,"transitionIndex")).SequenceEqual(exits[s]))throw new ArgumentException("Weapon transition priority differs.");
            foreach(var edge in outgoing)
            {
                Expect(edge,new{customResultNodeIndex=-1,bDesiredTransitionReturnValue=true,bAutomaticRemainingTimeRule=false,automaticRuleTriggerTime=-1,syncGroupNameToRequireValidMarkersRule="None"});
                if(edge.GetProperty("poseEvaluatorLinks").GetArrayLength()!=0)throw new ArgumentException("Custom weapon evaluation link.");
                var rule=Int(edge,"canTakeDelegateIndex");if(Text(nodes[rule],"class")!="AnimGraphNode_TransitionResult")throw new ArgumentException("Foreign weapon rule.");
                delegates[Int(edge,"transitionIndex")]=rule;
            }
            var players=state.GetProperty("playerNodeIndices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            if(players.Distinct().Count()!=players.Length||players.Any(p=>Text(nodes[p],"class") is not("AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator")||Text(nodes[p],"graph")!=Text(nodes[id],"graph")))throw new ArgumentException("Weapon player closure differs.");
            var actual=nodes.Values.Where(n=>Text(n,"graph")==Text(nodes[id],"graph")&&Text(n,"class") is "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator").Select(n=>Int(n,"compiledNodeIndex")).Order();
            if(!actual.SequenceEqual(players.Order()))throw new ArgumentException("Dropped weapon state player.");
            _states[s]=new(Text(state,"stateName"),id,exits[s].ToArray(),players);
        }
        var curveRows=item.GetProperty("curves").EnumerateArray().OrderBy(c=>Text(c,"path"),StringComparer.Ordinal).ToArray();
        string CurvePath(string n)=>$"/ALS/ALS/Data/AnimationInstance/Blending/CF_Als_Aim_{n}.CF_Als_Aim_{n}";
        if(!curveRows.Select(c=>Text(c,"path")).SequenceEqual(new[]{CurvePath("In"),CurvePath("Out")}))throw new ArgumentException("Weapon curve inventory differs.");
        _curves=curveRows.Select(CompileCurve).ToArray();
        int[] from=[0,1,2,2,2,2],to=[2,2,0,0,1,0],curveIds=[-1,1,-1,-1,0,-1],notifies=[0,-1,1,-1,-1,-1];
        float[] seconds=[.2f,1,.75f,.75f,kind==AlsRefactoredWeaponKind.Bow?.5f:.2f,kind==AlsRefactoredWeaponKind.Bow?.4f:.2f];
        AlsTransitionBlend[] blends=[AlsTransitionBlend.HermiteCubic,AlsTransitionBlend.Custom,AlsTransitionBlend.Cubic,AlsTransitionBlend.Cubic,AlsTransitionBlend.Custom,AlsTransitionBlend.HermiteCubic];
        for(var e=0;e<6;e++)
        {
            var edge=edges[e];Expect(edge,new{previousState=from[e],nextState=to[e],minTimeBeforeReentry=-1,startNotify=notifies[e],endNotify=-1,interruptNotify=-1,blendMode=blends[e].ToString(),logicType="TLT_StandardBlend",bAllowInertializationForSelfTransitions=false});
            if(edge.GetProperty("crossfadeDuration").GetSingle()!=seconds[e]||Text(edge,"customCurve")!=(curveIds[e]<0?"":Text(curveRows[curveIds[e]],"path"))||Text(edge,"blendProfile")!=(e==2?"/ALS/ALS/Character/SK_Als.SK_Als:QuickFeetBlend":""))throw new ArgumentException("Weapon transition asset differs.");
            var authored=editor.Single(n=>Text(n,"class")=="AnimStateTransitionNode"&&Text(n.GetProperty("properties"),"BoundGraph")==Text(nodes[delegates[e]],"graph")).GetProperty("properties");
            if(authored.GetProperty("CrossfadeDuration").GetSingle()!=seconds[e])throw new ArgumentException("Authored weapon duration differs.");
            Expect(authored,new{BlendMode=blends[e].ToString(),LogicType="TLT_StandardBlend",bAutomaticRuleBasedOnSequencePlayerInState=false,bDisabled=false,MinTimeBeforeReentry=-1});
            _edges[e]=new(from[e],to[e],delegates[e],seconds[e],blends[e],curveIds[e],e==2,notifies[e]);
        }
        var profile=item.GetProperty("blendProfiles").EnumerateArray().Single();Expect(profile,new{path="/ALS/ALS/Character/SK_Als.SK_Als:QuickFeetBlend",skeleton="/ALS/ALS/Character/SK_Als.SK_Als",mode=1});
        var bones=profile.GetProperty("bones").EnumerateArray().ToArray();
        var layout=catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        if(!bones.Select(b=>Text(b,"name")).SequenceEqual(layout.BoneNames.ToArray())||!bones.Select(b=>Int(b,"parent")).SequenceEqual(layout.Parents.ToArray()))throw new ArgumentException("Weapon profile layout differs.");
        var entries=bones.Select(b=>Int(b,"entry")).ToArray();if(!entries.Where(i=>i>=0).Order().SequenceEqual(Enumerable.Range(0,18)))throw new ArgumentException("QuickFeet entries differ.");
        QuickFeet=new(bones.Select(b=>Text(b,"name")).ToArray(),bones.Select(b=>Int(b,"parent")).ToArray(),bones.Select(b=>b.GetProperty("scale").GetSingle()).ToArray(),entries.Select(i=>i>=0).ToArray());
        var cases=profile.GetProperty("nativeCases");if(cases.GetArrayLength()!=33)throw new ArgumentException("Missing QuickFeet reference.");
        foreach(var row in cases.EnumerateArray())
        {
            if(row.GetProperty("incoming").GetArrayLength()!=18||row.GetProperty("outgoing").GetArrayLength()!=18)throw new ArgumentException("QuickFeet reference layout differs.");
            for(var i=0;i<entries.Length;i++)if(entries[i]>=0){var w=QuickFeet.Weights(i,row.GetProperty("alpha").GetSingle());if(MathF.Abs(w.X-row.GetProperty("incoming")[entries[i]].GetSingle())>2e-6f||MathF.Abs(w.Y-row.GetProperty("outgoing")[entries[i]].GetSingle())>2e-6f)throw new ArgumentException("QuickFeet native mismatch.");}
        }
    }
    private static AlsMovementInputCurve CompileCurve(JsonElement row)
    {
        var curve=row.GetProperty("curve");Expect(curve,new{preInfinityExtrap="RCCE_Constant",postInfinityExtrap="RCCE_Constant"});
        var keys=curve.GetProperty("keys").EnumerateArray().Select(k=>{
            Expect(k,new{tangentWeightMode="RCTWM_WeightedNone"});
            return new AlsCurveKey(k.GetProperty("time").GetSingle(),k.GetProperty("value").GetSingle(),k.GetProperty("arriveTangent").GetSingle(),k.GetProperty("leaveTangent").GetSingle(),Text(k,"interpMode") switch{"RCIM_Cubic"=>AlsCurveInterpolationMode.Cubic,"RCIM_Linear"=>AlsCurveInterpolationMode.Linear,_=>throw new ArgumentException("Unsupported weapon curve interpolation.")});
        }).ToArray();
        if(keys[0].TimeSeconds!=0||keys[^1].TimeSeconds!=1)throw new ArgumentException("Weapon blend curve domain differs.");
        var result=new AlsMovementInputCurve(keys,nativePrecision:true);var samples=row.GetProperty("verification");if(samples.GetArrayLength()!=201)throw new ArgumentException("Missing weapon curve reference.");
        for(var i=0;i<201;i++){var rowSample=samples[i];var t=rowSample.GetProperty("input").GetSingle();if(t!=i*(1f/200)||MathF.Abs(result.Sample(t)-rowSample.GetProperty("value").GetSingle())>2e-6f)throw new ArgumentException("Weapon curve native mismatch.");}
        return result;
    }
    private static string Text(JsonElement e,string name)=>e.GetProperty(name).GetString()!;
    private static int Int(JsonElement e,string name)=>e.GetProperty(name).GetInt32();
}
