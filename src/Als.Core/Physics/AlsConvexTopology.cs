using System.Numerics;

namespace GodotAls.Core.Physics;

public readonly record struct AlsConvexPlane(Vector3 Normal, Vector3 Point, int FirstVertex, int VertexCount);

// Immutable cooked geometry in native convex-local cm. Construction validates
// face ranges and geometry; it never merges triangles or reorders faces.
public sealed class AlsConvexTopology
{
    private readonly Vector3[] _vertices;
    private readonly AlsConvexPlane[] _planes;
    private readonly int[] _indices;
    public float Margin { get; }
    public int VertexCount => _vertices.Length;
    public int PlaneCount => _planes.Length;
    // Native cooked assets can contain unpaired or multiply used face edges.
    // Consumers requiring a closed manifold must check this explicitly.
    public bool HasClosedOrientedEdges { get; }
    public Vector3 VertexAt(int index) => _vertices[index];
    public AlsConvexPlane PlaneAt(int index) => _planes[index];
    public ReadOnlySpan<int> FaceVertices(int plane)
    { var p = _planes[plane]; return _indices.AsSpan(p.FirstVertex, p.VertexCount); }

    public AlsConvexTopology(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<AlsConvexPlane> planes,
        ReadOnlySpan<int> indices, float margin)
    {
        if (vertices.Length < 4 || planes.Length < 4 || !float.IsFinite(margin) || margin < 0)
            throw new ArgumentException("Invalid cooked convex dimensions or margin.");
        _vertices = vertices.ToArray(); _planes = planes.ToArray(); _indices = indices.ToArray(); Margin = margin;
        foreach (var v in _vertices) if (!Finite(v)) throw new ArgumentException("Nonfinite convex vertex.");
        var edges = new Dictionary<(int, int), (int Count, int Winding)>(); var offset = 0;
        foreach (var p in _planes)
        {
            if (!Finite(p.Normal) || !Finite(p.Point) || MathF.Abs(p.Normal.LengthSquared() - 1) > 1e-5f ||
                p.FirstVertex != offset || p.VertexCount < 3 || p.VertexCount > _indices.Length - offset)
                throw new ArgumentException("Invalid convex plane or face range.");
            var seen = new HashSet<int>(); var area = Vector3.Zero;
            for (var j = 0; j < p.VertexCount; j++)
            {
                var a = _indices[offset + j]; var b = _indices[offset + (j + 1) % p.VertexCount];
                if ((uint)a >= _vertices.Length || (uint)b >= _vertices.Length || !seen.Add(a))
                    throw new ArgumentException("Invalid or repeated face vertex.");
                // Chaos FConvex::MergeFaces uses a 1 cm distance tolerance.
                // Cooked polygon loops are therefore not necessarily coplanar;
                // preserve their original vertices rather than projecting them.
                if (MathF.Abs(Vector3.Dot(_vertices[a] - p.Point, p.Normal)) > 1f + 1e-3f)
                    throw new ArgumentException("Face vertex is not on its plane.");
                area += Vector3.Cross(_vertices[a] - p.Point, _vertices[b] - p.Point);
                var key = (System.Math.Min(a, b), System.Math.Max(a, b)); edges.TryGetValue(key, out var edge);
                edges[key] = (edge.Count + 1, edge.Winding + (a < b ? 1 : -1));
            }
            if (Vector3.Dot(area, p.Normal) <= 0) throw new ArgumentException("Degenerate or reversed convex face.");
            foreach (var v in _vertices) if (Vector3.Dot(v - p.Point, p.Normal) > 1e-3f)
                throw new ArgumentException("Vertex lies outside a convex plane.");
            offset += p.VertexCount;
        }
        if (offset != _indices.Length) throw new ArgumentException("Unused convex face indices.");
        HasClosedOrientedEdges = edges.Values.All(e => e.Count == 2 && e.Winding == 0);
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
