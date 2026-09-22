using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Polygon-only one-shot geometry. Quadratic shapes have a different reference
// face policy. Margin is the resolved pair margin, not a shape default.
public interface IAlsPolygonShape : IAlsGjkShape
{
    int Winding { get; }
    int SelectPlane(AlsDoubleVector point, AlsDoubleVector direction, int vertex, float minimumDistance);
    void Plane(int plane, out AlsDoubleVector normal, out AlsDoubleVector point);
    int FaceCount(int plane);
    AlsDoubleVector FaceVertex(int plane, int vertex);
    AlsDoubleVector ClosestEdge(int plane, AlsDoubleVector point);
}

public static class AlsPolygonManifold
{
    public static AlsConvexManifoldResult Build<TA, TB>(in TA a, in TB b,
        in AlsPrecisePose shape1To0, AlsGjkCache cache, AlsConvexManifoldWorkspace work,
        Span<AlsDetectedContact> destination, double cullDistance, double gjkEpsilon, double epaEpsilon,
        float minimumFaceSearchDistance, float planeNormalEpsilon, bool forceEdgeZeroCull = false, bool warmStart = true,
        AlsPrecisePose? shape0To1 = null)
        where TA : struct, IAlsPolygonShape where TB : struct, IAlsPolygonShape
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(work);
        a.Validate(); b.Validate();
        if (shape0To1 is { } reverse)
        {
            reverse.Validate(1e-5);
            if (reverse.Scale != AlsDoubleVector.One) throw new ArgumentException("Polygon transforms must be rigid.");
        }
        if (destination.Length < 4 || !double.IsFinite(cullDistance) || cullDistance < 0 ||
            !float.IsFinite(minimumFaceSearchDistance) || minimumFaceSearchDistance < 0 ||
            !float.IsFinite(planeNormalEpsilon) || planeNormalEpsilon < 0)
            throw new ArgumentException("Invalid polygon manifold destination or settings.");
        work.Staged.CopyFrom(cache);
        var contact = AlsGjkPenetration.Run(a,b,shape1To0,work.Staged,work.Epa,gjkEpsilon,epaEpsilon,warmStart);
        var phi = -contact.Penetration;
        if (phi > cullDistance + contact.MaxSupportDelta)
        { cache.CopyFrom(work.Staged); return new(0,AlsConvexContactFeature.None,-1,-1); }
        var normal1 = contact.NormalB * -1; var separation0 = normal1.Rotate(shape1To0.Rotation);
        var plane0 = a.SelectPlane(contact.PointA,separation0,contact.VertexA,minimumFaceSearchDistance);
        var plane1 = b.SelectPlane(contact.PointB,normal1 * -1,contact.VertexB,minimumFaceSearchDistance);
        a.Plane(plane0,out var n0,out var x0); b.Plane(plane1,out var n1,out var x1);
        var dot0 = System.Math.Abs(AlsDoubleVector.Dot(separation0 * -1,n0));
        var dot1 = System.Math.Abs(AlsDoubleVector.Dot(normal1,n1));
        var reference0 = !(dot1 + (double).002f > dot0);
        var isPlane = System.Math.Abs(dot0-1) <= planeNormalEpsilon || System.Math.Abs(dot1-1) <= planeNormalEpsilon;
        if (!isPlane)
        {
            if (forceEdgeZeroCull && phi > 0)
            { cache.CopyFrom(work.Staged); return new(0,AlsConvexContactFeature.None,plane0,plane1); }
            var point0 = contact.PointA; var point1 = contact.PointB;
            if (a.Margin > 0 || b.Margin > 0)
            {
                // Project both sides, including the zero-margin convex. Keep
                // the second edge point and project the first along the normal.
                var edge0 = a.ClosestEdge(plane0,point0); var edge1 = b.ClosestEdge(plane1,point1);
                var edge0In1 = (edge0-shape1To0.Position).Rotate(shape1To0.Rotation.Conjugate());
                var edgePhi = AlsDoubleVector.Dot(edge0In1-edge1,normal1);
                if (edgePhi > cullDistance)
                { cache.CopyFrom(work.Staged); return new(0,AlsConvexContactFeature.None,plane0,plane1); }
                point0 = (edge1+normal1*edgePhi).Rotate(shape1To0.Rotation)+shape1To0.Position;
                point1 = edge1;
                phi = edgePhi;
            }
            var result = new AlsDetectedContact(point0.ToSingle(),point1.ToSingle(),normal1.ToSingle()) {NativePhi=(float)phi};
            cache.CopyFrom(work.Staged); destination[0] = result;
            return new(1,AlsConvexContactFeature.EdgeEdge,plane0,plane1);
        }
        var inverse = shape1To0.Rotation.Conjugate();
        // Native computes the reverse relative transform from the two WORLD
        // poses. Inverting the forward result is not equivalent after particle
        // quaternion float storage, especially far from the shape origin.
        // Relative-only callers model shape 0 at identity; world owners pass both.
        var transform = reference0 ? shape1To0 : shape0To1 ??
            new AlsPrecisePose((shape1To0.Position * -1).Rotate(inverse),inverse,AlsDoubleVector.One);
        Span<AlsDetectedContact> staged = stackalloc AlsDetectedContact[4];
        var count = reference0
            ? Clip(a,b,plane0,plane1,transform,n0,x0,normal1,true,staged)
            : Clip(b,a,plane1,plane0,transform,n1,x1,normal1,false,staged);
        cache.CopyFrom(work.Staged); staged[..count].CopyTo(destination);
        return new(count,reference0?AlsConvexContactFeature.PlaneVertex:AlsConvexContactFeature.VertexPlane,plane0,plane1);
    }

    private static int Clip<TR, TI>(in TR referenceShape, in TI incidentShape, int referencePlane, int incidentPlane,
        in AlsPrecisePose transform, AlsDoubleVector normal, AlsDoubleVector point, AlsDoubleVector normal1,
        bool reference0, Span<AlsDetectedContact> output)
        where TR : struct, IAlsPolygonShape where TI : struct, IAlsPolygonShape
    {
        var referenceCount = referenceShape.FaceCount(referencePlane);
        var incidentCount = System.Math.Min(32,incidentShape.FaceCount(incidentPlane));
        var rented = System.Buffers.ArrayPool<AlsDoubleVector>.Shared.Rent(referenceCount+incidentCount);
        try
        {
            var reference = rented.AsSpan(0,referenceCount); var incident = rented.AsSpan(referenceCount,incidentCount);
            for (var i = 0; i < reference.Length; i++) reference[i] = referenceShape.FaceVertex(referencePlane,i);
            for (var i = 0; i < incident.Length; i++) incident[i] = incidentShape.FaceVertex(incidentPlane,i);
            return AlsConvexFaceManifold.Build(reference,incident,transform,normal,point,normal1,reference0,
                output,out _,referenceShape.Winding);
        }
        finally { System.Buffers.ArrayPool<AlsDoubleVector>.Shared.Return(rented); }
    }
}
