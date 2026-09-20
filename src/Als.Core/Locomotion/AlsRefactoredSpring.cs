namespace GodotAls.Core.Locomotion;

// Shared FMath::SpringDamper kernel. Callers retain their distinct ALS/Kismet
// initialization, time and target-velocity semantics.
internal static class AlsRefactoredSpring
{
    public static void Evaluate(ref float value, ref float velocity, float target, float targetVelocity,
        float delta, float frequency, float damping)
    {
        var w = frequency * 6.28318530718f;
        if (w < 1e-8f) { value += velocity * delta; return; }
        if (damping < 1e-8f)
        {
            var error = value - target; var b = velocity / w;
            var (s, c) = AlsRefactoredRigMath.SinCos(w * delta);
            value = target + error * c + b * s;
            velocity = velocity * c - error * (w * s);
            return;
        }
        var adjusted = target + targetVelocity * (damping * (2f / w));
        var err = value - adjusted;
        if (damping > 1)
        {
            var wd = w * MathF.Sqrt(damping * damping - 1f);
            var c2 = -(velocity + (w * damping - wd) * err) / (2f * wd);
            var c1 = err - c2;
            var a1 = wd - damping * w; var a2 = -(wd + damping * w);
            var e1 = AlsRefactoredRigMath.InvExp(-a1 * delta);
            var e2 = AlsRefactoredRigMath.InvExp(-a2 * delta);
            value = adjusted + e1 * c1 + e2 * c2;
            velocity = e1 * c1 * a1 + e2 * c2 * a2;
        }
        else if (damping < 1)
        {
            var wd = w * MathF.Sqrt(1f - damping * damping);
            var b = (velocity + err * (damping * w)) / wd;
            var (s, c) = AlsRefactoredRigMath.SinCos(wd * delta);
            var e = AlsRefactoredRigMath.InvExp(damping * w * delta);
            value = e * (err * c + b * s);
            velocity = -value * damping * w;
            velocity += e * (b * (wd * c) - err * (wd * s));
            value += adjusted;
        }
        else
        {
            var c2 = velocity + err * w;
            var e = AlsRefactoredRigMath.InvExp(w * delta);
            value = adjusted + (err + c2 * delta) * e;
            velocity = (c2 - err * w - c2 * (w * delta)) * e;
        }
    }
}
