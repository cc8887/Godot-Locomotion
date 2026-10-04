using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly struct LyraCachedPoseView
{
    private readonly LyraMainPoseCacheScope _owner;private readonly object _token;private readonly AlsCachedPoseScope _scope;private readonly int _node;
    internal LyraCachedPoseView(LyraMainPoseCacheScope owner,object token,AlsCachedPoseScope scope,int node){_owner=owner;_token=token;_scope=scope;_node=node;}
    private LyraCompositionPoseBuffer Buffer=>_owner.Output(_token,_scope,_node);
    public ReadOnlySpan<AlsPrecisePose> Pose=>Buffer.Pose;
    public ReadOnlySpan<LyraCurveSample> Curves=>Buffer.Curves;
    public ReadOnlySpan<LyraAttributeSample> Attributes=>Buffer.Attributes;
    public LyraRootMotionAttribute RootMotion=>Buffer.RootMotion;
    public LyraLayerPoseInput Input=>Buffer.Input;
}

// FCachedPoseScope owns complete data by node identity for one evaluation
// lifetime. A new lifetime clears the map even at the same traversal counter.
// It owns no update history, player, clock, Sync or skeleton publication.
internal sealed class LyraMainPoseCacheScope
{
    private static readonly int[] Nodes=[83,78,181];
    private readonly LyraCompositionPoseBuffer[] _buffers;
    private readonly AlsScopedPoseCache _payloadScopes=new(Nodes.Length,scopeCapacity:1);
    private readonly Func<int,AlsPoseCacheLifecycle>? _lifecycle;
    private readonly Action<int,object,AlsGraphTraversalCounter>? _validateEvaluation;
    private object? _frame;private AlsGraphTraversalCounter _counter;
    private readonly AlsPoseCacheLifecycle?[] _scopeOwners=new AlsPoseCacheLifecycle?[3];
    private object? _token;private AlsCachedPoseScope _scope;
    public bool Active=>_token is not null;
    public LyraMainPoseCacheScope(LyraLogicalSourceBank bank,Func<int,AlsPoseCacheLifecycle>? lifecycle=null,
        Action<int,object,AlsGraphTraversalCounter>? validateEvaluation=null)
    {_buffers=Nodes.Select(_=>new LyraCompositionPoseBuffer(bank)).ToArray();_lifecycle=lifecycle;_validateEvaluation=validateEvaluation;}
    private static int Index(int node){var index=Array.IndexOf(Nodes,node);return index>=0?index:throw new InvalidOperationException("Unbound Main cache node.");}
    public void Begin(object token,object? frame=null,AlsGraphTraversalCounter? counter=null)
    {
        ArgumentNullException.ThrowIfNull(token);if(Active)throw new InvalidOperationException("Main cache lifetime is already open.");
        if(_lifecycle is not null)
        {
            if(frame is null||counter is null||!counter.Value.HasUpdated)throw new InvalidOperationException("Live cache needs its owner frame and evaluation counter.");
            for(var i=0;i<Nodes.Length;i++){var owner=_lifecycle(Nodes[i]);owner.Validate(frame);_scopeOwners[i]=owner;}
            _frame=frame;_counter=counter.Value;
        }
        _payloadScopes.Reset();_scope=_payloadScopes.PushScope();_token=token;
    }
    private void Validate(object token)
    {if(_token is null||!ReferenceEquals(token,_token))throw new InvalidOperationException("Foreign or closed Main cache lifetime.");_payloadScopes.ThrowIfFaulted();_payloadScopes.ValidateScope(_scope);}
    public LyraCachedPoseView Read(object token,int node,Action<LyraCompositionPoseBuffer> evaluate)
    {
        Validate(token);var i=Index(node);ArgumentNullException.ThrowIfNull(evaluate);
        if(_validateEvaluation is not null)
        {
            if(_frame is null)throw new InvalidOperationException("Cache evaluation has no actual Proxy owner frame.");
            _validateEvaluation(node,_frame,_counter);
        }
        var owner=_scopeOwners[i];var actualNode=node==181?78:node;
        var synchronized=owner is null||owner.EvaluationMatches(_frame!,actualNode,_counter);
        if(_payloadScopes.BeginEvaluation(_scope,i,synchronized))
        {
            var failed=true;
            try
            {
                owner?.SynchronizeEvaluation(_frame!,actualNode,_counter);
                evaluate(_buffers[i]);_buffers[i].Validate(_buffers[i].Input);
                _payloadScopes.CompleteEvaluation(_scope,i);failed=false;
            }
            finally{_payloadScopes.EndEvaluation(_scope,i,failed);}
        }
        return new(this,token,_scope,node);
    }
    public int Evaluations(int node)=>_payloadScopes.Evaluations(Index(node));
    internal LyraCompositionPoseBuffer Output(object token,AlsCachedPoseScope scope,int node)
    {Validate(token);var i=Index(node);_scopeOwners[i]?.Validate(_frame!);_payloadScopes.RequireReadable(scope,i);return _buffers[i];}
    public void End(){if(Active)_payloadScopes.PopScope(_scope);_token=null;_frame=null;Array.Clear(_scopeOwners);}
}
