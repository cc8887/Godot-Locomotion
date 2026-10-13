using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Adapt a Refactored action's authored ViewBlock to the V4 upper graph's
// two view branches. The caller owns the montage blend and curve feedback.
public static class AlsRefactoredV4ViewMask
{
    public static void Apply(ReadOnlySpan<string> names, Span<AlsInertialCurve> curves)
    {
        if (names.Length != curves.Length) throw new ArgumentException("View mask layout differs.");
        var source = names.IndexOf("ViewBlock");
        if (source < 0 || !curves[source].Present) return;
        var value = curves[source].Value;
        if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite authored view block.");
        var block = Math.Clamp(value, 0, 1);
        if (block == 0) return;
        var aim = names.IndexOf("Mask_AimOffset");
        var spine = names.IndexOf("Enable_SpineRotation");
        if (aim < 0 || spine < 0) throw new ArgumentException("Incomplete V4 view mask targets.");
        if (!float.IsFinite(curves[aim].Value) || !float.IsFinite(curves[spine].Value))
            throw new ArgumentException("Nonfinite V4 view mask.");
        // Both gates retain their existing restrictions. Multiplication of
        // allowed weights also makes a zero ViewBlock an exact no-op.
        var oldMask = curves[aim].Present ? Math.Clamp(curves[aim].Value, 0, 1) : 0;
        curves[aim] = new(1 - (1 - oldMask) * (1 - block));
        if (curves[spine].Present)
            curves[spine] = curves[spine] with { Value = curves[spine].Value * (1 - block) };
    }
}
