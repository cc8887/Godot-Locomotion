namespace GodotAls.Core.Locomotion;

// AB_Als_Layering: reset Overlay slot-control curves, evaluate the Curves Slot,
// accumulate with cached Locomotion curves, then Override the pose branch curves.
// The caller must evaluate the actual Curves Slot between these two operations.
public sealed class AlsRefactoredLayeringCurveTail
{
    private readonly int[] _reset;
    private readonly int _count;
    public AlsRefactoredLayeringCurveTail(ReadOnlySpan<string> names)
    {
        var layout=names.ToArray();_count=layout.Length;
        if(layout.Any(string.IsNullOrWhiteSpace)||layout.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=layout.Length)
            throw new ArgumentException("Invalid layering tail curve layout.");
        string[] reset=["LayerHeadSlot","LayerArmLeftSlot","LayerArmRightSlot","LayerSpineSlot","LayerPelvisSlot","LayerLegsSlot"];
        _reset=reset.Select(name=>Array.FindIndex(layout,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(_reset.Any(i=>i<0))throw new ArgumentException("Layering tail requires all six authored slot curves.");
    }
    public void PrepareOverlay(ReadOnlySpan<AlsInertialCurve> overlay,Span<AlsInertialCurve> beforeCurvesSlot)
    {
        if(overlay.Length!=_count||beforeCurvesSlot.Length!=_count||overlay.Overlaps(beforeCurvesSlot,out var offset)&&offset!=0)
            throw new ArgumentException("Invalid layering tail buffers.");
        foreach(var curve in overlay)if(curve.Present&&!float.IsFinite(curve.Value))throw new ArgumentException("Nonfinite Overlay curve.");
        overlay.CopyTo(beforeCurvesSlot);
        // Native ModifyCurve default Blend mode, alpha=1: these names become present even if absent upstream.
        foreach(var index in _reset)beforeCurvesSlot[index]=new(0);
    }
    public void Compose(ReadOnlySpan<AlsInertialCurve> locomotion,ReadOnlySpan<AlsInertialCurve> afterCurvesSlot,
        Span<AlsInertialCurve> finalCurves)
    {
        if(locomotion.Length!=_count)throw new ArgumentException("Layering tail layout differs.");
        // Accumulate defaults to amount=1. The outer Override also has amount=1,
        // so no curve surviving only in the pose branch is retained.
        AlsLayeringCurves.Apply(locomotion,afterCurvesSlot,1,finalCurves);
    }
}
