using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsSprintInputSource : IDisposable
{
    private readonly AlsSprintInputProfile _profile;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsMovementAnimationSource _first,_impulse;
    private readonly AlsLocalPose[] _rest,_other;
    private readonly AlsPrecisePose[] _preciseOther;
    private AlsSprintInputState _poseState;
    private float _firstSeconds,_impulseSeconds;
    public string[] CurveNames { get; }
    public AlsOverlayAlphaPolicy Alpha=>_profile.Alpha;
    public int FirstPlayer=>_profile.FirstPlayerId;

    public AlsSprintInputSource(AlsAnimationLibraryBuildResult library,AlsAnimationSetDefinition set,AlsLocomotionSourceProfile sources)
    {
        _profile=AlsSprintInputCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_source_graph.json"),sources,set);
        var all=sources.Players;var samples=sources.Samples;
        _players=[all[_profile.FirstPlayerId],all[_profile.ImpulsePlayerId]];
        _samples=[samples[_players[0].SampleStart],samples[_players[1].SampleStart]];
        var pose=library.MovementSources(set,sources.SkeletonId);_rest=pose.ReferencePose.ToArray();_other=new AlsLocalPose[_rest.Length];
        _preciseOther=new AlsPrecisePose[_rest.Length];
        CurveNames=_samples.SelectMany(s=>set.Animations[s.AnimationId].Curves)
            .Where(c=>c.Provenance==AlsCurveProvenance.SourceCurve).Select(c=>c.SourceName).Distinct(StringComparer.Ordinal).ToArray();
        _first=pose.Create(_samples[0].AnimationId);
        try { _impulse=pose.Create(_samples[1].AnimationId); }
        catch { _first.Dispose();throw; }
    }

    public void Collect(in AlsSprintInputUpdate update,in AlsPoseUpdateContext context,bool active,float rate,
        ref AlsCycleSyncFrame candidate,Span<AlsLocomotionSourceUpdate> players,Span<AlsLocomotionSampleUpdate> samples,
        Span<bool> activity,ref int playerCount,ref int sampleCount)
    {
        if(!update.Visited)return;
        for(var child=0;child<2;child++)
        {
            var player=_players[child];var sample=_samples[child];
            var reset=child==0 ? update.ResetFirst : update.ResetImpulse;
            if(!reset)continue;
            candidate.Times[player.PlayerId]=AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence,player.StartPosition,
                sample.DurationSeconds,rate*player.DefaultPlayRate,player.PlayRateBasis,sample.AssetRateScale);
            candidate.Epochs[player.PlayerId]=AlsAssetSourceInitialization.NextEpoch(candidate.Epochs[player.PlayerId]);
            candidate.CachedWeights[player.PlayerId]=0;
        }
        for(var child=0;child<2;child++)
        {
            var weight=child==0 ? update.State.FirstWeight : update.State.ImpulseWeight;
            if(weight==0)continue;
            var player=_players[child];var sample=_samples[child];
            var scaled=context.Weight*weight;candidate.CachedWeights[player.PlayerId]=scaled;
            samples[sampleCount]=new(sample.SampleId,1,sample.SampleRateScale);
            activity[playerCount]=active;
            players[playerCount++]=new(player.PlayerId,candidate.Epochs[player.PlayerId],candidate.Times[player.PlayerId],scaled,
                sampleCount++,1,context.InertializationSync);
        }
    }

    public void PreparePose(in AlsStandingCycleFrame frame)
    {
        _poseState=frame.SprintInput.State;
        _firstSeconds=frame.Sync.Initialized ? frame.Sync.Times[_players[0].PlayerId] :
            frame.HasSourceSeconds ? frame.SourceSeconds[25] : frame.Times[25]*_samples[0].DurationSeconds;
        _impulseSeconds=frame.Sync.Times[_players[1].PlayerId];
    }
    public void Evaluate(Span<AlsLocalPose> output)
    {
        var state=_poseState;
        if(state.ImpulseWeight==0){_first.Sample(_rest,_firstSeconds,output);return;}
        if(state.FirstWeight==0){_impulse.Sample(_rest,_impulseSeconds,output);return;}
        _first.Sample(_rest,_firstSeconds,output);
        _impulse.Sample(_rest,_impulseSeconds,_other);
        // TwoWayBlend's in-place blend receives A's weight and derives B as
        // 1-A; preserve that float subtraction instead of reusing Alpha.
        var second=1-state.FirstWeight;
        for(var b=0;b<output.Length;b++)output[b]=AlsPoseBlender.Blend(output[b],_other[b],second);
    }

    public AlsInertialCurve Curve(in AlsStandingCycleFrame frame,string name)
    { PreparePose(frame);return Curve(name); }
    public void EvaluatePrecise(Span<AlsPrecisePose> output)
    {
        var state=_poseState;
        if(state.ImpulseWeight==0){_first.SamplePrecise(_firstSeconds,output);return;}
        if(state.FirstWeight==0){_impulse.SamplePrecise(_impulseSeconds,output);return;}
        _first.SamplePrecise(_firstSeconds,output); _impulse.SamplePrecise(_impulseSeconds,_preciseOther);
        var second=1-state.FirstWeight;
        for(var b=0;b<output.Length;b++)output[b]=AlsPrecisePoseBlender.Blend(output[b],_preciseOther[b],second);
    }
    public AlsInertialCurve Curve(string name)
    {
        var state=_poseState;
        if(state.ImpulseWeight==0)return _first.Curve(_firstSeconds,name);
        if(state.FirstWeight==0)return _impulse.Curve(_impulseSeconds,name);
        return AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_first.Curve(_firstSeconds,name),state.FirstWeight),
            _impulse.Curve(_impulseSeconds,name),1-state.FirstWeight);
    }
    public void Dispose(){_first.Dispose();_impulse.Dispose();}
}
