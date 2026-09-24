using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>Native-centimetre Refactored Layering -> Head stage. Source and Montage
/// owners remain external and must validate together before any owner commits.
/// Parent state reads only the last committed curves, never this frame's output.</summary>
public sealed class AlsRefactoredPostLayerRuntime
{
    private readonly AlsLayerBlendingRuntime _layer;
    private readonly AlsRefactoredHeadRuntime _head;
    private readonly AlsRefactoredLayeringInputModel _inputs;
    private readonly AlsLocalPose[] _layerPose;
    private readonly AlsPrecisePose[] _basis,_pose;
    private readonly AlsInertialCurve[] _layerCurves,_curves,_feedback;
    private readonly int _viewBlock,_aiming;
    private readonly uint _character,_generation;
    private AlsAnimationGraphFrame _graph;
    private AlsRefactoredViewState _view=AlsRefactoredViewState.Initial;
    private AlsRefactoredSpineState _spine=AlsRefactoredSpineState.Initial;
    private bool _prepared,_evaluated,_visited,_busy;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsRefactoredViewState CommittedView { get; private set; }=AlsRefactoredViewState.Initial;
    public AlsRefactoredSpineState CommittedSpine { get; private set; }=AlsRefactoredSpineState.Initial;
    public AlsRefactoredHeadState CommittedHead=>_head.Committed.Head;
    public AlsRefactoredViewState CandidateView=>_prepared?_view:throw new InvalidOperationException("No post-layer candidate.");
    public AlsRefactoredSpineState CandidateSpine=>_prepared?_spine:throw new InvalidOperationException("No post-layer candidate.");
    public AlsRefactoredHeadFrame CandidateHead=>_prepared?_head.Candidate:throw new InvalidOperationException("No post-layer candidate.");
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated?_pose:throw new InvalidOperationException("Post-layer pose is not ready.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated?_curves:throw new InvalidOperationException("Post-layer curves are not ready.");

    public AlsRefactoredPostLayerRuntime(uint character,uint generation,AlsLayerBlendingDefinition definition,
        ReadOnlySpan<string> names,ReadOnlySpan<int> parents,ReadOnlySpan<string> curves,ReadOnlySpan<AlsLocalPose> reference,
        AlsRefactoredHeadSettings settings,IAlsRefactoredLookPoseSampler look)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if(definition.PropertySchema!=AlsLayerPropertySchema.Refactored)throw new ArgumentException("Expected Refactored graph.");
        _layer=new(definition,names,parents,curves,reference);_head=new(character,generation,settings,parents,look);
        _character=character;_generation=generation;_inputs=new(curves);
        _layerPose=new AlsLocalPose[names.Length];_basis=new AlsPrecisePose[names.Length];_pose=new AlsPrecisePose[names.Length];
        _layerCurves=new AlsInertialCurve[curves.Length];_curves=new AlsInertialCurve[curves.Length];_feedback=new AlsInertialCurve[curves.Length];
        var curveNames=curves.ToArray();_viewBlock=Array.IndexOf(curveNames,"ViewBlock");_aiming=Array.IndexOf(curveNames,"PoseAiming");
        if(_viewBlock<0||_aiming<0)throw new ArgumentException("Post-layer feedback requires ViewBlock and PoseAiming.");
    }

    public void Prepare(in AlsAnimationGraphFrame graph,in AlsRefactoredViewInput viewInput,IAlsLayerBlendingSink sources,bool visited=true)
    {
        if(_prepared||_busy)throw new InvalidOperationException("Finish the post-layer candidate first.");
        graph.Validate(graph.Identity);
        if(graph.Identity.CharacterId!=_character||graph.Identity.SlotGeneration!=_generation||
            CommittedIdentity!=default&&graph.Identity.FrameId<=CommittedIdentity.FrameId)
            throw new ArgumentException("Post-layer owner or frame differs.");
        _prepared=true;_evaluated=false;_visited=visited;_graph=graph;_busy=true;
        try
        {
            var input=viewInput with {ViewBlock=ReadCurve(_viewBlock),PoseAiming=ReadCurve(_aiming)};
            (_view,_spine)=AlsRefactoredViewModel.RefreshView(input,CommittedView,CommittedSpine);
            var feedback=CommittedIdentity==default?ReadOnlySpan<AlsInertialCurve>.Empty:_feedback;
            var layering=_inputs.Evaluate(graph.Identity,CommittedIdentity,feedback);
            var context=new AlsPoseUpdateContext(graph.Identity,1,input.Delta).WithUpdateCounter(graph.Update);
            _layer.Prepare(context,layering,feedback,graph.Initialization,graph.Bones,graph.Evaluation,sources,visited);
            _head.Prepare(graph,input,_view,visited);
        }
        catch { _busy=false;Cancel();throw; }
        finally {_busy=false;}
    }
    public void Evaluate()
    {
        if(!_prepared||!_visited||_busy)throw new InvalidOperationException("Post-layer graph is not evaluable.");
        _busy=true;
        try
        {
            _layer.Evaluate(_layerPose,_layerCurves);
            for(var i=0;i<_basis.Length;i++)
            {
                var p=_layerPose[i];
                _basis[i]=new(new(p.Position.X,p.Position.Y,p.Position.Z),new(p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W),new(p.Scale.X,p.Scale.Y,p.Scale.Z));
            }
            _head.Evaluate(_basis,_layerCurves,_pose,_curves);_evaluated=true;
        }
        catch {_busy=false;Cancel();throw;}
        finally {_busy=false;}
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if(!_prepared||_busy||identity!=_graph.Identity||_visited&&!_evaluated)
            throw new InvalidOperationException("Post-layer candidate is not ready to commit.");
        _layer.ValidateCommit();_head.ValidateCommit(identity);
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);_layer.Commit();_head.Commit(identity);
        CommittedIdentity=identity;CommittedView=_view;CommittedSpine=_spine;
        if(_evaluated)_curves.CopyTo(_feedback,0);
        _prepared=_evaluated=false;
    }
    public void Cancel()
    {
        if(_busy)throw new InvalidOperationException("Cannot cancel from a post-layer source callback.");
        _layer.Cancel();_head.Cancel();_prepared=_evaluated=false;
    }
    private float ReadCurve(int index)=>CommittedIdentity!=default&&_feedback[index].Present?_feedback[index].Value:0;
}
