using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

/// <summary>Routes the linked Layering graph's seven Slots through its owner's frozen
/// Montage frame. Input/cache callbacks remain with the upstream source owner. No clock,
/// instance allocator or committed history is owned here.</summary>
public sealed class AlsRefactoredLayerSlotSink : IAlsLayerBlendingSink
{
    private readonly IAlsLayerBlendingSink _inputs;
    private readonly IAlsMontagePoseSource _samples;
    private readonly AlsMontageSlotPose _mixer;
    private readonly AlsPrecisePose[] _source, _output;
    private AlsMontageFrame? _frame;
    private AlsFrameIdentity _identity;
    private ushort _relevant;
    private bool _evaluating;

    public AlsRefactoredLayerSlotSink(IAlsLayerBlendingSink inputs, IAlsMontagePoseSource samples,
        ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<int> parents, int curveCount)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(samples);
        _inputs=inputs; _samples=samples; _mixer=new(reference,parents,curveCount);
        _source=new AlsPrecisePose[reference.Length]; _output=new AlsPrecisePose[reference.Length];
    }

    // Called after Montage Begin and before graph Prepare, again for every discarded retry.
    public void Begin(AlsMontageFrame frame, AlsFrameIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_evaluating || _frame is not null || identity.SlotGeneration==0 || frame.Identity!=identity)
            throw new InvalidOperationException("Layer Slot frame is active or stale.");
        _frame=frame; _identity=identity; _relevant=0;
    }
    public ushort RelevantSlots { get { RequireFrame(); return _relevant; } }
    // Owner completes notification relevance after graph Update, and ends this binding
    // after either graph Commit or Cancel, before releasing the shared Montage frame.
    public void End()
    {
        if(_evaluating)throw new InvalidOperationException("Cannot release an evaluating Layer Slot.");
        _frame=null; _identity=default; _relevant=0;
    }
    private AlsMontageFrame RequireFrame()
    {
        if (_frame is null || _frame.Identity!=_identity)
            throw new InvalidOperationException("Layer Slot frame is absent or stale.");
        return _frame;
    }
    public void InitializeInput(int index,string name)=>_inputs.InitializeInput(index,name);
    public void CacheInputBones(int index,string name)=>_inputs.CacheInputBones(index,name);
    public void UpdateInput(int index,string name,in AlsPoseUpdateContext context)=>_inputs.UpdateInput(index,name,context);
    public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)=>_inputs.EvaluateInput(index,name,pose,curves);
    public void OnCachedUpdatesSkipped(int index,ReadOnlySpan<AlsPoseUpdateContext> skipped)=>_inputs.OnCachedUpdatesSkipped(index,skipped);
    public void InitializeSlot(int index,string name)=>_=AlsMontageSlot.FromRefactoredLayerName(name);
    public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)
    {
        var frame=RequireFrame();
        if(context.Identity!=_identity)throw new ArgumentException("Layer Slot update identity differs.");
        return frame.SlotWeights(AlsMontageSlot.FromRefactoredLayerName(name));
    }
    public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)
    {
        if(GetSlotWeights(index,name,context)!=weights)throw new ArgumentException("Layer Slot update weights differ.");
        // FAnimInstanceProxy::UpdateSlotNodeWeight tracks notify relevance from
        // local Montage weight alone once the graph visits this Slot.
        if(weights.SlotNodeWeight>AlsPoseBlender.WeightThreshold)
            _relevant|=AlsMontageSlot.FromRefactoredLayerName(name).Mask;
    }
    public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool sourceEvaluated,
        ReadOnlySpan<AlsLocalPose> sourcePose,ReadOnlySpan<AlsInertialCurve> sourceCurves,
        Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        var frame=RequireFrame(); var slot=AlsMontageSlot.FromRefactoredLayerName(name);
        if(_evaluating || frame.SlotWeights(slot)!=weights || pose.Length!=_output.Length ||
            sourceEvaluated!=(weights.SourceWeight>AlsPoseBlender.WeightThreshold) ||
            sourceEvaluated && sourcePose.Length!=_source.Length)
            throw new ArgumentException("Layer Slot evaluation layout or weights differ.");
        _evaluating=true;
        try
        {
        if(sourceEvaluated)
            for(var i=0;i<_source.Length;i++)
            {
                var p=sourcePose[i];
                _source[i]=new(new(p.Position.X,p.Position.Y,p.Position.Z),
                    new(p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W),new(p.Scale.X,p.Scale.Y,p.Scale.Z));
            }
        _mixer.Evaluate(frame,_identity,slot,sourceEvaluated?_source:[],sourceEvaluated?sourceCurves:[],_output,curves,_samples);
        for(var i=0;i<pose.Length;i++)
        {
            var p=_output[i];
            pose[i]=new(new((float)p.Position.X,(float)p.Position.Y,(float)p.Position.Z),
                new((float)p.Rotation.X,(float)p.Rotation.Y,(float)p.Rotation.Z,(float)p.Rotation.W),
                new((float)p.Scale.X,(float)p.Scale.Y,(float)p.Scale.Z));
        }
        }
        finally { _evaluating=false; }
    }
}
