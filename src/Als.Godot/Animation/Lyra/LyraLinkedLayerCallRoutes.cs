using System.Collections.Immutable;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLinkedLayerCallTarget(LyraLinkedLayerCallRoutes Owner,
    LyraLayerCallSite CallSite,int MainNode,AlsLinkedLayerTarget Target,long Epoch,int PoseInputs);

// One entry per real call site. Self belongs to Main and has no Linked instance;
// an unbound target must not create an invisible replacement Provider either.
// Registry values are the actual graph owners allocated by the binding result.
internal sealed class LyraLinkedLayerCallRoutes
{
    private readonly IReadOnlyDictionary<long,LyraItemLayerGraphInstance> _instances;
    private readonly LyraLogicalSourceBank _layout;
    private readonly Dictionary<string,LyraLinkedLayerCallTarget> _calls=new(StringComparer.Ordinal);
    private readonly Dictionary<LyraLayerHook,string> _hooks=[];
    private bool _retired;

    internal LyraLinkedLayerCallRoutes(LyraMainLayerGraphCatalog graphs,LyraLinkedLayerContracts contracts,
        ImmutableArray<AlsLinkedLayerTarget> targets,LyraMainGraphStateOwner main,
        IReadOnlyDictionary<long,LyraItemLayerGraphInstance> instances)
    {
        _instances=instances.ToDictionary(p=>p.Key,p=>p.Value);_layout=main.Layout;
        if(targets.Length!=contracts.CallSites.Count)throw new ArgumentException("Incomplete call-site target inventory.");
        for(var n=0;n<targets.Length;n++)
        {
            var call=contracts.CallSites[n];var target=targets[n];long epoch=0;
            switch(target.Kind)
            {
                case AlsLinkedLayerTargetKind.External:
                    if(target.Instance<=0||!_instances.TryGetValue(target.Instance,out var instance)||instance.ClassPath!=target.Class||
                        !ReferenceEquals(instance.Sources.MainOwner,main)||instance.IsRetired)
                        throw new InvalidOperationException("Call site has a foreign, missing or retired graph owner.");
                    var group=instance.Contract.Functions[call.Hook].Group;
                    if(!string.Equals(string.Equals(group,"None",StringComparison.OrdinalIgnoreCase)?"":group,target.Group,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Call-site group differs from its graph owner.");
                    epoch=instance.Epoch;break;
                case AlsLinkedLayerTargetKind.Self:
                    if(target.Class!=graphs.DefaultLayers.ClassPath||target.Instance!=0)
                        throw new InvalidOperationException("Self layer must belong to Main.");
                    _=graphs.DefaultLayers.Graph(call.Hook);break;
                case AlsLinkedLayerTargetKind.Unbound:
                    if(target.Class is not null||target.Instance!=0)throw new InvalidOperationException("Invalid empty call-site target.");
                    break;
                default:throw new ArgumentOutOfRangeException(nameof(targets));
            }
            _calls.Add(call.Node,new(this,call,graphs.MainCalls[call.Hook],target,epoch,graphs.DefaultLayers.InputPoseCount(call.Hook)));
            if(!_hooks.TryAdd(call.Hook,call.Node))throw new NotSupportedException("Hook-only callers must use explicit call-site identities for repeated functions.");
        }
        if(_instances.Keys.Except(targets.Where(t=>t.Kind==AlsLinkedLayerTargetKind.External).Select(t=>t.Instance)).Any())
            throw new InvalidOperationException("Unreferenced graph owner in the call-site registry.");
    }
    internal LyraLinkedLayerCallTarget Call(LyraLayerHook hook)=>Call(_hooks[hook]);
    internal IEnumerable<LyraLinkedLayerCallTarget> PhaseCalls=>!_retired?_calls.Values:
        throw new InvalidOperationException("Retired call-site routes.");
    internal LyraLinkedLayerCallTarget Call(string node)
    {if(_retired)throw new InvalidOperationException("Retired call-site routes.");return _calls[node];}
    private void Validate(in LyraLinkedLayerCallTarget call)
    {
        if(_retired||!ReferenceEquals(call.Owner,this)||!_calls.TryGetValue(call.CallSite.Node,out var actual)||actual!=call)
            throw new InvalidOperationException("Foreign, stale or retired call-site invocation.");
    }
    internal LyraItemLayerGraphInstance External(in LyraLinkedLayerCallTarget call)
    {
        Validate(call);
        if(call.Target.Kind!=AlsLinkedLayerTargetKind.External)throw new InvalidOperationException("This call has no external graph instance.");
        var instance=_instances[call.Target.Instance];
        if(instance.IsRetired||instance.Epoch!=call.Epoch)throw new InvalidOperationException("Retired call-site graph owner.");
        return instance;
    }
    private void RootPhase(in LyraLinkedLayerCallTarget call,Action<LyraItemLayerGraphInstance,LyraLayerHook> external)
    {
        if(call.Target.Kind==AlsLinkedLayerTargetKind.External)external(External(call),call.CallSite.Hook);
        // Imported Main default functions contain a single empty Result root.
        // Their phase callback has no children, but the enclosing node still
        // traverses all input poses during full Initialize/CacheBones.
    }
    internal void Initialize(in LyraLinkedLayerCallTarget call,ReadOnlySpan<Action> inputs,
        Action<LyraItemLayerGraphInstance,LyraLayerHook> initializeExternal)
    {
        Validate(call);if(inputs.Length!=call.PoseInputs)throw new ArgumentException("Call-site pose argument count differs from its interface.");var captured=call;
        AlsLinkedLayerExecution.Initialize(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,inputs,()=>RootPhase(captured,initializeExternal));
    }
    internal void CacheBones(in LyraLinkedLayerCallTarget call,ReadOnlySpan<Action> inputs,
        Action<LyraItemLayerGraphInstance,LyraLayerHook> cacheExternal)
    {
        Validate(call);if(inputs.Length!=call.PoseInputs)throw new ArgumentException("Call-site pose argument count differs from its interface.");var captured=call;
        AlsLinkedLayerExecution.CacheBones(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,inputs,()=>RootPhase(captured,cacheExternal));
    }
    internal void InitializeSubGraph(in LyraLinkedLayerCallTarget call,
        Action<LyraItemLayerGraphInstance,LyraLayerHook> initializeExternal)
    {
        Validate(call);var captured=call;
        AlsLinkedLayerExecution.InitializeSubGraph(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,()=>RootPhase(captured,initializeExternal));
    }
    internal void CacheBonesSubGraph(in LyraLinkedLayerCallTarget call,
        Action<LyraItemLayerGraphInstance,LyraLayerHook> cacheExternal)
    {
        Validate(call);var captured=call;
        AlsLinkedLayerExecution.CacheBonesSubGraph(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,()=>RootPhase(captured,cacheExternal));
    }
    internal void Update(in LyraLinkedLayerCallTarget call,ReadOnlySpan<Action> inputs,
        Action<LyraItemLayerGraphInstance,LyraLayerHook> updateExternal)
    {
        Validate(call);if(inputs.Length!=call.PoseInputs)throw new ArgumentException("Call-site pose argument count differs from its interface.");var captured=call;
        AlsLinkedLayerExecution.Update(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,inputs,()=>
            {if(captured.Target.Kind==AlsLinkedLayerTargetKind.External)updateExternal(External(captured),captured.CallSite.Hook);});
    }
    internal void Evaluate(in LyraLinkedLayerCallTarget call,ReadOnlySpan<Action<LyraCompositionPoseBuffer>> inputs,
        LyraCompositionPoseBuffer output,bool additive,
        Action<LyraItemLayerGraphInstance,LyraLayerHook,LyraCompositionPoseBuffer> evaluateExternal)
    {
        Validate(call);
        if(inputs.Length!=call.PoseInputs||!ReferenceEquals(output.Layout,_layout))throw new ArgumentException("Call-site pose arguments or output layout differ from its interface.");
        var captured=call;
        void ResetPose(LyraCompositionPoseBuffer destination)
        {
            if(additive)Array.Fill(destination.Pose,new AlsPrecisePose(default,AlsQuaternion.Identity,default));
            else destination.Layout.Reference.CopyTo(destination.Pose);
        }
        AlsLinkedLayerExecution.Evaluate(call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,
            call.Target.Kind!=AlsLinkedLayerTargetKind.Unbound,inputs,destination=>
            {
                if(captured.Target.Kind==AlsLinkedLayerTargetKind.External)evaluateExternal(External(captured),captured.CallSite.Hook,destination);
                else ResetPose(destination); // The imported self Root has no Result link.
            },ResetPose,output);
    }
    internal void Retire()=>_retired=true;
}
