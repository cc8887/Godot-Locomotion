using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

internal sealed class AlsLocalPoseClip : IDisposable
{
    private readonly Godot.Animation _animation;
    private readonly bool _ownsAnimation;
    private readonly int[] _positions;
    private readonly int[] _rotations;
    private readonly int[] _scales;
    public double Length => _animation.Length;

    public AlsLocalPoseClip(Godot.Animation animation, Skeleton3D skeleton, bool ownsAnimation = true)
    {
        _animation = animation;
        _ownsAnimation = ownsAnimation;
        _positions = new int[skeleton.GetBoneCount()];
        _rotations = new int[_positions.Length];
        _scales = new int[_positions.Length];
        Array.Fill(_positions, -1);
        Array.Fill(_rotations, -1);
        Array.Fill(_scales, -1);
        try
        {
            for (var track = 0; track < animation.GetTrackCount(); track++)
            {
                using var path = animation.TrackGetPath(track);
                if (path.GetSubNameCount() != 1) throw new InvalidOperationException("Expected exact skeletal track.");
                var bone = skeleton.FindBone(path.GetSubName(0));
                if (bone < 0) throw new InvalidOperationException("Unknown local pose bone track.");
                var slots = animation.TrackGetType(track) switch
                {
                    Godot.Animation.TrackType.Position3D => _positions,
                    Godot.Animation.TrackType.Rotation3D => _rotations,
                    Godot.Animation.TrackType.Scale3D => _scales,
                    _ => throw new InvalidOperationException("Unsupported local pose track type."),
                };
                if (slots[bone] != -1) throw new InvalidOperationException("Duplicate local pose bone track.");
                slots[bone] = track;
            }
        }
        catch { if (_ownsAnimation) animation.Dispose(); throw; }
    }

    public void Sample(ReadOnlySpan<AlsLocalPose> rest, double timeSeconds, Span<AlsLocalPose> output)
    {
        if (rest.Length != _positions.Length || output.Length != rest.Length || !double.IsFinite(timeSeconds) ||
            timeSeconds < 0 || timeSeconds > Length) throw new ArgumentException("Invalid local pose sample.");
        for (var bone = 0; bone < output.Length; bone++)
        {
            var pose = rest[bone];
            if (_positions[bone] >= 0)
            {
                var p = _animation.PositionTrackInterpolate(_positions[bone], timeSeconds);
                pose = pose with { Position = new NVector3(p.X, p.Y, p.Z) };
            }
            if (_rotations[bone] >= 0)
            {
                var r = _animation.RotationTrackInterpolate(_rotations[bone], timeSeconds);
                pose = pose with { Rotation = new NQuaternion(r.X, r.Y, r.Z, r.W) };
            }
            if (_scales[bone] >= 0)
            {
                var s = _animation.ScaleTrackInterpolate(_scales[bone], timeSeconds);
                pose = pose with { Scale = new NVector3(s.X, s.Y, s.Z) };
            }
            output[bone] = pose;
        }
    }

    public void SampleSourceSeconds(ReadOnlySpan<AlsLocalPose> rest, float timeSeconds, float sourceDurationSeconds,
        Span<AlsLocalPose> output)
    {
        if (!float.IsFinite(sourceDurationSeconds) || sourceDurationSeconds <= 0 || (float)Length != sourceDurationSeconds ||
            !float.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > sourceDurationSeconds)
            throw new ArgumentException("Source seconds do not match the local pose clip.");
        // A valid float endpoint can round slightly above Godot's double-precision duration.
        Sample(rest, Math.Min(timeSeconds, Length), output);
    }

    public void Dispose() { if (_ownsAnimation) _animation.Dispose(); }
}
