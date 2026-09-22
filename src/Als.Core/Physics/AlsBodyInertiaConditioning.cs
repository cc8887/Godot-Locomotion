using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Keep native cm / kg units through this float-precision Chaos calculation.
public readonly record struct AlsBodyInertiaSettings(bool Enabled,float MaxDistanceCm,float MaxRotationRatio,
    float MaxInvInertiaComponentRatio,float InverseMassTolerance,float InverseInertiaTolerance,float ExtentToleranceCm);

public static class AlsBodyInertiaConditioning
{
    public static Vector3 Calculate(float inverseMass,Vector3 inverseInertia,Vector3 extents,AlsBodyInertiaSettings settings)
    {
        if(!float.IsFinite(inverseMass)||inverseMass<0||!Finite(inverseInertia)||!Finite(extents)||
            Min(inverseInertia)<0||Min(extents)<0||!float.IsFinite(settings.MaxDistanceCm)||settings.MaxDistanceCm<0||
            !float.IsFinite(settings.MaxRotationRatio)||settings.MaxRotationRatio<=0||
            !float.IsFinite(settings.MaxInvInertiaComponentRatio)||settings.MaxInvInertiaComponentRatio<0||
            !ValidTolerance(settings.InverseMassTolerance)||!ValidTolerance(settings.InverseInertiaTolerance)||!ValidTolerance(settings.ExtentToleranceCm))
            throw new ArgumentOutOfRangeException(nameof(inverseMass));
        if(!settings.Enabled||inverseMass<=settings.InverseMassTolerance||Min(inverseInertia)<=settings.InverseInertiaTolerance||Min(extents)<=settings.ExtentToleranceCm)
            return Vector3.One;
        // FVector / scalar computes one float reciprocal, then multiplies
        // every component. Vector3 / scalar performs direct SIMD division.
        var sq=extents*extents;var ratio=inverseInertia*(1f/inverseMass);
        var rotationRatio=new Vector3(MathF.Max(sq.Y*ratio.X,sq.Z*ratio.X),MathF.Max(sq.X*ratio.Y,sq.Z*ratio.Y),MathF.Max(sq.X*ratio.Z,sq.Y*ratio.Z));
        var allowed=settings.MaxRotationRatio;
        if(settings.MaxDistanceCm>0)allowed*=MathF.Min(Max(extents)/settings.MaxDistanceCm,1);
        var scale=Vector3.Min(new Vector3(allowed)/rotationRatio,Vector3.One);
        if(settings.MaxInvInertiaComponentRatio>1)
        {
            var scaled=inverseInertia*scale;var ceiling=settings.MaxInvInertiaComponentRatio*Min(scaled);
            // Preserve native assignment semantics, including this optional branch.
            if(scaled.X>ceiling)scale.X=ceiling/scaled.X;
            if(scaled.Y>ceiling)scale.Y=ceiling/scaled.Y;
            if(scaled.Z>ceiling)scale.Z=ceiling/scaled.Z;
            scale=Vector3.Min(scale,Vector3.One);
        }
        if(!Finite(scale)||Min(scale)<=0)throw new ArgumentOutOfRangeException(nameof(extents),"Inertia conditioning overflowed.");
        return scale;
    }

    // Native first transforms the actor AABB into COM space, then takes half
    // its size. COM translation cancels; using distance from COM to each bound
    // instead would produce a different result for offset shapes.
    public static AlsDoubleVector CollisionExtents(AlsDoubleVector min,AlsDoubleVector max,AlsQuaternion massRotation)
    {
        if(!min.IsFinite||!max.IsFinite||(max-min).IsNegative||!double.IsFinite(massRotation.LengthSquared)||System.Math.Abs(massRotation.LengthSquared-1)>.00001)
            throw new ArgumentException("Invalid native bounds or mass rotation.");
        var half=(max-min)*.5;var inverse=massRotation.Conjugate();
        return Abs(new AlsDoubleVector(half.X,0,0).Rotate(inverse))+Abs(new AlsDoubleVector(0,half.Y,0).Rotate(inverse))+Abs(new AlsDoubleVector(0,0,half.Z).Rotate(inverse));
    }
    public static AlsDoubleVector IncludeConnector(AlsDoubleVector extents,AlsDoubleVector actorConnector,AlsPrecisePose massLocal)
    {
        massLocal.Validate();
        if(!extents.IsFinite||extents.IsNegative||!actorConnector.IsFinite||massLocal.Scale!=AlsDoubleVector.One)
            throw new ArgumentException("Invalid native connector or COM frame.");
        var arm=Abs((actorConnector-massLocal.Position).Rotate(massLocal.Rotation.Conjugate()));
        return new(System.Math.Max(extents.X,arm.X),System.Math.Max(extents.Y,arm.Y),System.Math.Max(extents.Z,arm.Z));
    }
    private static AlsDoubleVector Abs(AlsDoubleVector v)=>new(System.Math.Abs(v.X),System.Math.Abs(v.Y),System.Math.Abs(v.Z));
    private static bool ValidTolerance(float v)=>float.IsFinite(v)&&v>=0;
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static float Min(Vector3 v)=>MathF.Min(v.X,MathF.Min(v.Y,v.Z));
    private static float Max(Vector3 v)=>MathF.Max(v.X,MathF.Max(v.Y,v.Z));
}
