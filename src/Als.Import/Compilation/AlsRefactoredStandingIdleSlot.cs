using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original Standing Idle Slot60 and its fixed source63/61. Owns only
/// Slot source-weight history; the physical montage bank remains the clock owner.</summary>
public sealed class AlsRefactoredStandingIdleSlot
{
    private readonly AlsRefactoredStandingRestPose _rest;
    private readonly AlsRefactoredStandingRestGraph _graph;
    private readonly AlsMontageSlotPose _mixer;
    private readonly IAlsMontagePoseSource _sampler;
    private readonly AlsPrecisePose[] _source, _pose;
    private readonly AlsInertialCurve[] _sourceCurves, _curves;
    private AlsMontageFrame? _frame;
    private AlsFrameIdentity _identity, _committedIdentity;
    private AlsSlotWeights _weights;
    private AlsSlotSourceUpdate _sourceUpdate;
    private float _committedSource;
    private bool _hasCommitted, _evaluated, _faulted;
    public AlsSlotSourceUpdate SourceUpdate { get { Check();return _sourceUpdate; } }
    public AlsSlotWeights Weights { get { Check();return _weights; } }
    public bool SourceEvaluated { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose { get { RequireEvaluation();return _pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { RequireEvaluation();return _curves; } }

    public AlsRefactoredStandingIdleSlot(AlsRefactoredAnimationCatalog catalog, AlsRefactoredStandingRestGraph graph,
        AlsRefactoredStandingRestPose rest, AlsRefactoredRestMontagePose montages)
    {
        if(graph.CatalogDigest!=catalog.IndexDigest || rest.Graph.CatalogDigest!=catalog.IndexDigest || montages.CatalogDigest!=catalog.IndexDigest ||
            !rest.BoneNames.SequenceEqual(montages.BoneNames) || !rest.Parents.SequenceEqual(montages.Parents) ||
            !rest.CurveNames.SequenceEqual(montages.CurveNames))throw new ArgumentException("Foreign Standing Idle Slot layout.");
        _graph=graph;_rest=rest;_sampler=montages.CreateSampler();
        var reference=catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        _mixer=new(reference.ReferencePose,montages.Parents,montages.CurveNames.Length);
        _source=new AlsPrecisePose[rest.BoneNames.Length];_pose=new AlsPrecisePose[_source.Length];
        _sourceCurves=new AlsInertialCurve[rest.CurveNames.Length];_curves=new AlsInertialCurve[_sourceCurves.Length];
    }
    public void Prepare(AlsMontageFrame frame,in AlsPoseUpdateContext context,bool initialize=false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(_frame is not null || frame.Identity!=context.Identity || !context.HasSharedContext || context.UpdateCounter is not {HasUpdated:true} ||
            _hasCommitted&&(context.Identity.CharacterId!=_committedIdentity.CharacterId || context.Identity.SlotGeneration!=_committedIdentity.SlotGeneration ||
                context.Identity.FrameId<=_committedIdentity.FrameId))throw new ArgumentException("Foreign Standing Slot update.");
        var weights=frame.SlotWeights(AlsTurnSlot.Standing);
        var update=AlsSlotSourceUpdate.Resolve(initialize?0:_committedSource,weights,context,_graph.AlwaysUpdateSlotSource);
        _frame=frame;_identity=context.Identity;_weights=weights;_sourceUpdate=update;
        _evaluated=_faulted=SourceEvaluated=false;
    }
    public void Evaluate()
    {
        Check();_evaluated=false;SourceEvaluated=false;
        try
        {
            var hasSource=_weights.SourceWeight>AlsPoseBlender.WeightThreshold;
            if(hasSource){_rest.SampleIdleSource(_source,_sourceCurves);SourceEvaluated=true;}
            _mixer.Evaluate(_frame!,_identity,AlsTurnSlot.Standing,hasSource?_source:[],hasSource?_sourceCurves:[],_pose,_curves,_sampler);
            _evaluated=true;
        }
        catch{_faulted=true;throw;}
    }
    private void Check()
    {
        if(_frame is null || _faulted || _frame.Identity!=_identity || _frame.SlotWeights(AlsTurnSlot.Standing)!=_weights)
            throw new InvalidOperationException("Standing Slot candidate missing, changed or faulted.");
    }
    private void RequireEvaluation(){Check();if(!_evaluated)throw new InvalidOperationException("Standing Slot pose has not been evaluated.");}
    public void ValidateCommit(in AlsFrameIdentity identity){Check();if(identity!=_identity)throw new ArgumentException("Foreign Standing Slot commit.");}
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);_committedSource=_weights.SourceWeight;_committedIdentity=_identity;_hasCommitted=true;Cancel();
    }
    public void Cancel(){_frame=null;_evaluated=_faulted=SourceEvaluated=false;}
}
