using System.Numerics;
using GodotAls.Core.Camera;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>Character direction memory and animation-instance velocity history.
/// Pure candidate state: the host publishes it only with its animation frame.</summary>
public readonly record struct AlsRefactoredLocomotionHistory(AlsFrameIdentity Identity,
    AlsDoubleVector Velocity, AlsDoubleVector Acceleration, float InputYaw, float VelocityYaw,
    long BaseIdentity, AlsQuaternion BaseRotation, bool Moving)
{
    public static AlsRefactoredLocomotionHistory Advance(in AlsRefactoredLocomotionHistory previous,
        in AlsFrameInput frame, in AlsRefactoredMotionObservation observation, bool ignoreBaseRotation, bool pendingUpdate,
        bool inheritVelocityYaw = false)
    {
        if (observation.Identity != frame.Identity || frame.Identity.SlotGeneration == 0 ||
            !float.IsFinite(frame.DeltaTime) || frame.DeltaTime < 0 || !float.IsFinite(frame.CharacterYaw) ||
            previous.Identity.SlotGeneration != 0 && (previous.Identity.CharacterId != frame.Identity.CharacterId ||
                previous.Identity.SlotGeneration != frame.Identity.SlotGeneration || previous.Identity.FrameId >= frame.Identity.FrameId))
            throw new ArgumentException("Foreign or stale locomotion history.");
        var velocity = AlsFootIkCoordinates.ToNative(frame.ActualVelocity);
        if (!velocity.IsFinite || !previous.Velocity.IsFinite || !observation.InputDirection.IsFinite)
            throw new ArgumentException("Nonfinite locomotion history.");
        var baseId = observation.RelativeLocation ? frame.Floor.ColliderId : 0;
        var rotation = AlsQuaternion.Identity;
        if (baseId != 0)
        {
            var q = Quaternion.CreateFromRotationMatrix(frame.Floor.PlatformTransform);
            rotation = new(q.Z, -q.X, -q.Y, q.W);
            if (!double.IsFinite(rotation.LengthSquared) || System.Math.Abs(rotation.LengthSquared - 1) > .001)
                throw new ArgumentException("Invalid movement base rotation.");
            rotation = rotation.Normalized();
        }
        var priorVelocity = previous.Velocity;
        var initial = previous.Identity.SlotGeneration == 0;
        var inputYaw = initial ? (float)(-frame.CharacterYaw * (180d / System.Math.PI)) : previous.InputYaw;
        var velocityYaw = initial ? inputYaw : previous.VelocityYaw;
        if (baseId != 0 && !initial && baseId == previous.BaseIdentity)
        {
            var deltaRotation = AlsCameraMath.Rotator(rotation * previous.BaseRotation.Conjugate());
            if (!ignoreBaseRotation) priorVelocity = AlsCharacterRotationMath.RotateVector(priorVelocity, deltaRotation);
            // Character::RefreshLocomotionEarly has an independent opt-in for
            // idle VelocityDirection, even when CMC ignores base rotation.
            if (!previous.Moving && inheritVelocityYaw)
                velocityYaw = (float)AlsCharacterRotationMath.Normalize((float)(velocityYaw + deltaRotation.Yaw));
        }
        var acceleration = !pendingUpdate && previous.Identity.SlotGeneration != 0 && frame.DeltaTime > 1e-8f
            ? (velocity - priorVelocity) * (1d / frame.DeltaTime) : default;
        return new(frame.Identity, velocity, acceleration,
            observation.HasInput ? Yaw(observation.InputDirection) : inputYaw,
            observation.HasVelocity ? Yaw(velocity) : velocityYaw, baseId, rotation, observation.Moving);
    }

    private static float Yaw(AlsDoubleVector value) => (float)(System.Math.Atan2(value.Y, value.X) * (180d / System.Math.PI));

}
