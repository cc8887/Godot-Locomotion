using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>Native BlendList evaluation paths, including direct passthrough and
/// two-pose in-place blending. Weights remain those produced during Update.</summary>
public sealed class AlsOverlayActionMix
{
    private readonly int[] _active = new int[4];
    private Vector4 _weights;
    private int _count;
    private bool _single, _two;
    public void Prepare(Vector4 weights)
    {
        var count = 0; Span<int> active = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!float.IsFinite(weights[i]) || weights[i] is < 0 or > 1) throw new ArgumentException("Invalid action weights.");
            if (weights[i] > AlsPoseBlender.WeightThreshold) active[count++] = i;
        }
        if (count == 0) throw new ArgumentException("No active action pose.");
        active[..count].CopyTo(_active);
        _weights = weights; _count = count;
        _single = count == 1 && weights[_active[0]] >= 1 - AlsPoseBlender.WeightThreshold;
        _two = count == 2 && MathF.Abs(weights[_active[0]] + weights[_active[1]] - 1f) <= 1e-8f;
    }
    public AlsPrecisePose Pose(ReadOnlySpan<AlsPrecisePose> poses)
    {
        if (_count == 0 || poses.Length != 4) throw new ArgumentException("Invalid action pose mix.");
        if (_single) return poses[_active[0]];
        var value = AlsPrecisePoseBlender.Scale(poses[_active[0]], _weights[_active[0]]);
        for (var i = 1; i < _count; i++) value = AlsPrecisePoseBlender.Accumulate(value, poses[_active[i]],
            _two ? 1f - _weights[_active[0]] : _weights[_active[i]]);
        return value.Normalized();
    }
    public AlsInertialCurve Curve(ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (_count == 0 || curves.Length != 4) throw new ArgumentException("Invalid action curve mix.");
        if (_single) return curves[_active[0]];
        if (_two) return AlsStandingCycleCurves.Lerp(curves[_active[0]], curves[_active[1]], 1f - _weights[_active[0]]);
        var value = AlsStandingCycleCurves.Scale(curves[_active[0]], _weights[_active[0]]);
        for (var i = 1; i < _count; i++) value = AlsStandingCycleCurves.Accumulate(value, curves[_active[i]], _weights[_active[i]]);
        return value;
    }
}
