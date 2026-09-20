using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using V2=System.Numerics.Vector2;
using V3=System.Numerics.Vector3;

namespace GodotAls.Animation;

internal readonly record struct AlsStandingCycleTailState(bool Initialized, float History, float Alpha, V2 Lean, float Diagonal);

// The original standing cycle's linked direction layer is followed by component
// diagonal scaling and a curve-driven local additive Lean. History is carried
// in the outer candidate frame, so discard/retry cannot advance this alpha.
internal sealed class AlsStandingCycleTail : IDisposable
{
    private readonly AlsLeanSamplingProfile _profile;
    private readonly AlsLeanPoseSampler _sampler;
    private readonly AlsLocomotionSourcePlayer _player;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsOverlayAlphaPolicy _alpha;
    private readonly V3 _scale;
    private readonly int _bone;
    private readonly int[] _parents;
    private readonly AlsLocalPose[] _scratch;
    private readonly AlsPrecisePose[] _preciseScratch;
    private readonly float[] _weights=new float[5], _times=new float[5];
    private readonly int[] _order=new int[5];
    public string CurveName { get; }
    public int PlayerId=>_profile.PlayerId;

    public AlsStandingCycleTail(AlsAnimationLibraryBuildResult library,AlsAnimationSetDefinition set,int skeleton,
        AlsLocomotionSourceProfile sources)
    {
        _profile=AlsLeanSamplingCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_lean_sampling.json"),sources,set,AlsLocomotionSourceDomain.Cycle);
        _player=sources.Players[_profile.PlayerId]; _samples=sources.Samples.ToArray();
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_anim_graph_inventory.json"));
        var content=_player.SourceNode[.._player.SourceNode.LastIndexOf('.')];
        var nodes=doc.RootElement.GetProperty("graphs").EnumerateArray().Single(g=>Text(g,"path")==content)
            .GetProperty("nodes").EnumerateArray().ToDictionary(n=>Text(n,"name"));
        var additive=nodes["AnimGraphNode_ApplyAdditive_0"]; var data=additive.GetProperty("properties").GetProperty("Node");
        Check(Text(data,"alphaInputType")=="Curve" && data.GetProperty("lODThreshold").GetInt32()==-1,"Unsupported standing Lean alpha mode.");
        CurveName=Text(Pin(additive,"AlphaCurveName"),"value");
        Check(CurveName=="Weight_Gait" && Pin(additive,"AlphaCurveName").GetProperty("links").GetArrayLength()==0,"Standing Lean curve input changed.");
        Link("AnimGraphNode_StateResult_0","Result","AnimGraphNode_ApplyAdditive_0","Pose");
        Link("AnimGraphNode_ApplyAdditive_0","Additive",_player.SourceNode[(content.Length+1)..],"Pose");
        Link("AnimGraphNode_ApplyAdditive_0","Base","AnimGraphNode_ComponentToLocalSpace_0","Pose");
        Link("AnimGraphNode_ComponentToLocalSpace_0","ComponentPose","AnimGraphNode_ModifyBone_0","Pose");
        Link("AnimGraphNode_ModifyBone_0","ComponentPose","AnimGraphNode_LocalToComponentSpace_0","ComponentPose");
        Link("AnimGraphNode_LocalToComponentSpace_0","LocalPose","AnimGraphNode_LinkedAnimLayer_1","Pose");
        var clamp=data.GetProperty("alphaScaleBiasClamp");
        Check(!clamp.GetProperty("bMapRange").GetBoolean(),"Unsupported standing Lean range mapping.");
        _alpha=new(Number(clamp,"scale"),Number(clamp,"bias"),clamp.GetProperty("bClampResult").GetBoolean(),
            Number(clamp,"clampMin"),Number(clamp,"clampMax"),clamp.GetProperty("bInterpResult").GetBoolean(),
            Number(clamp,"interpSpeedIncreasing"),Number(clamp,"interpSpeedDecreasing"));
        var scaleNode=nodes["AnimGraphNode_ModifyBone_0"]; var scaleData=scaleNode.GetProperty("properties").GetProperty("Node");
        Check(Text(scaleData,"translationMode")=="BMM_Ignore" && Text(scaleData,"rotationMode")=="BMM_Ignore" &&
            Text(scaleData,"scaleMode")=="BMM_Additive" && Text(scaleData,"scaleSpace")=="BCS_ComponentSpace" &&
            Text(scaleData,"alphaInputType")=="Float","Unsupported standing diagonal control.");
        Link("AnimGraphNode_ModifyBone_0","Alpha","K2Node_VariableGet_6","DiagonalScaleAmount");
        var scale=scaleData.GetProperty("scale"); _scale=new(Number(scale,"x"),Number(scale,"y"),Number(scale,"z"));
        var pose=library.MovementSources(set,skeleton);
        _bone=pose.Bone(Text(scaleData.GetProperty("boneToModify"),"boneName")); _parents=pose.Parents.ToArray();
        _scratch=new AlsLocalPose[pose.BoneCount];
        _preciseScratch=new AlsPrecisePose[pose.BoneCount];
        _sampler=new(library,set,_samples,_profile);
        void Link(string from,string pin,string to,string target)
        {
            var links=Pin(nodes[from],pin).GetProperty("links");
            Check(links.GetArrayLength()==1 && Text(links[0],"node")==to && Text(links[0],"pin")==target,"Standing cycle tail topology changed.");
        }
    }

    public AlsStandingCycleTailState Prepare(AlsStandingCycleTailState previous,bool initialize,bool update,
        float delta,float gait,V2 lean,float diagonal)
    {
        var state=initialize ? default : previous;
        if(!update)return state;
        var initialized=state.Initialized; var history=state.History;
        var alpha=AlsOverlayPoseWeights.Alpha(gait,_alpha,delta,ref initialized,ref history);
        return new(initialized,history,alpha,lean,diagonal);
    }

    public void Collect(in AlsStandingCycleTailState state,in AlsPoseUpdateContext context,ref AlsCycleSyncFrame frame,
        Span<AlsLocomotionSourceUpdate> players,Span<AlsLocomotionSampleUpdate> samples,Span<bool> active,ref int playerCount,ref int sampleCount,bool ancestorsActive)
    {
        if(!state.Initialized || state.Alpha<=AlsPoseBlender.WeightThreshold)return;
        var count=_profile.Runtime.Evaluate(state.Lean,_weights,_order); var start=sampleCount;
        for(var i=0;i<count;i++)
        {
            var sample=_samples[_profile.SampleStart+_order[i]];
            samples[sampleCount++]=new(sample.SampleId,_weights[_order[i]],sample.SampleRateScale);
        }
        var weight=context.Weight*state.Alpha;
        frame.CachedWeights[PlayerId]=weight;
        active[playerCount]=context.IsActive && ancestorsActive;
        players[playerCount++]=new(PlayerId,frame.Epochs[PlayerId],frame.Times[PlayerId],weight,start,count,context.InertializationSync);
    }

    public void Evaluate(in AlsStandingCycleFrame frame,ReadOnlySpan<string> names,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        var state=frame.Tail;
        AlsDiagonalScalePose.Apply(pose,_parents,_bone,_scale,state.Diagonal,_scratch,pose);
        if(!state.Initialized || state.Alpha<=AlsPoseBlender.WeightThreshold)return;
        _times.AsSpan().Clear();
        for(var s=0;s<frame.Sync.SampleCount;s++)
        {
            var sample=frame.Sync.Samples[s]; var index=sample.SampleId-_profile.SampleStart;
            if((uint)index<5)_times[index]=sample.Time;
        }
        _sampler.Compose(_times,state.Lean,pose,pose,state.Alpha);
        for(var c=0;c<curves.Length;c++)
        {
            var additive=_sampler.ComposeCurve(_times,state.Lean,names[c],default);
            if(additive.Present)curves[c]=new((curves[c].Present ? curves[c].Value : 0)+additive.Value*state.Alpha);
        }
    }
    public void Dispose()=>_sampler.Dispose();
    public void EvaluatePrecise(in AlsStandingCycleFrame frame,ReadOnlySpan<string> names,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        var state=frame.Tail;
        AlsDiagonalScalePose.Apply(pose,_parents,_bone,_scale,state.Diagonal,_preciseScratch,pose);
        if(!state.Initialized || state.Alpha<=AlsPoseBlender.WeightThreshold)return;
        _times.AsSpan().Clear();
        for(var s=0;s<frame.Sync.SampleCount;s++)
        {
            var sample=frame.Sync.Samples[s]; var index=sample.SampleId-_profile.SampleStart;
            if((uint)index<5)_times[index]=sample.Time;
        }
        _sampler.ComposePrecise(_times,state.Lean,pose,pose,state.Alpha);
        for(var c=0;c<curves.Length;c++)
        {
            var additive=_sampler.ComposeCurve(_times,state.Lean,names[c],default);
            if(additive.Present)curves[c]=new((curves[c].Present ? curves[c].Value : 0)+additive.Value*state.Alpha);
        }
    }
    private static JsonElement Pin(JsonElement n,string pin)=>n.GetProperty("pins").EnumerateArray().Single(p=>Text(p,"name")==pin && Text(p,"direction")=="input");
    private static string Text(JsonElement n,string key)=>n.GetProperty(key).GetString()!;
    private static float Number(JsonElement n,string key)=>n.GetProperty(key).GetSingle();
    private static void Check(bool value,string message) { if(!value)throw new FormatException(message); }
}
