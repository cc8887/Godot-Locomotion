using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Gathered world-space contact data. Normal points from body 1 toward body 0;
// negative normal error means penetration. Friction anchors / restitution targets
// are prepared by the manifold owner, not inferred from corrected body poses.
public readonly record struct AlsContactPointInput(Vector3 Arm0, Vector3 Arm1, Vector3 Normal,
    Vector3 TangentU, Vector3 TangentV, Vector3 Error, float TargetVelocity,
    bool DisablePosition = false, bool DisableVelocity = false, bool DisableFriction = false);
public readonly record struct AlsContactMaterial(float StaticFriction, float DynamicFriction,
    float VelocityFriction, float MinFrictionPushOut = 0, float Stiffness = 1,
    float PositionFrictionStiffness = .5f, float VelocityFrictionStiffness = 1);

// Chaos Gauss-Seidel hard-contact rows. No split impulse, soft shell,
// average-point restitution or one-dimensional friction. The owner
// solves ALL normals in a manifold before ALL position-friction rows; velocity
// rows visit points in order. Cache once per step; each instance owns its lambda.
public struct AlsCachedContactPoint
{
    private readonly record struct Axis(Vector3 Direction, Vector3 Cross0, Vector3 Cross1,
        Vector3 Response0, Vector3 Response1, float Mass);
    private Axis _n;
    private readonly Axis _u, _v;
    private readonly AlsContactPointInput _input;
    private readonly AlsContactMaterial _material;
    private float _inverseMass0, _inverseMass1, _scale0, _scale1;
    private readonly float _baseMass0, _baseMass1;
    private readonly AlsJointInertiaTensor _tensor0, _tensor1;
    public Vector3 PushOut { get; private set; }
    public Vector3 Impulse { get; private set; }
    public float StaticFrictionRatio { get; private set; }
    public readonly Vector3 ContactMass => new(_n.Mass, _u.Mass, _v.Mass);
    public readonly AlsContactPointInput Input => _input;

    public AlsCachedContactPoint(in AlsContactPointInput input, in AlsContactMaterial material,
        AlsQuaternion rotation0, AlsJointInverseMass mass0, AlsQuaternion rotation1, AlsJointInverseMass mass1)
        : this(input, material, rotation0, mass0, rotation1, mass1, false) { }

    // Native Gather subtracts the normal velocity in float before normalizing
    // the small remainder. Cancellation can leave N dot U above 1e-5 even for
    // valid input. Do not change those axes just to satisfy a stricter row API.
    internal AlsCachedContactPoint(in AlsContactPointInput input, in AlsContactMaterial material,
        AlsQuaternion rotation0, AlsJointInverseMass mass0, AlsQuaternion rotation1, AlsJointInverseMass mass1, bool fromNativeGather)
    {
        this = default;
        Validate(input, fromNativeGather); Validate(material); Validate(rotation0); Validate(rotation1);
        _ = AlsJointMassConditioning.Apply(mass0, mass1, 0, 0);
        _inverseMass0 = InverseMass(mass0.Mass); _inverseMass1 = InverseMass(mass1.Mass);
        _baseMass0 = _inverseMass0; _baseMass1 = _inverseMass1; _scale0 = _scale1 = 1;
        var tensor0 = AlsJointInertiaTensor.World(rotation0, _inverseMass0 > 0 ? mass0 : default);
        var tensor1 = AlsJointInertiaTensor.World(rotation1, _inverseMass1 > 0 ? mass1 : default);
        _tensor0 = tensor0; _tensor1 = tensor1;
        _n = MakeAxis(input.Normal, input.Arm0, input.Arm1, tensor0, tensor1, _inverseMass0, _inverseMass1);
        _u = MakeAxis(input.TangentU, input.Arm0, input.Arm1, tensor0, tensor1, _inverseMass0, _inverseMass1);
        _v = MakeAxis(input.TangentV, input.Arm0, input.Arm1, tensor0, tensor1, _inverseMass0, _inverseMass1);
        _input = input; _material = material;
    }

    public void SetShockPropagation(int level0, int level1, float scale)
    {
        if (!float.IsFinite(scale) || scale < 0 || scale > 1) throw new ArgumentOutOfRangeException(nameof(scale));
        var a = 1f; var b = 1f;
        if (_baseMass0 > 0 && _baseMass1 > 0 && level0 != level1)
        { if (level0 < level1) a = scale; else b = scale; }
        if (a == _scale0 && b == _scale1) return;
        _scale0 = a; _scale1 = b;
        _inverseMass0 = InverseMass(a * _baseMass0); _inverseMass1 = InverseMass(b * _baseMass1);
        // Native UpdateMassNormal updates per-contact linear inverse masses and
        // normal angular response/mass ONLY. Tangent mass/angular response and
        // accumulated pushout/impulse deliberately survive the switch.
        _n = MakeAxis(_input.Normal, _input.Arm0, _input.Arm1,
            ScaleTensor(_tensor0, a), ScaleTensor(_tensor1, b), _inverseMass0, _inverseMass1);
    }
    private static AlsJointInertiaTensor ScaleTensor(AlsJointInertiaTensor tensor, float scale) =>
        new(new(tensor.X.ToSingle() * scale), new(tensor.Y.ToSingle() * scale), new(tensor.Z.ToSingle() * scale));

    public void SolvePositionNormal(ref AlsProjectionDelta body0, ref AlsProjectionDelta body1)
    {
        if (_input.DisablePosition) return;
        var delta = 0f;
        if (_inverseMass0 > 0 && _inverseMass1 > 0)
        {
            delta += DotZxy(body0.Position - body1.Position, _n.Direction);
            delta += DotZxy(body0.Rotation, _n.Cross0); delta -= DotYxz(body1.Rotation, _n.Cross1);
        }
        else if (_inverseMass0 > 0)
        { delta += DotZxy(body0.Rotation, _n.Cross0); delta += DotZxy(body0.Position, _n.Direction); }
        else if (_inverseMass1 > 0)
        { delta -= DotYxz(body1.Rotation, _n.Cross1); delta -= DotYxz(body1.Position, _n.Direction); }
        var error = _input.Error.X + delta;
        if (error >= 0 && PushOut.X <= 1e-8f) return;
        var push = -_material.Stiffness * error * _n.Mass;
        if (PushOut.X + push <= 0) push = -PushOut.X;
        PushOut = new(PushOut.X + push, PushOut.Y, PushOut.Z);
        if (_inverseMass0 > 0) body0 = new(body0.Position + (_inverseMass0 * push) * _n.Direction, body0.Rotation + _n.Response0 * push);
        if (_inverseMass1 > 0) body1 = new(body1.Position + (_inverseMass1 * -push) * _n.Direction, body1.Rotation + _n.Response1 * -push);
    }

    public void SolvePositionFriction(ref AlsProjectionDelta body0, ref AlsProjectionDelta body1)
    {
        var stiffness = _material.Stiffness * _material.PositionFrictionStiffness;
        if (_input.DisablePosition || _input.DisableFriction || stiffness <= 0) return;
        var maximum = MathF.Max(_material.MinFrictionPushOut, PushOut.X);
        if (maximum <= 0 && PushOut.Y == 0 && PushOut.Z == 0) return;
        var du = -stiffness * _u.Mass * TangentError(_u, _input.Error.Y, body0, body1);
        var dv = -stiffness * _v.Mass * TangentError(_v, _input.Error.Z, body0, body1);
        var totalU = PushOut.Y + du; var totalV = PushOut.Z + dv; var ratio = 1f;
        if (maximum < 1e-4f) { du = -PushOut.Y; dv = -PushOut.Z; totalU = totalV = ratio = 0; }
        else
        {
            var staticMaximum = _material.StaticFriction * maximum;
            var sizeSquared = totalU * totalU + totalV * totalV;
            if (sizeSquared > staticMaximum * staticMaximum)
            {
                ratio = (_material.DynamicFriction * maximum) * (1 / MathF.Sqrt(sizeSquared));
                totalU = ratio * totalU; totalV = ratio * totalV;
                du = totalU - PushOut.Y; dv = totalV - PushOut.Z;
            }
        }
        PushOut = new(PushOut.X, totalU, totalV); StaticFrictionRatio = ratio;
        var push = du * _u.Direction + dv * _v.Direction;
        if (_inverseMass0 > 0) body0 = new(body0.Position + _inverseMass0 * push, body0.Rotation + (_u.Response0 * du + _v.Response0 * dv));
        if (_inverseMass1 > 0) body1 = new(body1.Position + -_inverseMass1 * push, body1.Rotation + (_u.Response1 * -du + _v.Response1 * -dv));
    }

    public void SolveVelocity(ref AlsProjectionVelocity body0, ref AlsProjectionVelocity body1, float dt, bool applyFriction)
    {
        if (!float.IsFinite(dt) || dt <= 0 || !float.IsFinite(1 / dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        if (_input.DisableVelocity) return;
        var normalActive = PushOut.X > 0 || _input.DisablePosition;
        var frictionActive = applyFriction && _material.VelocityFriction > 0 && !_input.DisableFriction && (normalActive || _input.Error.X < 0);
        if (!normalActive && !frictionActive) return;
        // Native /fp:fast shares the rounded reciprocal for these pushout to
        // impulse conversions. Dividing each numerator gives different clamps.
        var inverseDt = 1 / dt;
        var minimum = MathF.Min(0, PushOut.X * -inverseDt);
        var correction = normalActive ? NormalVelocityCorrection(body0, body1) : 0;
        var dn = (_material.Stiffness * _n.Mass) * correction;
        if (Impulse.X + dn < minimum) dn = minimum - Impulse.X;
        var nextN = Impulse.X + dn; var du = 0f; var dv = 0f;
        if (frictionActive)
        {
            var frictionStiffness = _material.Stiffness * _material.VelocityFrictionStiffness;
            du = frictionStiffness * _u.Mass * TangentVelocityCorrection(_u, body0, body1);
            dv = frictionStiffness * _v.Mass * TangentVelocityCorrection(_v, body0, body1);
            var totalN = nextN + PushOut.X * inverseDt;
            var maximum = _material.VelocityFriction * MathF.Max(_material.MinFrictionPushOut * inverseDt, totalN);
            var totalU = Impulse.Y + PushOut.Y * inverseDt; var totalV = Impulse.Z + PushOut.Z * inverseDt;
            var sizeSquared = (totalU + du) * (totalU + du) + (totalV + dv) * (totalV + dv);
            if (sizeSquared > maximum * maximum + 1e-8f)
            {
                var scale = maximum * (1 / MathF.Sqrt(sizeSquared));
                du = (totalU + du) * scale - totalU; dv = (totalV + dv) * scale - totalV;
                StaticFrictionRatio = scale;
            }
        }
        Impulse = new(nextN, Impulse.Y + du, Impulse.Z + dv);
        var impulse = dn * _n.Direction;
        if (frictionActive) impulse += du * _u.Direction + dv * _v.Direction;
        if (_inverseMass0 > 0) body0 = new(body0.Linear + _inverseMass0 * impulse,
            (body0.Angular + _n.Response0 * dn) + (_u.Response0 * du + _v.Response0 * dv));
        if (_inverseMass1 > 0) body1 = new(body1.Linear + -_inverseMass1 * impulse,
            (body1.Angular + _n.Response1 * -dn) + (_u.Response1 * -du + _v.Response1 * -dv));
    }

    private readonly float TangentError(in Axis axis, float error, in AlsProjectionDelta b0, in AlsProjectionDelta b1)
    {
        // Preserve the reference Win64 Chaos /fp:fast reduction, including where
        // the initial error joins the sum. A Dot followed by += changes rounding
        // on the first friction pass (the normal-only passes do not exercise it).
        if (_inverseMass0 > 0 && _inverseMass1 > 0)
        {
            var p = (b0.Position - b1.Position) * axis.Direction;
            var r0 = b0.Rotation * axis.Cross0; var r1 = b1.Rotation * axis.Cross1;
            error = (p.X + p.Y) + (p.Z + error);
            error += (r0.Z + r0.X) + r0.Y;
            error -= (r1.Y + r1.X) + r1.Z;
        }
        else if (_inverseMass0 > 0)
        {
            var p = b0.Position * axis.Direction; var r = b0.Rotation * axis.Cross0;
            error = ((p.X + p.Z) + p.Y) + ((r.X + r.Z) + (r.Y + error));
        }
        else if (_inverseMass1 > 0)
        {
            var p = b1.Position * axis.Direction; var r = b1.Rotation * axis.Cross1;
            error -= (p.Y + p.X) + p.Z; error -= (r.Y + r.X) + r.Z;
        }
        return error;
    }
    private readonly float NormalVelocityCorrection(in AlsProjectionVelocity b0, in AlsProjectionVelocity b1)
    {
        var r0 = b0.Angular * _n.Cross0;
        var relative = DotYxz(b0.Linear - b1.Linear, _n.Direction) - _input.TargetVelocity;
        relative += r0.Z; relative += r0.X + r0.Y;
        return DotYxz(b1.Angular, _n.Cross1) - relative;
    }
    private static float TangentVelocityCorrection(in Axis axis, in AlsProjectionVelocity b0, in AlsProjectionVelocity b1)
    {
        var p = (b0.Linear - b1.Linear) * axis.Direction;
        return DotYxz(b1.Angular, axis.Cross1) - (DotZxy(b0.Angular, axis.Cross0) + ((p.Y + p.Z) + p.X));
    }
    private static Axis MakeAxis(Vector3 axis, Vector3 arm0, Vector3 arm1, AlsJointInertiaTensor tensor0, AlsJointInertiaTensor tensor1, float mass0, float mass1)
    {
        var cross0 = Cross(arm0, axis); var cross1 = Cross(arm1, axis);
        var response0 = Transform(tensor0, cross0); var response1 = Transform(tensor1, cross1); var inverse = 0f;
        if (mass0 > 0) inverse += Vector3.Dot(cross0, response0) + mass0;
        if (mass1 > 0) inverse += Vector3.Dot(cross1, response1) + mass1;
        return new(axis, cross0, cross1, response0, response1, inverse > 1e-8f ? 1 / inverse : 0);
    }
    private static Vector3 Transform(AlsJointInertiaTensor tensor, Vector3 v) => tensor.X.ToSingle() * v.X + tensor.Y.ToSingle() * v.Y + tensor.Z.ToSingle() * v.Z;
    // Native scalar contact rows reduce body 0 in Z/X/Y order and body 1
    // in Y/X/Z order. Keep products separate, including across .NET versions.
    private static float DotZxy(Vector3 a, Vector3 b) => (a.Z * b.Z + a.X * b.X) + a.Y * b.Y;
    private static float DotYxz(Vector3 a, Vector3 b) => (a.Y * b.Y + a.X * b.X) + a.Z * b.Z;
    private static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static float InverseMass(double value)
    {
        var result = (float)value;
        if (!float.IsFinite(result)) throw new ArgumentOutOfRangeException(nameof(value));
        return result > 1.17549435e-38f ? result : 0;
    }
    private static void Validate(AlsQuaternion rotation)
    {
        if (!double.IsFinite(rotation.LengthSquared) || System.Math.Abs(rotation.LengthSquared - 1) > 1e-5)
            throw new ArgumentException("Contact inertia requires a unit rotation.");
    }
    private static void Validate(in AlsContactMaterial material)
    {
        ReadOnlySpan<float> values = stackalloc float[] { material.StaticFriction, material.DynamicFriction, material.VelocityFriction,
            material.MinFrictionPushOut, material.Stiffness, material.PositionFrictionStiffness, material.VelocityFrictionStiffness };
        foreach (var value in values) if (!float.IsFinite(value) || value < 0) throw new ArgumentException("Invalid contact material.");
        if (material.Stiffness > 1) throw new ArgumentException("Contact stiffness must not exceed one.");
    }
    private static void Validate(in AlsContactPointInput input, bool fromNativeGather)
    {
        ReadOnlySpan<Vector3> vectors = stackalloc Vector3[] { input.Arm0, input.Arm1, input.Normal, input.TangentU, input.TangentV, input.Error };
        foreach (var v in vectors) if (!new AlsDoubleVector(v).IsFinite) throw new ArgumentException("Contact input must be finite.");
        if (!float.IsFinite(input.TargetVelocity) || MathF.Abs(input.Normal.LengthSquared() - 1) > 1e-5f ||
            MathF.Abs(input.TangentU.LengthSquared() - 1) > 1e-5f ||
            (!fromNativeGather && (MathF.Abs(input.TangentV.LengthSquared() - 1) > 1e-5f ||
                MathF.Abs(Vector3.Dot(input.Normal, input.TangentU)) > 1e-5f)) ||
            Vector3.Distance(Vector3.Cross(input.Normal, input.TangentU), input.TangentV) > 1e-5f)
            throw new ArgumentException($"Contact basis must be orthonormal and right handed: N={input.Normal}, U={input.TangentU}, V={input.TangentV}, NdotU={Vector3.Dot(input.Normal, input.TangentU)}.");
    }
}
