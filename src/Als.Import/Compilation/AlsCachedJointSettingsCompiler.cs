using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

// Decode the exported effective Chaos settings for the implemented cached rows.
// Unsupported native features are errors, never silently replaced by defaults.
public static class AlsCachedJointSettingsCompiler
{
    public static AlsPrecisePose RigidConnector(in AlsPrecisePose exported)
    {
        // GetRefFrame reconstructs scale from float axes. Chaos consumes the
        // connector's location and rotation only (GetComRelativeTransform /
        // InitDerivedState), so discard roundoff scale at the solver boundary.
        Require((exported.Scale - AlsDoubleVector.One).NearlyZero(1e-5), "Scaled joint connectors are unsupported.");
        return exported with { Scale = AlsDoubleVector.One };
    }

    public static AlsAngularJointSettings Angular(JsonElement joint, JsonElement solver)
    {
        Require(B(joint, "bUseLinearSolver") && B(solver, "bSolvePositionLast") && B(solver, "bUsePositionBasedDrives"),
            "Cached position-last, position-based joints are required.");
        Require(D(solver, "MinSolverStiffness") == 1 && D(solver, "MaxSolverStiffness") == 1 &&
            D(solver, "NumShockPropagationIterations") == 0, "Iteration stiffness or shock propagation is unsupported.");
        Require(B(solver, "bEnableTwistLimits") && B(solver, "bEnableSwingLimits") && B(solver, "bEnableDrives"),
            "Global constraint disabling is unsupported.");
        foreach (var property in solver.EnumerateObject())
            if (property.Name.EndsWith("Override", StringComparison.Ordinal))
                Require(property.Value.GetDouble() == -1, "Solver setting overrides are unsupported.");
        Require(!B(joint, "bAngularSLerpPositionDriveEnabled") && !B(joint, "bAngularSLerpVelocityDriveEnabled"),
            "SLERP drives are unsupported.");
        Require(V(joint, "AngularDriveVelocityTarget") == AlsDoubleVector.Zero, "Nonzero drive velocity targets are unsupported.");
        foreach (var field in new[] { "LinearMotionTypes", "AngularMotionTypes", "bLinearPositionDriveEnabled", "bLinearVelocityDriveEnabled" })
            Require(joint.GetProperty(field).GetArrayLength() == 3, "Invalid joint axis count.");
        var torque = V(joint, "AngularDriveMaxTorque");
        for (var i = 0; i < 3; i++)
        {
            Require(torque[i] is 0 or (double)float.MaxValue, "Finite drive torque caps are unsupported.");
            Require(joint.GetProperty("LinearMotionTypes")[i].GetInt32() == 2 &&
                !joint.GetProperty("bLinearPositionDriveEnabled")[i].GetBoolean() &&
                !joint.GetProperty("bLinearVelocityDriveEnabled")[i].GetBoolean(), "Only hard locked linear anchors without linear drives are supported.");
        }
        Require(D(joint, "ParentInvMassScale") == 1 && !B(joint, "bShockPropagationEnabled"), "Parent scaling and shock propagation are unsupported.");
        Require(D(joint, "LinearRestitution") == 0 && D(joint, "TwistRestitution") == 0 && D(joint, "SwingRestitution") == 0,
            "Joint restitution is unsupported.");
        var limitMode = joint.GetProperty("AngularSoftForceMode").GetInt32();
        var driveMode = joint.GetProperty("AngularDriveForceMode").GetInt32();
        Require(limitMode is 0 or 1 && driveMode is 0 or 1, "Unknown force mode.");
        return new(Axis(0), Axis(1), Axis(2), Q(joint.GetProperty("AngularDrivePositionTarget")),
            B(joint, "bMassConditioningEnabled"), limitMode == 0, driveMode == 0, B(solver, "bUseSimd"),
            D(joint, "Stiffness"), D(solver, "AngleTolerance"), D(solver, "MinParentMassRatio"), D(solver, "MaxInertiaRatio"));

        AlsAngularAxisSettings Axis(int index)
        {
            var motion = joint.GetProperty("AngularMotionTypes")[index].GetInt32();
            Require(motion is >= 0 and <= 2, "Unknown angular motion.");
            var name = index == 0 ? "Twist" : "Swing"; var driveIndex = index == 0 ? 0 : 2;
            return new((AlsAngularMotion)motion, V(joint, "AngularLimits")[index], B(joint, "bSoft" + name + "LimitsEnabled"),
                D(joint, "Soft" + name + "Stiffness"), D(joint, "Soft" + name + "Damping"),
                B(joint, "bAngular" + name + "PositionDriveEnabled") ? V(joint, "AngularDriveStiffness")[driveIndex] : 0,
                B(joint, "bAngular" + name + "VelocityDriveEnabled") ? V(joint, "AngularDriveDamping")[driveIndex] : 0);
        }
    }

    public static AlsIslandProjection Projection(JsonElement joint, JsonElement solver)
    {
        Require(D(joint, "AngularProjection") == 0, "Angular projection is unsupported.");
        Require(D(solver, "LinearProjectionOverride") == -1 && D(solver, "AngularProjectionOverride") == -1,
            "Projection overrides are unsupported.");
        Require(!B(joint, "bProjectionEnabled") || B(solver, "bUseSimd"), "Only SIMD linear projection has been verified.");
        // Cached Chaos InitProjection explicitly excludes TeleportAngle; it does
        // not activate angular projection while AngularProjection is zero.
        return new(B(joint, "bProjectionEnabled"), (float)D(joint, "LinearProjection"), (float)D(joint, "TeleportDistance"));
    }

    private static double D(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetDouble(); Require(double.IsFinite(value), "Nonfinite joint setting."); return value;
    }
    private static bool B(JsonElement element, string name) => element.GetProperty(name).GetBoolean();
    private static AlsDoubleVector V(JsonElement element, string name)
    {
        var v = element.GetProperty(name); Require(v.GetArrayLength() == 3, "Invalid vector size.");
        var value = new AlsDoubleVector(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        Require(value.IsFinite, "Nonfinite joint vector."); return value;
    }
    private static AlsQuaternion Q(JsonElement value)
    {
        Require(value.GetArrayLength() == 4, "Invalid rotation size.");
        var q = new AlsQuaternion(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble());
        Require(double.IsFinite(q.LengthSquared) && System.Math.Abs(q.LengthSquared - 1) < 1e-5, "Invalid rotation."); return q;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
