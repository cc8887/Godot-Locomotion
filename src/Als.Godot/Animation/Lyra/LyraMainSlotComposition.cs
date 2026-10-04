using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Original Slot/operator/cache positions. The Main owner supplies evaluated
// linked-layer boundaries; callbacks are never invoked for a hidden source.
internal sealed class LyraMainSlotComposition:IDisposable
{
    private readonly LyraMontageTrackSampler _sampler;
    private readonly LyraMontageSlotPose _slots;
    private readonly LyraMainCompositionOperators _operators;
    private readonly LyraMainPoseCacheScope _cache;
    private readonly LyraCompositionPoseBuffer[] _outputs,_inputs;
    private readonly LyraCompositionPoseBuffer _reference,_aimed,_additive,_recovery,_root;
    private int _busy;private bool _disposed;
    public int LastLocomotionEvaluations{get;private set;}
    public int LastSplitEvaluations{get;private set;}
    public int LastInputEvaluations{get;private set;}
    private readonly int[] _sourceEvaluations=new int[5];
    public int SourceEvaluations(int slot)=>_sourceEvaluations[slot];
    internal LyraLayerPoseInput DiagnosticAimInput=>_aimed.Input;
    internal LyraLayerPoseInput DiagnosticLower=>_inputs[0].Input;
    internal LyraLayerPoseInput DiagnosticDynamic=>_operators.DiagnosticDynamic;
    internal LyraLayerPoseInput DiagnosticInertiaOutput=>_recovery.Input;
    public LyraMainSlotComposition(LyraLogicalSourceBank bank,LyraMontageCatalog catalog,string profile,Func<int,AlsPoseCacheLifecycle>? lifecycle=null,
        Action<int,object,AlsGraphTraversalCounter>? validateEvaluation=null)
    {
        _sampler=new(bank,catalog);_slots=new(bank,catalog,_sampler);_operators=new(bank,profile);_cache=new(bank,lifecycle,validateEvaluation);
        _outputs=Enumerable.Range(0,5).Select(_=>new LyraCompositionPoseBuffer(bank)).ToArray();
        _inputs=Enumerable.Range(0,5).Select(_=>new LyraCompositionPoseBuffer(bank)).ToArray();
        _reference=new(bank);_aimed=new(bank);_additive=new(bank);_recovery=new(bank);_root=new(bank);
        for(var b=0;b<81;b++)_reference.Pose[b]=new(default,AlsQuaternion.Identity,default);
    }
    public void Evaluate(AlsMontageFrame frame,AlsFrameIdentity identity,float dynamicAlpha,float rootYaw,
        Action<LyraCompositionPoseBuffer> locomotion,Action<LyraCompositionPoseBuffer,LyraCompositionPoseBuffer> aiming,
        Action<LyraCompositionPoseBuffer> additives,LyraCompositionPoseBuffer output,
        Action<LyraCompositionPoseBuffer,LyraCompositionPoseBuffer>? inertia=null,object? cacheFrame=null,AlsGraphTraversalCounter? counter=null,
        Action? enterAiming=null)
    {
        if(_disposed||identity!=frame.Identity||identity.SlotGeneration==0||!float.IsFinite(dynamicAlpha)||!float.IsFinite(rootYaw))
            throw new InvalidOperationException("Invalid Main Slot composition frame.");
        ArgumentNullException.ThrowIfNull(locomotion);ArgumentNullException.ThrowIfNull(aiming);ArgumentNullException.ThrowIfNull(additives);
        if(Interlocked.CompareExchange(ref _busy,1,0)!=0)throw new InvalidOperationException("Main Slot composition is already evaluating.");
        var token=new object();_cache.Begin(token,cacheFrame,counter);Array.Clear(_sourceEvaluations);
        try
        {
            LyraCompositionPoseBuffer Slot(int s,Action<LyraCompositionPoseBuffer> source)
            {
                var hasSource=frame.SlotWeights(new(s)).SourceWeight>AlsPoseBlender.WeightThreshold;
                if(hasSource){source(_inputs[s]);_sourceEvaluations[s]++;}
                _slots.Evaluate(frame,identity,new(s),hasSource?_inputs[s].Input:default,_outputs[s]);return _outputs[s];
            }
            LyraCachedPoseView Locomotion()=>_cache.Read(token,83,locomotion);
            LyraCompositionPoseBuffer Aiming()
            {
                enterAiming?.Invoke();
                var input=_cache.Read(token,181,destination=>
                {
                    var split=_cache.Read(token,78,target=>
                    {
                        var preAim=Slot(2,basis=>
                        {
                            // Evaluate visits the base and its additive before
                            // the upper branch; Update traverses upper first.
                            var lower=Locomotion();
                            var additive=_reference;
                            if(dynamicAlpha>AlsPoseBlender.WeightThreshold)additive=Slot(1,a=>a.Copy(_reference.Input));
                            var upper=Slot(0,u=>u.Copy(Locomotion().Input));
                            _operators.Upper(lower.Input,upper.Input,additive.Input,dynamicAlpha,1,basis);
                        });
                        target.Copy(preAim.Input);
                    });
                    destination.Copy(split.Input);
                });
                // Copy cache data into a stable input buffer for the layer;
                // neither its source nor a previous cache lifetime is exposed.
                _aimed.Copy(input.Input);aiming(_aimed,_inputs[3]);return _inputs[3];
            }
            var full=Slot(4,destination=>
            {
                var hit=Slot(3,d=>d.Copy(Aiming().Input));additives(_additive);
                LyraMainCompositionOperators.Additive(hit.Input,_additive.Input,.65f,_recovery);destination.Copy(_recovery.Input);
            });
            var beforeRotate=full;
            if(inertia is not null){inertia(full,_recovery);beforeRotate=_recovery;}
            LyraMainCompositionOperators.RotateRoot(beforeRotate.Input,rootYaw,_root);output.Copy(_root.Input);
            LastLocomotionEvaluations=_cache.Evaluations(83);LastSplitEvaluations=_cache.Evaluations(78);LastInputEvaluations=_cache.Evaluations(181);
        }
        finally{_cache.End();Volatile.Write(ref _busy,0);}
    }
    public void Dispose(){if(Volatile.Read(ref _busy)!=0)throw new InvalidOperationException("Cannot dispose an active Main Slot composition.");if(!_disposed){_sampler.Dispose();_disposed=true;}}
}
