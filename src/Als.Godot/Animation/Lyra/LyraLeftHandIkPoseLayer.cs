using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraLeftHandIkPoseLayer
{
    private readonly bool _disabled;
    private readonly AlsComponentCopyBoneController _copy;
    private readonly AlsTwoBoneIkController _solver;
    public LyraLeftHandIkPoseLayer(LyraLogicalSourceBank bank, bool disabled)
    {
        _disabled = disabled;
        _copy = new(bank.Parents, bank.Bone("VB IK_Hand_L_weaponSpace"), bank.Bone("ik_hand_l"), true, true, false);
        _solver = new(bank.Parents, bank.Bone("hand_l"), bank.Bone("ik_hand_l"), bank.Bone("lowerarm_l"), new(0, -50, 0), true);
    }
    public int CopyFrames { get; private set; }
    public int EvaluatedFrames { get; private set; }
    public int AppliedFrames { get; private set; }
    public float Alpha { get; private set; }
    public void CopyTarget(ReadOnlySpan<AlsPrecisePose> input, Span<AlsPrecisePose> output)
    { LyraPoseBuffers.Validate(input, output); _copy.Evaluate(input, 1, output); CopyFrames++; }
    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float disableCurve, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        Alpha = Math.Clamp((_disabled ? 0f : 1f) - disableCurve, 0, 1);
        _solver.Evaluate(input, default, Alpha, output);
        EvaluatedFrames++;
        if (Alpha > AlsPoseBlender.WeightThreshold) AppliedFrames++;
    }
}
