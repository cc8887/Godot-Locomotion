namespace GodotAls.Core.Locomotion;

/// <summary>Optional platform numerical backend. Yaw is expressed in degrees.</summary>
public delegate void AlsYawSinCos(float yaw, out double sine, out double cosine);

public static class AlsRootRotationMath
{
    public static AlsQuaternion Quaternion(float yaw, AlsYawSinCos? backend = null)
    {
        if (!float.IsFinite(yaw)) throw new ArgumentOutOfRangeException(nameof(yaw));
        double sine, cosine;
        if (backend is not null) backend(yaw, out sine, out cosine);
        else
        {
            var angle = ((double)yaw % 360d) * (System.Math.PI / 180d / 2d);
            (sine, cosine) = System.Math.SinCos(angle);
        }
        return new(0, 0, sine, cosine);
    }
}
