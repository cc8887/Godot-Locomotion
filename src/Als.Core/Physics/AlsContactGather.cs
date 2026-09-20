using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Narrow-phase coordinates and persistent state, all in the owning shape's
// rigid local space. Normal belongs to shape 1 and points toward shape 0.
public readonly record struct AlsContactGeometry(Vector3 Point0, Vector3 Point1, Vector3 Normal1,
    Vector3 Anchor0, Vector3 Anchor1, bool HasAnchor, bool InitialContact,
    float InitialPhi = 0, float TargetPhi = 0,
    bool DisablePosition = false, bool DisableVelocity = false, bool DisableFriction = false);
public readonly record struct AlsContactGatherBody(AlsPrecisePose ShapeWorld, AlsDoubleVector CenterOfMass,
    float InverseMass, AlsProjectionVelocity Velocity);
public readonly record struct AlsContactGatherSettings(float Dt, float Restitution, float RestitutionThreshold,
    float MaxPushOutVelocity = 0, float MaxDepenetrationVelocity = -1,
    bool PerContactInitialPhi = true, bool InitialManifold = true, float MinInitialPhi = 0);
public readonly record struct AlsGatheredContact(AlsContactPointInput Point, float InitialPhi);

// Native Gauss-Seidel geometry -> solver input boundary. Double world positions,
// float contact offsets/velocity math. Default 2-D friction and post-integration
// restitution; split impulses and the experimental pre-integrate CVar are absent.
// The caller persists InitialPhi only when it commits the complete physics step.
public static class AlsContactGather
{
    public static AlsGatheredContact Gather(in AlsContactGeometry geometry, in AlsContactGatherBody body0,
        in AlsContactGatherBody body1, in AlsContactGatherSettings settings)
    {
        Validate(body0); Validate(body1); Validate(geometry, settings);
        var sumMass = (double)body0.InverseMass + body1.InverseMass;
        if (sumMass <= 0) throw new ArgumentException("Contact gathering requires a dynamic participant.");
        var p0 = Transform(body0.ShapeWorld, geometry.Point0); var p1 = Transform(body1.ShapeWorld, geometry.Point1);
        var common = (p0 * body0.InverseMass + p1 * body1.InverseMass) * (1 / sumMass);
        var arm0 = (common - body0.CenterOfMass).ToSingle(); var arm1 = (common - body1.CenterOfMass).ToSingle();
        var normal = new AlsDoubleVector(geometry.Normal1).Rotate(body1.ShapeWorld.Rotation).ToSingle();
        var u = Vector3.Cross(Vector3.UnitY, normal);
        if (!Normalize(ref u)) { u = Vector3.Cross(Vector3.UnitX, normal); u *= 1 / MathF.Sqrt(u.LengthSquared()); }
        var contactVelocity = Vector3.Zero; var normalVelocity = 0f;
        if (!geometry.HasAnchor || settings.Restitution > 0 || geometry.InitialContact)
        {
            var v0 = body0.Velocity.Linear + Vector3.Cross(body0.Velocity.Angular, arm0);
            var v1 = body1.Velocity.Linear + Vector3.Cross(body1.Velocity.Angular, arm1);
            contactVelocity = v0 - v1; normalVelocity = Vector3.Dot(contactVelocity, normal);
            var sliding = contactVelocity - normalVelocity * normal;
            if (Normalize(ref sliding)) u = sliding;
        }
        var v = Vector3.Cross(normal, u);
        var frictionDelta = geometry.HasAnchor
            ? (Transform(body0.ShapeWorld, geometry.Anchor0) - Transform(body1.ShapeWorld, geometry.Anchor1)).ToSingle()
            : contactVelocity * settings.Dt;
        var delta = (p0 - p1).ToSingle(); var errorN = Vector3.Dot(delta, normal);
        var errorU = Vector3.Dot(delta + frictionDelta, u); var errorV = Vector3.Dot(delta + frictionDelta, v);
        var target = settings.Restitution > 0 && normalVelocity < -(settings.RestitutionThreshold * settings.Dt)
            ? -settings.Restitution * normalVelocity : 0;
        var initialPhi = geometry.InitialPhi;
        if (settings.MaxDepenetrationVelocity >= 0)
        {
            initialPhi = 0;
            if (geometry.InitialContact)
            {
                initialPhi = MathF.Min(errorN, 0) - MathF.Min(normalVelocity * settings.Dt, 0);
                if (settings.PerContactInitialPhi && !settings.InitialManifold)
                    initialPhi = MathF.Max(initialPhi, settings.MinInitialPhi);
            }
            else
            {
                var previous = settings.PerContactInitialPhi ? geometry.InitialPhi : settings.MinInitialPhi;
                if (previous < 0) initialPhi = MathF.Max(errorN, previous);
            }
            initialPhi = MathF.Min(initialPhi + settings.MaxDepenetrationVelocity * settings.Dt, 0);
            errorN -= initialPhi;
        }
        var maxPushOut = settings.MaxPushOutVelocity * settings.Dt;
        if (maxPushOut > 0 && errorN < -maxPushOut) errorN = -maxPushOut;
        errorN -= geometry.TargetPhi;
        var point = new AlsContactPointInput(arm0, arm1, normal, u, v, new(errorN, errorU, errorV), target,
            geometry.DisablePosition, geometry.DisableVelocity, geometry.DisableFriction);
        if (!Finite(arm0) || !Finite(arm1) || !Finite(normal) || !Finite(u) || !Finite(v) ||
            !Finite(point.Error) || !float.IsFinite(target) || !float.IsFinite(initialPhi))
            throw new ArgumentException("Contact gathering overflowed solver precision.");
        return new(point, initialPhi);
    }

    private static AlsDoubleVector Transform(in AlsPrecisePose pose, Vector3 point) =>
        new AlsDoubleVector(point).Rotate(pose.Rotation) + pose.Position;
    private static bool Normalize(ref Vector3 value)
    {
        var squared = value.LengthSquared();
        if (squared <= 1e-4f) return false; // Native tolerance is on SQUARED length.
        value *= 1 / MathF.Sqrt(squared); return true;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static void Validate(in AlsContactGatherBody body)
    {
        body.ShapeWorld.Validate(1e-5);
        if (body.ShapeWorld.Scale != AlsDoubleVector.One || !body.CenterOfMass.IsFinite ||
            !float.IsFinite(body.InverseMass) || body.InverseMass < 0 || !Finite(body.Velocity.Linear) || !Finite(body.Velocity.Angular))
            throw new ArgumentException("Contact bodies require rigid poses and finite mass/velocity.");
    }
    private static void Validate(in AlsContactGeometry geometry, in AlsContactGatherSettings settings)
    {
        if (!Finite(geometry.Point0) || !Finite(geometry.Point1) || !Finite(geometry.Normal1) ||
            !Finite(geometry.Anchor0) || !Finite(geometry.Anchor1) || MathF.Abs(geometry.Normal1.LengthSquared() - 1) > 1e-5f ||
            !float.IsFinite(geometry.InitialPhi) || !float.IsFinite(geometry.TargetPhi))
            throw new ArgumentException("Invalid contact geometry.");
        if (!float.IsFinite(settings.Dt) || settings.Dt <= 0 || !float.IsFinite(1 / settings.Dt) ||
            !float.IsFinite(settings.Restitution) || settings.Restitution is < 0 or > 1 ||
            !float.IsFinite(settings.RestitutionThreshold) || settings.RestitutionThreshold < 0 ||
            !float.IsFinite(settings.MaxPushOutVelocity) || settings.MaxPushOutVelocity < 0 ||
            !float.IsFinite(settings.MaxDepenetrationVelocity) || !float.IsFinite(settings.MinInitialPhi))
            throw new ArgumentException("Invalid contact gather settings.");
    }
}
