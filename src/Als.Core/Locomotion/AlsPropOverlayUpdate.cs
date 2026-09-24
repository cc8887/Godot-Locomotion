using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsPropOverlayUpdateState(AlsOverlayActionBlendState Actions,
    AlsBinaryBlendState Aim, bool IdleResetPending);
public readonly record struct AlsPropOverlayUpdateResult(AlsPropOverlayUpdateState State,
    bool UpdateIdle, bool ResetIdle, float IdleWeight);

/// <summary>Binoculars/Torch nested action and rotation-tag updates. Pose evaluation
/// is separate; the caller commits this value only with its shared source batch.</summary>
public static class AlsPropOverlayUpdate
{
    public static AlsPropOverlayUpdateResult Advance(in AlsPropOverlayUpdateState previous,
        AlsOverlayAction action, bool aiming, float delta, bool reinitialize = false)
    {
        var actions = AlsOverlayActionBlend.Advance(reinitialize ? default : previous.Actions,
            action, delta, new Vector4(.5f, .2f, 0, .2f));
        var update = actions.State.Weights.X > AlsPoseBlender.WeightThreshold || actions.ZeroWeightPrevious == 0;
        var aim = reinitialize ? default : previous.Aim;
        if (update) aim = AlsBinaryBlendList.Advance(aim, aiming ? 1 : 0, delta,
            new(.75f, .2f, AlsTransitionBlend.HermiteCubic)).State;
        var reset = previous.IdleResetPending || reinitialize;
        return new(new(actions.State, aim, reset && !update), update, reset && update, actions.State.Weights.X * .5f);
    }

    public static Vector2 AimWeights(in AlsPropOverlayUpdateState state) => state.Aim.Initialized ?
        new(state.Aim.FirstWeight, state.Aim.SecondWeight) : new(1, 0);
}
