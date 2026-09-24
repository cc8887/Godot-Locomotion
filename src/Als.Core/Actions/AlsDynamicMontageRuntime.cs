using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsDynamicMontageAsset(int AnimationId, AlsTurnSlot Slot, int GroupId, float Duration);
public readonly record struct AlsSequenceMontageAsset(int AnimationId, AlsMontageSlot Slot, int GroupId, float Duration, int AdditiveType);
public readonly record struct AlsSequenceMontageCommand(int AnimationId, AlsMontageSlot Slot, float PlayRate,
    float StartTime, float BlendInTime, float BlendOutTime, int LoopCount = 1, float BlendOutTriggerTime = 0);
// These are graph slot identities, not montage-local slot array indices.
public readonly record struct AlsMontageSlot(int Id)
{
    public static AlsMontageSlot BaseLayer => new(2);
    public static AlsMontageSlot Grounded => new(3);
    public static implicit operator AlsMontageSlot(AlsTurnSlot slot) => new((int)slot);
}

// Single-section, single segment; authored actions and transient
// sequences share the same physical instance clock, group arbitration and fades.
public readonly record struct AlsAuthoredMontageAsset(int ActionDefinitionId, int AnimationId,
    AlsMontageSlot Slot, int GroupId, float Duration, float ClipStart, float ClipRate,
    AlsActionLifecycleSettings Lifecycle, bool RootMotionEnabled = false, int MontageId = -1)
{
    public int AdditiveType { get; init; }
    public float RateScale { get; init; } = 1;
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
    public float EffectivePlayRate => PlayRate * RateScale;
}
public readonly record struct AlsMontageEvaluation(long InstanceId, int AnimationId, AlsMontageSlot Slot,
    float Position, float Weight, int ActionDefinitionId = -1)
{
    public int AdditiveType { get; init; }
}
public readonly record struct AlsMontageTraversal(long InstanceId, int AnimationId, AlsMontageSlot Slot,
    float PreviousPosition, float CurrentPosition, float NotifyWeight, bool Interrupted, bool Terminated, int ActionDefinitionId = -1);

public readonly record struct AlsMontageRootMotionRange(AlsFrameIdentity Identity, long InstanceId,
    int AnimationId, float StartSeconds, float EndSeconds)
{
    public bool HasMotion => InstanceId > 0;
}

// A bank survives Commit and remains readable while the next candidate is prepared.
// Consumers retain Identity and reject a bank recycled for a later frame.
public sealed class AlsMontageFrame
{
    internal AlsMontageEvaluation[] Entries = new AlsMontageEvaluation[8];
    internal int Count;
    public AlsFrameIdentity Identity { get; internal set; }
    public ReadOnlySpan<AlsMontageEvaluation> Evaluations => Entries.AsSpan(0, Count);
    public AlsSlotWeights SlotWeights(AlsMontageSlot slot)
    {
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
public sealed class AlsMontageRuntime
{
    private readonly Dictionary<(int, AlsTurnSlot), AlsDynamicMontageAsset> _assets;
    private readonly Dictionary<int, AlsAuthoredMontageAsset> _actions;
    private readonly Dictionary<(int, AlsMontageSlot), AlsSequenceMontageAsset> _sequences;
    private AlsMontageInstance[] _committed = new AlsMontageInstance[8], _candidate = new AlsMontageInstance[8];
    private AlsMontageFrame _committedFrame = new(), _frame = new();
    private AlsMontageTraversal[] _traversal = new AlsMontageTraversal[8];
    private AlsTurnSlotObservation[] _observations = new AlsTurnSlotObservation[8];
    private int _committedCount, _count, _evaluationCount, _traversalCount;
    private long _committedSerial, _serial;
    private long _committedRootMotionInstance, _rootMotionInstance;
    private AlsMontageRootMotionRange _rootMotionRange;
    public long CommittedRootMotionInstance => _committedRootMotionInstance;
    public long CandidateRootMotionInstance { get { RequirePrepared(); return _rootMotionInstance; } }
    public AlsMontageRootMotionRange RootMotionRange { get { RequirePrepared(); return _rootMotionRange; } }
    private AlsFrameIdentity _identity;
    private bool _prepared;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ReadOnlySpan<AlsMontageInstance> Committed => _committed.AsSpan(0, _committedCount);
    public ReadOnlySpan<AlsMontageInstance> Candidate { get { RequirePrepared(); return _candidate.AsSpan(0, _count); } }
    public AlsMontageFrame Frame { get { RequirePrepared(); return _frame; } }
    public ReadOnlySpan<AlsMontageEvaluation> Evaluation { get { RequirePrepared(); return _frame.Evaluations; } }
    public ReadOnlySpan<AlsMontageTraversal> Traversal { get { RequirePrepared(); return _traversal.AsSpan(0, _traversalCount); } }
    public bool TryGetActionAsset(int definitionId, out AlsAuthoredMontageAsset asset) => _actions.TryGetValue(definitionId, out asset);

    public AlsMontageRuntime(ReadOnlySpan<AlsDynamicMontageAsset> assets, ReadOnlySpan<AlsAuthoredMontageAsset> actions = default,
        ReadOnlySpan<AlsSequenceMontageAsset> sequences = default)
    {
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
            if (asset.AnimationId < 0 || asset.Slot.Id is < 0 or > 3 || asset.GroupId < 0 ||
                !float.IsFinite(asset.Duration) || asset.Duration <= .00005f || (uint)asset.AdditiveType > 2 ||
                asset.Slot.Id < 2 && _assets.ContainsKey((asset.AnimationId, (AlsTurnSlot)asset.Slot.Id)) ||
                !_sequences.TryAdd((asset.AnimationId, asset.Slot), asset))
                throw new ArgumentException("Invalid or duplicate sequence montage asset.");
        }
        _actions = new();
        var nativeAssets = new Dictionary<int, AlsAuthoredMontageAsset>();
        foreach (var action in actions)
        {
            if (action.ActionDefinitionId < 0 || action.MontageId < -1 || action.AnimationId < 0 || action.Slot.Id < 0 || action.GroupId < 0 || (uint)action.AdditiveType > 2 ||
                !float.IsFinite(action.Duration) || action.Duration <= .00005f || !float.IsFinite(action.ClipStart) || action.ClipStart < 0 ||
                !float.IsFinite(action.ClipRate) || action.ClipRate <= 0 || !float.IsFinite(action.ClipStart + action.Duration * action.ClipRate) ||
                !float.IsFinite(action.RateScale) || action.RateScale <= 0 ||
                action.Lifecycle.Mode == AlsActionLifecycleMode.LegacySectionEnd || !AlsActionLifecycle.IsValid(action.Lifecycle) ||
                !_actions.TryAdd(action.ActionDefinitionId, action)) throw new ArgumentException("Invalid authored montage asset.");
            var nativeId = action.MontageId >= 0 ? action.MontageId : action.ActionDefinitionId;
            var canonical = action with { MontageId = nativeId, ActionDefinitionId = 0 };
            if (nativeAssets.TryGetValue(nativeId, out var existing) && existing != canonical)
                throw new ArgumentException("Action aliases disagree about their authored montage.");
            nativeAssets[nativeId] = canonical;
        }
    }

    public void Begin(AlsFrameIdentity identity, float delta, bool ragdoll = false)
    {
        if (_prepared || identity.SlotGeneration == 0 || !float.IsFinite(delta) || delta <= 0 ||
            CommittedIdentity != default && (identity.SlotGeneration != CommittedIdentity.SlotGeneration ||
                identity.CharacterId != CommittedIdentity.CharacterId || identity.FrameId <= CommittedIdentity.FrameId))
            throw new ArgumentException("Invalid montage frame identity, phase or delta.");
        Ensure(_committedCount); _count = _evaluationCount = _traversalCount = 0;
        _identity = identity; _serial = _committedSerial;
        _rootMotionInstance = _committedRootMotionInstance; _rootMotionRange = new(identity, 0, -1, 0, 0);
        if (ragdoll) _rootMotionInstance = 0;
        for (var i = 0; i < _committedCount; i++)
        {
            var state = _committed[i];
            if (ragdoll && state.Blend.DesiredWeight > 0)
            {
                var stopBlend = state.Blend;
                AlsActionLifecycle.Stop(.2f, state.Settings.BlendOutOption, ref stopBlend);
                state = state with { Blend = stopBlend, BlendTime = .2f, BlendResetPending = false,
                    Interrupted = true, OwnsActiveActionLookup = false };
            }
            var blend = state.Blend; var previousWeight = blend.CurrentWeight;
            // UE selects its motion owner before Advance. Auto-blend-out clears
            // future ownership, but this tick still extracts its traversed range.
            var extractMotion = state.InstanceId == _rootMotionInstance && state.Playing;
            if (state.BlendResetPending)
            {
                AlsActionLifecycle.ResetRange(state.BlendTime,
                    blend.BlendingOut == 1 ? state.Settings.BlendOutOption : state.Settings.BlendInOption, ref blend);
                state = state with { BlendResetPending = false };
            }
            AlsActionLifecycle.AdvanceWeight(state.Settings, delta, ref blend);
            var previous = state.Position; var position = previous; var playing = state.Playing;
            var traversalEnd = position;
            if (playing)
            {
                var rate = state.EffectivePlayRate;
                var move = delta * rate;
                if (!float.IsFinite(move)) throw new ArgumentException("Montage time overflow.");
                var forward = rate > 0;
                var boundary = forward ? state.Duration : 0;
                position = System.Math.Clamp(position + move, 0, state.Duration); traversalEnd = position;
                var remaining = MathF.Abs(rate) <= 1e-8f ? float.MaxValue : MathF.Abs((boundary - position) / rate);
                var wasStopped = blend.BlendingOut == 1;
                AlsActionLifecycle.TryBeginBlendOut(state.Settings, remaining, ref blend);
                if (!wasStopped && blend.BlendingOut == 1)
                {
                    state = state with { BlendTime = state.Settings.BlendOutTriggerSeconds >= 0 ? state.Settings.BlendOutSeconds : remaining };
                    if (_rootMotionInstance == state.InstanceId) _rootMotionInstance = 0;
                }
                if (move != 0 && position == boundary)
                {
                    playing = false;
                    position = forward ? state.Duration - .00005f : 0;
                }
                if (blend.BlendingOut == 1 && state.BlendTime <= 0) playing = false;
            }
            var terminated = AlsActionLifecycle.IsComplete(blend);
            if (extractMotion && previous != traversalEnd)
                _rootMotionRange = new(identity, state.InstanceId, state.AnimationId,
                    state.ClipStart + previous * state.ClipRate, state.ClipStart + traversalEnd * state.ClipRate);
            _traversal[_traversalCount++] = new(state.InstanceId, state.AnimationId, state.Slot,
                previous, traversalEnd, MathF.Max(previousWeight, blend.CurrentWeight), state.Interrupted, terminated, state.ActionDefinitionId);
            if (terminated) continue;
            state = state with { Position = position, Playing = playing, Blend = blend,
                OwnsActiveActionLookup = state.OwnsActiveActionLookup && blend.DesiredWeight > 0 };
            _candidate[_count++] = state;
            if (blend.CurrentWeight > AlsPoseBlender.WeightThreshold)
                _frame.Entries[_evaluationCount++] = new(state.InstanceId, state.AnimationId, state.Slot,
                    state.ClipStart + position * state.ClipRate, blend.CurrentWeight, state.ActionDefinitionId) { AdditiveType = state.AdditiveType };
        }
        _frame.Count = _evaluationCount; _frame.Identity = identity;
        _prepared = true;
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
        RequirePrepared();
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
        RequirePrepared();
        if (!float.IsFinite(playRate) || !float.IsFinite(startTime)) throw new ArgumentException("Invalid authored montage request.");
        if (!_actions.TryGetValue(definitionId, out var asset)) return false;
        Play(asset, playRate, startTime, stopGroup);
        return true;
    }

    public bool PlaySequence(in AlsSequenceMontageCommand command)
    {
        RequirePrepared();
        if (!float.IsFinite(command.PlayRate) || !float.IsFinite(command.StartTime) || !float.IsFinite(command.BlendInTime) ||
            !float.IsFinite(command.BlendOutTime) || !float.IsFinite(command.BlendOutTriggerTime) ||
            command.BlendInTime < 0 || command.BlendOutTime < 0 || command.LoopCount != 1)
            throw new ArgumentException("Invalid sequence montage command.");
        if (!_sequences.TryGetValue((command.AnimationId, command.Slot), out var asset)) return false;
        var lifecycle = new AlsActionLifecycleSettings(AlsActionLifecycleMode.MontageAutoBlendOut,
            command.BlendInTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTime, AlsActionBlendOption.HermiteCubic, command.BlendOutTriggerTime);
        Play(new AlsAuthoredMontageAsset(-1, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration, 0, 1, lifecycle)
            { AdditiveType = asset.AdditiveType }, command.PlayRate, command.StartTime, true);
        return true;
    }

    // Physical instance cancellation must not accidentally stop a later replay of
    // the same authored asset. Stopped instances can still have a shorter fade set.
    public bool StopInstance(long instanceId, float blendTime, AlsActionBlendOption option)
    {
        RequirePrepared();
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
        for (var i = 0; i < _count; i++)
        {
            // Montage_Stop(nullptr) only visits IsActive (desired weight > 0).
            // Existing auto/replacement fades retain their original duration.
            if (_candidate[i].Blend.DesiredWeight <= 0) continue;
            var instance = _candidate[i].InstanceId;
            Stop(i, .2f, _candidate[i].Settings.BlendOutOption);
            for (var t = 0; t < _traversalCount; t++)
                if (_traversal[t].InstanceId == instance) _traversal[t] = _traversal[t] with { Interrupted = true };
        }
        _rootMotionInstance = 0;
        _rootMotionRange = new(_identity, 0, -1, 0, 0);
    }

    public long ActiveActionInstance(int definitionId)
    {
        RequirePrepared();
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
                Stop(i, asset.Lifecycle.BlendInSeconds, asset.Lifecycle.BlendInOption);
            // ActiveMontagesMap selects the newest play of the authored asset.
            // Removing that entry later does not reactivate an older instance.
            if (montageId >= 0 && _candidate[i].MontageId == montageId)
                _candidate[i] = _candidate[i] with { OwnsActiveActionLookup = false };
        }
        _candidate[_count++] = new(++_serial, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration,
            System.Math.Clamp(startTime, 0, asset.Duration), playRate, asset.Lifecycle.BlendInSeconds,
            // Native FAlphaBlend starts with a 0.2 second timer. Play configures
            // its requested duration but does not update/reset it this frame.
            asset.Lifecycle, new AlsActionLifecycleState { DesiredWeight = 1, RemainingSeconds = .2f }, true, false)
            { BlendResetPending = true, ActionDefinitionId = asset.ActionDefinitionId, ClipStart = asset.ClipStart, ClipRate = asset.ClipRate,
                MontageId = montageId, OwnsActiveActionLookup = montageId >= 0, AdditiveType = asset.AdditiveType, RateScale = asset.RateScale };
        if (asset.RootMotionEnabled) _rootMotionInstance = _serial;
    }

    private void Stop(int index, float seconds, AlsActionBlendOption option)
    {
        var old = _candidate[index];
        if (_rootMotionInstance == old.InstanceId) _rootMotionInstance = 0;
        var blend = old.Blend; var duration = old.BlendTime;
        var pending = old.BlendResetPending;
        var settings = old.Settings;
        if (blend.BlendingOut == 0)
        {
            duration = seconds;
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
            blend.BeginWeight = blend.CurrentWeight;
            pending = true;
        }
        _candidate[index] = old with { Blend = blend, BlendTime = duration, BlendResetPending = pending, Settings = settings,
            Interrupted = true, Playing = old.Playing && duration > 0, OwnsActiveActionLookup = false };
    }

    public AlsSlotWeights SlotWeights(AlsMontageSlot slot)
    {
        RequirePrepared(); return _frame.SlotWeights(slot);
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { RequirePrepared(); if (identity != _identity) throw new ArgumentException("Foreign montage commit."); }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); (_committed, _candidate) = (_candidate, _committed);
        (_committedFrame, _frame) = (_frame, _committedFrame);
        _committedCount = _count; _committedSerial = _serial; CommittedIdentity = identity; _prepared = false;
        _committedRootMotionInstance = _rootMotionInstance;
    }
    public void Discard() { _prepared = false; _count = _evaluationCount = _traversalCount = 0; _frame.Count = 0; _frame.Identity = default; }
    public void ClearForLifecycle()
    {
        if (_prepared) throw new InvalidOperationException("Discard the montage candidate before lifecycle cleanup.");
        Array.Clear(_committed); Array.Clear(_candidate);
        _committedCount = _count = _evaluationCount = _traversalCount = 0;
        _committedRootMotionInstance = _rootMotionInstance = 0; _rootMotionRange = default;
        _committedFrame.Count = _frame.Count = 0;
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
