using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>One character observation shared by the linked graph and foot lock.
/// Speed is horizontal and in centimeters per second; platform motion does not
/// imply an actor attachment or a different inertialization parent.</summary>
public readonly record struct AlsRefactoredMotionObservation(AlsFrameIdentity Identity, float Speed,
    bool HasInput, bool HasVelocity, bool Moving, bool MovingSmooth, bool RelativeLocation)
{
    public static AlsRefactoredMotionObservation Capture(in AlsFrameInput frame, float movingThreshold, float smoothThreshold)
    {
        if (frame.Identity.SlotGeneration == 0 || !float.IsFinite(movingThreshold) || movingThreshold < 0 ||
            !float.IsFinite(smoothThreshold) || smoothThreshold < 0 || !new AlsDoubleVector(frame.ActualVelocity).IsFinite ||
            !new AlsDoubleVector(frame.InputDirection).IsFinite)
            throw new ArgumentException("Invalid locomotion observation.");
        var velocity = AlsFootIkCoordinates.ToNative(frame.ActualVelocity);
        var speed = (float)System.Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        if (!float.IsFinite(speed)) throw new ArgumentException("Nonfinite locomotion speed.");
        var hasInput = frame.InputDirection.LengthSquared() > 1e-4f;
        var hasVelocity = speed >= 1;
        return new(frame.Identity, speed, hasInput, hasVelocity,
            hasInput && hasVelocity || speed > movingThreshold,
            hasInput && hasVelocity || speed > smoothThreshold,
            frame.Floor.IsGrounded == 1 && frame.Floor.PlatformId >= 0 && frame.Floor.ColliderId > 0);
    }
}
