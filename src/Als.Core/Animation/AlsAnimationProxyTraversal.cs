using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

// One actual graph instance owns its phase history. The enclosing Main owns
// the transaction, including providers whose roots were not visited this frame.
public sealed class AlsAnimationProxyTraversal
{
    private object? _frame;
    private AlsAnimationProxyCounters _candidate;
    private ulong _externalFrame;
    private bool _rootEvaluation,_linkedEvaluation;
    public AlsAnimationProxyCounters Committed {get;private set;}
    public bool HasPending=>_frame is not null;
    public void SetIdle(AlsAnimationProxyPhase phase,AlsGraphTraversalCounter counter)
    {
        if(HasPending)throw new InvalidOperationException("A pending Proxy phase cannot be replaced.");
        Committed=Committed.WithCounter(phase,counter);_candidate=Committed;
    }
    public void Begin(object frame,ulong externalFrame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(HasPending||externalFrame==ulong.MaxValue)throw new InvalidOperationException("Invalid or pending Proxy traversal frame.");
        _frame=frame;_externalFrame=externalFrame;_candidate=Committed;_rootEvaluation=_linkedEvaluation=false;
    }
    public void Validate(object frame)
    {if(!ReferenceEquals(frame,_frame))throw new InvalidOperationException("Foreign or stale Proxy traversal frame.");}
    public AlsAnimationProxyCounters Prepared(object frame){Validate(frame);return _candidate;}
    public ulong ExternalFrame(object frame){Validate(frame);return _externalFrame;}
    public bool MainEvaluationEntered(object frame){Validate(frame);return _rootEvaluation;}
    public bool EvaluationEntered(object frame){Validate(frame);return _rootEvaluation||_linkedEvaluation;}
    public AlsGraphTraversalCounter Advance(object frame,AlsAnimationProxyPhase phase)
    {
        Validate(frame);_candidate=_candidate.AdvanceMainRoot(phase,_externalFrame);
        if(phase==AlsAnimationProxyPhase.Evaluation)_rootEvaluation=true;
        return _candidate.Counter(phase);
    }
    public void Synchronize(object frame,AlsAnimationProxyPhase phase,in AlsAnimationProxyCounters caller)
    {Validate(frame);_candidate=_candidate.SynchronizeLinked(phase,caller);if(phase==AlsAnimationProxyPhase.Evaluation)_linkedEvaluation=true;}
    public void Commit(object frame){Validate(frame);Committed=_candidate;_frame=null;_rootEvaluation=_linkedEvaluation=false;}
    public void Cancel(){_frame=null;_candidate=Committed;_rootEvaluation=_linkedEvaluation=false;}
}
