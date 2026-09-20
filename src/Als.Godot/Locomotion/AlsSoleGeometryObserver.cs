using Godot;
using NVector3 = System.Numerics.Vector3;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Main-thread imported mesh access: exports immutable full-foot support data and
// independently observes a fixed lower-sole subset through actual skinning.
// Geometric contact does not claim a load-bearing physics constraint.
internal sealed class AlsSoleGeometryObserver
{
    private sealed record Surface(Skeleton3D Skeleton, Transform3D[] Binds, int[] Bones,
        Vector3[] Vertices, int[] Indices, float[] Weights, int Stride);
    private sealed record Point(Surface Surface, int Vertex, bool Left, float RestHeight);
    private readonly Point[] _points;
    private readonly Point[] _supportPoints;
    private readonly Dictionary<Skeleton3D, Transform3D[]> _poses = [];
    public int LeftCount { get; }
    public int RightCount { get; }
    public float BindErrorMeters { get; }

    public AlsSoleGeometryObserver(Node root)
    {
        RequireMainThread();
        var candidates = new List<Point>();
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is not { } resource || mesh.GetSkinReference() is not { } reference) continue;
            var skin = reference.GetSkin();
            if (skin is null || skin.GetBindCount() == 0) continue;
            var skeleton = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
            if (mesh.GetBlendShapeCount() != 0)
                throw new InvalidOperationException("Sole observation does not yet evaluate mesh blend shapes.");
            var count = skin.GetBindCount(); var bones = new int[count]; var binds = new Transform3D[count];
            for (var i = 0; i < count; i++)
            {
                var name = skin.GetBindName(i);
                bones[i] = name.IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(name);
                if ((uint)bones[i] >= skeleton.GetBoneCount()) throw new InvalidOperationException("Unresolved skin binding.");
                binds[i] = skin.GetBindPose(i);
            }
            var rest = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneGlobalRest).ToArray();
            _poses.TryAdd(skeleton, new Transform3D[rest.Length]);
            for (var s = 0; s < resource.GetSurfaceCount(); s++)
            {
                using var arrays = resource.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                if (vertices.Length == 0 || indices.Length != weights.Length || indices.Length % vertices.Length != 0)
                    throw new InvalidOperationException("Incomplete skinned surface arrays.");
                var stride = indices.Length / vertices.Length;
                if (stride is not (4 or 8)) throw new InvalidOperationException("Unsupported skin weight count.");
                var surface = new Surface(skeleton, binds, bones, vertices, indices, weights, stride);
                for (var v = 0; v < vertices.Length; v++)
                {
                    float left = 0, right = 0, total = 0;
                    for (var w = 0; w < stride; w++)
                    {
                        var weight = weights[v * stride + w];
                        if (!float.IsFinite(weight) || weight < 0) throw new InvalidOperationException("Invalid skin weight.");
                        if (weight == 0) continue;
                        var bind = indices[v * stride + w];
                        if ((uint)bind >= bones.Length) throw new InvalidOperationException("Invalid skin index.");
                        var boneName = skeleton.GetBoneName(bones[bind]).ToString().ToLowerInvariant();
                        if (boneName is "foot_l" or "ball_l") left += weight;
                        if (boneName is "foot_r" or "ball_r") right += weight;
                        total += weight;
                    }
                    if (MathF.Abs(total - 1) > .001f) throw new InvalidOperationException("Skin weights are not normalized.");
                    var worldRest = skeleton.GlobalTransform * Skin(surface, v, rest);
                    BindErrorMeters = MathF.Max(BindErrorMeters, worldRest.DistanceTo(mesh.GlobalTransform * vertices[v]));
                    if (left >= .99f || right >= .99f) candidates.Add(new(surface, v, left >= .99f, worldRest.Y));
                }
            }
        }
        if (BindErrorMeters > .0001f || !candidates.Any(p => p.Left) || !candidates.Any(p => !p.Left))
            throw new InvalidOperationException($"Mesh bind/foot selection failed: bind_error_m={BindErrorMeters} candidates={candidates.Count}.");
        var leftMin = candidates.Where(p => p.Left).Min(p => p.RestHeight);
        _supportPoints = candidates.ToArray();
        var rightMin = candidates.Where(p => !p.Left).Min(p => p.RestHeight);
        // Keep the authored lower 4 mm of each foot's mesh in bind pose. This
        // geometric selection remains fixed; it is never adjusted to pass a trace.
        _points = candidates.Where(p => p.RestHeight <= (p.Left ? leftMin : rightMin) + .004f).ToArray();
        LeftCount = _points.Count(p => p.Left); RightCount = _points.Length - LeftCount;
    }

    public object Capture(Transform3D surfaceToWorld, float surfaceTop, bool includeFullSupport = false)
    {
        RequireMainThread();
        foreach (var (skeleton, poses) in _poses)
            for (var bone = 0; bone < poses.Length; bone++) poses[bone] = skeleton.GetBoneGlobalPose(bone);
        var inverse = surfaceToWorld.AffineInverse();
        var left = new List<NVector3>(); var right = new List<NVector3>();
        foreach (var p in _points)
        {
            var local = inverse * (p.Surface.Skeleton.GlobalTransform * Skin(p.Surface, p.Vertex, _poses[p.Surface.Skeleton]));
            (p.Left ? left : right).Add(new(local.X, local.Y - surfaceTop, local.Z));
        }
        if (includeFullSupport)
        {
            float leftMinimum = float.PositiveInfinity, rightMinimum = float.PositiveInfinity;
            int leftCount = 0, rightCount = 0;
            foreach (var point in _supportPoints)
            {
                var local = inverse * (point.Surface.Skeleton.GlobalTransform * Skin(point.Surface, point.Vertex, _poses[point.Surface.Skeleton]));
                if (point.Left) { leftMinimum = MathF.Min(leftMinimum, local.Y - surfaceTop); leftCount++; }
                else { rightMinimum = MathF.Min(rightMinimum, local.Y - surfaceTop); rightCount++; }
            }
            return new { Left = left, Right = right, LeftMinimumHeight = left.Min(p => p.Y), RightMinimumHeight = right.Min(p => p.Y),
                LeftCount, RightCount, BindErrorMeters, Scope = "Fixed lower sole samples plus complete foot/toe-weighted support vertices; geometric plane distance",
                FullSupport = new { LeftCount = leftCount, RightCount = rightCount, LeftMinimumHeight = leftMinimum, RightMinimumHeight = rightMinimum } };
        }
        return new { Left = left, Right = right, LeftMinimumHeight = left.Min(p => p.Y), RightMinimumHeight = right.Min(p => p.Y),
            LeftCount, RightCount, BindErrorMeters, Scope = "Fixed bind-pose sole vertices; geometric plane distance, not support force" };
    }

    // Diagnostic export for offline skinning of captured graph stages. Bone-local
    // data uses the same native axes/cm convention as the graph pose capture.
    public object CaptureSupportBindings()
    {
        RequireMainThread();
        var points = new List<object>();
        foreach (var point in _supportPoints)
        {
            var surface = point.Surface;
            var influences = new List<object>();
            for (var w = 0; w < surface.Stride; w++)
            {
                var weight = surface.Weights[point.Vertex * surface.Stride + w];
                if (weight == 0) continue;
                var bind = surface.Indices[point.Vertex * surface.Stride + w];
                var local = surface.Binds[bind] * surface.Vertices[point.Vertex];
                influences.Add(new { bone = surface.Skeleton.GetBoneName(surface.Bones[bind]).ToString(),
                    position = new[] { local.X * 100d, -local.Y * 100d, local.Z * 100d }, weight });
            }
            points.Add(new { id = points.Count, left = point.Left, influences });
        }
        return new { schemaVersion = 1, BindErrorMeters,
            scope = "All >=99% foot/toe weighted vertices; native bone-local axes and centimeters; geometric support only", points };
    }

    // Export data once on the main thread. All >=99% foot/toe-weighted vertices
    // participate in runtime support; the diagnostic's lower 4 mm subset stays
    // independently observable and does not determine a fitted foot height.
    public AlsFootSupportGeometry CreateSupportGeometry(ReadOnlySpan<string> logicalNames)
    {
        RequireMainThread();
        var map = logicalNames.ToArray().Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var points = new List<AlsFootSupportPoint>(); var influences = new List<AlsFootSupportInfluence>();
        foreach (var point in _supportPoints)
        {
            var surface = point.Surface; var first = influences.Count;
            for (var w = 0; w < surface.Stride; w++)
            {
                var weight = surface.Weights[point.Vertex * surface.Stride + w];
                if (weight == 0) continue;
                var bind = surface.Indices[point.Vertex * surface.Stride + w];
                var name = surface.Skeleton.GetBoneName(surface.Bones[bind]).ToString();
                if (!map.TryGetValue(name, out var bone)) throw new InvalidOperationException("Missing support bone: " + name);
                var local = surface.Binds[bind] * surface.Vertices[point.Vertex];
                influences.Add(new(bone, new(local.X * 100d, -local.Y * 100d, local.Z * 100d), weight));
            }
            points.Add(new(first, influences.Count - first, point.Left));
        }
        var geometry = new AlsFootSupportGeometry(logicalNames.Length, points.ToArray(), influences.ToArray());
        GD.Print($"ALS_FOOT_SUPPORT_GEOMETRY left={geometry.LeftCount} right={geometry.RightCount} bones={logicalNames.Length} bind_error_m={BindErrorMeters:R}");
        return geometry;
    }

    private static void RequireMainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
            throw new InvalidOperationException("Imported mesh access requires the main thread.");
    }

    private static Vector3 Skin(Surface surface, int vertex, Transform3D[] poses)
    {
        var position = Vector3.Zero;
        for (var w = 0; w < surface.Stride; w++)
        {
            var i = vertex * surface.Stride + w; var weight = surface.Weights[i];
            if (weight != 0) position += (poses[surface.Bones[surface.Indices[i]]] * surface.Binds[surface.Indices[i]] * surface.Vertices[vertex]) * weight;
        }
        return position;
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren()) foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
