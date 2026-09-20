using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Effective Chaos settings, before any Godot units or solver adaptation. These
// enums are Chaos enums, not EAngularConstraintMotion or Godot joint constants.
public enum AlsJointMotion { Free, Limited, Locked }
public enum AlsJointForceMode { Acceleration, Force }
public readonly record struct AlsJointMotions(AlsJointMotion X, AlsJointMotion Y, AlsJointMotion Z);
public readonly record struct AlsJointSoftLimit(bool Enabled, double Stiffness, double Damping,
    double Restitution, double ContactDistance);
public readonly record struct AlsJointAngularDrive(bool TwistPosition, bool TwistVelocity,
    bool SwingPosition, bool SwingVelocity, bool SlerpPosition, bool SlerpVelocity,
    AlsJointForceMode ForceMode, AlsDoubleVector Stiffness, AlsDoubleVector Damping,
    AlsDoubleVector MaxTorque, AlsQuaternion Target, AlsDoubleVector VelocityTarget);
public readonly record struct AlsJointProjection(bool Enabled, double LinearAlpha, double AngularAlpha,
    double TeleportDistanceCm, double TeleportAngleRad);
public sealed record AlsPhysicsJointSettings(int Index, AlsJointMotions LinearMotion, AlsJointMotions AngularMotion,
    double LinearLimitCm, AlsDoubleVector AngularLimitsRad, double Stiffness,
    AlsJointSoftLimit LinearSoftLimit, AlsJointSoftLimit TwistSoftLimit, AlsJointSoftLimit SwingSoftLimit,
    AlsJointForceMode AngularSoftForceMode, AlsJointAngularDrive AngularDrive, AlsJointProjection Projection,
    bool MassConditioning, bool ShockPropagationEnabled, double ShockPropagation, double ParentInvMassScale,
    bool CollisionEnabled, JsonElement NativeSettings);

public static class AlsPhysicsJointCompiler
{
    public const string Coordinates = "UE native joint frames; cm, kg, radians; axes X=Twist,Y=Swing2,Z=Swing1";

    // Bind by native joint identity and both bodies, never by JSON row order.
    // The current consumer supports the cached/linear Chaos branch only.
    public static AlsPhysicsJointSettings[] Compile(string json, AlsRagdollPhysicsDefinition definition)
    {
        try { return CompileDocument(json,definition); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed native joint settings.",e); }
    }

    private static AlsPhysicsJointSettings[] CompileDocument(string json, AlsRagdollPhysicsDefinition definition)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root,"coordinates") == Coordinates,
            "Joint schema/units differ.");
        Require(Text(root,"engine").StartsWith("5.9.0-",StringComparison.Ordinal),"Unverified native joint engine version.");
        var rows = root.GetProperty("meshes").EnumerateArray().Where(m => Text(m,"mesh") == definition.Mesh).ToArray();
        Require(rows.Length == 1,"Missing or ambiguous native joint mesh."); var row = rows[0];
        Require(Text(row,"physicsAsset") == definition.PhysicsAsset,"Native joint asset differs from body asset.");
        var entries = new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in row.GetProperty("joints").EnumerateArray())
            Require(entries.TryAdd(Text(j,"joint"),j),"Duplicate native joint.");
        Require(entries.Count == definition.Joints.Length,"Joint count differs from body definition.");
        var output = new AlsPhysicsJointSettings[definition.Joints.Length];
        foreach (var joint in definition.Joints)
        {
            Require(entries.TryGetValue(Text(joint.NativeInstance,"jointName"),out var j),"Missing native joint.");
            Require(string.Equals(Text(j,"child"),definition.Bodies[joint.ChildBody].Bone,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Text(j,"parent"),definition.Bodies[joint.ParentBody].Bone,StringComparison.OrdinalIgnoreCase),
                "Native joint body binding differs.");
            var s = j.GetProperty("settings"); Require(B(s,"bUseLinearSolver"),"Nonlinear joint solver is not supported by this consumer.");
            var angular = Motions(s.GetProperty("AngularMotionTypes")); var limits = V(s,"AngularLimits",true);
            CheckLimit(angular.X,limits.X); CheckLimit(angular.Y,limits.Y); CheckLimit(angular.Z,limits.Z);
            var drive = new AlsJointAngularDrive(B(s,"bAngularTwistPositionDriveEnabled"),B(s,"bAngularTwistVelocityDriveEnabled"),
                B(s,"bAngularSwingPositionDriveEnabled"),B(s,"bAngularSwingVelocityDriveEnabled"),
                B(s,"bAngularSLerpPositionDriveEnabled"),B(s,"bAngularSLerpVelocityDriveEnabled"),Mode(s,"AngularDriveForceMode"),
                V(s,"AngularDriveStiffness",true),V(s,"AngularDriveDamping",true),V(s,"AngularDriveMaxTorque",true),
                Q(s.GetProperty("AngularDrivePositionTarget")),V(s,"AngularDriveVelocityTarget",false));
            output[joint.Index] = new(joint.Index,Motions(s.GetProperty("LinearMotionTypes")),angular,N(s,"LinearLimit"),limits,
                Unit(s,"Stiffness"),Soft(s,"Linear"),Soft(s,"Twist"),Soft(s,"Swing"),Mode(s,"AngularSoftForceMode"),drive,
                new(B(s,"bProjectionEnabled"),Unit(s,"LinearProjection"),Unit(s,"AngularProjection"),N(s,"TeleportDistance"),N(s,"TeleportAngle")),
                B(s,"bMassConditioningEnabled"),B(s,"bShockPropagationEnabled"),Unit(s,"ShockPropagation"),N(s,"ParentInvMassScale"),
                B(s,"bCollisionEnabled"),s.Clone());
        }
        return output;
    }

    private static AlsJointSoftLimit Soft(JsonElement s,string axis) => new(B(s,"bSoft"+axis+"LimitsEnabled"),
        N(s,"Soft"+axis+"Stiffness"),N(s,"Soft"+axis+"Damping"),Unit(s,axis+"Restitution"),N(s,axis+"ContactDistance"));
    private static AlsJointMotions Motions(JsonElement e)
    {
        Require(e.GetArrayLength() == 3,"Invalid joint motion axis count.");
        AlsJointMotion Read(int i) { var value = e[i].GetInt32(); Require(value is >= 0 and <= 2,"Unknown joint motion."); return (AlsJointMotion)value; }
        return new(Read(0),Read(1),Read(2));
    }
    private static AlsJointForceMode Mode(JsonElement s,string key)
    { var n = s.GetProperty(key).GetInt32(); Require(n is 0 or 1,"Unknown joint force mode."); return (AlsJointForceMode)n; }
    private static void CheckLimit(AlsJointMotion motion,double value)
    {
        // Chaos keeps sentinel/unused angles on free and locked axes. Preserve
        // them, but never interpret them as an enabled angular limit.
        if (motion == AlsJointMotion.Limited) Require(value > 0 && value <= (double)(float)Math.PI,"Invalid limited angular range.");
    }
    private static AlsDoubleVector V(JsonElement s,string key,bool nonnegative)
    {
        var e = s.GetProperty(key); Require(e.GetArrayLength() == 3,"Invalid joint vector.");
        double Read(int i) { var n = e[i].GetDouble(); Require(double.IsFinite(n) && (!nonnegative || n >= 0),"Invalid "+key); return n; }
        return new(Read(0),Read(1),Read(2));
    }
    private static AlsQuaternion Q(JsonElement e)
    {
        Require(e.GetArrayLength() == 4,"Invalid joint quaternion.");
        var q = new AlsQuaternion(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble(),e[3].GetDouble());
        Require(double.IsFinite(q.LengthSquared) && Math.Abs(q.LengthSquared-1) < 1e-6,"Invalid joint target rotation."); return q;
    }
    private static bool B(JsonElement s,string key) => s.GetProperty(key).GetBoolean();
    private static string Text(JsonElement s,string key)
    { var value = s.GetProperty(key).GetString(); Require(!string.IsNullOrWhiteSpace(value),"Missing joint identity."); return value!; }
    private static double N(JsonElement s,string key)
    { var n = s.GetProperty(key).GetDouble(); Require(double.IsFinite(n) && n >= 0,"Invalid "+key); return n; }
    private static double Unit(JsonElement s,string key)
    { var n = N(s,key); Require(n <= 1,"Invalid "+key); return n; }
    private static void Require(bool condition,string message) { if (!condition) throw new InvalidDataException(message); }
}
