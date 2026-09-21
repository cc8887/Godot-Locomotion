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
    {
        ArgumentNullException.ThrowIfNull(hull0); ArgumentNullException.ThrowIfNull(hull1);
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(work);
        if (!hull0.HasNativeVertexPlanes || !hull1.HasNativeVertexPlanes || hull0.Margin != 0 || hull1.Margin != 0 ||
            destination.Length < 4 || !double.IsFinite(cullDistance) || cullDistance < 0 ||
            !float.IsFinite(minimumFaceSearchDistance) || minimumFaceSearchDistance < 0 ||
            !float.IsFinite(planeNormalEpsilon) || planeNormalEpsilon < 0)
            throw new ArgumentException("Raw convex manifold requires zero margins, native adjacency and valid settings.");
        work.Staged.CopyFrom(cache);
        var a = new AlsGjkConvexShape(hull0,AlsDoubleVector.One); var b = new AlsGjkConvexShape(hull1,AlsDoubleVector.One);
        var contact = AlsGjkPenetration.Run(a,b,shape1To0,work.Staged,work.Epa,gjkEpsilon,epaEpsilon,warmStart);
        var phi = -contact.Penetration;
        if (phi > cullDistance + contact.MaxSupportDelta)
        { cache.CopyFrom(work.Staged); return new(0,AlsConvexContactFeature.None,-1,-1); }
        var normal1 = contact.NormalB * -1; var separation0 = normal1.Rotate(shape1To0.Rotation);
        var plane0 = AlsConvexPlaneSelection.Unscaled(hull0,contact.PointA,separation0,0,contact.VertexA,minimumFaceSearchDistance);
        var plane1 = AlsConvexPlaneSelection.Unscaled(hull1,contact.PointB,normal1 * -1,0,contact.VertexB,minimumFaceSearchDistance);
        var p0 = hull0.PlaneAt(plane0); var p1 = hull1.PlaneAt(plane1);
        var n0 = new AlsDoubleVector(p0.Normal); var n1 = new AlsDoubleVector(p1.Normal);
        var dot0 = System.Math.Abs(AlsDoubleVector.Dot(separation0 * -1,n0));
        var dot1 = System.Math.Abs(AlsDoubleVector.Dot(normal1,n1));
        var reference0 = !(dot1 + (double).002f > dot0);
        var isPlane = System.Math.Abs(dot0-1) <= planeNormalEpsilon || System.Math.Abs(dot1-1) <= planeNormalEpsilon;
        if (!isPlane)
        {
            if (forceEdgeZeroCull && phi > 0) { cache.CopyFrom(work.Staged); return new(0,AlsConvexContactFeature.None,plane0,plane1); }
            var result = new AlsDetectedContact(contact.PointA.ToSingle(),contact.PointB.ToSingle(),normal1.ToSingle());
            cache.CopyFrom(work.Staged); destination[0] = result;
            return new(1,AlsConvexContactFeature.EdgeEdge,plane0,plane1);
        }
        // Keep the full reference face: clipping may visit more than 32 edges.
        // Incident vertices truncate exactly where the native clipper does.
        var referenceHull = reference0 ? hull0 : hull1; var incidentHull = reference0 ? hull1 : hull0;
        var referenceFace = referenceHull.FaceVertices(reference0 ? plane0 : plane1);
        var incidentFace = incidentHull.FaceVertices(reference0 ? plane1 : plane0);
        // Cooked face size is data-dependent; pooled storage avoids unbounded stack use.
        var rented = System.Buffers.ArrayPool<AlsDoubleVector>.Shared.Rent(referenceFace.Length + System.Math.Min(32,incidentFace.Length));
        try
        {
            var reference = rented.AsSpan(0,referenceFace.Length); var incident = rented.AsSpan(reference.Length,System.Math.Min(32,incidentFace.Length));
            for (var i = 0; i < reference.Length; i++) reference[i] = new(referenceHull.VertexAt(referenceFace[i]));
            for (var i = 0; i < incident.Length; i++) incident[i] = new(incidentHull.VertexAt(incidentFace[i]));
            var inverse = shape1To0.Rotation.Conjugate();
            var transform = reference0 ? shape1To0 : new AlsPrecisePose((shape1To0.Position * -1).Rotate(inverse),inverse,AlsDoubleVector.One);
            Span<AlsDetectedContact> staged = stackalloc AlsDetectedContact[4];
            var count = AlsConvexFaceManifold.Build(reference,incident,transform,reference0?n0:n1,
                new(reference0?p0.Point:p1.Point),normal1,reference0,staged,out _);
            cache.CopyFrom(work.Staged); staged[..count].CopyTo(destination);
            return new(count,reference0?AlsConvexContactFeature.PlaneVertex:AlsConvexContactFeature.VertexPlane,plane0,plane1);
        }
        finally { System.Buffers.ArrayPool<AlsDoubleVector>.Shared.Return(rented); }
    }
}
