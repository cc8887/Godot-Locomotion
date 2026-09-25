using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStandingMovementInput(float GroundedAmount,float UnweightedRunningAmount,float StandingMachineWeight,float FeetCrossing);

/// <summary>One deferred cache pass from Standing's Move/Stop readers through
/// cache66, cache67 and all direction caches. The host supplies state-local reads,
/// owns Parent/players/pose stages and commits the whole character atomically.</summary>
public sealed class AlsRefactoredStandingMovementTraversal : IAlsPoseCacheUpdateSink
{
    public AlsPoseCacheDefinition Caches { get; }
    public AlsRefactoredMovementTraversal Movement { get; }
    public AlsRefactoredMovementDetailsRuntime DetailsMachine { get; }
    public AlsRefactoredMovementDetailsSourceRuntime DetailsSources { get; }
    private readonly AlsRefactoredStandingResources _standing;
    private readonly AlsRefactoredMovementEntryRuntime _entry;
    private readonly AlsPoseCacheTraversal _traversal;
    private readonly HashSet<int> _readers;
    private readonly AlsRefactoredDirectionCacheUpdate[] _updates=new AlsRefactoredDirectionCacheUpdate[9];
    private readonly AlsPoseUpdateContext[] _outerSkipped=new AlsPoseUpdateContext[6];
    private int _updateCount,_skippedCount,_handler=-1;
    private AlsGraphTraversalCounter _initialization,_nextInitialization,_requestedInitialization;
    private bool _pendingReset,_nextPendingReset,_pendingInstance,_nextPendingInstance;
    private AlsRefactoredMovementParentRuntime? _parent,_owner;
    private AlsRefactoredStandingMovementInput _input;
    private AlsFrameIdentity _identity,_committed;
    private AlsPoseUpdateContext _detailsContext;
    private AlsPoseUpdateContext _rootContext;
    private bool _prepared,_updated,_hasCommitted;
    public bool HasMovement=>_prepared?_updated:throw new InvalidOperationException("No Standing movement candidate.");
    public AlsPoseUpdateContext DetailsContext=>_prepared&&_updated?_detailsContext:throw new InvalidOperationException("Movement Details not updated.");
    public ReadOnlySpan<AlsRefactoredDirectionCacheUpdate> CacheUpdates=>_prepared?_updates.AsSpan(0,_updateCount):throw new InvalidOperationException("No Standing movement candidate.");
    public int OuterSkippedHandler=>_prepared?_handler:throw new InvalidOperationException("No Standing movement candidate.");
    public ReadOnlySpan<AlsPoseUpdateContext> OuterSkippedContexts=>_prepared?_outerSkipped.AsSpan(0,_skippedCount):throw new InvalidOperationException("No Standing movement candidate.");

    public AlsRefactoredStandingMovementTraversal(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStandingResources standing,
        AlsRefactoredMovementDetailsPoseGraph details,AlsRefactoredDirectionSourceProfile direction,int firstPlayer)
    {
        if(catalog.IndexDigest!=standing.CatalogDigest)throw new ArgumentException("Foreign Standing movement catalog.");
        _standing=standing;Movement=new(catalog,details,direction,firstPlayer);DetailsMachine=new(details.Resources);DetailsSources=new(details,firstPlayer);
        _entry=new(standing,details.Callbacks);_readers=standing.MovementReaders.ToArray().ToHashSet();
        var payload=catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("compiledNodeIndex").GetInt32());
        var selected=Movement.Caches.UpdateOrder.ToArray().Append(standing.MovementCachePropertyIndex).ToHashSet();
        var order=payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n=>n.GetProperty("root").GetString()=="AnimGraph")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n=>nodes[n.GetInt32()].GetProperty("propertyIndex").GetInt32()).Where(selected.Contains).ToArray();
        if(order.Length!=9||order[0]!=66||!order.AsSpan(1).SequenceEqual(Movement.Caches.UpdateOrder))throw new ArgumentException("Standing cache order differs.");
        Caches=new(Movement.Caches.NodeCount,order,Movement.Caches.Reads.ToArray().Concat(_readers.Select(r=>new AlsPoseCacheReadBinding(r,66))).ToArray());
        _traversal=new(Caches,38);
    }
    public void Prepare(in AlsPoseUpdateContext context,ReadOnlySpan<AlsRefactoredMovementCacheRead> reads,ReadOnlySpan<int> initializationReads,
        AlsGraphTraversalCounter initialization,AlsRefactoredMovementParentRuntime parent,AlsRefactoredStandingMovementInput input,bool initializeInstance=false)
    {
        var identity=context.Identity;
        if(_prepared||!context.HasSharedContext||context.UpdateCounter is not {HasUpdated:true}||!initialization.HasUpdated||
            _owner is not null&&!ReferenceEquals(_owner,parent)||_hasCommitted&&(identity.FrameId<=_committed.FrameId||identity.CharacterId!=_committed.CharacterId||identity.SlotGeneration!=_committed.SlotGeneration)||
            !float.IsFinite(input.GroundedAmount)||!float.IsFinite(input.UnweightedRunningAmount)||!float.IsFinite(input.StandingMachineWeight)||!float.IsFinite(input.FeetCrossing))
            throw new ArgumentException("Invalid Standing movement frame/context.");
        parent.ValidateContext(identity,_standing.CatalogDigest);
        foreach(var read in reads)if(!_readers.Contains(read.ReadPropertyIndex)||read.CachePropertyIndex!=66||read.Context.Identity!=identity||
            read.Context.UpdateCounter!=context.UpdateCounter||read.Context.Delta!=context.Delta)throw new ArgumentException("Foreign Movement Details reader.");
        foreach(var read in initializationReads)if(!_readers.Contains(read))throw new ArgumentException("Foreign Movement Details initialization.");
        _identity=identity;_rootContext=context;_parent=parent;_input=input;_requestedInitialization=initialization;
        _nextInitialization=initializeInstance?default:_initialization;
        _nextPendingReset=initializeInstance||_pendingReset;_nextPendingInstance=initializeInstance||_pendingInstance;
        _updated=false;_updateCount=_skippedCount=0;_handler=-1;
        try
        {
            foreach(var read in initializationReads)
                if(!_nextInitialization.MatchesCounter(initialization)){_nextInitialization=initialization;_nextPendingReset=true;}
            _traversal.Begin(identity);foreach(var read in reads)_traversal.Use(read.ReadPropertyIndex,read.Context);
            _traversal.Drain(this);if(_updated)Movement.CompleteShared(identity.FrameId);
            _owner??=parent;_prepared=true;
        }
        catch{Cancel();throw;}
    }
    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cache,in AlsPoseUpdateContext context)
    {
        _updates[_updateCount++]=new(cache,context);
        if(cache==66)
        {
            var initializeInstance=_nextPendingInstance;_entry.Begin(context,_parent!,initializeInstance);
            _detailsContext=AlsRefactoredMovementInertialization.SourceContext(context);
            var state=_parent!.MovementCandidate;var forward=_parent.ForwardInput();
            DetailsMachine.Prepare(_identity.FrameId,new(forward.Gait,_input.GroundedAmount,_input.UnweightedRunningAmount,_input.StandingMachineWeight,_parent.Candidate.PivotActive),
                DetailsSources.CommittedObservations,context.Delta,context.Weight,_nextPendingReset,context.UpdateCounter);
            DetailsSources.Prepare(DetailsMachine,_detailsContext,state.VelocityBlend,initializeInstance,_parent);
            Movement.PrepareShared(_identity.FrameId,DetailsMachine,DetailsSources,_requestedInitialization,_parent.DirectionInput(_input.FeetCrossing),
                _parent.PlayerInput(),forward,initializeInstance,state.YawOffsets,_parent,_traversal);
            // Source.Update returns after enqueueing saved-pose reads. Those
            // sources execute later in this same Drain, outside this callback stack.
            _entry.Complete(_identity.FrameId);_updated=true;_nextPendingReset=_nextPendingInstance=false;
        }
        else ((IAlsPoseCacheUpdateSink)Movement).UpdateCachedSource(cache,context);
    }
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {
        if(_updates[_updateCount-1].PropertyIndex!=66)((IAlsPoseCacheUpdateSink)Movement).OnCachedUpdatesSkipped(handler,skipped);
        else
        {
            if(_handler>=0&&_handler!=handler)throw new ArgumentException("Ambiguous outer skipped handler.");
            _handler=handler;skipped.CopyTo(_outerSkipped.AsSpan(_skippedCount));_skippedCount+=skipped.Length;
        }
    }
    public void ValidateCommit(long frame)
    {
        if(!_prepared||frame!=_identity.FrameId)throw new ArgumentException("Invalid Standing movement commit.");
        if(_updated){_entry.ValidateCommit(frame);DetailsMachine.ValidateCommit(frame);DetailsSources.ValidateCommit(frame);Movement.ValidateCommit(frame);}
    }
    internal void ValidateContext(in AlsPoseUpdateContext context)
    { ValidateCommit(context.Identity.FrameId);if(_rootContext!=context)throw new ArgumentException("Foreign Standing traversal context."); }
    public void Commit(long frame)
    {
        ValidateCommit(frame);
        if(_updated){_entry.Commit(frame);DetailsMachine.Commit(frame);DetailsSources.Commit(frame);Movement.Commit(frame);}
        _initialization=_nextInitialization;_pendingReset=_nextPendingReset;_pendingInstance=_nextPendingInstance;
        _committed=_identity;_hasCommitted=true;Cancel();
    }
    public void Cancel(){_prepared=_updated=false;_parent=null;_entry.Cancel();DetailsMachine.Cancel();DetailsSources.Cancel();Movement.Cancel();}
}
