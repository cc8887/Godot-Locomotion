namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLandingBlendInputs(float Land, float Moving, bool MovingAdditiveActive = false)
{
    public AlsLandingBlendInputs Update(int state, float fallSpeedMetres)
    {
        if (state is not (3 or 6) || !float.IsFinite(fallSpeedMetres)) throw new ArgumentException("Invalid landing input.");
        var speed = MathF.Abs(fallSpeedMetres * 100);
        if (!float.IsFinite(speed)) throw new ArgumentException("Landing speed conversion overflow.");
        var alpha = state == 3 ? (speed - 500) / 500 : (speed - 750) / 750 * .75f;
        alpha = System.Math.Clamp(alpha, 0, 1);
        return state == 3 ? this with { Land = alpha } : this with { Moving = alpha, MovingAdditiveActive = true };
    }
    public float PoseAlpha(int state)
    {
        var alpha = state switch { 3 => Land, 6 => Moving, _ => throw new ArgumentException("Not a landing state.") };
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentException("Invalid captured landing alpha.");
        return alpha <= AlsPoseBlender.WeightThreshold ? 0 : alpha >= 1 - AlsPoseBlender.WeightThreshold ? 1 : alpha;
    }
}
