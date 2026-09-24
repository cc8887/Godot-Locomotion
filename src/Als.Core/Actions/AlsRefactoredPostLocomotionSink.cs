using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

/// <summary>The parent graph's PostLocomotion Slot, before the linked Layering
/// input. Shares the frozen Montage frame with regional Slots; owns only this
/// Slot's source-weight history. Upstream sources retain their own transactions.</summary>
public sealed class AlsRefactoredPostLocomotionSink:IAlsLayerBlendingSink
{
    private readonly IAlsLayerBlendingSink _inputs;
    private readonly IAlsMontagePoseSource _samples;
    private readonly AlsMontageSlotPose _mixer;
    private readonly AlsLocalPose[] _local;
    private readonly AlsPrecisePose[] _source,_output;
    private readonly AlsInertialCurve[] _curves;
    private AlsMontageFrame? _frame;
    private AlsFrameIdentity _identity;
    private AlsSlotWeights _weights;
    private float _committedSource,_candidateSource,_previousSource;
    private bool _visited,_evaluated,_busy;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ushort RelevantSlots=>_frame is not null&&_visited&&_weights.SlotNodeWeight>AlsPoseBlender.WeightThreshold?AlsMontageSlot.PostLocomotion.Mask:(ushort)0;
    public AlsRefactoredPostLocomotionSink(IAlsLayerBlendingSink inputs,IAlsMontagePoseSource samples,
        ReadOnlySpan<AlsPrecisePose> reference,ReadOnlySpan<int> parents,int curveCount)
    {
        ArgumentNullException.ThrowIfNull(inputs);ArgumentNullException.ThrowIfNull(samples);
        _inputs=inputs;_samples=samples;_mixer=new(reference,parents,curveCount);
        _local=new AlsLocalPose[reference.Length];_source=new AlsPrecisePose[reference.Length];_output=new AlsPrecisePose[reference.Length];_curves=new AlsInertialCurve[curveCount];
    }
    public void Begin(AlsMontageFrame frame,AlsFrameIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(_frame is not null||_busy||identity.SlotGeneration==0||frame.Identity!=identity||CommittedIdentity!=default&&
            (identity.CharacterId!=CommittedIdentity.CharacterId||identity.SlotGeneration!=CommittedIdentity.SlotGeneration||identity.FrameId<=CommittedIdentity.FrameId))
            throw new InvalidOperationException("PostLocomotion frame is active, foreign or stale.");
        _frame=frame;_identity=identity;_weights=frame.SlotWeights(AlsMontageSlot.PostLocomotion);
        _previousSource=_candidateSource=_committedSource;_visited=_evaluated=false;
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        RequireFrame();if(identity!=_identity||_busy||_visited&&!_evaluated)throw new InvalidOperationException("PostLocomotion candidate is incomplete.");
    }
    public void Commit(AlsFrameIdentity identity)
    {ValidateCommit(identity);_committedSource=_candidateSource;CommittedIdentity=identity;Cancel();}
    public void Cancel()
    {if(_busy)throw new InvalidOperationException("PostLocomotion callback is active.");_frame=null;_visited=_evaluated=false;}
    private AlsMontageFrame RequireFrame()=>_frame is not null&&_frame.Identity==_identity&&_frame.SlotWeights(AlsMontageSlot.PostLocomotion)==_weights
        ?_frame:throw new InvalidOperationException("PostLocomotion frozen frame changed or ended.");
    public void InitializeInput(int index,string name)
    {
        RequireFrame();if(name=="Locomotion Input")_previousSource=_candidateSource=0;
        _inputs.InitializeInput(index,name);
    }
    public void CacheInputBones(int index,string name)=>_inputs.CacheInputBones(index,name);
    public void UpdateInput(int index,string name,in AlsPoseUpdateContext context)
    {
        RequireFrame();if(context.Identity!=_identity)throw new ArgumentException("PostLocomotion update frame differs.");
        if(name!="Locomotion Input"){_inputs.UpdateInput(index,name,context);return;}
        if(_visited)throw new InvalidOperationException("PostLocomotion linked input updated twice.");
        _visited=true;
        var source=AlsSlotSourceUpdate.Resolve(_previousSource,_weights,context,alwaysUpdateSource:false);
        _candidateSource=_weights.SourceWeight;
        if(source.Updated)_inputs.UpdateInput(index,name,source.Context);
    }
    public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        var frame=RequireFrame();if(name!="Locomotion Input"){_inputs.EvaluateInput(index,name,pose,curves);return;}
        if(_busy||!_visited||pose.Length!=_local.Length||curves.Length!=_curves.Length)throw new InvalidOperationException("PostLocomotion pose layout or traversal differs.");
        _busy=true;
        try
        {
            var hasSource=_weights.SourceWeight>AlsPoseBlender.WeightThreshold;
            if(hasSource)
            {
                _inputs.EvaluateInput(index,name,_local,_curves);
                for(var i=0;i<_local.Length;i++)
                {var p=_local[i];_source[i]=new(new(p.Position.X,p.Position.Y,p.Position.Z),new(p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W),new(p.Scale.X,p.Scale.Y,p.Scale.Z));}
            }
            _mixer.Evaluate(frame,_identity,AlsMontageSlot.PostLocomotion,hasSource?_source:[],hasSource?_curves:[],_output,curves,_samples);
            for(var i=0;i<pose.Length;i++)
            {var p=_output[i];pose[i]=new(new((float)p.Position.X,(float)p.Position.Y,(float)p.Position.Z),new((float)p.Rotation.X,(float)p.Rotation.Y,(float)p.Rotation.Z,(float)p.Rotation.W),new((float)p.Scale.X,(float)p.Scale.Y,(float)p.Scale.Z));}
            _evaluated=true;
        }
        catch{_evaluated=false;throw;}
        finally{_busy=false;}
    }
    public void InitializeSlot(int index,string name)=>_inputs.InitializeSlot(index,name);
    public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>_inputs.GetSlotWeights(index,name,context);
    public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)=>_inputs.UpdateSlot(index,name,weights,source,context);
    public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool evaluated,ReadOnlySpan<AlsLocalPose> pose,ReadOnlySpan<AlsInertialCurve> curves,
        Span<AlsLocalPose> output,Span<AlsInertialCurve> result)=>_inputs.EvaluateSlot(index,name,weights,evaluated,pose,curves,output,result);
    public void OnCachedUpdatesSkipped(int index,ReadOnlySpan<AlsPoseUpdateContext> contexts)=>_inputs.OnCachedUpdatesSkipped(index,contexts);
}
