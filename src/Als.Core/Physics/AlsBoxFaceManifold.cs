using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Box-box vertex/plane branch with the reference box as shape 1. Only a
// complete incident box over one flat face is supported; edge clipping, deep
// penetration and ambiguous incident faces remain the caller's responsibility.
public static class AlsBoxFaceManifold
{
    public static bool TryInteriorFace(AlsDoubleVector half0, in AlsPrecisePose pose0,
        AlsDoubleVector half1, in AlsPrecisePose pose1, double cullDistance,
        Span<AlsDetectedContact> destination, out int count, double edgeInset = 0)
    {
        count = 0; pose0.Validate(1e-5); pose1.Validate(1e-5);
        if (!half0.IsFinite || !half1.IsFinite || half0.X <= 0 || half0.Y <= 0 || half0.Z <= 0 ||
            half1.X <= 0 || half1.Y <= 0 || half1.Z <= 0 || pose0.Scale != AlsDoubleVector.One || pose1.Scale != AlsDoubleVector.One ||
            !double.IsFinite(cullDistance) || cullDistance < 0 || !double.IsFinite(edgeInset) || edgeInset < 0)
            throw new ArgumentException("Box face contacts need positive finite dimensions and rigid poses.");
        if (destination.Length < 4) throw new ArgumentException("Box face contacts require four-point capacity.");
        var relative = AlsPrecisePose.Relative(pose0, pose1);
        Span<AlsDoubleVector> vertices = stackalloc AlsDoubleVector[8];
        for (var i = 0; i < 8; i++) vertices[i] = Vertex(half0, i).Rotate(relative.Rotation) + relative.Position;
        var best = double.PositiveInfinity; var normal = AlsDoubleVector.Zero;
        var plane = AlsDoubleVector.Zero;
        for (var axis = 0; axis < 3; axis++) for (var sign = -1; sign <= 1; sign += 2)
        {
            if (sign * relative.Position[axis] <= half1[axis]) continue;
            var minimum = double.PositiveInfinity;
            foreach (var vertex in vertices) minimum = System.Math.Min(minimum, sign * vertex[axis]);
            var depth = half1[axis] - minimum;
            if (depth >= best) continue;
            var clearance = System.Math.Max(depth, 0) + edgeInset + 1e-3;
            var inside = true;
            foreach (var vertex in vertices) for (var tangent = 0; tangent < 3; tangent++) if (tangent != axis)
                inside &= System.Math.Abs(vertex[tangent]) < half1[tangent] - clearance;
            if (!inside) continue;
            best = depth; normal = axis == 0 ? new(sign, 0, 0) : axis == 1 ? new(0, sign, 0) : new(0, 0, sign);
            plane = normal * half1[axis];
        }
        if (!double.IsFinite(best)) return false;
        if (-best > cullDistance) return true;
        var localNormal = normal.Rotate(relative.Rotation.Conjugate());
        var incidentAxis = 0;
        for (var axis = 1; axis < 3; axis++)
            if (System.Math.Abs(localNormal[axis]) > System.Math.Abs(localNormal[incidentAxis])) incidentAxis = axis;
        // Native SelectContactPlane can depend on its GJK feature at a tie.
        // Do not invent a feature when two incident faces are equally suitable.
        for (var axis = 0; axis < 3; axis++) if (axis != incidentAxis &&
            System.Math.Abs(System.Math.Abs(localNormal[axis]) - System.Math.Abs(localNormal[incidentAxis])) < 1e-6) return false;
        var face = incidentAxis + (localNormal[incidentAxis] < 0 ? 3 : 0);
        ReadOnlySpan<int> faceVertices = [0,4,6,2, 0,1,5,4, 0,2,3,1, 1,3,7,5, 2,6,7,3, 4,5,7,6];
        // Native clipping leaves an interior face in cyclic order, then swaps
        // vertices 1/2 so the solver visits opposite corners consecutively.
        for (var i = 0; i < 4; i++)
        {
            var order = i == 1 ? 2 : i == 2 ? 1 : i;
            var vertex = faceVertices[face * 4 + order]; var p = vertices[vertex];
            var q = p - normal * AlsDoubleVector.Dot(p - plane, normal);
            destination[i] = new(Vertex(half0, vertex).ToSingle(), q.ToSingle(), normal.ToSingle());
        }
        count = 4; return true;
    }
    private static AlsDoubleVector Vertex(AlsDoubleVector half, int index) =>
        new((index & 1) == 0 ? -half.X : half.X, (index & 2) == 0 ? -half.Y : half.Y, (index & 4) == 0 ? -half.Z : half.Z);
}
