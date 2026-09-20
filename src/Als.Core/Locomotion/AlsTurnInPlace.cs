using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsTurnSlot : byte { Standing, Crouching }

public readonly record struct AlsTurnAsset(int AnimationId, AlsTurnSlot Slot, float AnimatedAngle,
    float PlayRate, bool ScaleTurnAngle);

// One observation per MontageInstance, in the owner's original order. Active and
// Playing are lifecycle flags, never inferred from a remaining blend weight.
// Slot/SegmentCount/AnimationId describe GetAnimationData for the queried slot.
public readonly record struct AlsTurnSlotObservation(bool Active, bool Playing, bool Transient,
    bool HasSlot, AlsTurnSlot Slot, int SegmentCount, int AnimationId);

public readonly record struct AlsTurnMontageCommand(int AnimationId, AlsTurnSlot Slot, float PlayRate,
    float StartTime, float BlendInTime, float BlendOutTime, int LoopCount, float BlendOutTriggerTime);

// AttemptPlayback is an instruction for the action owner, not proof of playback.
// RotationScale is the Blueprint's value after that call, even when it returns null.
public readonly record struct AlsTurnInPlaceDecision(bool AttemptPlayback, AlsTurnAsset Asset,
    double TurnAngle, AlsTurnMontageCommand Command, float RotationScale);

public sealed class AlsTurnInPlaceModel
{
    private readonly AlsTurnAsset[] _assets;
    public float Turn180Threshold { get; }
    public float BlendInTime { get; }
    public float BlendOutTime { get; }
    public int LoopCount { get; }
    public float BlendOutTriggerTime { get; }

    // Order: standing L90/R90/L180/R180, then crouching in the same order.
    public AlsTurnInPlaceModel(float threshold, ReadOnlySpan<AlsTurnAsset> assets,
        float blendIn, float blendOut, int loopCount, float blendOutTrigger)
    {
        if (!float.IsFinite(threshold) || threshold <= 0 || threshold > 180 || assets.Length != 8 ||
            !float.IsFinite(blendIn) || blendIn < 0 || !float.IsFinite(blendOut) || blendOut < 0 ||
            loopCount != 1 || !float.IsFinite(blendOutTrigger))
            throw new ArgumentException("Invalid TurnInPlace settings.");
        for (var i = 0; i < assets.Length; i++)
        {
            var asset = assets[i];
            if (asset.AnimationId < 0 || asset.Slot != (i < 4 ? AlsTurnSlot.Standing : AlsTurnSlot.Crouching) ||
                !float.IsFinite(asset.AnimatedAngle) || asset.AnimatedAngle == 0 ||
                MathF.Sign(asset.AnimatedAngle) != (i % 2 == 0 ? -1 : 1) ||
                !float.IsFinite(asset.PlayRate) || asset.PlayRate <= 0)
                throw new ArgumentException("Invalid TurnInPlace asset binding.");
        }
        _assets = assets.ToArray(); Turn180Threshold = threshold;
        BlendInTime = blendIn; BlendOutTime = blendOut; LoopCount = loopCount; BlendOutTriggerTime = blendOutTrigger;
    }

    public AlsTurnAsset Select(double normalizedUeAngle, AlsStance stance)
    {
        if (!double.IsFinite(normalizedUeAngle) || normalizedUeAngle is <= -180 or > 180 || (uint)stance > 1)
            throw new ArgumentException("Invalid normalized turn angle or stance.");
        return _assets[((int)stance * 4) + (System.Math.Abs(normalizedUeAngle) < Turn180Threshold ? 0 : 2) +
            (normalizedUeAngle < 0 ? 0 : 1)];
    }

    public AlsTurnInPlaceDecision Evaluate(in AlsTurnInPlaceRequest request, float characterYaw,
        AlsStance stance, float previousRotationScale, ReadOnlySpan<AlsTurnSlotObservation> montages)
    {
        if (!float.IsFinite(previousRotationScale)) throw new ArgumentException("Invalid previous rotation scale.");
        if (!request.Requested) return new(false, default, 0, default, previousRotationScale);
        if (!float.IsFinite(characterYaw) || !float.IsFinite(request.TargetYawRadians) ||
            !float.IsFinite(request.PlayRateScale) || !float.IsFinite(request.StartTime))
            throw new ArgumentException("Invalid TurnInPlace request.");
        var angle = (characterYaw - (double)request.TargetYawRadians) * (180 / System.Math.PI) % 360;
        if (angle < 0) angle += 360;
        if (angle > 180) angle -= 360;
        var asset = Select(angle, stance);
        if (!request.OverrideCurrent && IsPlayingSlotAnimation(asset, montages))
            return new(false, asset, angle, default, previousRotationScale);
        var rate = asset.PlayRate * request.PlayRateScale;
        var scale = asset.ScaleTurnAngle ? (float)(angle / asset.AnimatedAngle * asset.PlayRate * request.PlayRateScale) : rate;
        if (!float.IsFinite(rate) || !float.IsFinite(scale)) throw new ArgumentException("TurnInPlace multiplication overflow.");
        return new(true, asset, angle, new(asset.AnimationId, asset.Slot, rate, request.StartTime,
            BlendInTime, BlendOutTime, LoopCount, BlendOutTriggerTime), scale);
    }

    public static bool IsPlayingSlotAnimation(in AlsTurnAsset asset, ReadOnlySpan<AlsTurnSlotObservation> montages)
    {
        foreach (var montage in montages)
            if (montage.Active && montage.Playing && montage.Transient && montage.HasSlot &&
                montage.Slot == asset.Slot && montage.SegmentCount == 1)
                // UE returns at the first eligible track, including a different asset.
                return montage.AnimationId == asset.AnimationId;
        return false;
    }
}
