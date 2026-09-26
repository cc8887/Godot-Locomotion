using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Standing root203 and optional Idle57/59/58 callback scopes. Begin
/// precedes machine65 Update; Complete follows all synchronous source updates.
/// Deferred cache66 callbacks still belong to StandingMovementTraversal.</summary>
public sealed class AlsRefactoredStandingRestTraversal
{
    private readonly AlsRefactoredStandingRestGraph _graph;
    private readonly AlsRefactoredStanceCallbackRuntime _callbacks;
    private readonly AlsRefactoredStanceCallback _rotate;
    private AlsRefactoredRestParentRuntime? _owner;
    private AlsFrameIdentity _identity;
    private bool _prepared, _idleStarted, _idleComplete, _complete;
    public AlsRefactoredStandingRestTraversal(AlsRefactoredStandingRestGraph graph, AlsRefactoredStanceCallbacks callbacks)
    {
        if(graph.CatalogDigest!=callbacks.CatalogDigest||graph.IdleCallbacks.ToArray().Any(c=>!callbacks.Nodes.Contains(c)))throw new ArgumentException("Foreign Standing rest traversal callbacks.");
        _rotate=callbacks.Nodes.ToArray().Single(c=>c.Function==AlsRefactoredStanceFunction.RefreshRotateInPlace);
        if(_rotate.PropertyIndex!=(graph.Crouching?111:203)||_rotate.SourcePropertyIndex!=(graph.Crouching?2:68)||_rotate.OnBecomeRelevant)throw new ArgumentException("Stance root callback differs.");
        _graph=graph;_callbacks=new(callbacks);
    }
    public void Begin(in AlsPoseUpdateContext context, AlsRefactoredRestParentRuntime parent, bool initializeInstance=false)
    {
        if(_prepared||context.UpdateCounter is not {HasUpdated:true}||!context.HasSharedContext||_owner is not null&&!ReferenceEquals(_owner,parent))throw new ArgumentException("Invalid Standing rest source context.");
        parent.ValidateContext(context.Identity,_graph.CatalogDigest);
        try
        {
            _callbacks.Prepare(context.Identity.FrameId,context.UpdateCounter.Value,initializeInstance);
            var command=_callbacks.Enter(context.Identity.FrameId,_rotate.PropertyIndex);
            if(command is not null)parent.Apply(context.Identity,command);
            _owner??=parent;_identity=context.Identity;_prepared=true;_idleStarted=_idleComplete=_complete=false;
        }
        catch{_callbacks.Cancel();throw;}
    }
    public void BeginIdle(long frame)
    {
        Check(frame);if(_idleStarted||_complete)throw new ArgumentException("Duplicate Idle callback traversal.");
        _idleStarted=true;
        foreach(var node in _graph.IdleCallbacks)
        {
            var command=_callbacks.Enter(frame,node.PropertyIndex);
            if(command is not null)_owner!.Apply(_identity,command);
        }
    }
    public void CompleteIdle(long frame)
    {
        Check(frame);if(!_idleStarted||_idleComplete)throw new ArgumentException("Invalid Idle source completion.");
        for(var i=_graph.IdleCallbacks.Length-1;i>=0;i--)_callbacks.Leave(frame,_graph.IdleCallbacks[i].PropertyIndex);
        _idleComplete=true;
    }
    public void Complete(long frame)
    {
        Check(frame);if(_complete||_idleStarted&&!_idleComplete)throw new ArgumentException("Standing source has not returned.");
        _callbacks.Leave(frame,_rotate.PropertyIndex);_callbacks.ValidateCommit(frame);_complete=true;
    }
    private void Check(long frame)
    {
        if(!_prepared||frame!=_identity.FrameId)throw new ArgumentException("Invalid Standing rest frame.");
        _owner!.ValidateContext(_identity,_graph.CatalogDigest);
    }
    public void ValidateCommit(long frame){Check(frame);if(!_complete)throw new ArgumentException("Standing rest source incomplete.");_callbacks.ValidateCommit(frame);}
    internal bool IdleUpdated(in AlsFrameIdentity identity,string digest)
    { ValidateCommit(identity.FrameId);if(identity!=_identity||digest!=_graph.CatalogDigest)throw new ArgumentException("Foreign Standing Slot traversal.");return _idleStarted; }
    public void Commit(long frame){ValidateCommit(frame);_callbacks.Commit(frame);Cancel();}
    public void Cancel(){_prepared=_complete=_idleStarted=_idleComplete=false;_callbacks.Cancel();}
}
