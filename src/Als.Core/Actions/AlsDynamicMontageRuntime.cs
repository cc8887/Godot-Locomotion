using System.Collections.Immutable;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsDynamicMontageAsset(int AnimationId, AlsTurnSlot Slot, int GroupId, float Duration);
public readonly record struct AlsSequenceMontageAsset(int AnimationId, AlsMontageSlot Slot, int GroupId, float Duration, int AdditiveType);
public readonly record struct AlsSequenceMontageCommand(int AnimationId, AlsMontageSlot Slot, float PlayRate,
    float StartTime, float BlendInTime, float BlendOutTime, int LoopCount = 1, float BlendOutTriggerTime = 0)
{
    public bool InertialBlendOut { get; init; }
}
public readonly record struct AlsMontageInertialRequest(float Duration, AlsActionBlendOption BlendOption)
{
    public bool UseBlendMode => BlendOption != AlsActionBlendOption.Linear;
}
// These are graph slot identities, not montage-local slot array indices.
public readonly record struct AlsMontageSlot(int Id)
{
    public const int Count = 13;
    public const ushort AllMask = (1 << Count) - 1;
    public bool IsValid => (uint)Id < Count;
    public ushort Mask => IsValid ? (ushort)(1 << Id) : throw new ArgumentOutOfRangeException(nameof(Id));
    public static AlsMontageSlot BaseLayer => new(2);
    public static AlsMontageSlot Grounded => new(3);
    public static AlsMontageSlot PostLocomotion => new(4);
    public static AlsMontageSlot Head => new(5);
    public static AlsMontageSlot Spine => new(6);
    public static AlsMontageSlot ArmLeft => new(7);
    public static AlsMontageSlot ArmRight => new(8);
    public static AlsMontageSlot Pelvis => new(9);
    public static AlsMontageSlot Legs => new(10);
    public static AlsMontageSlot Curves => new(11);
    public static AlsMontageSlot Transition => new(12);
    public static AlsMontageSlot FromRefactoredLayerName(string name) => name switch
    {
        "Head" => Head, "Spine" => Spine, "ArmLeft" => ArmLeft, "ArmRight" => ArmRight,
        "Pelvis" => Pelvis, "Legs" => Legs, "Curves" => Curves,
        _ => throw new ArgumentException("Unknown Refactored Layering Slot: " + name)
    };
    public static implicit operator AlsMontageSlot(AlsTurnSlot slot) => new((int)slot);
}

public readonly record struct AlsMontageTrack(int AnimationId, AlsMontageSlot Slot,
    float ClipStart, float ClipRate, int AdditiveType)
{
    // -1 retains the legacy unbounded segment without a nonfinite value in
    // serialized instance diagnostics or candidate snapshots.
    public float ClipEnd { get; init; } = -1;
    public float SamplePosition(float montagePosition) => ClipStart +
        (ClipEnd < 0 ? montagePosition : System.Math.Clamp(montagePosition, 0, (ClipEnd - ClipStart) / ClipRate)) * ClipRate;
}

// Single-section, one segment per track; authored actions and transient
// sequences share the same physical instance clock, group arbitration and fades.
public readonly record struct AlsAuthoredMontageAsset(int ActionDefinitionId, int AnimationId,
    AlsMontageSlot Slot, int GroupId, float Duration, float ClipStart, float ClipRate,
    AlsActionLifecycleSettings Lifecycle, bool RootMotionEnabled = false, int MontageId = -1)
{
    public int AdditiveType { get; init; }
    public float RateScale { get; init; } = 1;
    public bool InertialBlendOut { get; init; }
    public ImmutableArray<AlsMontageTrack> AdditionalTracks { get; init; }
    public int BlendInProfileId { get; init; } = -1;
    public int BlendOutProfileId { get; init; } = -1;
    public float ClipEnd { get; init; } = -1;
}

public readonly record struct AlsMontageInstance(long InstanceId, int AnimationId, AlsMontageSlot Slot,
    int GroupId, float Duration, float Position, float PlayRate, float BlendTime,
    AlsActionLifecycleSettings Settings, AlsActionLifecycleState Blend, bool Playing, bool Interrupted)
{
    // FAlphaBlend setters defer range/time reset until Update. This is candidate
    // state too: discarding a frame must discard a pending reset with it.
    public bool BlendResetPending { get; init; }
    public int ActionDefinitionId { get; init; } = -1;
    public float ClipStart { get; init; }
    public float ClipRate { get; init; } = 1;
    public int MontageId { get; init; } = -1;
    public bool OwnsActiveActionLookup { get; init; }
    public int AdditiveType { get; init; }
    public float RateScale { get; init; } = 1;
    public bool InertialBlendOut { get; init; }
    public float EffectivePlayRate => PlayRate * RateScale;
    public ImmutableArray<AlsMontageTrack> AdditionalTracks { get; init; }
    public float BlendStartAlpha { get; init; }
    public int ActiveBlendProfileId { get; init; } = -1;
    public int BlendOutProfileId { get; init; } = -1;
    public AlsMontageDeltaTimeRecord DeltaTimeRecord { get; init; }
    public float PreviousWeight {get;init;}
    public float NotifyWeight {get;init;}
    public float ClipEnd { get; init; } = -1;
}
// The proxy freezes the complete blend, including linear alpha before the last
// range change. Per-bone profiles cannot reconstruct this from CurrentWeight.
public readonly record struct AlsMontageBlendSnapshot(float Alpha, float BeginWeight,
    float DesiredWeight, float StartAlpha, AlsActionBlendOption Option, int ProfileId = -1);
// Physical montage movement before the section-end pose offset. Stopped
// instances retain this record; zero-delta ticks reset it only while playing.
public readonly record struct AlsMontageDeltaTimeRecord(float PreviousPosition, float Delta);
public readonly record struct AlsMontageEvaluation(long InstanceId, int AnimationId, AlsMontageSlot Slot,
    float Position, float Weight, int ActionDefinitionId = -1)
{
    public int AdditiveType { get; init; }
    public AlsMontageBlendSnapshot BlendSnapshot { get; init; } = new(0, 0, 0, 0, AlsActionBlendOption.Linear);
    public float MontagePosition { get; init; }
    public AlsMontageDeltaTimeRecord DeltaTimeRecord { get; init; }
}
public readonly record struct AlsMontageTraversal(long InstanceId, int AnimationId, AlsMontageSlot Slot,
    float PreviousPosition, float CurrentPosition, float NotifyWeight, bool Interrupted, bool Terminated, int ActionDefinitionId = -1);
public enum AlsMontageEventPhase { Requests, Weight, Advance, PostTick }
public readonly record struct AlsMontageEventProduction(AlsMontageEvent Event,AlsMontageEventPhase Phase);

public readonly record struct AlsMontageRootMotionRange(AlsFrameIdentity Identity, long InstanceId,
    int AnimationId, float StartSeconds, float EndSeconds)
{
    public bool HasMotion => InstanceId > 0;
}

// A bank survives Commit and remains readable while the next candidate is prepared.
// Consumers retain Identity and reject a bank recycled for a later frame.
public sealed class AlsMontageFrame
{
    internal readonly Dictionary<int,AlsMontageInertialRequest> InertialRequests = new();
    public bool TryGetInertializationRequest(int groupId, out AlsMontageInertialRequest request) => InertialRequests.TryGetValue(groupId,out request);
    internal AlsMontageEvaluation[] Entries = new AlsMontageEvaluation[8];
    internal int Count;
    public AlsFrameIdentity Identity { get; internal set; }
    public ReadOnlySpan<AlsMontageEvaluation> Evaluations => Entries.AsSpan(0, Count);
    public AlsSlotWeights SlotWeights(AlsMontageSlot slot)
    {
        if (!slot.IsValid) throw new ArgumentOutOfRangeException(nameof(slot));
        var total = 0f; var nonAdditive = 0f;
        foreach (var entry in Evaluations)
            if (entry.Slot == slot) { total += entry.Weight; if (entry.AdditiveType == 0) nonAdditive += entry.Weight; }
        // GetSlotWeight normalizes the non-additive subtotal by the complete
        // montage weight. Additive-only slots keep their entire source pose.
        if (total > 1) nonAdditive /= total;
        return new(1 - nonAdditive, MathF.Min(total, 1), total);
    }
}

// Exclusive per-character owner for one-section montages, both
// transient sequence montages and authored actions. Begin models UE's pre-Blueprint tick and freezes pose
// evaluation data. Play happens afterwards; newly created instances tick next frame.
// Arrays grow only when overlap capacity is first exceeded; no old instance is evicted.
public sealed partial class AlsMontageRuntime
{
    // Mutable AnimInstance requests and the proxy snapshot have distinct lifetimes.
    private Dictionary<int,AlsMontageInertialRequest> _requests = new(), _committedRequests = new();
    private readonly Dictionary<(int, AlsTurnSlot), AlsDynamicMontageAsset> _assets;
    private readonly Dictionary<int, AlsAuthoredMontageAsset> _actions;
    private readonly Dictionary<(int, AlsMontageSlot), AlsSequenceMontageAsset> _sequences;
    private AlsMontageInstance[] _committed = new AlsMontageInstance[8], _candidate = new AlsMontageInstance[8];
    private AlsMontageFrame _committedFrame = new(), _frame = new();
    private AlsMontageTraversal[] _traversal = new AlsMontageTraversal[8];
    private AlsMontageTraversal[] _notifyTraversal = new AlsMontageTraversal[8];
    private int _notifyTraversalCount;
    private AlsTurnSlotObservation[] _observations = new AlsTurnSlotObservation[8];
    private int _committedCount, _count, _evaluationCount, _traversalCount;
    private long _committedSerial, _serial;
    private long _committedRootMotionInstance, _rootMotionInstance;
    private AlsMontageRootMotionRange _rootMotionRange;
    public long CommittedRootMotionInstance => _dispatchingEvents ? _rootMotionInstance : _committedRootMotionInstance;
    public long CandidateRootMotionInstance { get { RequirePrepared(); return _rootMotionInstance; } }
    public AlsMontageRootMotionRange RootMotionRange { get { RequirePrepared(); return _rootMotionRange; } }
    private AlsFrameIdentity _identity;
    private bool _prepared;
    private AlsMontageEventQueue _committedEvents=new();
    private AlsMontageEventQueue _candidateEvents=new();
    private readonly bool _captureMontageEvents;
    private readonly List<AlsMontageEvent> _occurrences=[];
    private readonly List<AlsMontageEventProduction> _eventProduction=[];
    private AlsMontageEventPhase _eventPhase;
    // AnimInstance owns this phase independently of whether its bank is empty.
    public bool MontageEventsQueued => (_prepared?_candidateEvents!:_committedEvents).IsQueuing;
    public bool CommittedMontageEventsQueued => _committedEvents.IsQueuing;
    public AlsMontageEvent[] ImmediateMontageEvents { get { RequirePrepared();return _candidateEvents!.Immediate; } }
    public AlsMontageEvent[] MontageEventOccurrences { get { RequirePrepared();return _occurrences.ToArray(); } }
    public AlsMontageEventProduction[] MontageEventProduction {get{RequirePrepared();return _eventProduction.ToArray();}}
    public AlsMontageEvent[] QueuedMontageEvents(AlsMontageEventKind kind) =>
        (_prepared?_candidateEvents!:_committedEvents).Queued(kind);
    public void DeliverImmediateMontageEvents(Action<AlsMontageEvent>? observer=null)
    {RequirePrepared();RequireMontageTransactionControl();DeliverImmediateEvents(_candidateEvents,observer);}
    public void AcknowledgeImmediateMontageEvents(ReadOnlySpan<AlsMontageEvent> delivered)
    {
        RequirePrepared();
        RequireMontageMutation();
        RequireMontageTransactionControl();
        if(!delivered.SequenceEqual(_candidateEvents!.Immediate))throw new InvalidOperationException("Physical Montage event receipt differs on animation retry.");
        _candidateEvents.AcknowledgeImmediate();
    }
    public void DispatchMontageEventCallbacks(Action<AlsMontageEvent>? observer=null)
    {
        DispatchCommittedEvents(observer);
    }
    private bool _beforeAdvanceHasInstances;
    private ulong _preparationSerial;
    private readonly AlsMantlingBranchingRuntime? _branching;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ReadOnlySpan<AlsMontageInstance> Committed => _dispatchingEvents ? _candidate.AsSpan(0,_count) : _committed.AsSpan(0, _committedCount);
    public ReadOnlySpan<AlsMontageInstance> Candidate { get { RequirePrepared(); return _candidate.AsSpan(0, _count); } }
    public AlsMontageFrame Frame { get { RequirePrepared(); return _frame; } }
    // A cancelled/retried identity can reproduce the same values in the reused
    // frame buffer. Captured consumers must still reject the discarded lifetime.
    public ulong PreparationSerial { get { RequirePrepared(); return _preparationSerial; } }
    public ReadOnlySpan<AlsMontageEvaluation> Evaluation { get { RequirePrepared(); return _frame.Evaluations; } }
    public ReadOnlySpan<AlsMontageTraversal> Traversal { get { RequirePrepared(); return _traversal.AsSpan(0, _traversalCount); } }
    // Ordered HandleEvents ranges before post-movement branching Tick. The action
    // Traversal above remains one whole-frame summary per physical instance.
    public ReadOnlySpan<AlsMontageTraversal> NotifyTraversal { get { RequirePrepared(); return _notifyTraversal.AsSpan(0,_notifyTraversalCount); } }
    public bool TryGetActionAsset(int definitionId, out AlsAuthoredMontageAsset asset) => _actions.TryGetValue(definitionId, out asset);
    public bool TryGetSequenceAsset(int animationId, AlsMontageSlot slot, out AlsSequenceMontageAsset asset) => _sequences.TryGetValue((animationId, slot), out asset);

    public AlsMontageRuntime(ReadOnlySpan<AlsDynamicMontageAsset> assets, ReadOnlySpan<AlsAuthoredMontageAsset> actions = default,
        ReadOnlySpan<AlsSequenceMontageAsset> sequences = default, AlsMantlingBranchingRuntime? branching = null,
        bool captureMontageEvents = false)
    {
        _branching=branching;
        // Legacy ALS owners deliver their existing action outcomes. The Lyra
        // AnimInstance adapter explicitly owns the new four-container dispatch.
        _captureMontageEvents=captureMontageEvents;
        _assets = new();
        foreach (var asset in assets)
        {
            if (asset.AnimationId < 0 || (uint)asset.Slot > 1 || asset.GroupId < 0 || !float.IsFinite(asset.Duration) || asset.Duration <= .00005f ||
                !_assets.TryAdd((asset.AnimationId, asset.Slot), asset))
                throw new ArgumentException("Invalid dynamic montage asset.");
        }
        _sequences = new();
        foreach (var asset in sequences)
        {
            if (asset.AnimationId < 0 || !asset.Slot.IsValid || asset.GroupId < 0 ||
                !float.IsFinite(asset.Duration) || asset.Duration <= .00005f || (uint)asset.AdditiveType > 2 ||
                asset.Slot.Id < 2 && _assets.ContainsKey((asset.AnimationId, (AlsTurnSlot)asset.Slot.Id)) ||
                !_sequences.TryAdd((asset.AnimationId, asset.Slot), asset))
                throw new ArgumentException("Invalid or duplicate sequence montage asset.");
        }
        _actions = new();
        var nativeAssets = new Dictionary<int, AlsAuthoredMontageAsset>();
        foreach (var action in actions)
        {
            if (action.ActionDefinitionId < 0 || action.MontageId < -1 || action.BlendInProfileId < -1 || action.BlendOutProfileId < -1 || action.AnimationId < 0 || !action.Slot.IsValid || action.GroupId < 0 || (uint)action.AdditiveType > 2 ||
                !float.IsFinite(action.Duration) || action.Duration <= .00005f || !float.IsFinite(action.ClipStart) || action.ClipStart < 0 ||
                action.ClipEnd != -1 && (!float.IsFinite(action.ClipEnd) || action.ClipEnd <= action.ClipStart) ||
                !float.IsFinite(action.ClipRate) || action.ClipRate <= 0 || !float.IsFinite(action.ClipStart + action.Duration * action.ClipRate) ||
                !float.IsFinite(action.RateScale) || action.RateScale <= 0 ||
                action.Lifecycle.Mode == AlsActionLifecycleMode.LegacySectionEnd || !AlsActionLifecycle.IsValid(action.Lifecycle) ||
                !_actions.TryAdd(action.ActionDefinitionId, action)) throw new ArgumentException("Invalid authored montage asset.");
            var nativeId = action.MontageId >= 0 ? action.MontageId : action.ActionDefinitionId;
            var canonical = action with { MontageId = nativeId, ActionDefinitionId = 0 };
            var slots = new HashSet<int> { action.Slot.Id };
            foreach (var track in action.AdditionalTracks.IsDefault ? [] : action.AdditionalTracks)
                if (track.AnimationId < 0 || !track.Slot.IsValid || !slots.Add(track.Slot.Id) ||
                    !float.IsFinite(track.ClipStart) || track.ClipStart < 0 || !float.IsFinite(track.ClipRate) || track.ClipRate <= 0 ||
                    track.ClipEnd != -1 && (!float.IsFinite(track.ClipEnd) || track.ClipEnd <= track.ClipStart) ||
                    !float.IsFinite(track.ClipStart + action.Duration * track.ClipRate) || (uint)track.AdditiveType > 2)
                    throw new ArgumentException("Invalid or repeated authored Montage track.");
            if (nativeAssets.TryGetValue(nativeId, out var existing) &&
                ((existing with { AdditionalTracks = default }) != (canonical with { AdditionalTracks = default }) ||
                 !existing.AdditionalTracks.AsSpan().SequenceEqual(canonical.AdditionalTracks.AsSpan())))
                throw new ArgumentException("Action aliases disagree about their authored montage.");
            nativeAssets[nativeId] = canonical;
        }
        _branching?.Attach(actions);
    }

    public void Begin(AlsFrameIdentity identity, float delta, bool ragdoll = false,
        AlsMontagePositionOverride? positionOverride = null)
    {
        // Do not discard an already prepared frame on a caller phase error.
        if(_prepared||_dispatchingEvents)throw new ArgumentException("A montage frame or event dispatch is already active.");
        try{BeginCore(identity,delta,ragdoll,positionOverride);}
        catch{Discard();throw;}
    }

    // An independently ticking AnimInstance can receive Montage_Play from its
    // prerequisite component before advancing. Requests affect only the working
    // frame; cancellation retains committed instances and their serials.
    public void BeginWithActionRequests(AlsFrameIdentity identity,float delta,ReadOnlySpan<AlsMontageActionRequest> requests,
        ReadOnlySpan<AlsMontageStopRequest> stops=default,Action<AlsMontageRuntime>? beforeWeight=null)
    {
        if(_prepared||_dispatchingEvents)throw new ArgumentException("A montage frame or event dispatch is already active.");
        try{BeginCore(identity,delta,false,null,requests,stops,beforeWeight);}
        catch{Discard();throw;}
    }

    private void BeginCore(AlsFrameIdentity identity, float delta, bool ragdoll, AlsMontagePositionOverride? positionOverride,
        ReadOnlySpan<AlsMontageActionRequest> beforeAdvance=default,ReadOnlySpan<AlsMontageStopRequest> beforeStops=default,
        Action<AlsMontageRuntime>? beforeWeight=null)
    {
        if (_prepared || identity.SlotGeneration == 0 || !float.IsFinite(delta) || delta < 0 ||
            CommittedIdentity != default && (identity.SlotGeneration != CommittedIdentity.SlotGeneration ||
                identity.CharacterId != CommittedIdentity.CharacterId || identity.FrameId <= CommittedIdentity.FrameId))
            throw new ArgumentException("Invalid montage frame identity, phase or delta.");
        if (positionOverride is { } seek)
        {
            if (seek.InstanceId <= 0 || !float.IsFinite(seek.Position) || seek.Position < 0)
                throw new ArgumentException("Invalid montage position override.");
            var found = false;
            foreach (var instance in Committed)
                if (instance.InstanceId == seek.InstanceId)
                {
                    if (instance.Slot != AlsMontageSlot.PostLocomotion || instance.Interrupted ||
                        !instance.Playing || seek.Position > instance.Duration)
                        throw new ArgumentException("Mantle seek requires its live physical PostLocomotion instance.");
                    found = true;
                }
            if (!found) throw new ArgumentException("Expired montage position override.");
        }
        _branching?.Begin(identity);
        _candidateEvents.CopyCandidateFrom(_committedEvents);_occurrences.Clear();_eventProduction.Clear();_eventPhase=AlsMontageEventPhase.Requests;
        CopyCandidateEventBindings();
        _requests.Clear();foreach(var request in _committedRequests)_requests.Add(request.Key,request.Value);
        Ensure(_committedCount); _count = _evaluationCount = _traversalCount = _notifyTraversalCount = 0;
        _identity = identity; _serial = _committedSerial;
        _rootMotionInstance = _committedRootMotionInstance; _rootMotionRange = new(identity, 0, -1, 0, 0);
        if (ragdoll) _rootMotionInstance = 0;
        Array.Copy(_committed,_candidate,_committedCount);_count=_committedCount;_prepared=true;
        if(!beforeAdvance.IsEmpty||!beforeStops.IsEmpty)
        {
            foreach(var request in beforeAdvance)
                if(!PlayAction(request.ActionDefinitionId,request.PlayRate,request.StartTime,request.StopGroup))
                    throw new ArgumentException("Unbound pre-tick Montage request.");
            foreach(var stop in beforeStops)
            {
                if(!_actions.TryGetValue(stop.ActionDefinitionId,out var stoppedAsset)||!float.IsFinite(stop.BlendOutSeconds)||stop.BlendOutSeconds<0)
                    throw new ArgumentException("Invalid pre-tick Montage stop.");
                var stoppedMontage=stoppedAsset.MontageId>=0?stoppedAsset.MontageId:stoppedAsset.ActionDefinitionId;
                for(var stopIndex=_count-1;stopIndex>=0;stopIndex--)
                    if(_candidate[stopIndex].MontageId==stoppedMontage&&_candidate[stopIndex].OwnsActiveActionLookup)
                    {Stop(stopIndex,stop.BlendOutSeconds,_candidate[stopIndex].Settings.BlendOutOption);break;}
            }
        }
        // Configure candidate bindings on new plays before their first event.
        // This hook must not publish external effects.
        beforeWeight?.Invoke(this);
        _beforeAdvanceHasInstances=_count>0;
        // UE updates all instance weights before Montage_Advance enables queuing.
        _eventPhase=AlsMontageEventPhase.Weight;
        for(var i=0;i<_count;i++)
        {
            var state=_candidate[i];
            // Native Montage_SetPosition precedes montage Advance. Seeking does
            // not emit notifies for the skipped interval or change blend time.
            if (!ragdoll && positionOverride is { } overridePosition && overridePosition.InstanceId == state.InstanceId)
                state = state with { Position = overridePosition.Position };
            if (ragdoll && state.Blend.DesiredWeight > 0)
            {
                Stop(i,.2f,state.Settings.BlendOutOption);state=_candidate[i];
            }
            var blend = state.Blend;state=state with{PreviousWeight=blend.CurrentWeight};
            bool wasComplete=blend.CurrentWeight==blend.DesiredWeight;
            if (state.BlendResetPending)
            {
                AlsActionLifecycle.ResetRange(state.BlendTime,
                    blend.BlendingOut == 1 ? state.Settings.BlendOutOption : state.Settings.BlendInOption, ref blend);
                state = state with { BlendResetPending = false };
            }
            AlsActionLifecycle.AdvanceWeight(state.Settings, delta, ref blend);
            state=state with{Blend=blend};_candidate[i]=state;
            if(blend.BlendingOut==0&&!wasComplete&&blend.CurrentWeight==blend.DesiredWeight)
                EmitMontageEvent(state,AlsMontageEventKind.BlendedIn);
            // This callback can stop or replay a physical instance. Freeze the
            // notify weight now, before later instance callbacks change it.
            state=_candidate[i];_candidate[i]=state with{NotifyWeight=MathF.Max(state.PreviousWeight,state.Blend.CurrentWeight)};
        }
        _beforeAdvanceHasInstances=_count>0;
        _candidateEvents.BeginQueueing();_eventPhase=AlsMontageEventPhase.Advance;
        Span<float> notifyMarkers=stackalloc float[4];
        for(var i=0;i<_count;)
        {
            var state=_candidate[i];var blend=state.Blend;
            var extractMotion=state.InstanceId==_rootMotionInstance&&state.Playing;
            var previous = state.Position; var position = previous; var playing = state.Playing;
            var deltaRecord = playing ? new AlsMontageDeltaTimeRecord(previous, 0) : state.DeltaTimeRecord;
            var traversalEnd = position;
            // With no evaluation time UE gathers active states, but never enters
            // the substep loop (including its automatic blend-out boundary check).
            if (playing && delta > 0)
            {
                var rate = state.EffectivePlayRate;
                var move = delta * rate;
                if (!float.IsFinite(move)) throw new ArgumentException("Montage time overflow.");
                var forward = rate > 0;
                var boundary = forward ? state.Duration : 0;
                var deltaMove = forward ? MathF.Min(move, state.Duration - previous) : MathF.Max(move, -previous);
                deltaRecord = deltaRecord with { Delta = deltaMove };
                // Native clamps the substep movement before adding it. The
                // rounded sum can remain one ULP inside the section, affecting
                // the automatic fade duration even when the boundary was hit.
                var reachedBoundary = forward ? move >= state.Duration - previous : move <= -previous;
                position += deltaMove; traversalEnd = position;
                var remaining = MathF.Abs(rate) <= 1e-8f ? float.MaxValue : MathF.Abs((boundary - position) / rate);
                var wasStopped = blend.BlendingOut == 1;
                var startAlpha = blend.Alpha;
                AlsActionLifecycle.TryBeginBlendOut(state.Settings, remaining, ref blend);
                if (!wasStopped && blend.BlendingOut == 1)
                {
                    state = state with { BlendTime = state.Settings.BlendOutTriggerSeconds >= 0 ? state.Settings.BlendOutSeconds : remaining,
                        BlendStartAlpha = startAlpha, ActiveBlendProfileId = state.BlendOutProfileId };
                    if(state.InertialBlendOut)
                    {
                        QueueInertialization(state.GroupId,state.BlendTime,state.Settings.BlendOutOption);
                        AlsActionLifecycle.Stop(0,state.Settings.BlendOutOption,ref blend);state=state with{BlendTime=0};
                    }
                    EmitMontageEvent(state,AlsMontageEventKind.BlendingOut,state.Interrupted);
                    if (_rootMotionInstance == state.InstanceId) _rootMotionInstance = 0;
                }
                if (move != 0 && reachedBoundary)
                {
                    playing = false;
                    position = forward ? state.Duration - .00005f : 0;
                }
                if (blend.BlendingOut == 1 && state.BlendTime <= 0) playing = false;
            }
            var terminated = AlsActionLifecycle.IsComplete(blend);
            state = state with { Position = position, Playing = playing, Blend = blend,
                DeltaTimeRecord = deltaRecord,
                OwnsActiveActionLookup = state.OwnsActiveActionLookup && blend.DesiredWeight > 0 };
            // HandleEvents collects queues before the post-movement branching Tick.
            // Keep that interruption state and each marker's context time even if
            // EarlyBlendOut stops this instance later in the same frame.
            var notifyInterrupted=state.Interrupted;
            var notifyMarkerCount=!notifyInterrupted&&_branching is not null
                ?_branching.CopyCrossedMarkers(state.ActionDefinitionId,previous,traversalEnd,notifyMarkers):0;
            if(_branching is not null)
            {
                var stopSeconds=_branching.Advance(state,previous,traversalEnd,terminated);
                if(stopSeconds>=0)
                {
                    bool firstStop=state.Blend.BlendingOut==0;
                    bool inertialStop=firstStop&&state.InertialBlendOut;
                    state=StopInstance(state,stopSeconds,state.Settings.BlendOutOption);
                    if(_rootMotionInstance==state.InstanceId)_rootMotionInstance=0;
                    if(firstStop)EmitMontageEvent(state,AlsMontageEventKind.BlendingOut,true);
                    if(inertialStop)QueueInertialization(state.GroupId,stopSeconds,state.Settings.BlendOutOption);
                    blend=state.Blend;
                }
            }
            if (extractMotion && previous != traversalEnd)
            {
                var motionTrack = new AlsMontageTrack(state.AnimationId,state.Slot,state.ClipStart,state.ClipRate,state.AdditiveType) { ClipEnd=state.ClipEnd };
                _rootMotionRange = new(identity, state.InstanceId, state.AnimationId,
                    motionTrack.SamplePosition(previous), motionTrack.SamplePosition(traversalEnd));
            }
            _traversal[_traversalCount++] = new(state.InstanceId,state.AnimationId,state.Slot,
                previous,traversalEnd,state.NotifyWeight,state.Interrupted,terminated,state.ActionDefinitionId);
            if(_notifyTraversalCount+notifyMarkerCount+1>_notifyTraversal.Length)
                Array.Resize(ref _notifyTraversal,System.Math.Max(_notifyTraversal.Length*2,_notifyTraversalCount+notifyMarkerCount+1));
            var notifyPrevious=previous;
            for(var marker=0;marker<=notifyMarkerCount;marker++)
            {
                var notifyEnd=marker<notifyMarkerCount?notifyMarkers[marker]:traversalEnd;
                _notifyTraversal[_notifyTraversalCount++] = new(state.InstanceId, state.AnimationId, state.Slot,
                    notifyPrevious, notifyEnd, state.NotifyWeight, notifyInterrupted,
                    terminated&&marker==notifyMarkerCount, state.ActionDefinitionId);
                notifyPrevious=notifyEnd;
            }
            _candidate[i]=state;
            if (terminated)
            {
                EmitMontageEvent(state,AlsMontageEventKind.Ended,state.Interrupted);RemoveCandidateEventBindings(state.InstanceId);
                Array.Copy(_candidate,i+1,_candidate,i,_count-i-1);_candidate[--_count]=default;
                continue;
            }
            if (blend.CurrentWeight > AlsPoseBlender.WeightThreshold)
            {
                Ensure(_evaluationCount + 1 + (state.AdditionalTracks.IsDefault ? 0 : state.AdditionalTracks.Length));
                var snapshot = new AlsMontageBlendSnapshot(blend.Alpha, blend.BeginWeight, blend.DesiredWeight,
                    state.BlendStartAlpha, blend.BlendingOut == 1 ? state.Settings.BlendOutOption : state.Settings.BlendInOption,
                    state.ActiveBlendProfileId);
                _frame.Entries[_evaluationCount++] = new(state.InstanceId, state.AnimationId, state.Slot,
                    new AlsMontageTrack(state.AnimationId,state.Slot,state.ClipStart,state.ClipRate,state.AdditiveType)
                        { ClipEnd=state.ClipEnd }.SamplePosition(position), blend.CurrentWeight, state.ActionDefinitionId)
                    { AdditiveType = state.AdditiveType, BlendSnapshot = snapshot, MontagePosition = position, DeltaTimeRecord = deltaRecord };
                foreach (var track in state.AdditionalTracks.IsDefault ? [] : state.AdditionalTracks)
                    _frame.Entries[_evaluationCount++] = new(state.InstanceId, track.AnimationId, track.Slot,
                        track.SamplePosition(position), blend.CurrentWeight, state.ActionDefinitionId)
                        { AdditiveType = track.AdditiveType, BlendSnapshot = snapshot, MontagePosition = position, DeltaTimeRecord = deltaRecord };
            }
            i++;
        }
        _frame.Count = _evaluationCount; _frame.Identity = identity;
        _frame.InertialRequests.Clear();foreach(var request in _requests)_frame.InertialRequests.Add(request.Key,request.Value);
        _requests.Clear();
        _preparationSerial=checked(_preparationSerial+1);
        _eventPhase=AlsMontageEventPhase.PostTick;_prepared = true;
    }

    public ReadOnlySpan<AlsTurnSlotObservation> Observations
    {
        get
        {
            RequirePrepared();
            for (var i = 0; i < _count; i++)
            {
                var item = _candidate[i];
                _observations[i] = new(item.Blend.DesiredWeight != 0, item.Playing, item.ActionDefinitionId < 0,
                    item.Slot.Id < 2, item.Slot.Id < 2 ? (AlsTurnSlot)item.Slot.Id : default, 1, item.AnimationId);
            }
            return _observations.AsSpan(0, _count);
        }
    }

    public bool Play(in AlsTurnMontageCommand command)
    {
        RequireMontageMutation();
        if (!float.IsFinite(command.PlayRate) || !float.IsFinite(command.StartTime) || !float.IsFinite(command.BlendInTime) ||
            !float.IsFinite(command.BlendOutTime) || !float.IsFinite(command.BlendOutTriggerTime) || command.BlendInTime < 0 || command.BlendOutTime < 0 || command.LoopCount != 1)
            throw new ArgumentException("Invalid dynamic montage command.");
        if (!_assets.TryGetValue((command.AnimationId, command.Slot), out var asset)) return false;
        var settings = new AlsActionLifecycleSettings(AlsActionLifecycleMode.MontageAutoBlendOut,
            command.BlendInTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTriggerTime);
        Play(new(-1, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration, 0, 1, settings), command.PlayRate, command.StartTime, true);
        return true;
    }

    public bool PlayAction(int definitionId, float playRate, float startTime = 0, bool stopGroup = true)
    {
        RequireMontageMutation();
        if (!float.IsFinite(playRate) || !float.IsFinite(startTime)) throw new ArgumentException("Invalid authored montage request.");
        if (!_actions.TryGetValue(definitionId, out var asset)) return false;
        Play(asset, playRate, startTime, stopGroup);
        return true;
    }

    public bool PlaySequence(in AlsSequenceMontageCommand command)
    {
        RequireMontageMutation();
        if (!float.IsFinite(command.PlayRate) || !float.IsFinite(command.StartTime) || !float.IsFinite(command.BlendInTime) ||
            !float.IsFinite(command.BlendOutTime) || !float.IsFinite(command.BlendOutTriggerTime) ||
            command.BlendInTime < 0 || command.BlendOutTime < 0 || command.LoopCount != 1)
            throw new ArgumentException("Invalid sequence montage command.");
        if (!_sequences.TryGetValue((command.AnimationId, command.Slot), out var asset)) return false;
        var lifecycle = new AlsActionLifecycleSettings(AlsActionLifecycleMode.MontageAutoBlendOut,
            command.BlendInTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTriggerTime);
        Play(new AlsAuthoredMontageAsset(-1, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration, 0, 1, lifecycle)
            { AdditiveType = asset.AdditiveType, InertialBlendOut = command.InertialBlendOut }, command.PlayRate, command.StartTime, true);
        return true;
    }

    // Physical instance cancellation must not accidentally stop a later replay of
    // the same authored asset. Stopped instances can still have a shorter fade set.
    public bool StopInstance(long instanceId, float blendTime, AlsActionBlendOption option)
    {
        RequireMontageMutation();
        if (!float.IsFinite(blendTime) || blendTime < 0 || option is < AlsActionBlendOption.Linear or > AlsActionBlendOption.HermiteCubic)
            throw new ArgumentException("Invalid montage stop.");
        for (var i = _count - 1; i >= 0; i--)
            if (_candidate[i].InstanceId == instanceId) { Stop(i, blendTime, option); return true; }
        return false;
    }

    // ALS entry stops every slot/group with its authored blend-out option.
    public void StopForRagdoll()
    {
        RequirePrepared();
        RequireMontageMutation();
        for (var i = 0; i < _count; i++)
        {
            // Montage_Stop(nullptr) only visits IsActive (desired weight > 0).
            // Existing auto/replacement fades retain their original duration.
            if (_candidate[i].Blend.DesiredWeight <= 0) continue;
            var instance = _candidate[i].InstanceId;
            Stop(i, .2f, _candidate[i].Settings.BlendOutOption);
            for (var t = 0; t < _traversalCount; t++)
                if (_traversal[t].InstanceId == instance) _traversal[t] = _traversal[t] with { Interrupted = true };
            for (var t = 0; t < _notifyTraversalCount; t++)
                if (_notifyTraversal[t].InstanceId == instance) _notifyTraversal[t] = _notifyTraversal[t] with { Interrupted = true };
        }
        _rootMotionInstance = 0;
        _rootMotionRange = new(_identity, 0, -1, 0, 0);
    }

    public long ActiveActionInstance(int definitionId)
    {
        RequireMontageWorkingState();
        if (!_actions.TryGetValue(definitionId, out var asset)) return 0;
        var montageId = asset.MontageId >= 0 ? asset.MontageId : asset.ActionDefinitionId;
        for (var i = _count - 1; i >= 0; i--)
            if (_candidate[i].MontageId == montageId && _candidate[i].OwnsActiveActionLookup)
                return _candidate[i].InstanceId;
        return 0;
    }

    // UAnimInstance::Montage_IsPlaying uses the active asset lookup, not every
    // still-playing outgoing fade. Automatic blend-out removes that lookup.
    public bool IsActionPlaying(int definitionId)
    {
        var id = ActiveActionInstance(definitionId);
        if (id == 0) return false;
        for (var i = 0; i < _count; i++)
            if (_candidate[i].InstanceId == id) return _candidate[i].Playing;
        return false;
    }

    // UAnimInstance::IsAnyMontagePlaying is an instance-presence query, unlike
    // Montage_IsPlaying(nullptr). Stopped and paused instances still count.
    public bool IsAnyMontagePlaying
    {
        get
        {
            RequireMontageWorkingState();
            return _count > 0;
        }
    }

    public bool IsAnyMontagePlayingBeforeAdvance
    {
        get { RequirePrepared(); return _beforeAdvanceHasInstances; }
    }

    // UAlsMontageUtility::StopMontagesWithSlot visits only IsActive instances.
    // Negative duration preserves each asset's authored blend-out setting.
    public void StopSlots(ReadOnlySpan<AlsMontageSlot> slots, float blendTime = -1)
    {
        RequireMontageMutation();
        if (!float.IsFinite(blendTime)) throw new ArgumentException("Invalid slot stop duration.");
        foreach (var slot in slots)
            if (!slot.IsValid) throw new ArgumentException("Invalid montage stop slot.");
        foreach (var slot in slots)
        for (var i = 0; i < _count; i++)
        {
            var instance = _candidate[i];
            if (instance.Blend.DesiredWeight <= 0 || instance.Slot != slot &&
                (instance.AdditionalTracks.IsDefaultOrEmpty || !instance.AdditionalTracks.Any(t => t.Slot == slot))) continue;
            Stop(i, blendTime >= 0 ? blendTime : instance.Settings.BlendOutSeconds, instance.Settings.BlendOutOption);
        }
    }

    private void Play(in AlsAuthoredMontageAsset asset, float playRate, float startTime, bool stopGroup)
    {
        if (!float.IsFinite(playRate * asset.RateScale)) throw new ArgumentException("Montage effective rate overflow.");
        if (_serial == long.MaxValue) throw new InvalidOperationException("Montage instance identity exhausted.");
        Ensure(_count + 1);
        var montageId = asset.ActionDefinitionId < 0 ? -1 : asset.MontageId >= 0 ? asset.MontageId : asset.ActionDefinitionId;
        // UE stops all instances in the group in reverse order, including old fades.
        for (var i = _count - 1; i >= 0; i--)
        {
            if (stopGroup && _candidate[i].GroupId == asset.GroupId)
                Stop(i, asset.Lifecycle.BlendInSeconds, asset.Lifecycle.BlendInOption, false);
            // ActiveMontagesMap selects the newest play of the authored asset.
            // Removing that entry later does not reactivate an older instance.
            if (montageId >= 0 && _candidate[i].MontageId == montageId)
                _candidate[i] = _candidate[i] with { OwnsActiveActionLookup = false };
        }
        // Synchronous outgoing delegates may have created further instances.
        Ensure(_count+1);
        _candidate[_count++] = new(++_serial, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration,
            System.Math.Clamp(startTime, 0, asset.Duration), playRate, asset.Lifecycle.BlendInSeconds,
            // Native FAlphaBlend starts with a 0.2 second timer. Play configures
            // its requested duration but does not update/reset it this frame.
            asset.Lifecycle, new AlsActionLifecycleState { DesiredWeight = 1, RemainingSeconds = .2f }, true, false)
            { BlendResetPending = true, ActionDefinitionId = asset.ActionDefinitionId, ClipStart = asset.ClipStart, ClipRate = asset.ClipRate,
                MontageId = montageId, OwnsActiveActionLookup = montageId >= 0, AdditiveType = asset.AdditiveType, RateScale = asset.RateScale,
                InertialBlendOut = asset.InertialBlendOut, AdditionalTracks = asset.AdditionalTracks,
                ActiveBlendProfileId = asset.BlendInProfileId, BlendOutProfileId = asset.BlendOutProfileId, ClipEnd=asset.ClipEnd };
        if (asset.RootMotionEnabled) _rootMotionInstance = _serial;
    }

    private void Stop(int index, float seconds, AlsActionBlendOption option, bool useAssetBlendMode = true)
    {
        var old = _candidate[index];
        if (_rootMotionInstance == old.InstanceId) _rootMotionInstance = 0;
        _candidate[index]=StopInstance(old,seconds,option,useAssetBlendMode);
        // Publish the stop before invoking its delegate, and do not overwrite
        // a nested stop/replay when that delegate returns.
        if(old.Blend.BlendingOut==0)
        {
            // Even a zero-duration Stop leaves bPlaying intact during the
            // instance delegate and global broadcast. UE clears it afterwards.
            _candidate[index]=_candidate[index] with{Playing=old.Playing};
            EmitMontageEvent(_candidate[index],AlsMontageEventKind.BlendingOut,true);
            if(useAssetBlendMode&&old.InertialBlendOut)QueueInertialization(old.GroupId,seconds,option);
            if(_candidate[index].BlendTime<=0)_candidate[index]=_candidate[index] with{Playing=false};
        }
    }
    private AlsMontageInstance StopInstance(in AlsMontageInstance old,float seconds,AlsActionBlendOption option,bool useAssetBlendMode = true)
    {
        var blend = old.Blend; var duration = old.BlendTime;
        var pending = old.BlendResetPending;
        var settings = old.Settings;
        var startAlpha = old.BlendStartAlpha;
        var profileId = old.ActiveBlendProfileId;
        if (blend.BlendingOut == 0)
        {
            startAlpha = blend.Alpha;
            profileId = old.BlendOutProfileId;
            var inertial=useAssetBlendMode&&old.InertialBlendOut;
            duration = inertial?0:seconds;
            // First Stop calls Blend.Update(0) in UE.
            AlsActionLifecycle.Stop(duration, option, ref blend);
            settings = settings with { BlendOutOption = option };
            pending = false;
        }
        else if (seconds < duration)
        {
            // Shortening an already stopped montage only sets the range;
            // its observable alpha, weight and remaining time stay intact.
            duration = seconds;
            startAlpha = blend.Alpha;
            blend.BeginWeight = blend.CurrentWeight;
            pending = true;
        }
        return old with { Blend = blend, BlendTime = duration, BlendResetPending = pending, Settings = settings,
            BlendStartAlpha = startAlpha, ActiveBlendProfileId = profileId,
            Interrupted = true, Playing = old.Playing && duration > 0, OwnsActiveActionLookup = false };
    }

    public AlsSlotWeights SlotWeights(AlsMontageSlot slot)
    {
        RequirePrepared(); return _frame.SlotWeights(slot);
    }
    private void EmitMontageEvent(in AlsMontageInstance instance,AlsMontageEventKind kind,bool interrupted=false)
    {
        if(!_captureMontageEvents)return;
        var value=new AlsMontageEvent(kind,instance.InstanceId,instance.ActionDefinitionId,instance.MontageId,interrupted);
        if(_prepared){_occurrences.Add(value);_eventProduction.Add(new(value,_eventPhase));}
        _candidateEventBindings.TryGetValue((instance.InstanceId,kind),out var callback);
        MontageEventQueue.Emit(value,callback.Effect,callback.Update);
    }
    private void QueueInertialization(int group,float duration,AlsActionBlendOption option)
    {
        // Montage arbitration uses the last request per group, not the shortest.
        _requests[group]=new(duration,option);
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { RequirePrepared(); RequireMontageMutation(); RequireMontageTransactionControl(); if (identity != _identity) throw new ArgumentException("Foreign montage commit."); _branching?.ValidateCommit(identity); }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); _branching?.Commit(); (_committed, _candidate) = (_candidate, _committed);
        (_committedFrame, _frame) = (_frame, _committedFrame);
        _committedCount = _count; _committedSerial = _serial; CommittedIdentity = identity; _prepared = false;
        _committedRootMotionInstance = _rootMotionInstance;
        (_committedEvents,_candidateEvents)=(_candidateEvents,_committedEvents);_committedEvents.Publish();
        (_committedEventBindings,_candidateEventBindings)=(_candidateEventBindings,_committedEventBindings);
        (_committedRequests,_requests)=(_requests,_committedRequests);
    }
    public void Discard() { RequireMontageTransactionControl();if(_dispatchingEvents||_deliveringImmediateEvents)throw new InvalidOperationException("Cannot discard a callback's physical state.");_branching?.Discard(); _prepared = false; _candidateEvents.Clear();_candidateEventBindings.Clear();_occurrences.Clear();_eventProduction.Clear(); _count = _evaluationCount = _traversalCount = _notifyTraversalCount = 0; _frame.Count = 0; _frame.Identity = default; _frame.InertialRequests.Clear();_requests.Clear(); }
    // Resource notifies have already run. UE clears the phase before invoking
    // queued montage delegates, including when there are no queued events.
    public void DispatchQueuedMontageEvents(Action? triggerEvents = null)
    {
        DispatchCommittedEvents(null);
        triggerEvents?.Invoke();
    }
    public void ClearForLifecycle()
    {
        if (_prepared||_dispatchingEvents) throw new InvalidOperationException("Complete the montage candidate/dispatch before lifecycle cleanup.");
        _branching?.Clear();
        _committedEvents.Clear();
        _candidateEvents.Clear();_committedEventBindings.Clear();_candidateEventBindings.Clear();
        Array.Clear(_committed); Array.Clear(_candidate);
        _committedCount = _count = _evaluationCount = _traversalCount = _notifyTraversalCount = 0;
        _committedRootMotionInstance = _rootMotionInstance = 0; _rootMotionRange = default;
        _committedFrame.Count = _frame.Count = 0;
        _requests.Clear();_committedRequests.Clear();_frame.InertialRequests.Clear();_committedFrame.InertialRequests.Clear();
        // Keep the frame boundary and serial allocator: resuming this generation
        // must never reuse a physical playback identity.
    }
    private void RequirePrepared() { if (!_prepared) throw new InvalidOperationException("No prepared montage frame."); }
    private void Ensure(int count)
    {
        if (count <= _candidate.Length && count <= _frame.Entries.Length && count <= _traversal.Length) return;
        var capacity = System.Math.Max(count, System.Math.Max(_candidate.Length, _frame.Entries.Length) * 2);
        Array.Resize(ref _candidate, capacity); Array.Resize(ref _frame.Entries, capacity);
        Array.Resize(ref _traversal, capacity); Array.Resize(ref _observations, capacity);
    }
}
