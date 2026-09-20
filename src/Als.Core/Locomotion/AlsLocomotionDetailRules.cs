using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsDetailState : byte { Walking, Running, WalkRun, FirstPivot, SecondPivot, RunStart, RunConduit }
public enum AlsDetailTransitionLogic : byte { Standard, Inertialization }
public enum AlsDetailCondition : byte
{
    WalkingAndGaitCurveBelow, RelevantAnimationFinished, Pivot, PivotAfterElapsed,
    RunningGaitAndGroundedFull, RunningGaitAndGroundedNotFull, DetailFull, DetailNotFull,
}

public readonly record struct AlsDetailTransitionDefinition(AlsDetailState From, AlsDetailState To,
    float DurationSeconds, AlsDetailTransitionLogic Logic, AlsDetailCondition Condition, float Threshold);

public readonly record struct AlsDetailRuleInput(AlsGait Gait, float WeightGait, bool Pivot,
    float StateElapsedSeconds, float RelevantTimeRemainingSeconds, float MainGroundedWeight, float DetailWeight);

/// <summary>Source-rule selection only. Inertialization requests are not ordinary crossfades.</summary>
public static class AlsLocomotionDetailRules
{
    public static bool TrySelect(ReadOnlySpan<AlsDetailTransitionDefinition> transitions, AlsDetailState state,
        in AlsDetailRuleInput input, out int firstEdge, out int terminalEdge)
    {
        if ((uint)state >= (uint)AlsDetailState.RunConduit || (uint)input.Gait > (uint)AlsGait.Sprinting ||
            !float.IsFinite(input.WeightGait) || !float.IsFinite(input.StateElapsedSeconds) || input.StateElapsedSeconds < 0 ||
            !float.IsFinite(input.RelevantTimeRemainingSeconds) || input.RelevantTimeRemainingSeconds < 0 ||
            !float.IsFinite(input.MainGroundedWeight) || input.MainGroundedWeight is < 0 or > 1 ||
            !float.IsFinite(input.DetailWeight) || input.DetailWeight is < 0 or > 1)
            throw new ArgumentException("Invalid Detail rule input.");
        firstEdge = terminalEdge = -1;
        // Definitions retain the baked exit priority, including ties in editor PriorityOrder.
        for (var i = 0; i < transitions.Length; i++)
        {
            var edge = transitions[i];
            if (edge.From != state || !Evaluate(edge, input)) continue;
            if (edge.To != AlsDetailState.RunConduit)
            {
                firstEdge = terminalEdge = i;
                return true;
            }
            for (var j = 0; j < transitions.Length; j++)
            {
                var terminal = transitions[j];
                if (terminal.From != AlsDetailState.RunConduit || !Evaluate(terminal, input)) continue;
                if (terminal.To == AlsDetailState.RunConduit) throw new ArgumentException("Recursive Detail conduit.");
                firstEdge = i;
                terminalEdge = j;
                return true;
            }
        }
        return false;
    }

    private static bool Evaluate(in AlsDetailTransitionDefinition edge, in AlsDetailRuleInput input) => edge.Condition switch
    {
        AlsDetailCondition.WalkingAndGaitCurveBelow => input.Gait == AlsGait.Walking && input.WeightGait < edge.Threshold,
        AlsDetailCondition.RelevantAnimationFinished => input.RelevantTimeRemainingSeconds == 0,
        AlsDetailCondition.Pivot => input.Pivot,
        AlsDetailCondition.PivotAfterElapsed => input.Pivot && input.StateElapsedSeconds > edge.Threshold,
        AlsDetailCondition.RunningGaitAndGroundedFull => input.Gait is AlsGait.Running or AlsGait.Sprinting && input.MainGroundedWeight == 1,
        AlsDetailCondition.RunningGaitAndGroundedNotFull => input.Gait is AlsGait.Running or AlsGait.Sprinting && input.MainGroundedWeight != 1,
        AlsDetailCondition.DetailFull => input.DetailWeight == 1,
        AlsDetailCondition.DetailNotFull => input.DetailWeight != 1,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };
}
