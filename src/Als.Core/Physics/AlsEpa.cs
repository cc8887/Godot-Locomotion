using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public enum AlsEpaStatus { Ok, MaxIterations, Degenerate, BadInitialSimplex }

// Coordinates for both witnesses are in A's frame. A failed/degenerate EPA
// result must be resolved by the GJK caller using the native fallback rules.
public readonly record struct AlsEpaResult(AlsEpaStatus Status, double Penetration,
    AlsDoubleVector Normal, AlsDoubleVector PointA, AlsDoubleVector PointBInA,
    int VertexA, int VertexB, int Iterations);

// One workspace per worker/owner; never share concurrent calls. Buffers grow
// on demand and survive calls. No arbitrary geometry-capacity fallback.
public sealed class AlsEpaWorkspace
{
    internal AlsGjkCache StagedCache { get; } = new();
    public int MaximumQueueCount { get; internal set; }
    internal AlsDoubleVector[] A = new AlsDoubleVector[32], B = new AlsDoubleVector[32];
    internal AlsEpa.Face[] Faces = new AlsEpa.Face[32];
    internal int[] Queue = new int[32];
    internal AlsEpa.Edge[] Border = new AlsEpa.Edge[32], Stack = new AlsEpa.Edge[32];
    internal int Vertices, FaceCount, QueueCount, BorderCount, StackCount;
    internal static void Ensure<T>(ref T[] buffer, int count)
    { if (buffer.Length < count) Array.Resize(ref buffer, System.Math.Max(count, buffer.Length * 2)); }
}

public static class AlsEpa
{
    internal struct Indices(int x, int y, int z)
    {
        private int _x = x, _y = y, _z = z;
        public int this[int i]
        {
            readonly get => i == 0 ? _x : i == 1 ? _y : _z;
            set { if (i == 0) _x = value; else if (i == 1) _y = value; else _z = value; }
        }
    }
    internal struct Face
    {
        public Indices Vertices, AdjFaces, AdjEdges;
        public AlsDoubleVector Normal;
        public double Distance;
        public bool Obsolete;
    }
    internal readonly record struct Edge(int Face, int Index);

    public static AlsEpaResult Run<TA, TB>(in TA shapeA, in TB shapeB, in AlsPrecisePose bToA,
        AlsGjkCache cache, AlsEpaWorkspace work, double epsilon)
        where TA : struct, IAlsGjkShape where TB : struct, IAlsGjkShape
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(work);
        shapeA.Validate(); shapeB.Validate(); bToA.Validate(1e-5);
        if (cache.Count is < 1 or > 4 || bToA.Scale != AlsDoubleVector.One || !double.IsFinite(epsilon) || epsilon <= 0)
            throw new ArgumentException("EPA requires a valid simplex, rigid pose and positive epsilon.");
        work.Vertices = cache.Count; work.FaceCount = 4; work.QueueCount = work.BorderCount = work.StackCount = 0;
        work.MaximumQueueCount = 0;
        for (var i = 0; i < cache.Count; i++)
        { work.A[i] = cache.WitnessA[i]; work.B[i] = cache.WitnessB[i].Rotate(bToA.Rotation) + bToA.Position; }
        var vertexA = -1; var vertexB = -1;
        if (!Initialize(shapeA, shapeB, bToA, work, ref vertexA, ref vertexB, out var touch))
            return new(AlsEpaStatus.BadInitialSimplex, 0, touch, AlsDoubleVector.Zero, AlsDoubleVector.Zero, vertexA, vertexB, 0);
        for (var i = 0; i < work.FaceCount; i++)
            if (work.Faces[i].Distance <= 0 || ProjectedInside(work, work.Faces[i])) Enqueue(work, i);
        var last = work.Faces[work.QueueCount > 0 ? work.Queue[work.QueueCount - 1] : 0];
        var upper = double.MaxValue; var dirty = true; var iteration = 0; var status = AlsEpaStatus.MaxIterations;
        while (work.QueueCount > 0 && iteration++ < 128)
        {
            if (dirty) { AlsEpaQueueSort.Sort(work); dirty = false; }
            var index = work.Queue[--work.QueueCount]; var face = work.Faces[index];
            if (face.Obsolete) continue;
            if (face.Distance > upper) { status = AlsEpaStatus.Ok; break; }
            var a = SupportA(shapeA, face.Normal, ref vertexA);
            var b = SupportB(shapeB, bToA, face.Normal * -1, ref vertexB); var point = a - b;
            upper = System.Math.Min(upper, AlsDoubleVector.Dot(face.Normal, point));
            var lower = face.Distance;
            if (upper <= (1 + epsilon) * System.Math.Abs(lower)) { status = AlsEpaStatus.Ok; last = face; break; }
            if (upper < lower) { status = AlsEpaStatus.Ok; break; }
            last = face; var newVertex = work.Vertices; AddVertex(work, a, b);
            work.Faces[index].Obsolete = true; Visibility(work, index, point);
            var first = work.FaceCount; var borderCount = work.BorderCount;
            AlsEpaWorkspace.Ensure(ref work.Faces, first + borderCount); work.FaceCount += borderCount;
            if (borderCount < 3) { status = AlsEpaStatus.Degenerate; break; }
            var terminate = false;
            for (var i = 0; i < borderCount; i++)
            {
                var border = work.Border[i]; var old = work.Faces[border.Face]; var current = first + i;
                var next = i + 1 < borderCount ? current + 1 : first;
                var previous = i > 0 ? current - 1 : first + borderCount - 1;
                var valid = InitFace(work, current, old.Vertices[(border.Index + 1) % 3], old.Vertices[border.Index], newVertex,
                    new(border.Face, previous, next), new(border.Index, 2, 1));
                work.Faces[border.Face].AdjFaces[border.Index] = current;
                work.Faces[border.Face].AdjEdges[border.Index] = 0;
                if (!valid) { status = AlsEpaStatus.Degenerate; terminate = true; break; }
                var added = work.Faces[current];
                if (added.Distance <= upper && (added.Distance <= 0 || ProjectedInside(work, added)))
                { Enqueue(work, current); dirty = true; }
            }
            if (terminate) break;
        }
        return Results(work, last, status, vertexA, vertexB, iteration);
    }

    private static AlsDoubleVector SupportA<T>(in T shape, AlsDoubleVector direction, ref int vertex) where T : struct, IAlsGjkShape
    {
        var point = shape.Support(direction, out vertex, out var delta);
        if (!point.IsFinite || !double.IsFinite(delta) || vertex < 0) throw new InvalidOperationException("Invalid EPA support.");
        return point;
    }
    private static AlsDoubleVector SupportB<T>(in T shape, in AlsPrecisePose pose, AlsDoubleVector direction, ref int vertex) where T : struct, IAlsGjkShape
        => SupportA(shape, direction.Rotate(pose.Rotation.Conjugate()), ref vertex).Rotate(pose.Rotation) + pose.Position;
    private static void AddVertex(AlsEpaWorkspace w, AlsDoubleVector a, AlsDoubleVector b)
    {
        AlsEpaWorkspace.Ensure(ref w.A, w.Vertices + 1); AlsEpaWorkspace.Ensure(ref w.B, w.Vertices + 1);
        w.A[w.Vertices] = a; w.B[w.Vertices++] = b;
    }
    private static void Farther<TA, TB>(in TA a, in TB b, in AlsPrecisePose pose, AlsEpaWorkspace w,
        AlsDoubleVector dir, ref int va, ref int vb) where TA : struct, IAlsGjkShape where TB : struct, IAlsGjkShape
    {
        // Query order matters: returned vertex IDs are from the last support
        // calls, not necessarily the endpoint selected by the distance test.
        var a0 = SupportA(a, dir, ref va); var a1 = SupportA(a, dir * -1, ref va);
        var b0 = SupportB(b, pose, dir * -1, ref vb); var b1 = SupportB(b, pose, dir, ref vb);
        if (AlsDoubleVector.Dot(a1 - b1, dir * -1) >= AlsDoubleVector.Dot(a0 - b0, dir)) AddVertex(w, a1, b1);
        else AddVertex(w, a0, b0);
    }
    private static AlsDoubleVector Minkowski(AlsEpaWorkspace w, int i) => w.A[i] - w.B[i];
    private static bool InitFace(AlsEpaWorkspace w, int index, int x, int y, int z, Indices faces, Indices edges)
    {
        var p = Minkowski(w, x); var normal = AlsDoubleVector.Cross(Minkowski(w, y) - p, Minkowski(w, z) - p);
        var squared = normal.LengthSquared;
        if (!double.IsFinite(squared)) throw new InvalidOperationException("Nonfinite EPA face.");
        if (squared < 1e-8 * 1e-8 * 1e-8 * 1e-8) return false;
        normal *= 1 / System.Math.Sqrt(squared);
        w.Faces[index] = new() { Vertices = new(x,y,z), AdjFaces = faces, AdjEdges = edges, Normal = normal, Distance = AlsDoubleVector.Dot(normal,p) };
        return true;
    }
    private static bool Initialize<TA, TB>(in TA a, in TB b, in AlsPrecisePose pose, AlsEpaWorkspace w,
        ref int va, ref int vb, out AlsDoubleVector touch) where TA : struct, IAlsGjkShape where TB : struct, IAlsGjkShape
    {
        touch = new(0,0,1); var count = w.Vertices; var valid = false;
        if (count == 1) Farther(a,b,pose,w,touch,ref va,ref vb);
        if (count <= 2)
        {
            var dir = Minkowski(w,1) - Minkowski(w,0);
            if (dir.LengthSquared <= 1e-4) return false;
            var axis = 0; var min = System.Math.Abs(dir.X);
            if (System.Math.Abs(dir.Y) < min) { axis = 1; min = System.Math.Abs(dir.Y); }
            if (System.Math.Abs(dir.Z) < min) axis = 2;
            var orthogonal = AlsDoubleVector.Cross(dir, axis == 0 ? new(1,0,0) : axis == 1 ? new(0,1,0) : new(0,0,1));
            var second = AlsDoubleVector.Cross(orthogonal,dir);
            Farther(a,b,pose,w,orthogonal,ref va,ref vb); Farther(a,b,pose,w,second,ref va,ref vb);
            valid = InitTetrahedron(w);
            if (!valid) { touch = orthogonal * (1 / System.Math.Sqrt(orthogonal.LengthSquared)); return false; }
        }
        else if (count == 3)
        {
            valid = InitFace(w,3,0,2,1,new(1,0,2),new(2,0,0));
            if (valid)
            {
                var normal = w.Faces[3].Normal; Farther(a,b,pose,w,normal,ref va,ref vb);
                valid = InitSides(w);
                if (!valid) { touch = normal; return false; }
            }
        }
        else valid = InitTetrahedron(w);
        if (valid)
        {
            var farthest = 0d;
            for (var i = 0; i < 4; i++) if (System.Math.Abs(w.Faces[i].Distance) > System.Math.Abs(farthest)) farthest = w.Faces[i].Distance;
            if (farthest < 0) for (var i = 0; i < 4; i++) SwapWinding(w, i);
        }
        return valid;
    }
    private static bool InitSides(AlsEpaWorkspace w)
    {
        var valid = InitFace(w,0,1,2,3,new(3,1,2),new(1,1,1));
        valid &= InitFace(w,1,0,3,2,new(2,0,3),new(2,1,0));
        valid &= InitFace(w,2,0,1,3,new(3,0,1),new(2,2,0)); return valid;
    }
    private static bool InitTetrahedron(AlsEpaWorkspace w)
    { var valid = InitSides(w); return InitFace(w,3,0,2,1,new(1,0,2),new(2,0,0)) & valid; }
    private static void SwapWinding(AlsEpaWorkspace w, int i)
    {
        var f = w.Faces[i]; (f.Vertices[0], f.Vertices[1]) = (f.Vertices[1], f.Vertices[0]);
        w.Faces[f.AdjFaces[1]].AdjEdges[f.AdjEdges[1]] = 2;
        w.Faces[f.AdjFaces[2]].AdjEdges[f.AdjEdges[2]] = 1;
        (f.AdjFaces[1], f.AdjFaces[2]) = (f.AdjFaces[2], f.AdjFaces[1]);
        (f.AdjEdges[1], f.AdjEdges[2]) = (f.AdjEdges[2], f.AdjEdges[1]);
        f.Normal *= -1; f.Distance *= -1; w.Faces[i] = f;
    }
    private static bool ProjectedInside(AlsEpaWorkspace w, Face f)
    {
        var pn = f.Normal * f.Distance;
        var a = Minkowski(w,f.Vertices[0]) - pn; var b = Minkowski(w,f.Vertices[1]) - pn; var c = Minkowski(w,f.Vertices[2]) - pn;
        var ac = AlsDoubleVector.Dot(AlsDoubleVector.Cross(a,c),f.Normal);
        var cb = AlsDoubleVector.Dot(AlsDoubleVector.Cross(c,b),f.Normal);
        if (ac < 0 && cb > 0 || ac > 0 && cb < 0) return false;
        var ba = AlsDoubleVector.Dot(AlsDoubleVector.Cross(b,a),f.Normal);
        return !(ac < 0 && ba > 0 || ac > 0 && ba < 0);
    }
    private static void Enqueue(AlsEpaWorkspace w, int index)
    {
        AlsEpaWorkspace.Ensure(ref w.Queue,w.QueueCount+1); w.Queue[w.QueueCount++] = index;
        w.MaximumQueueCount = System.Math.Max(w.MaximumQueueCount,w.QueueCount);
    }
    private static void Push(AlsEpaWorkspace w, Edge edge)
    { AlsEpaWorkspace.Ensure(ref w.Stack,w.StackCount+1); w.Stack[w.StackCount++] = edge; }
    private static void Visibility(AlsEpaWorkspace w, int index, AlsDoubleVector point)
    {
        w.BorderCount = w.StackCount = 0; var f = w.Faces[index];
        for (var i = 0; i < 3; i++) Push(w,new(f.AdjFaces[i],f.AdjEdges[i]));
        var iteration = 0;
        while (w.StackCount > 0 && iteration++ < 10000)
        {
            var edge = w.Stack[--w.StackCount]; f = w.Faces[edge.Face];
            if (f.Obsolete) continue;
            if (AlsDoubleVector.Dot(f.Normal,point) - f.Distance <= 1e-8)
            { AlsEpaWorkspace.Ensure(ref w.Border,w.BorderCount+1); w.Border[w.BorderCount++] = edge; }
            else
            {
                w.Faces[edge.Face].Obsolete = true;
                var next = (edge.Index+1)%3; var previous = (edge.Index+2)%3;
                Push(w,new(f.AdjFaces[next],f.AdjEdges[next])); Push(w,new(f.AdjFaces[previous],f.AdjEdges[previous]));
            }
        }
        if (iteration >= 10000) throw new InvalidOperationException("EPA visibility traversal exceeded its safety limit.");
    }
    private static AlsEpaResult Results(AlsEpaWorkspace w, Face f, AlsEpaStatus status, int va, int vb, int iteration)
    {
        Span<AlsDoubleVector> a = stackalloc AlsDoubleVector[4], b = stackalloc AlsDoubleVector[4], p = stackalloc AlsDoubleVector[4];
        Span<double> weights = stackalloc double[4]; var count = 3;
        for (var i = 0; i < 3; i++) { a[i] = w.A[f.Vertices[i]]; b[i] = w.B[f.Vertices[i]]; p[i] = a[i] - b[i]; }
        var normal = AlsGjkSimplex.Closest(p,ref count,weights,a,b); var penetration = System.Math.Sqrt(normal.LengthSquared);
        if (penetration < 1e-4) { normal = f.Normal; if (f.Distance < 0) penetration *= -1; }
        else { normal *= 1 / penetration; if (f.Distance < 0) { normal *= -1; penetration *= -1; } }
        var pa = AlsDoubleVector.Zero; var pb = AlsDoubleVector.Zero;
        for (var i = 0; i < count; i++) { pa += a[i]*weights[i]; pb += b[i]*weights[i]; }
        return new(status,penetration,normal,pa,pb,va,vb,iteration);
    }
}
