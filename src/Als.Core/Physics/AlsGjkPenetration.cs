using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsGjkContact(double Penetration, AlsDoubleVector NormalA, AlsDoubleVector NormalB,
    AlsDoubleVector PointA, AlsDoubleVector PointB, int VertexA, int VertexB, double MaxSupportDelta,
    AlsEpaStatus? EpaStatus, bool GjkIterationLimit);

public static class AlsGjkPenetration
{
    // Both output points/normals are in their respective shape's frame.
    // The owner must still stage this cache across subsequent manifold work.
    public static AlsGjkContact Run<TA, TB>(in TA shapeA, in TB shapeB, in AlsPrecisePose bToA,
        AlsGjkCache cache, AlsEpaWorkspace workspace, double gjkEpsilon, double epaEpsilon, bool warmStart = true)
        where TA : struct, IAlsGjkShape where TB : struct, IAlsGjkShape
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(workspace);
        if (!double.IsFinite(epaEpsilon) || epaEpsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epaEpsilon));
        var staged = workspace.StagedCache; staged.CopyFrom(cache);
        var gjk = AlsGjkSearch.Run(shapeA,shapeB,bToA,staged,gjkEpsilon,warmStart);
        var normal = gjk.NormalA; var normalB = gjk.NormalB;
        var pointA = gjk.PointA; var pointB = gjk.PointB;
        var penetration = shapeA.Margin + (double)shapeB.Margin - gjk.CoreDistance;
        var va = gjk.VertexA; var vb = gjk.VertexB; AlsEpaStatus? status = null;
        if (gjk.NeedsEpa)
        {
            var epa = AlsEpa.Run(shapeA,shapeB,bToA,staged,workspace,epaEpsilon); status = epa.Status;
            if (epa.VertexA >= 0) va = epa.VertexA;
            if (epa.VertexB >= 0) vb = epa.VertexB;
            if (epa.Status is AlsEpaStatus.Ok or AlsEpaStatus.MaxIterations)
            {
                normal = epa.Normal; normalB = normal.Rotate(bToA.Rotation.Conjugate());
                penetration = epa.Penetration + shapeA.Margin + shapeB.Margin;
                pointA = epa.PointA + normal * shapeA.Margin;
                pointB = (epa.PointBInA - normal * shapeB.Margin - bToA.Position).Rotate(bToA.Rotation.Conjugate());
            }
            else if (epa.Status == AlsEpaStatus.BadInitialSimplex)
            {
                normal = epa.Normal; normalB = normal.Rotate(bToA.Rotation.Conjugate());
                penetration = shapeA.Margin + (double)shapeB.Margin + epa.Penetration;
                // Reconstruct from raw cache; removing the old margin from an
                // already rounded point introduces an unnecessary subtraction.
                pointA = AlsDoubleVector.Zero; pointB = AlsDoubleVector.Zero;
                for (var i = 0; i < staged.Count; i++)
                { pointA += staged.WitnessA[i] * staged.Weights[i]; pointB += staged.WitnessB[i] * staged.Weights[i]; }
                pointA += normal * shapeA.Margin; pointB -= normalB * shapeB.Margin;
            }
            // Degenerate keeps the last GJK normal, distance and witnesses.
        }
        if (!double.IsFinite(penetration) || !normal.IsFinite || !normalB.IsFinite || !pointA.IsFinite || !pointB.IsFinite)
            throw new InvalidOperationException("Nonfinite GJK/EPA contact; cache was not committed.");
        cache.CopyFrom(staged);
        return new(penetration,normal,normalB,pointA,pointB,va,vb,gjk.MaxSupportDelta,status,gjk.HitIterationLimit);
    }
}
