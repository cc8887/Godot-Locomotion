using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsLayeringCurve : byte
{
    AimOffsetMask, BasePoseNormal, BasePoseCrouching, SpineAdditive, HeadAdditive,
    LeftArmAdditive, RightArmAdditive, LeftHand, RightHand, LeftHandIk, LeftArmLayer,
    RightHandIk, RightArmLayer, LeftArmLocalSpace, RightArmLocalSpace,
}

// Blueprint real-valued variables are doubles. The curve collection supplies
// floats, which are promoted before its Lerp and integer Floor expressions.
public readonly record struct AlsLayeringInput(AlsFrameIdentity Identity, AlsFrameIdentity FeedbackIdentity,
    double EnableAimOffset, double BasePoseNormal, double BasePoseCrouching,
    double SpineAdditive, double HeadAdditive, double LeftArmAdditive, double RightArmAdditive,
    double LeftHand, double RightHand, double LeftHandIk, double RightHandIk,
    double LeftArmLocalSpace, double LeftArmMeshSpace, double RightArmLocalSpace, double RightArmMeshSpace)
{
    public double GetValue(string nativePropertyName) => nativePropertyName switch
    {
        "Enable_AimOffset" => EnableAimOffset,
        "BasePose_N" => BasePoseNormal, "BasePose_CLF" => BasePoseCrouching,
        "Spine_Add" => SpineAdditive, "Head_Add" => HeadAdditive,
        "Arm_L_Add" => LeftArmAdditive, "Arm_R_Add" => RightArmAdditive,
        "Hand_L" => LeftHand, "Hand_R" => RightHand,
        "Enable_HandIK_L" => LeftHandIk, "Enable_HandIK_R" => RightHandIk,
        "Arm_L_LS" => LeftArmLocalSpace, "Arm_L_MS" => LeftArmMeshSpace,
        "Arm_R_LS" => RightArmLocalSpace, "Arm_R_MS" => RightArmMeshSpace,
        _ => throw new ArgumentException("Unknown UpdateLayerValues output property.", nameof(nativePropertyName)),
    };
}

public sealed class AlsLayeringInputModel
{
    public const int CurveCount = 15;
    private readonly string[] _names;
    public ReadOnlySpan<string> CurveNames => _names;

    public AlsLayeringInputModel(IReadOnlyList<string> curveNames)
    {
        ArgumentNullException.ThrowIfNull(curveNames);
        if (curveNames.Count != CurveCount || curveNames.Any(string.IsNullOrWhiteSpace) ||
            curveNames.Distinct(StringComparer.Ordinal).Count() != CurveCount)
            throw new ArgumentException("Layering requires distinct names for all native curve inputs.", nameof(curveNames));
        _names = curveNames.ToArray();
    }

    // The caller supplies the previous committed final curve collection. This
    // model does not retain a candidate, advance a source clock or read current
    // BaseLayer curves, so a discarded frame can be evaluated again unchanged.
    public AlsLayeringInput Evaluate(AlsFrameIdentity identity, AlsFrameIdentity previousCommittedIdentity,
        ReadOnlySpan<string> names, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (identity.SlotGeneration == 0 || names.Length != curves.Length)
            throw new ArgumentException("Invalid layering frame or final curve layout.");
        if (previousCommittedIdentity.SlotGeneration == 0)
        {
            if (previousCommittedIdentity != default || !names.IsEmpty)
                throw new ArgumentException("Cold layering inputs must not contain uncommitted curves.");
        }
        else if (previousCommittedIdentity.CharacterId != identity.CharacterId ||
            previousCommittedIdentity.SlotGeneration != identity.SlotGeneration || previousCommittedIdentity.FrameId >= identity.FrameId)
            throw new ArgumentException("Layering requires a past final curve frame from this character generation.");

        Span<double> values = stackalloc double[CurveCount];
        values.Clear();
        var found = 0;
        for (var source = 0; source < names.Length; source++)
        {
            if (string.IsNullOrEmpty(names[source]) || !float.IsFinite(curves[source].Value))
                throw new ArgumentException("Invalid final layering curve name/value.");
            for (var role = 0; role < CurveCount; role++)
            {
                if (!string.Equals(names[source], _names[role], StringComparison.Ordinal)) continue;
                if ((found & (1 << role)) != 0) throw new ArgumentException("Duplicate native layering input curve.");
                found |= 1 << role;
                // AnimInstance.GetCurveValue returns zero for an absent name.
                values[role] = curves[source].Present ? curves[source].Value : 0;
                break;
            }
        }
        return new(identity, previousCommittedIdentity,
            1 - values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8],
            values[9] * values[10], values[11] * values[12],
            values[13], OneMinusFloor(values[13]), values[14], OneMinusFloor(values[14]));
    }

    private static double OneMinusFloor(double value)
    {
        // UE FFloor(double) narrows FloorToInt64 to int32, then Subtract_IntInt
        // computes 1 - floor. Preserve that narrowing/wrap for finite float
        // curve inputs rather than clamping LS or treating MS as 1 - LS.
        var floor = unchecked((int)(long)(System.Math.Floor(value) % 4294967296d));
        return unchecked(1 - floor);
    }
}
