using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Produces Post Layering, not the outer Aim/spine/hands/Foot IK/Ragdoll result.
// Parent owner prepares all sources, evaluates them, then validates every owner
// before committing. No source clocks or gameplay events are advanced here.
internal sealed class AlsLayerBlendingFrameStage : IAlsLayerBlendingSink
{
    private readonly AlsLayerBlendingRuntime _layer;
    private readonly AlsBasePosesRuntime _bases;
    private readonly AlsBasePosesSourceSampler _sampler;
    private readonly AlsLocalPose[] _base, _overlay, _pose;
    private readonly AlsInertialCurve[] _baseCurves, _overlayCurves, _curves;
    private readonly int[] _baseCurveMap;
    private readonly AlsPoseUpdateContext[] _contexts = new AlsPoseUpdateContext[3];
    private readonly bool[] _updated = new bool[3];
    private readonly int[] _order = new int[3];
    private AlsGraphTraversalCounter _initialization, _bones;
    private AlsAnimationGraphFrame _committedTraversal, _candidateTraversal;
    private AlsFrameIdentity _identity;
    private AlsLayeringInput _input;
    private bool _prepared, _evaluated, _unvisited;
    private int _count;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    internal AlsLayeringInput CandidateInput => _prepared ? _input : throw new InvalidOperationException("No layering input candidate.");
    public AlsBasePosesState BasePosesState => _bases.State;
    public AlsBasePosesState CommittedBasePosesState => _bases.CommittedState;
    public ReadOnlySpan<int> InputUpdateOrder => _order.AsSpan(0,_count);
    public AlsPoseUpdateContext BaseContext => Context(0);
    public AlsPoseUpdateContext OverlayContext => Context(1);
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _pose : throw new InvalidOperationException("Post Layering has not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Post Layering has not evaluated.");

    public AlsLayerBlendingFrameStage(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set,
        AlsAnimationLibraryBuildResult library, ReadOnlySpan<string> names, ReadOnlySpan<string> baseNames)
    {
        _sampler = new(definition.BasePoses,set,library,names,definition.BasePoseRetarget,definition.BasePoseSourceKeys);
        var layout = _sampler.Layout;
        _layer = new(definition.LayerBlending,layout.Names,layout.Parents,names,layout.ReferencePose);
        _bases = new(definition.BasePoses,layout.ReferencePose,names);
        _base = new AlsLocalPose[layout.Names.Length]; _overlay = new AlsLocalPose[_base.Length]; _pose = new AlsLocalPose[_base.Length];
        _baseCurves = new AlsInertialCurve[names.Length]; _overlayCurves = new AlsInertialCurve[names.Length]; _curves = new AlsInertialCurve[names.Length];
        var all = names.ToArray(); _baseCurveMap = baseNames.ToArray().Select(n=>Array.IndexOf(all,n)).ToArray();
        if(_baseCurveMap.Any(i=>i<0)) throw new ArgumentException("Post Layering must preserve every BaseLayer curve name.");
    }

    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput input, ReadOnlySpan<AlsInertialCurve> feedback)
        => Prepare(context,input,feedback,_committedTraversal.Next(context.Identity,(ulong)context.Identity.FrameId));

    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput input, ReadOnlySpan<AlsInertialCurve> feedback,
        in AlsAnimationGraphFrame traversal, bool updateSource = true)
    {
        if(_prepared || input.Identity!=context.Identity) throw new InvalidOperationException("Invalid Post Layering candidate.");
        traversal.Validate(context.Identity); _initialization=traversal.Initialization; _bones=traversal.Bones;
        _candidateTraversal=traversal;
        _identity=context.Identity; _input=input; _count=0; Array.Clear(_updated); _prepared=true; _evaluated=false; _unvisited=!updateSource;
        try
        {
            _bases.BeginCandidate(_identity,_sampler);
            _layer.Prepare(context.WithUpdateCounter(traversal.Update),input,feedback,_initialization,_bones,traversal.Evaluation,this,updateSource);
            // Both are relevant through the original curve tail, even with all
            // upper-body pose weights zero. Do not silently substitute an idle pose.
            if(updateSource){_=BaseContext; _=OverlayContext;}
        }
        catch { Cancel(); throw; }
    }
    public void Evaluate(AlsFrameIdentity identity, ReadOnlySpan<AlsLocalPose> basePose, ReadOnlySpan<AlsInertialCurve> baseCurves,
        ReadOnlySpan<AlsLocalPose> overlayPose, ReadOnlySpan<AlsInertialCurve> overlayCurves)
    {
        if(!_prepared || _unvisited || identity!=_identity || basePose.Length!=_base.Length || overlayPose.Length!=_overlay.Length ||
            baseCurves.Length!=_baseCurveMap.Length || overlayCurves.Length!=_overlayCurves.Length)
            throw new ArgumentException("Post Layering source frame/layout differs.");
        try
        {
            basePose.CopyTo(_base); overlayPose.CopyTo(_overlay); overlayCurves.CopyTo(_overlayCurves); Array.Clear(_baseCurves);
            for(var i=0;i<baseCurves.Length;i++) _baseCurves[_baseCurveMap[i]]=baseCurves[i];
            _layer.Evaluate(_pose,_curves); _evaluated=true;
        }
        catch { Cancel(); throw; }
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if(!_prepared || !(_evaluated || _unvisited) || identity!=_identity) throw new InvalidOperationException("Post Layering is not ready for commit.");
        _layer.ValidateCommit(); _bases.ValidateCommit();
    }
    public void Commit(AlsFrameIdentity identity)
    { ValidateCommit(identity); _layer.Commit(); _bases.Commit(); CommittedIdentity=identity; _committedTraversal=_candidateTraversal; _prepared=_evaluated=false; }
    public void Cancel() { _layer.Cancel(); _bases.Cancel(); _prepared=_evaluated=false; }
    private AlsPoseUpdateContext Context(int source) => _prepared && _updated[source] ? _contexts[source] : throw new InvalidOperationException("Missing linked input update.");
    private static int Source(string name) => name switch {"Base Layer Input"=>0,"Overlay Layer Input"=>1,"Base Poses Input"=>2,_=>throw new ArgumentException("Foreign linked input.")};
    public void InitializeInput(int nodeIndex,string name) { if(Source(name)==2) _bases.Initialize(_initialization); }
    public void CacheInputBones(int nodeIndex,string name) { if(Source(name)==2) _bases.CacheBones(_bones); }
    public void UpdateInput(int nodeIndex,string name,in AlsPoseUpdateContext context)
    {
        var source=Source(name); if(_updated[source]) throw new InvalidOperationException("Linked input was updated twice after cache suppression.");
        _updated[source]=true; _contexts[source]=context; _order[_count++]=source;
        if(source==2) _bases.Update(context,_input);
    }
    public void EvaluateInput(int nodeIndex,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        switch(Source(name))
        {
            case 0: _base.CopyTo(pose); _baseCurves.CopyTo(curves); break;
            case 1: _overlay.CopyTo(pose); _overlayCurves.CopyTo(curves); break;
            case 2: _bases.Evaluate(pose,curves); break;
        }
    }
    // Upper-body slots currently have no registered physical montage assets.
    // Grounded/turn/action slots are consumed inside BaseLayer before this stage.
    public void InitializeSlot(int nodeIndex,string name) { }
    public AlsSlotWeights GetSlotWeights(int nodeIndex,string name,in AlsPoseUpdateContext context) => AlsSlotWeights.Passthrough;
    public void UpdateSlot(int nodeIndex,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context) { }
    public void EvaluateSlot(int nodeIndex,string name,in AlsSlotWeights weights,bool sourceEvaluated,
        ReadOnlySpan<AlsLocalPose> sourcePose,ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        if(!sourceEvaluated || weights!=AlsSlotWeights.Passthrough) throw new InvalidOperationException("An active upper-body montage requires its physical slot consumer.");
        sourcePose.CopyTo(pose); sourceCurves.CopyTo(curves);
    }
    public void OnCachedUpdatesSkipped(int handlerNodeIndex,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    { throw new InvalidOperationException("Post Layering input needs its external skipped-update handler."); }
}
