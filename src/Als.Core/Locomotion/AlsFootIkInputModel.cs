namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootIkObservation(AlsMovementStateInput MovementState, double AnimationDelta,
    AlsFootLockObservation LeftLock, AlsFootLockObservation RightLock,
    AlsFootOffsetObservation LeftOffset, AlsFootOffsetObservation RightOffset);
public readonly record struct AlsFootIkInputUpdate(AlsFootIkPropertyState State, AlsDoubleVector LeftTarget, AlsDoubleVector RightTarget);

// Original UpdateFootIK dispatch. Physics traces and socket transforms are
// observations captured outside this pure, candidate-only property update.
public sealed class AlsFootIkInputModel(AlsFootLockInputModel @lock, AlsFootOffsetInputModel offset,
    AlsFootIkResetModel reset, AlsPelvisIkInputModel pelvis, AlsFootIkPropertyState initial)
{
    public AlsFootLockInputModel Lock { get; } = @lock;
    public AlsFootOffsetInputModel Offset { get; } = offset;
    public AlsFootIkResetModel Reset { get; } = reset;
    public AlsPelvisIkInputModel Pelvis { get; } = pelvis;
    public AlsFootIkPropertyState InitialState { get; } = initial;

    public AlsFootIkInputUpdate Evaluate(in AlsFootIkPropertyState previous, in AlsFootIkObservation input)
    {
        if (!double.IsFinite(input.AnimationDelta) || input.AnimationDelta < 0 || !float.IsFinite((float)input.AnimationDelta) ||
            input.LeftLock.EnableCurve != input.LeftOffset.EnableCurve || input.RightLock.EnableCurve != input.RightOffset.EnableCurve ||
            input.MovementState is < AlsMovementStateInput.None or > AlsMovementStateInput.Mantling)
            throw new ArgumentException("Invalid original Foot IK property update.");
        var next = previous with { LeftLock = Lock.Evaluate(previous.LeftLock, input.LeftLock) };
        next = next with { RightLock = Lock.Evaluate(previous.RightLock, input.RightLock) };
        switch (input.MovementState)
        {
            case AlsMovementStateInput.None:
            case AlsMovementStateInput.Grounded:
            case AlsMovementStateInput.Mantling:
                // Function-local targets start at zero on every invocation.
                var left = Offset.Evaluate(previous.LeftOffset, default, input.LeftOffset, input.AnimationDelta);
                var right = Offset.Evaluate(previous.RightOffset, default, input.RightOffset, input.AnimationDelta);
                next = next with { LeftOffset = left.State, RightOffset = right.State,
                    Pelvis = Pelvis.Evaluate(previous.Pelvis, left.LocationTarget, right.LocationTarget,
                        input.LeftOffset.EnableCurve, input.RightOffset.EnableCurve, input.AnimationDelta) };
                return new(next, left.LocationTarget, right.LocationTarget);
            case AlsMovementStateInput.InAir:
                next = next with { Pelvis = Pelvis.Evaluate(previous.Pelvis, default, default,
                    input.LeftOffset.EnableCurve, input.RightOffset.EnableCurve, input.AnimationDelta) };
                return new(Reset.Evaluate(next, input.AnimationDelta), default, default);
            case AlsMovementStateInput.Ragdoll:
                return new(next, default, default);
            default:
                throw new ArgumentException("Unknown original movement state.");
        }
    }
}
