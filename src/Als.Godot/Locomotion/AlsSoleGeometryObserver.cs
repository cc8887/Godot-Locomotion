using Godot;
using NVector3 = System.Numerics.Vector3;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Contracts;

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
    private readonly List<Surface> _surfaces = [];
    public int LeftCount { get; }
    public int RightCount { get; }
    public float BindErrorMeters { get; }

    // Model adaptation uses the same complete, bind-validated support vertices
    // as contact evaluation. The ankle's projection onto its reference sole
    // plane supplies a rotation-covariant contact marker, in bone-local meters.
    internal Vector3 ReferenceContactMarker(Skeleton3D skeleton, bool left)
    {
        RequireMainThread();
        var rest = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneGlobalRest).ToArray();
        var points = _supportPoints.Where(p => p.Left == left && p.Surface.Skeleton == skeleton).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("Reference contact marker has no support geometry.");
        var minimum = points.Min(p => Skin(p.Surface, p.Vertex, rest).Z);
        var ankle = rest[skeleton.FindBone(left ? "foot_l" : "foot_r")];
        return ankle.AffineInverse() * new Vector3(ankle.Origin.X, ankle.Origin.Y, minimum);
    }

    // Measure the already-published skeleton, independently of the Rig's
    // desired contacts. The complete support mesh determines penetration.
    internal (AlsFootQualitySample Left, AlsFootQualitySample Right) CaptureQuality(
        AlsFrameIdentity identity, long surfaceId, Transform3D surfaceToWorld, float surfaceTop,
        bool leftContact, bool rightContact)
    {
        RequireMainThread();
        foreach(var (skeleton,poses) in _poses)
            for(var bone=0;bone<poses.Length;bone++)poses[bone]=skeleton.GetBoneGlobalPose(bone);
        var inverse=surfaceToWorld.AffineInverse();
        var left=Vector3.Zero;var right=Vector3.Zero;float leftMinimum=float.PositiveInfinity,rightMinimum=float.PositiveInfinity;
        foreach(var point in _points)
        {
            var local=inverse*(point.Surface.Skeleton.GlobalTransform*Skin(point.Surface,point.Vertex,_poses[point.Surface.Skeleton]));
            if(point.Left)left+=local;else right+=local;
        }
        foreach(var point in _supportPoints)
        {
            var local=inverse*(point.Surface.Skeleton.GlobalTransform*Skin(point.Surface,point.Vertex,_poses[point.Surface.Skeleton]));
            if(point.Left)leftMinimum=MathF.Min(leftMinimum,local.Y-surfaceTop);else rightMinimum=MathF.Min(rightMinimum,local.Y-surfaceTop);
        }
        left/=LeftCount;right/=RightCount;
        var leftSkeleton=_points.First(p=>p.Left).Surface.Skeleton;var rightSkeleton=_points.First(p=>!p.Left).Surface.Skeleton;
        return (new(identity,surfaceId,leftContact,new(left.X*100d,left.Y*100d,left.Z*100d),leftMinimum*100d,Knee(leftSkeleton,"l")),
            new(identity,surfaceId,rightContact,new(right.X*100d,right.Y*100d,right.Z*100d),rightMinimum*100d,Knee(rightSkeleton,"r")));
        double Knee(Skeleton3D skeleton,string side)
        {
            var poses=_poses[skeleton];
            var upper=poses[skeleton.FindBone("calf_"+side)].Origin-poses[skeleton.FindBone("thigh_"+side)].Origin;
            var lower=poses[skeleton.FindBone("foot_"+side)].Origin-poses[skeleton.FindBone("calf_"+side)].Origin;
            return Math.Acos(Math.Clamp(upper.Normalized().Dot(lower.Normalized()),-1d,1d));
        }
    }

    public AlsSoleGeometryObserver(Node root, bool referenceOnly = false)
    {
        RequireMainThread();
        var candidates = new List<Point>();
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is not { } resource) continue;
            // Model installation happens before the character joins the scene
            // tree. Its immutable Skin is already available; runtime observation
            // continues to require the registered SkinReference.
            var skin = referenceOnly ? mesh.Skin : mesh.GetSkinReference()?.GetSkin();
            if (skin is null || skin.GetBindCount() == 0) continue;
            var skeleton = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
            if (referenceOnly && mesh.GetParent() != skeleton)
                throw new InvalidOperationException("Reference marker extraction requires a directly bound mesh.");
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
                _surfaces.Add(surface);
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
                    var worldRest = referenceOnly ? Skin(surface, v, rest) : skeleton.GlobalTransform * Skin(surface, v, rest);
                    var referenceVertex = (referenceOnly ? mesh.Transform : mesh.GlobalTransform) * vertices[v];
                    BindErrorMeters = MathF.Max(BindErrorMeters, worldRest.DistanceTo(referenceVertex));
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
    public AlsFootSupportGeometry CreateSupportGeometry(ReadOnlySpan<string> logicalNames,
        ReadOnlySpan<int> parents = default, ReadOnlySpan<AlsPrecisePose> reference = default)
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
        var model = _surfaces.Select(s => s.Skeleton).Distinct().SingleOrDefault(s =>
            s.HasMeta("als_pose_binding") && s.GetMeta("als_pose_binding").AsString() == "preserve-proportions-v1");
        AlsSkeletonPoseRetargetModel? retarget = null;
        if (model is not null)
        {
            var nativeReference = reference.ToArray();
            for (var bone = 0; bone < nativeReference.Length; bone++)
                nativeReference[bone] = AlsCharacterModelBinding.NativePose(nativeReference[bone]);
            retarget = AlsCharacterModelBinding.CreateRetarget(logicalNames, parents, nativeReference, model);
        }
        var geometry = new AlsFootSupportGeometry(logicalNames.Length, points.ToArray(), influences.ToArray(), retarget);
        GD.Print($"ALS_FOOT_SUPPORT_GEOMETRY left={geometry.LeftCount} right={geometry.RightCount} bones={logicalNames.Length} bind_error_m={BindErrorMeters:R}");
        return geometry;
    }

    internal AlsRetargetBodyGeometry CreateBodyGeometry(ReadOnlySpan<string> logicalNames,
        ReadOnlySpan<int> parents, int contactRoot, ReadOnlySpan<AlsRetargetLimb> ikLimbs)
    {
        RequireMainThread();
        var map = logicalNames.ToArray().Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var allowed = new bool[parents.Length];
        for (var bone = 0; bone < parents.Length; bone++)
        {
            allowed[bone] = bone == contactRoot || parents[bone] >= 0 && allowed[parents[bone]];
            foreach (var limb in ikLimbs) if (bone == limb.Upper) allowed[bone] = false;
        }
        var points = new List<AlsRetargetBodyPoint>();
        var influences = new List<AlsRetargetBodyInfluence>();
        foreach (var surface in _surfaces)
        {
            for (var vertex = 0; vertex < surface.Vertices.Length; vertex++)
            {
                var first = influences.Count; var rigid = true;
                for (var index = 0; index < surface.Stride; index++)
                {
                    var weight = surface.Weights[vertex * surface.Stride + index];
                    if (weight == 0) continue;
                    var bind = surface.Indices[vertex * surface.Stride + index];
                    var bone = map[surface.Skeleton.GetBoneName(surface.Bones[bind]).ToString()];
                    if (!allowed[bone]) { rigid = false; break; }
                    var local = surface.Binds[bind] * surface.Vertices[vertex];
                    influences.Add(new(bone, new(local.X * 100d, -local.Y * 100d, local.Z * 100d), weight));
                }
                if (rigid && influences.Count > first) points.Add(new(first, influences.Count - first));
                else influences.RemoveRange(first, influences.Count - first);
            }
        }
        GD.Print($"ALS_RETARGET_BODY_GEOMETRY points={points.Count} influences={influences.Count} bind_error_m={BindErrorMeters:R}");
        return new(logicalNames.Length, points.ToArray(), influences.ToArray());
    }

    private static void RequireMainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
            throw new InvalidOperationException("Imported mesh access requires the main thread.");
    }

    internal readonly record struct VolumePenetration(int Vertices, int Inside, float MaximumDepthMeters,
        Vector3 DeepestWorld, string DominantBone);

    // Observe the complete published skin against a known fixture volume;
    // reuse the same verified bind mapping and skinning as sole observation.
    internal VolumePenetration CaptureVolumePenetration(Transform3D volumeToWorld, Aabb volume)
    {
        RequireMainThread();
        foreach (var (skeleton, poses) in _poses)
            for (var bone=0;bone<poses.Length;bone++) poses[bone]=skeleton.GetBoneGlobalPose(bone);
        var inverse=volumeToWorld.AffineInverse();
        int vertices=0,inside=0;float depth=0;var deepest=Vector3.Zero;var dominant="";
        foreach (var surface in _surfaces)
        for (var vertex=0;vertex<surface.Vertices.Length;vertex++)
        {
            vertices++;
            var world=surface.Skeleton.GlobalTransform*Skin(surface,vertex,_poses[surface.Skeleton]);
            var local=inverse*world;
            if (!volume.HasPoint(local)) continue;
            inside++;
            var min=local-volume.Position;var max=volume.End-local;
            var distance=MathF.Min(MathF.Min(min.X,max.X),MathF.Min(MathF.Min(min.Y,max.Y),MathF.Min(min.Z,max.Z)));
            if (distance<=depth) continue;
            depth=distance;deepest=world;float weight=0;
            for (var w=0;w<surface.Stride;w++)
            {
                var i=vertex*surface.Stride+w;
                if (surface.Weights[i]<=weight) continue;
                weight=surface.Weights[i];dominant=surface.Skeleton.GetBoneName(surface.Bones[surface.Indices[i]]).ToString();
            }
        }
        return new(vertices,inside,depth,deepest,dominant);
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
