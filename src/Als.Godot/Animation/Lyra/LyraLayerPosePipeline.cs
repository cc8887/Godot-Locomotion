using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraPoseEvaluation(LyraMotionPhase Phase, bool IsCrouching,
    bool IsOnGround, float AimYaw, float AimPitch, float LeftHandDisable, float HandRetargetDisable,
    float RightHandDisable = 0, float LeftHandIkDisable = 0);

// Implemented parts of ABP_Mannequin_Base. Raw 81-bone sources feed the default
// layer path; AnimationPlayer remains an adapter. Slots, native graph
// inertialization and FootPlant are pending.
internal sealed class LyraLayerPosePipeline
{
    internal const float FullBodyAdditiveAlpha = 0.65f;
    private readonly Skeleton3D _skeleton;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _first;
    private readonly AlsLocalPose[] _second;
    private readonly AlsLocalPose[] _additive;
    private readonly AlsLocalPose[] _output;
    private bool _hasApplied;
    private readonly LyraLogicalSourceBank? _logicalBank;
    private readonly AlsPrecisePose[] _logicalBasis = new AlsPrecisePose[81], _logicalFirst = new AlsPrecisePose[81],
        _logicalSecond = new AlsPrecisePose[81], _logicalAdditive = new AlsPrecisePose[81], _logicalOutput = new AlsPrecisePose[81];

    public LyraLayerPosePipeline(Skeleton3D skeleton, LyraLogicalSourceBank? logicalBank = null)
    {
        _skeleton = skeleton;
        _logicalBank = logicalBank;
        var bones = skeleton.GetBoneCount();
        _basis = new AlsLocalPose[bones];
        _first = new AlsLocalPose[bones];
        _second = new AlsLocalPose[bones];
        _additive = new AlsLocalPose[bones];
        _output = new AlsLocalPose[bones];
    }

    public int AppliedFrames { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> LogicalSourcePose => _logicalBasis;
    public ReadOnlySpan<AlsPrecisePose> LogicalOutputPose => _logicalOutput;
    private readonly AlsPrecisePose[] _logicalPreControls = new AlsPrecisePose[81];
    public ReadOnlySpan<AlsPrecisePose> LogicalPreControlsPose => _logicalPreControls;

    public void ApplyLogical(LyraItemLayerInstance item, LyraRootYawOffset rootYaw,
        in LyraPoseEvaluation frame, LyraLogicalSourcePlayback source, double time, double delta)
    {
        if (_hasApplied || _logicalBank is null) throw new InvalidOperationException("Invalid logical pipeline lifecycle.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        source.Sample(time, delta, _logicalBasis);
        Evaluate(item, rootYaw, frame, _logicalBasis, _logicalOutput);
        for (var skin = 0; skin < _output.Length; skin++)
        {
            var pose = _logicalOutput[_logicalBank.SkinLogicalIndices[skin]];
            _output[skin] = new(new((float)(pose.Position.X * .01), (float)(-pose.Position.Y * .01), (float)(pose.Position.Z * .01)),
                new((float)-pose.Rotation.X, (float)pose.Rotation.Y, (float)-pose.Rotation.Z, (float)pose.Rotation.W), pose.Scale.ToSingle());
        }
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true; AppliedFrames++;
    }

    public void Evaluate(LyraItemLayerInstance item, LyraRootYawOffset rootYaw, in LyraPoseEvaluation frame,
        ReadOnlySpan<AlsPrecisePose> input, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        if (_logicalBank is null || !float.IsFinite(frame.AimYaw) || !float.IsFinite(frame.AimPitch) ||
            !float.IsFinite(frame.LeftHandDisable) || !float.IsFinite(frame.HandRetargetDisable) || !float.IsFinite(frame.RightHandDisable) ||
            !float.IsFinite(frame.LeftHandIkDisable) || item.LeftHandIk is null)
            throw new ArgumentException("Invalid logical pose evaluation.");
        if (frame.Phase == LyraMotionPhase.Idle) input.CopyTo(_logicalFirst);
        else item.HipFire.EvaluatePose(input, frame.IsCrouching, _logicalFirst);
        item.LeftHand.EvaluatePose(_logicalFirst, frame.LeftHandDisable, _logicalSecond);
        item.Aiming.EvaluatePose(_logicalSecond, frame.AimYaw, frame.AimPitch, item.HipFire.AimOffsetBlendWeight, _logicalFirst);
        item.Additives.EvaluateAdditive(frame.IsOnGround, _logicalAdditive);
        for (var bone = 0; bone < 81; bone++)
            _logicalSecond[bone] = AlsPrecisePoseBlender.LocalApply(_logicalFirst[bone], _logicalAdditive[bone], FullBodyAdditiveAlpha);
        rootYaw.EvaluatePose(_logicalSecond, _logicalFirst);
        _logicalFirst.AsSpan().CopyTo(_logicalPreControls);
        item.HandRetarget.EvaluatePose(_logicalFirst, frame.HandRetargetDisable, _logicalSecond);
        item.LeftHandIk.CopyTarget(_logicalSecond, _logicalFirst);
        item.RightHandIk.EvaluatePose(_logicalFirst, frame.RightHandDisable, _logicalSecond);
        item.LeftHandIk.EvaluatePose(_logicalSecond, frame.LeftHandIkDisable, output);
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        // Restore the pre-layer source once, including bones absent from a clip.
        // Never feed the previous final pose back into an AnimationPlayer blend.
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void Apply(LyraItemLayerInstance? item, LyraRootYawOffset rootYaw, in LyraPoseEvaluation frame)
    {
        if (_hasApplied) throw new InvalidOperationException("Restore the previous Lyra source pose first.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        Evaluate(item, rootYaw, frame, _basis, _output);
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true;
        AppliedFrames++;
    }

    public void Evaluate(LyraItemLayerInstance? item, LyraRootYawOffset rootYaw,
        in LyraPoseEvaluation frame, ReadOnlySpan<AlsLocalPose> input, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (!float.IsFinite(frame.AimYaw) || !float.IsFinite(frame.AimPitch) ||
            !float.IsFinite(frame.LeftHandDisable) || !float.IsFinite(frame.HandRetargetDisable) ||
            !float.IsFinite(frame.RightHandDisable))
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (item is null)
        {
            rootYaw.EvaluatePose(input, output);
            return;
        }
        if (frame.Phase == LyraMotionPhase.Idle) input.CopyTo(_first);
        else item.HipFire.EvaluatePose(input, frame.IsCrouching, _first);
        item.LeftHand.EvaluatePose(_first, frame.LeftHandDisable, _second);
        item.Aiming.EvaluatePose(_second, frame.AimYaw, frame.AimPitch,
            item.HipFire.AimOffsetBlendWeight, _first);
        item.Additives.EvaluateAdditive(frame.IsOnGround, _additive);
        ApplyFullBodyAdditive(_first, _additive, _second);
        rootYaw.EvaluatePose(_second, _first);
        item.HandRetarget.EvaluatePose(_first, frame.HandRetargetDisable, _second);
        item.RightHandIk.EvaluatePose(_second, frame.RightHandDisable, output);
    }

    internal static void ApplyFullBodyAdditive(ReadOnlySpan<AlsLocalPose> input,
        ReadOnlySpan<AlsLocalPose> additive, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, additive.Length);
        if (additive.Overlaps(output)) throw new ArgumentException("Lyra additive output overlaps its source.");
        for (var bone = 0; bone < input.Length; bone++)
            output[bone] = AlsLocalAdditivePose.Apply(input[bone], additive[bone], FullBodyAdditiveAlpha);
    }
}
