namespace GodotAls.Core.Locomotion;

// The two source DoOnce instances begin open and reset one another on completion.
// They belong to the AnimInstance, not to any animation node's relevance history.
public readonly record struct AlsMovementUpdateGateState(bool TrueClosed, bool FalseClosed);
public readonly record struct AlsMovementUpdateGate(AlsMovementUpdateGateState State,
    bool ChangedToTrue, bool ChangedToFalse, bool WhileTrue, bool WhileFalse)
{
    public static AlsMovementUpdateGate Evaluate(AlsMovementUpdateGateState previous, bool grounded, bool shouldMove)
    {
        if (previous.TrueClosed && previous.FalseClosed) throw new ArgumentException("Both movement gate branches are closed.");
        if (!grounded) return new(previous, false, false, false, false);
        return shouldMove ? new(new(true, false), !previous.TrueClosed, false, true, false) :
            new(new(false, true), false, !previous.FalseClosed, false, true);
    }
}
