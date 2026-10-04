using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraItemAimingLayer : ILyraAimingLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly LyraUnarmedAimOffset _relaxed;
    private readonly LyraUnarmedAimOffset _ads;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _relaxedPose;
    private readonly AlsLocalPose[] _adsPose;
    private readonly AlsLocalPose[] _output;
    private bool _hasApplied;
    private readonly AlsPrecisePose[] _logicalRelaxed = new AlsPrecisePose[81], _logicalAds = new AlsPrecisePose[81];

    public LyraItemAimingLayer(LyraUnarmedAimOffset relaxed, LyraUnarmedAimOffset ads)
    {
        if (!ReferenceEquals(relaxed.Skeleton, ads.Skeleton))
            throw new ArgumentException("Item AimOffsets must share one skeleton.");
        _skeleton = relaxed.Skeleton;
        _relaxed = relaxed;
        _ads = ads;
        var count = _skeleton.GetBoneCount();
        _basis = new AlsLocalPose[count];
        _relaxedPose = new AlsLocalPose[count];
        _adsPose = new AlsLocalPose[count];
        _output = new AlsLocalPose[count];
    }

    public int AppliedFrames { get; private set; }
    public void ConfigureLogical(LyraLogicalSourceBank bank)
    { _relaxed.ConfigureLogical(bank); _ads.ConfigureLogical(bank); }

    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float yaw, float pitch,
        float aimOffsetBlendWeight, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        if (!float.IsFinite(aimOffsetBlendWeight) || aimOffsetBlendWeight is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(aimOffsetBlendWeight));
        if (aimOffsetBlendWeight <= AlsPoseBlender.WeightThreshold)
            _relaxed.EvaluatePose(input, yaw, pitch, 1, output);
        else if (aimOffsetBlendWeight >= 1 - AlsPoseBlender.WeightThreshold)
            _ads.EvaluatePose(input, yaw, pitch, 1, output);
        else
        {
            _relaxed.EvaluatePose(input, yaw, pitch, 1, _logicalRelaxed);
            _ads.EvaluatePose(input, yaw, pitch, 1, _logicalAds);
            for (var bone = 0; bone < 81; bone++)
                output[bone] = AlsPrecisePoseBlender.Blend(_logicalRelaxed[bone], _logicalAds[bone], aimOffsetBlendWeight);
        }
        AppliedFrames++;
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void Apply(float yaw, float pitch, float aimOffsetBlendWeight)
    {
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, yaw, pitch, aimOffsetBlendWeight, _output);
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, float yaw, float pitch,
        float aimOffsetBlendWeight, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (!float.IsFinite(aimOffsetBlendWeight) || aimOffsetBlendWeight is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(aimOffsetBlendWeight));
        if (aimOffsetBlendWeight <= AlsPoseBlender.WeightThreshold)
            _relaxed.Evaluate(input, yaw, pitch, output);
        else if (aimOffsetBlendWeight >= 1 - AlsPoseBlender.WeightThreshold)
            _ads.Evaluate(input, yaw, pitch, output);
        else
        {
            _relaxed.Evaluate(input, yaw, pitch, _relaxedPose);
            _ads.Evaluate(input, yaw, pitch, _adsPose);
            for (var bone = 0; bone < output.Length; bone++)
                output[bone] = AlsPoseBlender.Blend(_relaxedPose[bone], _adsPose[bone],
                    aimOffsetBlendWeight);
        }
        AppliedFrames++;
    }
}
