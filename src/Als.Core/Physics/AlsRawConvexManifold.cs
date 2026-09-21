using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public enum AlsConvexContactFeature { None, EdgeEdge, PlaneVertex, VertexPlane }
public readonly record struct AlsConvexManifoldResult(int Count, AlsConvexContactFeature Feature, int Plane0, int Plane1);
public sealed class AlsConvexManifoldWorkspace
{
    internal AlsGjkCache Staged { get; } = new();
    internal AlsEpaWorkspace Epa { get; } = new();
}

// Initial one-shot manifold for two unwrapped zero-margin cooked FConvexes.
// Persistent-manifold injection/restoration belongs to the pair owner.
public static class AlsRawConvexManifold
{
    public static AlsConvexManifoldResult Build(AlsConvexTopology hull0, AlsConvexTopology hull1,
        in AlsPrecisePose shape1To0, AlsGjkCache cache, AlsConvexManifoldWorkspace work,
        Span<AlsDetectedContact> destination, double cullDistance, double gjkEpsilon, double epaEpsilon,
        float minimumFaceSearchDistance, float planeNormalEpsilon, bool forceEdgeZeroCull = false, bool warmStart = true)
        => AlsPolygonManifold.Build(new AlsConvexPolygonShape(hull0),new AlsConvexPolygonShape(hull1),
            shape1To0,cache,work,destination,cullDistance,gjkEpsilon,epaEpsilon,minimumFaceSearchDistance,
            planeNormalEpsilon,forceEdgeZeroCull,warmStart);
}
