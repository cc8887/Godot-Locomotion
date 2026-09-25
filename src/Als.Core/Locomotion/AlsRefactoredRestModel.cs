using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRefactoredRotatePolicy(float YawThreshold, float FirstPersonYawThreshold, Vector2 ReferenceSpeed, Vector2 Rate);
public readonly record struct AlsRefactoredTurnPolicy(float YawThreshold, float SpeedThreshold, Vector2 Delay, float Turn180Threshold);
public readonly record struct AlsRefactoredRotateState(bool Left, bool Right, float PlayRate);
public readonly record struct AlsRefactoredTurnSelection(float Delay, int AssetIndex);

/// <summary>Original Parent rest arithmetic. Resource validity, per-frame call
/// guards and deferred game-thread action queues belong to the owning runtime.</summary>
public static class AlsRefactoredRestModel
{
    public static AlsRefactoredRotateState Rotate(AlsRefactoredRotatePolicy settings, float previousRate,
        bool moving, bool aiming, bool firstPerson, float yaw, float speed, float delta, bool pendingUpdate)
    {
        var threshold = firstPerson && !aiming ? settings.FirstPersonYawThreshold : settings.YawThreshold;
        var allowed = !moving && (aiming || firstPerson);
        var left = allowed && yaw < -threshold; var right = allowed && yaw > threshold;
        var target = left || right ? Map(settings.ReferenceSpeed,settings.Rate,speed) : settings.Rate.X;
        return new(left,right,pendingUpdate ? target : previousRate+(target-previousRate)*AlsRefactoredRigMath.DamperAlpha(delta,.15f));
    }
    public static AlsRefactoredTurnSelection Turn(AlsRefactoredTurnPolicy settings, float previousDelay,
        bool allowed, bool crouching, bool validStance, float yaw, float speed, float delta)
    {
        if (!allowed || speed >= settings.SpeedThreshold || MathF.Abs(yaw) <= settings.YawThreshold) return new(0,-1);
        var delay = previousDelay+delta;
        if (delay <= Map(new(settings.YawThreshold,180),settings.Delay,MathF.Abs(yaw)) || !validStance) return new(delay,-1);
        var left = (yaw > 175 ? yaw-360 : yaw) <= 0;
        return new(delay,(crouching ? 4 : 0)+(MathF.Abs(yaw) < settings.Turn180Threshold ? 0 : 2)+(left ? 0 : 1));
    }
    public static int Dynamic(float distanceThreshold, float scale, float leftLock, float rightLock,
        AlsDoubleVector leftTarget, AlsDoubleVector leftLocation, AlsDoubleVector rightTarget, AlsDoubleVector rightLocation, bool crouching)
    {
        var distance = distanceThreshold*scale; var threshold = distance*distance;
        var leftDistance = (leftTarget-leftLocation).LengthSquared; var rightDistance = (rightTarget-rightLocation).LengthSquared;
        var left = leftLock > AlsPoseBlender.WeightThreshold && leftDistance > threshold;
        var right = rightLock > AlsPoseBlender.WeightThreshold && rightDistance > threshold;
        if (!left && !right) return -1;
        return (crouching ? 2 : 0)+(!left || right && leftDistance < rightDistance ? 1 : 0);
    }
    private static float Map(Vector2 source, Vector2 target, float value)
    {
        var alpha = System.Math.Clamp((value-source.X)/(source.Y-source.X),0,1);
        return target.X+(target.Y-target.X)*alpha;
    }
}
