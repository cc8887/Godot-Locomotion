using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsJumpAnimationInput(AlsFrameIdentity Identity, bool Jumped,
    float PlayRate, float DelayRemaining, bool DelayPending);

// Animation observes Frame; the world's later latent-action phase produces Next.
// Both are candidates until the enclosing final-pose owner accepts the frame.
public readonly record struct AlsJumpInputUpdate(AlsJumpAnimationInput Frame, AlsJumpAnimationInput Next);

public sealed class AlsJumpAnimationInputModel
{
    private readonly double _minSpeed, _maxSpeed, _minRate, _maxRate;
    private readonly float _delay;
    public AlsJumpAnimationInput InitialState { get; }

    public AlsJumpAnimationInputModel(bool initialJumped, float initialRate, double minSpeed, double maxSpeed,
        double minRate, double maxRate, float delay)
    {
        if (!double.IsFinite(minSpeed) || !double.IsFinite(maxSpeed) || maxSpeed <= minSpeed ||
            !double.IsFinite(minRate) || !double.IsFinite(maxRate) || minRate <= 0 || maxRate < minRate ||
            maxRate > float.MaxValue || !float.IsFinite(initialRate) || initialRate <= 0 || !float.IsFinite(delay) || delay <= 0)
            throw new ArgumentException("Invalid authored jump input settings.");
        _minSpeed = minSpeed; _maxSpeed = maxSpeed; _minRate = minRate; _maxRate = maxRate; _delay = delay;
        InitialState = new(default, initialJumped, initialRate, 0, false);
    }

    public float PlayRate(float storedSpeedMetersPerSecond)
    {
        if (!float.IsFinite(storedSpeedMetersPerSecond) || storedSpeedMetersPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(storedSpeedMetersPerSecond));
        var alpha = System.Math.Clamp(((double)storedSpeedMetersPerSecond * 100 - _minSpeed) / (_maxSpeed - _minSpeed), 0, 1);
        return (float)(_minRate + (_maxRate - _minRate) * alpha);
    }

    // BPI_Jumped is delivered before this frame's UpdateCharacterInfo, so it reads
    // the previous animation Speed. JumpPressed or becoming airborne is not the event.
    public AlsJumpInputUpdate Evaluate(in AlsFrameInput frame, float storedSpeed, in AlsJumpAnimationInput previous)
    {
        if (frame.Identity.SlotGeneration == 0 || frame.JumpAccepted > 1 || !float.IsFinite(frame.DeltaTime) || frame.DeltaTime <= 0 ||
            previous.Identity.SlotGeneration != 0 && (previous.Identity.CharacterId != frame.Identity.CharacterId ||
                previous.Identity.SlotGeneration != frame.Identity.SlotGeneration || previous.Identity.FrameId >= frame.Identity.FrameId))
            throw new ArgumentException("Invalid jump event frame or history.");
        if (!float.IsFinite(storedSpeed) || storedSpeed < 0 || !float.IsFinite(previous.PlayRate) || previous.PlayRate <= 0 ||
            !float.IsFinite(previous.DelayRemaining) || previous.DelayRemaining < 0 ||
            previous.DelayPending != (previous.DelayRemaining > 0)) throw new ArgumentException("Invalid jump input state.");
        var animation = previous with { Identity = frame.Identity };
        if (frame.JumpAccepted == 1)
        {
            animation = animation with { Jumped = true, PlayRate = PlayRate(storedSpeed) };
            // Kismet Delay ignores an existing action; it is not RetriggerableDelay.
            if (!animation.DelayPending) animation = animation with { DelayPending = true, DelayRemaining = _delay };
        }
        var next = animation;
        if (next.DelayPending)
        {
            var remaining = next.DelayRemaining - frame.DeltaTime; // FDelayAction uses float subtraction.
            next = remaining <= 0 ? next with { Jumped = false, DelayRemaining = 0, DelayPending = false } :
                next with { DelayRemaining = remaining };
        }
        return new(animation, next);
    }
}
