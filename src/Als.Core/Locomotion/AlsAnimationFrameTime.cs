using M = System.Math;

namespace GodotAls.Core.Locomotion;

// Native frame/subframe conversion shared by raw and decoded compressed tracks.
public static class AlsAnimationFrameTime
{
    public const float MaxSubframe = .999999940395355224609375f;
    public static (int Frame, float Subframe) FromFramePosition(double position, AlsRawFrameTimeRounding rounding)
    {
        if (!double.IsFinite(position) || rounding is not (AlsRawFrameTimeRounding.OptimizedCancellation or AlsRawFrameTimeRounding.RoundSubframe))
            throw new ArgumentOutOfRangeException(nameof(position));
        var floor = M.Floor(position);
        var frame = (int)M.Clamp(floor, int.MinValue, int.MaxValue);
        var subframe = (float)(position - floor);
        var carry = (int)subframe;
        subframe -= carry; frame = unchecked(frame + carry);
        if (subframe > 0) subframe = MathF.Min(subframe, MaxSubframe);
        if (rounding == AlsRawFrameTimeRounding.RoundSubframe) subframe = (subframe + .5f) - .5f;
        return (frame, M.Clamp(subframe, 0, MaxSubframe));
    }
}
