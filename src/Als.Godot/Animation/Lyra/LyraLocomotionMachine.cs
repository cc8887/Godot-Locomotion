using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLocomotionRuleInputs(bool HasAcceleration, bool HasVelocity, bool IsMelee,
    bool RunningIntoWall, bool LinkedLayerChanged, bool CrouchChanged, bool AdsChanged, bool IsJumping, bool IsFalling,
    bool IsOnGround, AlsDoubleVector LocalVelocity, AlsDoubleVector LocalAcceleration, int StartDirection,
    int VelocityDirection, int PivotInitialDirection, double DisplacementSpeed, double RootYawOffset, double LastPivotTime,
    double TimeToJumpApex, double GroundDistance, float StateElapsed, bool PivotNotify = false,
    bool LocomotionSyncValid = false, bool RelevantSourceValid = false, float SourceLength = 0, float SourceTime = 0,
    bool SourceLooping = false, bool SourcePreviousValid = false, float SourcePrevious = 0, float SourceDelta = 0);

internal sealed record LyraLocomotionTransition(int Previous, int Next, int Edge, IReadOnlyList<int> ConduitPath,
    float Duration, bool Inertial, float CrossfadeAdjustment, bool DiscardFirstBlend)
{
    public float StandardBlendDuration => Inertial || DiscardFirstBlend ? 0 : MathF.Max(Duration - CrossfadeAdjustment, 0);
}

// Exact predicates and baked exit selection, separated from player traversal,
// pose blend weights and sync. The caller must supply their real source state.
internal sealed class LyraLocomotionMachine
{
    private readonly LyraCompiledMachine _definition;
    private bool _firstUpdate = true;
    public LyraLocomotionMachine(LyraRuntimeGraphCatalog catalog) { _definition = catalog.Locomotion; State = _definition.InitialState; }
    public int State { get; private set; }
    public float Elapsed { get; private set; }
    public int TransitionCount { get; private set; }
    public LyraMotionPhase Phase => _definition.States[State].Name switch
    {
        "Idle" => LyraMotionPhase.Idle, "Start" => LyraMotionPhase.Start, "Cycle" => LyraMotionPhase.Cycle,
        "Stop" => LyraMotionPhase.Stop, "Pivot" => LyraMotionPhase.Pivot, "JumpStart" => LyraMotionPhase.JumpStart,
        "JumpApex" => LyraMotionPhase.JumpApex, "FallLand" => LyraMotionPhase.FallLand,
        "JumpStartLoop" => LyraMotionPhase.JumpStartLoop, "FallLoop" => LyraMotionPhase.FallLoop,
        _ => throw new InvalidOperationException("A conduit cannot become a current state."),
    };

    public void Reset() { State = _definition.InitialState; Elapsed = 0; TransitionCount = 0; _firstUpdate = true; }
    public void CopyFrom(LyraLocomotionMachine source)
    {
        if (!ReferenceEquals(_definition, source._definition)) throw new ArgumentException("Different compiled state machine.");
        State = source.State; Elapsed = source.Elapsed; TransitionCount = source.TransitionCount; _firstUpdate = source._firstUpdate;
    }
    public LyraLocomotionTransition? Update(in LyraLocomotionRuleInputs observations, float delta, bool contextActive = true)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var input = observations with { StateElapsed = Elapsed };
        var selected = Select(State, input, contextActive);
        if (selected is not null)
        {
            selected = selected with { DiscardFirstBlend = _firstUpdate };
            State = selected.Next; Elapsed = 0; TransitionCount++;
        }
        _firstUpdate = false; Elapsed += delta;
        return selected;
    }
    public LyraLocomotionTransition? Select(int state, in LyraLocomotionRuleInputs input, bool contextActive = true)
    {
        Validate(input);
        if ((uint)state >= _definition.States.Count || _definition.States[state].Conduit)
            throw new ArgumentException("Invalid current locomotion state.");
        var selected = Find(state, input, new HashSet<int>(), contextActive);
        return selected is null || selected.Next == state ? null : selected with { Previous = state };
    }
    private LyraLocomotionTransition? Find(int state, in LyraLocomotionRuleInputs input, HashSet<int> visited, bool contextActive)
    {
        if (!visited.Add(state)) return null;
        foreach (var exit in _definition.States[state].Exits)
        {
            if (exit.OnlyWhenActive && !contextActive) continue;
            var edge = _definition.Edges[exit.Edge]; var adjustment = 0f; bool result;
            if (exit.RequiredSyncGroup != "None" && !input.LocomotionSyncValid) result = false;
            else if (exit.Automatic)
            {
                var trigger = exit.AutomaticTriggerTime >= 0 ? exit.AutomaticTriggerTime :
                    _definition.States[edge.Next].Conduit ? 0 : edge.Duration;
                var remaining = input.SourceLength - input.SourceTime;
                if (input.SourceLooping && remaining > 0 && input.SourcePreviousValid &&
                    (input.SourceTime - input.SourcePrevious) * input.SourceDelta < 0) remaining = 0;
                adjustment = trigger - remaining;
                result = input.RelevantSourceValid && adjustment >= 0;
            }
            else result = Predicate(exit.Edge, input);
            if (result != exit.Desired) continue;
            if (_definition.States[edge.Next].Conduit)
            {
                // Both original entry rules are constant true; the outgoing
                // selectors still decide whether this path reaches a real state.
                var selected = Find(edge.Next, input, visited, contextActive);
                if (selected is not null)
                    return selected with { ConduitPath = selected.ConduitPath.Append(exit.Edge).ToArray() };
            }
            else return new(state, edge.Next, exit.Edge, new[] { exit.Edge }, edge.Duration, edge.Inertial, adjustment, false);
        }
        return null;
    }

    public static bool Predicate(int edge, in LyraLocomotionRuleInputs i)
    {
        var moving = i.HasAcceleration || i.IsMelee && i.HasVelocity;
        var reversal = AlsDoubleVector.Dot(i.LocalVelocity, i.LocalAcceleration) < 0 && !i.RunningIntoWall;
        var perpendicular = (i.PivotInitialDirection <= 1) != (i.VelocityDirection <= 1);
        return edge switch
        {
            0 => moving,
            1 or 6 or 11 or 14 or 18 or 31 => true,
            2 or 9 => reversal,
            3 or 12 or 17 => i.LinkedLayerChanged,
            4 or 10 => !moving,
            5 => Math.Abs(i.RootYawOffset) > 60,
            8 => i.StartDirection != i.VelocityDirection || i.CrouchChanged || i.AdsChanged ||
                i.StateElapsed > .15 && i.DisplacementSpeed < 10,
            13 or 30 => i.HasAcceleration,
            15 => i.CrouchChanged || i.AdsChanged,
            19 => i.PivotNotify,
            20 => !i.HasAcceleration,
            21 => i.CrouchChanged || i.AdsChanged || perpendicular && i.LastPivotTime <= 0,
            22 => i.IsFalling,
            23 => i.IsJumping,
            25 or 27 or 28 or 29 or 33 or 35 => i.IsOnGround,
            32 => i.TimeToJumpApex < .4,
            34 => i.GroundDistance < 200,
            _ => throw new ArgumentOutOfRangeException(nameof(edge), "Automatic edges require an actual relevant player."),
        };
    }
    private static void Validate(in LyraLocomotionRuleInputs input)
    {
        if (!double.IsFinite(input.DisplacementSpeed) || !double.IsFinite(input.RootYawOffset) ||
            !double.IsFinite(input.LastPivotTime) || !double.IsFinite(input.TimeToJumpApex) ||
            !double.IsFinite(input.GroundDistance) || !float.IsFinite(input.StateElapsed) ||
            input.StateElapsed < 0 || !float.IsFinite(input.SourceLength) || !float.IsFinite(input.SourceTime) ||
            // SequenceEvaluator exposes its authored ExplicitTime to relevant
            // time rules, including negative distance-match results. Sampling
            // clamps separately; selection must retain this native value.
            input.SourceLength < 0 || !float.IsFinite(input.SourcePrevious) || !float.IsFinite(input.SourceDelta) || (uint)input.StartDirection >= 4 ||
            (uint)input.VelocityDirection >= 4 || (uint)input.PivotInitialDirection >= 4 ||
            !double.IsFinite(input.LocalVelocity.LengthSquared) || !double.IsFinite(input.LocalAcceleration.LengthSquared))
            throw new ArgumentException("Invalid locomotion rule observation.");
    }
}
