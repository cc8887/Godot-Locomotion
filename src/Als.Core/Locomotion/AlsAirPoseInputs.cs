using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsAirBlendInput(bool HasHistory, float History, float Alpha)
{
    // Native Initialize resets the interpolator flag, not the node's previously computed blend alpha.
    public AlsAirBlendInput Initialize() => this with { HasHistory = false };
    // Initialize clears both relevance flags; Evaluate falls back to A until Update visits this node.
    public float PoseAlpha => !HasHistory || Alpha <= AlsPoseBlender.WeightThreshold ? 0 : Alpha >= 1 - AlsPoseBlender.WeightThreshold ? 1 : Alpha;
    public AlsAirBlendInput Update(float target, float delta, float increasing, float decreasing)
    {
        if (!float.IsFinite(target) || !float.IsFinite(delta) || delta < 0 || !float.IsFinite(History) ||
            !float.IsFinite(increasing) || increasing <= 0 || !float.IsFinite(decreasing) || decreasing <= 0)
            throw new ArgumentException("Invalid air blend input.");
        var distance = target - History;
        var result = !HasHistory || distance * distance < 1e-8f ? target :
            History + distance * System.Math.Clamp(delta * (target > History ? increasing : decreasing), 0, 1);
        if (!float.IsFinite(result)) throw new ArgumentException("Air blend overflow.");
        return new(true, result, System.Math.Clamp(result, 0, 1));
    }
}

// Each Fall/Jump state owns its own copy. Only visited nodes capture exposed inputs.
public readonly record struct AlsAirPoseInputs(AlsAirBlendInput Prediction, AlsAirBlendInput Flail,
    AlsAirBlendInput Fast, float PredictionLight, Vector2 Lean, bool LeanHasSamples, bool AdditiveActive,
    bool PredictionLightHasUpdated = false)
{
    public AlsAirPoseInputs Initialize() => this with { Prediction = Prediction.Initialize(), Flail = Flail.Initialize(),
        Fast = Fast.Initialize(), LeanHasSamples = false, PredictionLightHasUpdated = false };

    public AlsAirPoseInputs Update(bool jump, float fallSpeedMetres, float prediction, Vector2 lean, float delta)
    {
        var speed = fallSpeedMetres * 100;
        if (!float.IsFinite(speed) || !float.IsFinite(lean.LengthSquared())) throw new ArgumentException("Invalid air pose inputs.");
        var result = this with { Prediction = Prediction.Update(prediction, delta, 20, 5) };
        if (result.Prediction.PoseAlpha < 1)
        {
            if (!jump)
            {
                result = result with { Flail = Flail.Update((speed + 500) / -2500, delta, 5, 5) };
                if (result.Flail.PoseAlpha < 1) result = result with { Fast = Fast.Update(1 - (speed + 1000) / 1000, delta, 5, 5) };
            }
            result = result with { Lean = lean, LeanHasSamples = true, AdditiveActive = true };
        }
        if (result.Prediction.PoseAlpha > 0) result = result with { PredictionLight = System.Math.Clamp((speed + 1000) / 500, 0, 1), PredictionLightHasUpdated = true };
        return result;
    }
    public float PredictionLightPoseAlpha => !PredictionLightHasUpdated || PredictionLight <= AlsPoseBlender.WeightThreshold ? 0 :
        PredictionLight >= 1 - AlsPoseBlender.WeightThreshold ? 1 : PredictionLight;
}
