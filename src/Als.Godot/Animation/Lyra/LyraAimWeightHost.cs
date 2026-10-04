using System.Text.Json;
using GodotAls.Core.Math;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraAimWeightInput(bool Crouching,bool Ground,bool Ads,double TimeSinceFired,
    double RootYaw,bool HasAcceleration,double Delta,float CommittedHipFireCurve);
internal readonly record struct LyraAimWeights(double HipFire,double Aim);
internal sealed record LyraAimWeightCandidate(LyraAimWeights Weights);

// Provider-global update, before any of its layer roots are traversed. This
// component owns no animation clocks and does not sample the AimOffset graph.
internal sealed class LyraAimWeightHost
{
    private readonly bool _raiseWhenCrouched;
    private readonly double _duration;
    private LyraAimWeightCandidate? _pending;
    public LyraAimWeights Weights {get;private set;}
    public LyraAimWeightHost(LyraMainLayerGraphCatalog graphs,string profile)
    {
        var defaults=graphs.Defaults(profile);
        double Number(string name)
        {
            var value=defaults.GetProperty(name);
            if(value.GetProperty("type").GetString()!="double")throw new NotSupportedException("Changed provider weight precision: "+name);
            return value.GetProperty("value").GetDouble();
        }
        _raiseWhenCrouched=defaults.GetProperty("RaiseWeaponAfterFiringWhenCrouched").GetProperty("value").GetBoolean();
        _duration=Number("RaiseWeaponAfterFiringDuration");
        Weights=new(Number("HipFireUpperBodyOverrideWeight"),Number("AimOffsetBlendWeight"));
        if(!double.IsFinite(_duration)||_duration<0||Weights!=new LyraAimWeights(0,1))
            throw new NotSupportedException("Changed provider weight initialization.");
    }
    public LyraAimWeightCandidate Prepare(in LyraAimWeightInput input)
    {
        if(_pending is not null)throw new InvalidOperationException("Provider weight update is pending.");
        if(!double.IsFinite(input.TimeSinceFired)||!double.IsFinite(input.RootYaw)||!double.IsFinite(input.Delta)||
            input.Delta<0||!float.IsFinite(input.CommittedHipFireCurve))throw new ArgumentException("Nonfinite provider weight input.");
        double hip,aim;
        if(!_raiseWhenCrouched&&input.Crouching || !input.Crouching&&input.Ads&&input.Ground)
        {hip=0;aim=1;}
        else if(input.TimeSinceFired<_duration || input.Ads&&(input.Crouching||!input.Ground) || input.CommittedHipFireCurve>0)
        {hip=1;aim=1;}
        else
        {
            hip=AlsMath.InterpolateTo(Weights.HipFire,0,input.Delta,1);
            // Original SelectFloat accepts/returns double in UE5.8. Preserve
            // the just-updated HipFire value until the exposed graph pin.
            var target=Math.Abs(input.RootYaw)<10&&input.HasAcceleration?hip:1d;
            aim=AlsMath.InterpolateTo(Weights.Aim,target,input.Delta,10);
        }
        return _pending=new(new(hip,aim));
    }
    public void Validate(LyraAimWeightCandidate candidate)
    {if(!ReferenceEquals(candidate,_pending))throw new InvalidOperationException("Foreign, stale or committed provider weight update.");}
    public void Commit(LyraAimWeightCandidate candidate)
    {Validate(candidate);Weights=candidate.Weights;_pending=null;}
    public void Cancel()=>_pending=null;
}

internal readonly record struct LyraAimingExposedPins(float Blend,float Yaw,float Pitch);
internal sealed class LyraAimingGraphDefinition
{
    public string IdleAsset {get;}
    public string RelaxedAsset {get;}
    public LyraAimingGraphDefinition(LyraMainLayerGraphCatalog graphs,string profile)
    {
        var graph=graphs.Graph(profile,LyraLayerHook.FullBody_Aiming);
        var nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("index").GetInt32());
        if(graph.GetProperty("root").GetInt32()!=81 || nodes.Count!=8 ||
            !nodes.Keys.Order().SequenceEqual(new[]{74,75,76,77,78,79,80,81}))
            throw new NotSupportedException("Changed Aiming compiled closure.");
        string Type(int node)=>nodes[node].GetProperty("type").GetString()!;
        void Links(int node,params int[] children)
        {
            if(!nodes[node].GetProperty("links").EnumerateArray().Select(v=>v.GetProperty("index").GetInt32()).SequenceEqual(children))
                throw new NotSupportedException("Changed Aiming input/cache links.");
        }
        Links(81,77);Links(77,79,74);Links(79,76);Links(74,75);Links(76,78);Links(75,78);Links(78,80);Links(80);
        if(Type(81)!="/Script/Engine.AnimNode_Root" || Type(77)!="/Script/AnimGraphRuntime.AnimNode_TwoWayBlend" ||
            Type(78)!="/Script/Engine.AnimNode_SaveCachedPose" || Type(80)!="/Script/Engine.AnimNode_LinkedInputPose" ||
            Type(75)!="/Script/Engine.AnimNode_UseCachedPose" || Type(76)!="/Script/Engine.AnimNode_UseCachedPose")
            throw new NotSupportedException("Changed Aiming node types.");
        var blend=nodes[77].GetProperty("settings");
        if(blend.GetProperty("alphaInputType").GetString()!="Float" || blend.GetProperty("bResetChildOnActivation").GetBoolean() ||
            blend.GetProperty("bAlwaysUpdateChildren").GetBoolean())throw new NotSupportedException("Changed Aiming branch update policy.");
        void Alpha(JsonElement settings)
        {
            var scale=settings.GetProperty("alphaScaleBias");var clamp=settings.GetProperty("alphaScaleBiasClamp");
            if(scale.GetProperty("scale").GetSingle()!=1 || scale.GetProperty("bias").GetSingle()!=0 ||
                clamp.GetProperty("bMapRange").GetBoolean() || clamp.GetProperty("bClampResult").GetBoolean() || clamp.GetProperty("bInterpResult").GetBoolean() ||
                clamp.GetProperty("scale").GetSingle()!=1 || clamp.GetProperty("bias").GetSingle()!=0)
                throw new NotSupportedException("Changed Aiming exposed alpha processing.");
        }
        Alpha(blend);
        var input=nodes[80].GetProperty("settings");
        if(input.GetProperty("name").GetString()!="PreAimPose" || input.GetProperty("graph").GetString()!="FullBody_Aiming" ||
            !input.GetProperty("bIsOutputLinked").GetBoolean())throw new NotSupportedException("Changed Aiming interface input.");
        foreach(var node in nodes.Values)
            if(node.GetProperty("functions").EnumerateObject().Any(p=>p.Value.GetString()!="None"))
                throw new NotSupportedException("Aiming graph gained a node callback.");
        foreach(var node in new[]{74,79})
        {
            var settings=nodes[node].GetProperty("settings");
            if(Type(node)!="/Script/AnimGraphRuntime.AnimNode_RotationOffsetBlendSpace" || settings.GetProperty("bApplyAdditiveInRootSpace").GetBoolean() ||
                settings.GetProperty("alpha").GetSingle()!=1 || settings.GetProperty("method").GetString()!="DoNotSync" || settings.GetProperty("lODThreshold").GetInt32()!=-1)
                throw new NotSupportedException("Changed Aiming additive or source policy.");
            Alpha(settings);
            if(settings.GetProperty("groupName").GetString()!="None" || settings.GetProperty("playRate").GetSingle()!=1 ||
                !settings.GetProperty("bLoop").GetBoolean() || !settings.GetProperty("bResetPlayTimeWhenBlendSpaceChanges").GetBoolean() ||
                settings.GetProperty("startPosition").GetSingle()!=0 || settings.GetProperty("alphaInputType").GetString()!="Float")
                throw new NotSupportedException("Changed Aiming player configuration.");
        }
        var defaults=graphs.Defaults(profile);IdleAsset=defaults.GetProperty("IdleAimOffset").GetProperty("value").GetString()!;
        RelaxedAsset=defaults.GetProperty("RelaxedAimOffset").GetProperty("value").GetString()!;
    }
    public LyraAimingExposedPins Expose(LyraAimWeights weights,double aimYaw,double aimPitch)
    {
        if(!double.IsFinite(weights.HipFire)||!double.IsFinite(weights.Aim)||!float.IsFinite((float)weights.Aim)||!double.IsFinite(aimYaw)||!double.IsFinite(aimPitch)||
            !float.IsFinite((float)aimYaw)||!float.IsFinite((float)aimPitch))throw new ArgumentException("Nonfinite Aiming pins.");
        return new((float)weights.Aim,(float)aimYaw,(float)aimPitch);
    }
}
