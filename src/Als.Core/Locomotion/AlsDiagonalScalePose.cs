using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>ALS component-space multiplicative scale, followed by UE local skeletal-control blending.
/// The ALS rig uses nonnegative scale; negative-scale matrix decomposition is not supported here.</summary>
public static class AlsDiagonalScalePose
{
    public static void Apply(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<int> parents, int target,
        Vector3 scale, float inputAlpha, Span<AlsPrecisePose> scratch, Span<AlsPrecisePose> output)
    {
        var count=basis.Length;
        if(count is <1 or >256 || parents.Length!=count || scratch.Length!=count || output.Length!=count ||
            (uint)target>=(uint)count || !float.IsFinite(inputAlpha) || !Finite(scale) || scale.X<0 || scale.Y<0 || scale.Z<0 ||
            basis.Overlaps(scratch) || scratch.Overlaps(output) || basis.Overlaps(output,out var offset) && offset!=0)
            throw new ArgumentException("Invalid precise diagonal scale buffers/input.");
        for(var b=0;b<count;b++)if(parents[b]<-1 || parents[b]>=b)throw new ArgumentException("Expected parent-first bones.");
        var alpha=System.Math.Clamp(inputAlpha,0,1);
        if(alpha<=AlsPoseBlender.WeightThreshold){basis.CopyTo(output);return;}
        Span<int> chain=stackalloc int[256]; var depth=0;
        for(var b=target;b>=0;b=parents[b])
        {
            basis[b].Validate(); if(basis[b].Scale.IsNegative)throw new ArgumentException("Negative diagonal scale ancestor.");
            chain[depth++]=b;
        }
        basis.CopyTo(output);
        for(var i=depth-1;i>=0;i--)
        {
            var b=chain[i]; scratch[b]=parents[b]<0 ? basis[b] : AlsPrecisePose.Compose(basis[b],scratch[parents[b]]).Normalized();
        }
        var parent=parents[target]; var original=scratch[target];
        var changed=original with {Scale=original.Scale*new AlsDoubleVector(scale)};
        var originalLocal=parent<0 ? original : AlsPrecisePose.Relative(original,scratch[parent]);
        var changedLocal=parent<0 ? changed : AlsPrecisePose.Relative(changed,scratch[parent]);
        if(alpha>=1-AlsPoseBlender.WeightThreshold)output[target]=parent<0 ? changed : changedLocal.Normalized();
        else
        {
            var beta=1-alpha; var bias=AlsQuaternion.Dot(changedLocal.Rotation,originalLocal.Rotation)>=0 ? 1 : -1;
            output[target]=new(changedLocal.Position+(originalLocal.Position-changedLocal.Position)*beta,
                (changedLocal.Rotation*(bias*(1.0-beta))+originalLocal.Rotation*beta).Normalized(),
                changedLocal.Scale+(originalLocal.Scale-changedLocal.Scale)*beta);
        }
        for(var i=1;i<depth;i++)
        {
            var b=chain[i]; output[b]=parents[b]<0 ? scratch[b] : AlsPrecisePose.Relative(scratch[b],scratch[parents[b]]).Normalized();
        }
    }

    public static void Apply(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<int> parents, int target,
        Vector3 scale, float inputAlpha, Span<AlsLocalPose> scratch, Span<AlsLocalPose> output)
    {
        var count = basis.Length;
        if (count is < 1 or > 256 || parents.Length != count || scratch.Length != count || output.Length != count ||
            (uint)target >= (uint)count || !float.IsFinite(inputAlpha) || !Finite(scale) || scale.X < 0 || scale.Y < 0 || scale.Z < 0 ||
            basis.Overlaps(scratch) || scratch.Overlaps(output) || basis.Overlaps(output, out var offset) && offset != 0)
            throw new ArgumentException("Invalid ALS diagonal scale buffers/input.");
        for (var i = 0; i < count; i++)
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Expected parent-first ALS bones.");
        var alpha = System.Math.Clamp(inputAlpha, 0, 1);
        Span<int> chain = stackalloc int[256]; var depth = 0;
        for (var bone = target; bone >= 0; bone = parents[bone])
        {
            chain[depth++] = bone;
            if (alpha > AlsPoseBlender.WeightThreshold && (!Valid(basis[bone]) ||
                basis[bone].Scale.X < 0 || basis[bone].Scale.Y < 0 || basis[bone].Scale.Z < 0))
                throw new ArgumentException("ALS diagonal scale requires finite, nonnegative-scale ancestor transforms.");
        }
        basis.CopyTo(output);
        if (alpha <= AlsPoseBlender.WeightThreshold) return;
        // FCSPose lazily computes only the target and its ancestors; other local poses stay untouched.
        for (var i = depth - 1; i >= 0; i--)
        {
            var bone = chain[i]; var parent = parents[bone]; var local = basis[bone];
            scratch[bone] = parent < 0 ? local : Multiply(local, scratch[parent]);
        }
        var parentId = parents[target]; var originalComponent = scratch[target];
        var changedComponent = originalComponent with { Scale = originalComponent.Scale * scale };
        if (alpha >= 1 - AlsPoseBlender.WeightThreshold)
            output[target] = parentId < 0 ? changedComponent : AlsPoseBlender.Normalize(Relative(changedComponent, scratch[parentId]));
        else
        {
            var originalLocal = parentId < 0 ? originalComponent : Relative(originalComponent, scratch[parentId]);
            var changedLocal = parentId < 0 ? changedComponent : Relative(changedComponent, scratch[parentId]);
            output[target] = BlendWith(changedLocal, originalLocal, 1 - alpha);
        }
        for (var i = 1; i < depth; i++)
        {
            var bone = chain[i]; var parent = parents[bone];
            output[bone] = parent < 0 ? scratch[bone] : AlsPoseBlender.Normalize(Relative(scratch[bone], scratch[parent]));
        }
    }

    private static AlsLocalPose Multiply(in AlsLocalPose local, in AlsLocalPose parent) => new(
        Vector3.Transform(parent.Scale * local.Position, parent.Rotation) + parent.Position,
        Quaternion.Normalize(parent.Rotation * local.Rotation), parent.Scale * local.Scale);

    private static AlsLocalPose Relative(in AlsLocalPose component, in AlsLocalPose parent)
    {
        var inverse = Quaternion.Conjugate(parent.Rotation);
        var reciprocal = new Vector3(Reciprocal(parent.Scale.X), Reciprocal(parent.Scale.Y), Reciprocal(parent.Scale.Z));
        return new(Vector3.Transform(component.Position - parent.Position, inverse) * reciprocal,
            inverse * component.Rotation, component.Scale * reciprocal);
    }

    private static AlsLocalPose BlendWith(in AlsLocalPose changed, in AlsLocalPose original, float beta)
    {
        if (beta <= AlsPoseBlender.WeightThreshold) return changed;
        if (beta >= 1 - AlsPoseBlender.WeightThreshold) return original;
        var bias = Quaternion.Dot(changed.Rotation, original.Rotation) >= 0 ? 1f : -1f;
        return new(changed.Position + (original.Position - changed.Position) * beta,
            Quaternion.Normalize(changed.Rotation * (bias * (1 - beta)) + original.Rotation * beta),
            changed.Scale + (original.Scale - changed.Scale) * beta);
    }
    private static float Reciprocal(float value) => MathF.Abs(value) <= 1e-8f ? 0 : 1 / value;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Valid(in AlsLocalPose pose) => Finite(pose.Position) && Finite(pose.Scale) &&
        float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .001f;
}
