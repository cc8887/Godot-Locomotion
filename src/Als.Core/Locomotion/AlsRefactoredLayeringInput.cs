using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Refactored RefreshLayering reads raw float curves, including negative values.
// Graph alpha nodes apply their own policies later; these are not V4 UpdateLayerValues.
public readonly record struct AlsRefactoredLayeringInput
{
    public AlsFrameIdentity Identity { get; init; }
    public AlsFrameIdentity FeedbackIdentity { get; init; }
    public float HeadBlendAmount { get; init; }
    public float HeadAdditiveBlendAmount { get; init; }
    public float HeadSlotBlendAmount { get; init; }
    public float ArmLeftBlendAmount { get; init; }
    public float ArmLeftAdditiveBlendAmount { get; init; }
    public float ArmLeftSlotBlendAmount { get; init; }
    public float ArmLeftLocalSpaceBlendAmount { get; init; }
    public float ArmLeftMeshSpaceBlendAmount { get; init; }
    public float ArmRightBlendAmount { get; init; }
    public float ArmRightAdditiveBlendAmount { get; init; }
    public float ArmRightSlotBlendAmount { get; init; }
    public float ArmRightLocalSpaceBlendAmount { get; init; }
    public float ArmRightMeshSpaceBlendAmount { get; init; }
    public float HandLeftBlendAmount { get; init; }
    public float HandRightBlendAmount { get; init; }
    public float SpineBlendAmount { get; init; }
    public float SpineAdditiveBlendAmount { get; init; }
    public float SpineSlotBlendAmount { get; init; }
    public float PelvisBlendAmount { get; init; }
    public float PelvisSlotBlendAmount { get; init; }
    public float LegsBlendAmount { get; init; }
    public float LegsSlotBlendAmount { get; init; }
    // Only the curve factors from RefreshView. View angles, action freeze, and
    // RefreshSpine's stateful yaw transitions are not computed by this reader.
    public float ViewAmount { get; init; }
    public float ViewHeadBlendAmount { get; init; }
    public float ViewSpineBlendAmount { get; init; }
    public float GetValue(string name)=>name switch
    {
        "HeadBlendAmount"=>HeadBlendAmount,"HeadAdditiveBlendAmount"=>HeadAdditiveBlendAmount,"HeadSlotBlendAmount"=>HeadSlotBlendAmount,
        "ArmLeftBlendAmount"=>ArmLeftBlendAmount,"ArmLeftAdditiveBlendAmount"=>ArmLeftAdditiveBlendAmount,
        "ArmLeftSlotBlendAmount"=>ArmLeftSlotBlendAmount,"ArmLeftLocalSpaceBlendAmount"=>ArmLeftLocalSpaceBlendAmount,
        "ArmLeftMeshSpaceBlendAmount"=>ArmLeftMeshSpaceBlendAmount,
        "ArmRightBlendAmount"=>ArmRightBlendAmount,"ArmRightAdditiveBlendAmount"=>ArmRightAdditiveBlendAmount,
        "ArmRightSlotBlendAmount"=>ArmRightSlotBlendAmount,"ArmRightLocalSpaceBlendAmount"=>ArmRightLocalSpaceBlendAmount,
        "ArmRightMeshSpaceBlendAmount"=>ArmRightMeshSpaceBlendAmount,
        "HandLeftBlendAmount"=>HandLeftBlendAmount,"HandRightBlendAmount"=>HandRightBlendAmount,
        "SpineBlendAmount"=>SpineBlendAmount,"SpineAdditiveBlendAmount"=>SpineAdditiveBlendAmount,"SpineSlotBlendAmount"=>SpineSlotBlendAmount,
        "PelvisBlendAmount"=>PelvisBlendAmount,"PelvisSlotBlendAmount"=>PelvisSlotBlendAmount,
        "LegsBlendAmount"=>LegsBlendAmount,"LegsSlotBlendAmount"=>LegsSlotBlendAmount,
        _=>throw new ArgumentException("Unknown Refactored LayeringState property: "+name)
    };
}

public sealed class AlsRefactoredLayeringInputModel
{
    private static readonly string[] RequiredNames=[
        "LayerHead","LayerHeadAdditive","LayerHeadSlot",
        "LayerArmLeft","LayerArmLeftAdditive","LayerArmLeftSlot","LayerArmLeftLocalSpace",
        "LayerArmRight","LayerArmRightAdditive","LayerArmRightSlot","LayerArmRightLocalSpace",
        "LayerHandLeft","LayerHandRight","LayerSpine","LayerSpineAdditive","LayerSpineSlot",
        "LayerPelvis","LayerPelvisSlot","LayerLegs","LayerLegsSlot","ViewBlock","PoseAiming"];
    public static ReadOnlySpan<string> CurveNames=>RequiredNames;
    private readonly int[] _indices;
    private readonly int _count;
    public AlsRefactoredLayeringInputModel(ReadOnlySpan<string> names)
    {
        var layout=names.ToArray();_count=layout.Length;
        if(layout.Any(string.IsNullOrWhiteSpace)||layout.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=layout.Length)
            throw new ArgumentException("Invalid Refactored layering curve layout.");
        _indices=RequiredNames.Select(name=>Array.FindIndex(layout,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public AlsRefactoredLayeringInput Evaluate(AlsFrameIdentity identity,AlsFrameIdentity previous,
        ReadOnlySpan<AlsInertialCurve> committedCurves)
    {
        if(identity.SlotGeneration==0||identity.FrameId<0||previous==default&&!committedCurves.IsEmpty||
            previous!=default&&(previous.SlotGeneration==0||previous.CharacterId!=identity.CharacterId||
                previous.SlotGeneration!=identity.SlotGeneration||previous.FrameId>=identity.FrameId||committedCurves.Length!=_count))
            throw new ArgumentException("Refactored layering requires past committed curves from this owner.");
        Span<float> values=stackalloc float[RequiredNames.Length];values.Clear();
        if(previous!=default)
        {
            for(var i=0;i<values.Length;i++)
            {
                var index=_indices[i];if(index<0||!committedCurves[index].Present)continue;
                var value=committedCurves[index].Value;
                if(!float.IsFinite(value))throw new ArgumentException("Nonfinite Refactored layering input.");
                values[i]=value;
            }
        }
        var view=1-System.Math.Clamp(values[20],0,1);var aiming=System.Math.Clamp(values[21],0,1);
        return new()
        {
            Identity=identity,FeedbackIdentity=previous,
            HeadBlendAmount=values[0],HeadAdditiveBlendAmount=values[1],HeadSlotBlendAmount=values[2],
            ArmLeftBlendAmount=values[3],ArmLeftAdditiveBlendAmount=values[4],ArmLeftSlotBlendAmount=values[5],
            ArmLeftLocalSpaceBlendAmount=values[6],ArmLeftMeshSpaceBlendAmount=values[6]>=1f-AlsPoseBlender.WeightThreshold?0:1,
            ArmRightBlendAmount=values[7],ArmRightAdditiveBlendAmount=values[8],ArmRightSlotBlendAmount=values[9],
            ArmRightLocalSpaceBlendAmount=values[10],ArmRightMeshSpaceBlendAmount=values[10]>=1f-AlsPoseBlender.WeightThreshold?0:1,
            HandLeftBlendAmount=values[11],HandRightBlendAmount=values[12],SpineBlendAmount=values[13],
            SpineAdditiveBlendAmount=values[14],SpineSlotBlendAmount=values[15],PelvisBlendAmount=values[16],
            PelvisSlotBlendAmount=values[17],LegsBlendAmount=values[18],LegsSlotBlendAmount=values[19],
            ViewAmount=view,ViewHeadBlendAmount=view*(1-aiming),ViewSpineBlendAmount=view*aiming
        };
    }
}
