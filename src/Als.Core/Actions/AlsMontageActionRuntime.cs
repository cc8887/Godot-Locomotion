using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMontageActionPolicy(int DefinitionId, int StartSectionId,
    float StartTime, float PlayRate, float CancelBlendSeconds, bool Interruptible);
public readonly record struct AlsMontageActionOwner(int GroupId, long InstanceId, long RequestId,
    int DefinitionId, int Priority, bool Interruptible);
public readonly record struct AlsMontageActionHistory(long LastRequestId, long LastCommandRequestId, AlsActionCommand LastCommand);

// Request ownership only. Playback time, completion and all visual fades belong to
// the shared montage bank; no second action clock or outgoing-pose lane exists here.
public sealed class AlsMontageActionRuntime
{
    private readonly AlsMontageRuntime _montages;
    private readonly Dictionary<int, (AlsMontageActionPolicy Policy, int Group)> _policies = new();
    private readonly int[] _groups;
    private AlsMontageActionOwner[] _committed, _candidate;
    private AlsMontageActionHistory _history, _nextHistory;
    private AlsActionOutcomeBuffer _outcomes;
    private AlsFrameIdentity _identity;
    private Phase _phase;
    private bool _requestApplied;
    private enum Phase { Idle, Preparing, Prepared, Faulted }
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    internal AlsMontageRuntime Montages => _montages;
    public ReadOnlySpan<AlsMontageActionOwner> CommittedOwners => _committed;
    public ReadOnlySpan<AlsMontageActionOwner> CandidateOwners { get { RequireCandidate(); return _candidate; } }
    public AlsMontageActionHistory CommittedHistory => _history;
    public ref readonly AlsActionOutcomeBuffer Outcomes { get { RequireCandidate(); return ref _outcomes; } }

    public AlsMontageActionRuntime(AlsMontageRuntime montages, ReadOnlySpan<AlsMontageActionPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(montages); _montages = montages;
        foreach (var policy in policies)
        {
            if (!_montages.TryGetActionAsset(policy.DefinitionId, out var asset) || policy.StartSectionId < 0 ||
                !float.IsFinite(policy.StartTime) || policy.StartTime < 0 || policy.StartTime >= asset.Duration ||
                !float.IsFinite(policy.PlayRate) || policy.PlayRate <= 0 || !float.IsFinite(policy.CancelBlendSeconds) || policy.CancelBlendSeconds < 0 ||
                !_policies.TryAdd(policy.DefinitionId, (policy,asset.GroupId))) throw new ArgumentException("Invalid action request policy.");
        }
        _groups = _policies.Values.Select(v=>v.Group).Distinct().Order().ToArray();
        _committed = new AlsMontageActionOwner[_groups.Length]; _candidate = new AlsMontageActionOwner[_groups.Length];
        _history = new(0,-1,AlsActionCommand.None);
    }

    public void Begin(AlsFrameIdentity identity, float delta)
    {
        if (_phase != Phase.Idle) throw new InvalidOperationException("An action frame is already prepared.");
        _montages.Begin(identity,delta);
        _identity=identity; _committed.CopyTo(_candidate,0); _nextHistory=_history; _outcomes=default; _requestApplied=false;
        _phase=Phase.Preparing;
    }

    public void ApplyRequest(in AlsActionRequest request, bool cancelForRuntimeFailure = false,
        AlsRollingStartContext? rolling = null, AlsMontageActionParameters parameters = default)
    {
        if (_phase != Phase.Preparing || _requestApplied) throw new InvalidOperationException("Action request phase differs.");
        _requestApplied=true;
        try
        {
            if (!parameters.IsValidFor(request.Command)) throw new ArgumentException("Invalid captured montage start parameters.");
            var canonicalNone = request.Command==AlsActionCommand.None && request.RequestId==-1 &&
                request.ActionDefinitionId==-1 && request.StartSectionId==-1 && request.Priority==0;
            if ((byte)request.Command>(byte)AlsActionCommand.CancelForRuntimeFailure ||
                request.Command==AlsActionCommand.None && !canonicalNone)
                throw new ArgumentException("Malformed action command.");
            if (cancelForRuntimeFailure)
                for (var i=0;i<_candidate.Length;i++) if (_candidate[i].InstanceId>0) Close(i,AlsActionResultCode.InterruptedByRuntimeFailure);
            // Recovery owns the old action even if this frame's physical tick
            // reaches its terminal boundary. Do not report Completed first.
            Reconcile();
            if (canonicalNone) return;
            if (request.Command==AlsActionCommand.Start && request.RequestId>0)
            {
                if (request.RequestId<=_nextHistory.LastRequestId) return;
                Record(request,true);
                if (request.SlotGeneration!=_identity.SlotGeneration || request.ActionDefinitionId<0 || request.StartSectionId<0 || request.Priority<0)
                { Reject(request,AlsActionResultCode.RejectedInvalidRequest); return; }
                if (!_policies.TryGetValue(request.ActionDefinitionId,out var binding))
                { Reject(request,AlsActionResultCode.RejectedMissingDefinition); return; }
                if (request.StartSectionId!=binding.Policy.StartSectionId)
                { Reject(request,AlsActionResultCode.RejectedInvalidRequest); return; }
                if (rolling is { } roll && !AlsRollingGameplay.CanStart(roll, _montages.IsActionPlaying(request.ActionDefinitionId)))
                { Reject(request,AlsActionResultCode.RejectedBusy); return; }
                var index=Array.IndexOf(_groups,binding.Group); var old=_candidate[index];
                if (old.InstanceId>0 && !old.Interruptible) { Reject(request,AlsActionResultCode.RejectedBusy); return; }
                if (old.InstanceId>0 && request.Priority<old.Priority) { Reject(request,AlsActionResultCode.RejectedLowerPriority); return; }
                // Play performs native same-group Stop using the incoming blend-in.
                // Do not stop the old physical instance again using the cancel duration.
                if (old.InstanceId>0) { Add(new(old.RequestId,old.DefinitionId,old.InstanceId,AlsActionResultCode.InterruptedByReplacement)); _candidate[index]=default; }
                if (!_montages.PlayAction(request.ActionDefinitionId,parameters.PlayRate > 0 ? parameters.PlayRate : binding.Policy.PlayRate,binding.Policy.StartTime))
                    throw new InvalidOperationException("Compiled action disappeared from the montage bank.");
                var id=_montages.ActiveActionInstance(request.ActionDefinitionId);
                if (id<=0) throw new InvalidOperationException("New action has no physical instance.");
                _candidate[index]=new(binding.Group,id,request.RequestId,request.ActionDefinitionId,request.Priority,binding.Policy.Interruptible);
                Add(new(request.RequestId,request.ActionDefinitionId,id,AlsActionResultCode.Accepted));
                return;
            }
            if (request.Command==AlsActionCommand.Cancel)
            {
                if (_nextHistory.LastCommand==AlsActionCommand.Cancel && _nextHistory.LastCommandRequestId==request.RequestId) return;
                var shape=request.RequestId>0 && request.SlotGeneration==_identity.SlotGeneration && request.ActionDefinitionId>=0 && request.StartSectionId==-1 && request.Priority==0;
                var owner=-1;
                if (shape) for(var i=0;i<_candidate.Length;i++)
                    if (_candidate[i].InstanceId>0 && _candidate[i].RequestId==request.RequestId && _candidate[i].DefinitionId==request.ActionDefinitionId) owner=i;
                if (owner>=0) { Record(request,false); Close(owner,AlsActionResultCode.InterruptedByExplicitCancel); }
                else if (request.RequestId>=_nextHistory.LastRequestId || request.RequestId<=0)
                { Record(request,true); Reject(request,AlsActionResultCode.RejectedInvalidRequest); }
                return;
            }
            if (_nextHistory.LastCommandRequestId==request.RequestId && _nextHistory.LastCommand==request.Command) return;
            Record(request,true); Reject(request,AlsActionResultCode.RejectedInvalidRequest);
        }
        catch { _phase=Phase.Faulted; throw; }
    }

    // Call after all Blueprint-driven playback commands (including dynamic turns).
    public void Complete()
    {
        if (_phase!=Phase.Preparing || !_requestApplied) throw new InvalidOperationException("Action frame was not fully prepared.");
        try { Reconcile(); _phase=Phase.Prepared; } catch { _phase=Phase.Faulted; throw; }
    }

    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase!=Phase.Prepared || identity!=_identity) throw new InvalidOperationException("Action candidate is not committable.");
        _montages.ValidateCommit(identity);
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); _montages.Commit(identity); (_committed,_candidate)=(_candidate,_committed);
        _history=_nextHistory; CommittedIdentity=identity; _phase=Phase.Idle;
    }
    public void Discard() { _montages.Discard(); _phase=Phase.Idle; _outcomes=default; }
    public void ClearForLifecycle(in AlsActionRequest abandonedInput)
    {
        if (_phase != Phase.Idle) throw new InvalidOperationException("Discard the action candidate before lifecycle cleanup.");
        _montages.ClearForLifecycle(); Array.Clear(_committed); Array.Clear(_candidate); _outcomes = default;
        if (abandonedInput.Command == AlsActionCommand.Start && abandonedInput.RequestId > _history.LastRequestId)
            _history = _history with { LastRequestId = abandonedInput.RequestId };
        if (abandonedInput.Command == AlsActionCommand.Cancel)
            _history = _history with { LastCommand = AlsActionCommand.Cancel, LastCommandRequestId = abandonedInput.RequestId };
        _nextHistory = _history;
    }

    private void Reconcile()
    {
        for(var i=0;i<_candidate.Length;i++)
        {
            var owner=_candidate[i]; if(owner.InstanceId<=0)continue;
            var present=false; var interrupted=false;
            foreach(var instance in _montages.Candidate)
                if(instance.InstanceId==owner.InstanceId) { present=true; interrupted=instance.Interrupted; break; }
            if(present && !interrupted)continue;
            if(!present)
            {
                var terminated=false;
                foreach(var tick in _montages.Traversal) if(tick.InstanceId==owner.InstanceId)
                { terminated=tick.Terminated; interrupted=tick.Interrupted; break; }
                if(!terminated)throw new InvalidOperationException("Action owner lost its physical instance without termination.");
            }
            Add(new(owner.RequestId,owner.DefinitionId,owner.InstanceId,
                interrupted ? AlsActionResultCode.InterruptedByReplacement : AlsActionResultCode.Completed));
            _candidate[i]=default;
        }
    }
    private void Close(int index, AlsActionResultCode reason)
    {
        var owner=_candidate[index]; var policy=_policies[owner.DefinitionId].Policy;
        _montages.TryGetActionAsset(owner.DefinitionId,out var asset);
        if(!_montages.StopInstance(owner.InstanceId,policy.CancelBlendSeconds,asset.Lifecycle.BlendOutOption))
        {
            var terminated = false;
            if (reason == AlsActionResultCode.InterruptedByRuntimeFailure)
                foreach (var tick in _montages.Traversal)
                    if (tick.InstanceId == owner.InstanceId && tick.Terminated) terminated = true;
            if (!terminated) throw new InvalidOperationException("Cannot cancel a missing physical action.");
        }
        Add(new(owner.RequestId,owner.DefinitionId,owner.InstanceId,reason)); _candidate[index]=default;
    }
    private void Record(in AlsActionRequest request,bool updateHighWatermark) => _nextHistory =
        new(updateHighWatermark ? System.Math.Max(_nextHistory.LastRequestId,request.RequestId) : _nextHistory.LastRequestId,request.RequestId,request.Command);
    private void Reject(in AlsActionRequest request,AlsActionResultCode reason) => Add(new(request.RequestId,request.ActionDefinitionId,0,reason));
    private void Add(AlsActionOutcome outcome)
    { if(!_outcomes.TryAdd(outcome))throw new InvalidOperationException("ActionOutcomeBufferOverflow: candidate cannot be published."); }
    private void RequireCandidate() { if(_phase is not (Phase.Preparing or Phase.Prepared))throw new InvalidOperationException("No action candidate."); }
}
