using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Bind the native reference-pose creation scale before consuming authored
// connectors. SkeletalMeshComponent adjusts Pos1/Pos2 even at unit actor scale.
public static class AlsPhysicsJointFrameCompiler
{
    public const string Coordinates = "UE actor-local joint frames; centimeters; creation scale before physics";

    public static AlsRagdollPhysicsDefinition Compile(string json, AlsRagdollPhysicsDefinition authored)
    {
        try { return CompileDocument(json, authored); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed native joint frame input.", e); }
    }

    private static AlsRagdollPhysicsDefinition CompileDocument(string json, AlsRagdollPhysicsDefinition authored)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("coordinates").GetString() == Coordinates,
            "Joint frame schema/units differ.");
        var rig = root.GetProperty("rigs").EnumerateArray().Single(r => r.GetProperty("mesh").GetString() == authored.Mesh);
        Require(rig.GetProperty("physicsAsset").GetString() == authored.PhysicsAsset, "Joint frame asset differs.");
        // Geometry and native mass inputs are currently bound to this creation
        // pose. Arbitrary rescaling also needs recooked shapes/mass properties.
        Require(V(rig.GetProperty("componentScale")) == AlsDoubleVector.One, "Scaled component creation is unsupported.");
        var bodies = rig.GetProperty("bodies");
        Require(bodies.GetArrayLength() == authored.Bodies.Length, "Joint frame body topology differs.");
        var scales = new AlsDoubleVector[authored.Bodies.Length];
        foreach (var body in authored.Bodies)
        {
            var native = bodies[body.Index];
            Require(native.GetProperty("index").GetInt32() == body.Index && native.GetProperty("bone").GetString() == body.Bone &&
                native.GetProperty("nativeMassKg").GetDouble() == body.MassKg && V(native.GetProperty("nativeInertiaKgCm2")) == body.InertiaKgCm2,
                "Joint frame body binding differs.");
            var defaults = V(native.GetProperty("defaultScale")); var instance = V(native.GetProperty("instanceScale"));
            Require(defaults.X > 0 && defaults.Y > 0 && defaults.Z > 0 &&
                (defaults - AlsDoubleVector.One).NearlyZero(1e-5) && (instance - AlsDoubleVector.One).NearlyZero(1e-5),
                "Joint creation scale exceeds the bound reference geometry.");
            // FVector::Reciprocal then component multiplication, not division
            // of the position itself. Component scale is exactly one here.
            scales[body.Index] = instance * new AlsDoubleVector(1 / defaults.X, 1 / defaults.Y, 1 / defaults.Z);
        }
        var source = rig.GetProperty("joints");
        Require(source.GetArrayLength() == authored.Joints.Length, "Joint frame constraint topology differs.");
        var joints = new AlsPhysicsJoint[authored.Joints.Length];
        foreach (var joint in authored.Joints)
        {
            var native = source[joint.Index];
            Require(native.GetProperty("index").GetInt32() == joint.Index &&
                native.GetProperty("childBone").GetString() == authored.Bodies[joint.ChildBody].Bone &&
                native.GetProperty("parentBone").GetString() == authored.Bodies[joint.ParentBody].Bone &&
                Pose(native.GetProperty("authoredChildFrame")) == joint.ChildFrame &&
                Pose(native.GetProperty("authoredParentFrame")) == joint.ParentFrame,
                "Joint frame authored binding differs or has already been adjusted.");
            joints[joint.Index] = joint with
            {
                ChildFrame = joint.ChildFrame with { Position = joint.ChildFrame.Position * scales[joint.ChildBody] },
                ParentFrame = joint.ParentFrame with { Position = joint.ParentFrame.Position * scales[joint.ParentBody] }
            };
        }
        return authored with { Joints = joints };
    }

    internal static AlsPrecisePose Pose(JsonElement value)
    {
        var q = value.GetProperty("rotation"); Require(q.GetArrayLength() == 4, "Invalid joint frame rotation.");
        var pose = new AlsPrecisePose(V(value.GetProperty("translation")), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            V(value.GetProperty("scale")));
        Require(double.IsFinite(pose.Rotation.LengthSquared) && Math.Abs(pose.Rotation.LengthSquared - 1) < .001 &&
            (pose.Scale - AlsDoubleVector.One).NearlyZero(1e-5), "Invalid joint frame transform.");
        return pose;
    }

    private static AlsDoubleVector V(JsonElement value)
    {
        Require(value.GetArrayLength() == 3, "Invalid joint frame vector.");
        var vector = new AlsDoubleVector(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
        Require(vector.IsFinite, "Nonfinite joint frame vector."); return vector;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
