using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsBaseLayerActionSlot : IAlsBaseLayerSlotPoseSink, IDisposable
{
    private readonly AlsStandingTurnSlot _slot;
    private readonly AlsLocalPose[] _rest;
    private readonly AlsPrecisePose[] _preciseRest;
    private readonly string[] _names;
    private AlsStandingTurnSlotInput _input;
    public AlsBaseLayerActionSlot(AlsAnimationLibraryBuildResult library,AlsAnimationSetDefinition set,
        ReadOnlySpan<int> animations,ReadOnlySpan<AlsLocalPose> rest,ReadOnlySpan<string> names)
    {
        if (animations.IsEmpty) throw new ArgumentException("BaseLayer actions require a bound animation set.");
        _slot=new(library,set,null,animations); _rest=rest.ToArray();
        _preciseRest=library.MovementSources(set,set.Animations[animations[0]].SkeletonId).PreciseReferencePose.ToArray();
        _names=names.ToArray();
    }
    public void Prepare(AlsMontageFrame frame,AlsFrameIdentity identity) =>
        _input=new(0,0,0,0,0,0,frame,identity,AlsMontageSlot.BaseLayer);
    public void EvaluateSlot(in AlsSlotWeights weights,ReadOnlySpan<AlsLocalPose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> output,Span<AlsInertialCurve> curves)
    {
        _slot.Validate(_input);
        if(_input.MontageFrame!.SlotWeights(AlsMontageSlot.BaseLayer)!=weights || output.Length!=_rest.Length || curves.Length!=_names.Length ||
            !source.IsEmpty && source.Length!=output.Length || !sourceCurves.IsEmpty && sourceCurves.Length!=curves.Length)
            throw new ArgumentException("Authored BaseLayer Slot layout or weights differ.");
        if(source.IsEmpty)_rest.CopyTo(output); else source.CopyTo(output);
        _slot.Compose(_input,_rest,output);
        for(var i=0;i<curves.Length;i++) curves[i]=_slot.CurveWithPresence(_input,_names[i],sourceCurves.IsEmpty ? default:sourceCurves[i]);
    }
    public void Dispose()=>_slot.Dispose();

    public void EvaluateSlot(in AlsSlotWeights weights,ReadOnlySpan<AlsPrecisePose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsPrecisePose> output,Span<AlsInertialCurve> curves)
    {
        _slot.Validate(_input);
        if(_input.MontageFrame!.SlotWeights(AlsMontageSlot.BaseLayer)!=weights || output.Length!=_preciseRest.Length || curves.Length!=_names.Length ||
            !source.IsEmpty && source.Length!=output.Length || !sourceCurves.IsEmpty && sourceCurves.Length!=curves.Length)
            throw new ArgumentException("Authored BaseLayer Slot layout or weights differ.");
        if(source.IsEmpty)_preciseRest.CopyTo(output); else source.CopyTo(output);
        _slot.Compose(_input,_preciseRest,output);
        for(var i=0;i<curves.Length;i++) curves[i]=_slot.CurveWithPresence(_input,_names[i],sourceCurves.IsEmpty ? default:sourceCurves[i]);
    }
}
