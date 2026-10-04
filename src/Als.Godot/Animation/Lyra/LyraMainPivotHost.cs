using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainPivotState(int InitialDirection,double LastPivotTime,bool Active,float Weight);
internal readonly record struct LyraMainPivotObserved(LyraPivotLayerPoseCandidate Pivot,
    LyraMainPivotState AfterRoot,LyraMainPivotState State,bool BecameRelevant);
internal sealed record LyraMainPivotCandidate(LyraMainUpdateCandidate Main,LyraPivotLayerPoseCandidate Pivot,
    LyraMainLeanCompositionCandidate Lean,LyraMainLeanSyncInputs LeanInputs,
    LyraMainPivotState Before,LyraMainPivotState AfterRoot,LyraMainPivotState State,bool BecameRelevant,
    AlsAssetSyncPlayer[] Players,AlsAssetSyncSample[] Samples,int[] Groups,ImmutableArray<float> Inertia);

// Actual Main StateResult20 -> ApplyAdditive23 -> Linked21/Lean22. Main owns
// the relevance latch and timer, the selected source may write the timer back.
// All sources join one enclosing Sync pass and publish in one transaction.
internal sealed class LyraMainPivotHost
{
    private readonly LyraMainUpdateHost _main=new();
    private readonly LyraPivotLayerPoseHost _pivot;
    private readonly LyraMainLeanCompositionHost _lean;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;
    private LyraMainPivotCandidate? _pending;
    private LyraMainLeanCompositionResolved? _resolved;
    private AlsAssetPlayerHistory[]? _players;
    private AlsAssetSampleHistory[]? _samples;
    private float _pendingBlendIn;
    public LyraMainObservationState Main => _main.State;
    public LyraMainTailState Tail => _main.Tail;
    public LyraMainPivotState State { get; private set; }
    public ImmutableArray<LyraMainLeanNodeState> LeanStates => _lean.LeanStates;
    public bool HasPose { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Main Pivot has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Main Pivot has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Main Pivot has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Main Pivot has no root attribute.");

    public LyraMainPivotHost(LyraPivotLayerPoseHost pivot,LyraLogicalSourceBank bank,
        AlsAssetSyncSequence[] sequences,int leanSequenceBase,LyraLayerSignature signature,
        int leanPlayerBase=1000,int leanAssetId=9000,long epoch=1)
    {
        if (signature.Hook!=LyraLayerHook.FullBody_PivotState || signature.Group!="ItemAnimLayers" ||
            signature.BlendInProfile!="" || signature.BlendOutProfile!="" || !float.IsFinite(signature.BlendInTime))
            throw new NotSupportedException("Changed Main Pivot linked contract.");
        ValidateOriginalBinding();
        _pivot=pivot; _pendingBlendIn=signature.BlendInTime;
        _lean=new(bank,sequences,leanPlayerBase,leanAssetId,leanSequenceBase,epoch);
        _curves=new LyraCurveSample[bank.Curves.Names.Length]; _attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }

    internal static void ValidateOriginalBinding()
    {
        var inventory=LyraLocomotionLayerInventory.Load(LyraSourceNodeCatalog.Load());
        var defaults=inventory.MainDefaults.GetProperty("fields");
        if (defaults.GetProperty("PivotInitialDirection").GetProperty("type").GetString()!="TEnumAsByte<AnimEnum_CardinalDirection>" ||
            defaults.GetProperty("PivotInitialDirection").GetProperty("value").GetString()!="Forward" ||
            defaults.GetProperty("LastPivotTime").GetProperty("type").GetString()!="double" ||
            defaults.GetProperty("LastPivotTime").GetProperty("value").GetDouble()!=0)
            throw new NotSupportedException("Changed original Main Pivot defaults.");
        var callbacks=LyraSourceNodeCatalog.Load().ForClass(LyraRuntimeGraphCatalog.MainClass).Callbacks;
        if (callbacks[20]!=new LyraSourceFunctions("None","SetUpPivotState","UpdatePivotState"))
            throw new NotSupportedException("Changed Main Pivot state-root callbacks.");
    }

    // Both standalone native comparison and the character's common scope
    // consume the same already prepared Main candidate. No second Main clock.
    internal static LyraMainPivotObserved PrepareObserved(LyraPivotLayerPoseHost host,LyraMainUpdateCandidate main,
        LyraMainPivotState before,float delta,float weight,double hipWeight,bool active,bool initialize,
        AlsPivotMovementSnapshot movement,AlsPrecisePose component,AlsQuaternion relativeRotation,
        Func<LyraMainUpdateCandidate,double>? linkedHipWeight=null)
    {
        if (!float.IsFinite(weight) || weight<0) throw new ArgumentException("Invalid Main Pivot root weight.");
        var observed=main.State;
        // Pose-link Initialize leaves Main's NodeRelevancy history intact.
        var relevant=active && weight>1e-5f && (!before.Active || !(before.Weight>1e-5f));
        var state=before with { Active=active,Weight=active ? weight : 0 };
        if (relevant) state=state with { InitialDirection=observed.Direction };
        if (active && state.LastPivotTime>0) state=state with { LastPivotTime=state.LastPivotTime-(double)delta };
        var afterRoot=state;
        var direction=observed.AccelerationDirection switch
        { 0=>LyraCardinalDirection.Forward,1=>LyraCardinalDirection.Backward,2=>LyraCardinalDirection.Left,
            3=>LyraCardinalDirection.Right,_=>throw new InvalidOperationException("Native acceleration direction") };
        var input=new LyraPivotInput(observed.Crouching,observed.Ads,direction,observed.LocalVelocity,observed.LocalAcceleration,
            (state.InitialDirection<2)!=(observed.Direction<2),observed.Displacement,state.LastPivotTime,movement);
        hipWeight=linkedHipWeight?.Invoke(main)??hipWeight;
        var pivot=host.Prepare(input,delta,weight,hipWeight,active,initialize,observed.DisplacementSpeed,
            new(observed.DirectionAngleWithOffset,component,relativeRotation,main.Observation.Frame));
        state=state with { LastPivotTime=pivot.Sources.Machine.Sources.Shared.LastPivotTime };
        return new(pivot,afterRoot,state,relevant);
    }

    public LyraMainPivotCandidate Prepare(in LyraMainUpdateInput input,float delta,float weight,
        double hipWeight,bool active,bool initialize,AlsPivotMovementSnapshot movement,
        AlsPrecisePose component,AlsQuaternion relativeRotation)
    {
        if (_pending is not null) throw new InvalidOperationException("Main Pivot frame is pending.");
        if (!float.IsFinite(weight) || weight<0) throw new ArgumentException("Invalid Main Pivot root weight.");
        HasPose=false;
        try
        {
            var main=_main.Prepare(input,delta);
            var prepared=PrepareObserved(_pivot,main,State,delta,weight,hipWeight,active,initialize,movement,component,relativeRotation);
            var pivot=prepared.Pivot;
            var lean=_lean.PrepareObserved(main,delta,[weight,0,0],[active,false,false],[initialize,false,false],[0,1,2]);
            var collected=_lean.CollectAtCommonSync(lean,pivot.Sources.Samples.Length);
            var inertia=pivot.Sources.Machine.Inertia;
            if (active && _pendingBlendIn>=0) inertia=inertia.Add(_pendingBlendIn);
            return _pending=new(main,pivot,lean,collected,State,prepared.AfterRoot,prepared.State,prepared.BecameRelevant,
                [..pivot.Sources.Players,..collected.Players],[..pivot.Sources.Samples,..collected.Samples],
                [..pivot.Sources.Groups,..collected.Players.Select(_=>-1)],inertia);
        }
        catch { Cancel(); throw; }
    }

    public void Resolve(LyraMainPivotCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is not null) throw new InvalidOperationException("Main Pivot Sync already resolved.");
        _resolved=_lean.Resolve(candidate.Lean,candidate.LeanInputs,players,samples);
        _players=players.ToArray(); _samples=samples.ToArray();
    }
    public LyraMainLeanNodeState PreparedLean(LyraMainPivotCandidate candidate)
    { Validate(candidate); return (_resolved ?? throw new InvalidOperationException("Main Pivot needs common Sync.")).Lean.States[0]; }
    public void Evaluate(LyraMainPivotCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate); HasPose=false;
        ValidateSync(candidate,players,samples);
        _pivot.Evaluate(candidate.Pivot,players);
        _root=_lean.Evaluate(_resolved!,0,_pivot.Pose,_pivot.Curves,_pivot.Attributes,_pivot.RootMotion,_pose,_curves,_attributes);
        HasPose=true;
    }
    public void ValidateCommit(LyraMainPivotCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateSync(candidate,players,samples);
        if (candidate.Pivot.Sources.Machine.Active && !HasPose) throw new InvalidOperationException("Main Pivot needs current evaluation.");
        _main.ValidateCommit(candidate.Main); _pivot.ValidateCommit(candidate.Pivot,players); _lean.ValidateCommit(_resolved!);
    }
    public void Commit(LyraMainPivotCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateCommit(candidate,players,samples);
        _pivot.Commit(candidate.Pivot,players); _lean.Commit(_resolved!); _main.Commit(candidate.Main); State=candidate.State;
        if (candidate.Pivot.Sources.Machine.Active) _pendingBlendIn=-1;
        _pending=null; _resolved=null; _players=null; _samples=null;
    }
    public void Cancel()
    { _pivot.Cancel(); _lean.Cancel(); _main.Cancel(); _pending=null; _resolved=null; _players=null; _samples=null; HasPose=false; }
    private void Validate(LyraMainPivotCandidate candidate)
    { if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Stale/repeated Main Pivot candidate."); }
    private void ValidateSync(LyraMainPivotCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is null || _players is null || _samples is null || !players.SequenceEqual(_players) || !samples.SequenceEqual(_samples))
            throw new InvalidOperationException("Different Main Pivot Sync snapshot.");
    }
}
