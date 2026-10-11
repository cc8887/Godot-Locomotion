namespace GodotAls.Core.Locomotion;

/// <summary>Scale temporal rotation around an authored reference without changing bone lengths or static offsets.</summary>
public static class AlsPoseRotationVariation
{
    public static AlsPrecisePose Amplify(in AlsPrecisePose sample, in AlsPrecisePose reference, float gain)
    {
        if (!float.IsFinite(gain) || gain is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(gain));
        if (gain == 1 || sample.Rotation == reference.Rotation) return sample;
        sample.Validate(); reference.Validate();
        if (gain == 0) return sample with { Rotation = reference.Rotation };
        var delta = (sample.Rotation * reference.Rotation.Conjugate()).Normalized();
        // q and -q represent the same rotation; always scale the shortest arc.
        if (delta.W < 0) delta = -delta;
        var length = System.Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        if (length <= 1e-12) return sample;
        var halfAngle = System.Math.Atan2(length, delta.W) * gain;
        var scale = System.Math.Sin(halfAngle) / length;
        var amplified = new AlsQuaternion(delta.X * scale, delta.Y * scale, delta.Z * scale, System.Math.Cos(halfAngle));
        return sample with { Rotation = (amplified * reference.Rotation).Normalized() };
    }
}
