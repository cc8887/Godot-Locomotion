using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// This is the native physics definition, not a claim that Godot implements Chaos.
// Keep UE units/axes until the explicit physical-body consumer boundary.
public sealed record AlsPhysicsShape(string Type, AlsPrecisePose Local, double RadiusCm,
    double CylinderLengthCm, AlsDoubleVector SizeCm, AlsDoubleVector[] VerticesCm,
    int[] Indices, bool ContributesToMass, int CollisionEnabled, double RestOffsetCm);
public sealed record AlsPhysicsBody(int Index, string Bone, int BoneIndex, int PhysicsType,
    double MassKg, AlsDoubleVector InertiaKgCm2, AlsPrecisePose MassLocal,
    AlsPrecisePose ReferenceComponent, AlsPhysicsShape[] Shapes, JsonElement Defaults, JsonElement Material);
public sealed record AlsPhysicsJoint(int Index, int ChildBody, int ParentBody,
    AlsPrecisePose ChildFrame, AlsPrecisePose ParentFrame, JsonElement NativeInstance, JsonElement Profiles);
public sealed record AlsPhysicsBone(string Name, int Parent, AlsPrecisePose Local);
public sealed record AlsRagdollPhysicsDefinition(string Mesh, string PhysicsAsset, AlsPhysicsBone[] Bones,
    AlsPhysicsBody[] Bodies, AlsPhysicsJoint[] Joints, (int A, int B)[] DisabledCollisions)
{
    public int[] Bind(IReadOnlyList<string> targetBones)
    {
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < targetBones.Count; i++)
            if (!indices.TryAdd(targetBones[i], i)) throw new InvalidDataException("Ambiguous target physics bone.");
        return Bodies.Select(b => indices.TryGetValue(b.Bone, out var i) ? i :
            throw new InvalidDataException("Missing target physics bone: " + b.Bone)).ToArray();
    }
}

public static class AlsPhysicsAssetCompiler
{
    public const string MeshRoot = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/";
    public static AlsRagdollPhysicsDefinition Compile(string json, string meshPath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("coordinates").GetString() == "UE mesh-local, centimeters, kilograms, degrees; inertia kg*cm^2",
            "Physics source schema/units differ.");
        ValidateNumbers(root);
        var rows = root.GetProperty("meshes").EnumerateArray().Where(m => Text(m, "mesh") == meshPath).ToArray();
        Require(rows.Length == 1, "Physics mesh binding missing or ambiguous.");
        var row = rows[0]; var asset = Text(row, "physicsAsset");
        Require(asset.StartsWith("/Game/", StringComparison.Ordinal), "Physics asset path missing.");
        var bones = row.GetProperty("bones").EnumerateArray().Select(b =>
            new AlsPhysicsBone(Text(b,"name"), b.GetProperty("parent").GetInt32(), Pose(b.GetProperty("local")))).ToArray();
        var names = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bones.Length; i++)
            Require(names.TryAdd(bones[i].Name,i) && bones[i].Parent >= -1 && bones[i].Parent < i, "Invalid native skeleton hierarchy.");
        var bodies = new List<AlsPhysicsBody>(); var bodyNames = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in row.GetProperty("bodies").EnumerateArray())
        {
            var index = b.GetProperty("index").GetInt32(); var bone = Text(b,"bone");
            Require(index == bodies.Count && names.ContainsKey(bone) && bodyNames.TryAdd(bone,index), "Invalid/duplicate native body binding.");
            var mass = Positive(b,"nativeMassKg"); var inertia = Vector(b.GetProperty("nativeInertiaKgCm2"));
            Require(inertia.X > 0 && inertia.Y > 0 && inertia.Z > 0, "Native inertia must be positive.");
            var shapes = b.GetProperty("shapes").EnumerateArray().Select(Shape).ToArray();
            Require(shapes.Length > 0, "Native body has no collision geometry.");
            var defaults = b.GetProperty("defaults");
            Nonnegative(defaults,"linearDamping"); Nonnegative(defaults,"angularDamping");
            var material = b.GetProperty("material"); Positive(material,"densityGPerCm3");
            Nonnegative(material,"friction"); Nonnegative(material,"staticFriction");
            Require(Number(material,"restitution") is >= 0 and <= 1, "Invalid restitution.");
            var physicsType = b.GetProperty("physicsType").GetInt32(); Require(physicsType is >= 0 and <= 2, "Invalid physics type.");
            bodies.Add(new(index,bone,names[bone],physicsType,mass,inertia,Pose(b.GetProperty("nativeMassLocal")),
                Pose(b.GetProperty("nativeBodyComponent")),shapes,defaults.Clone(),material.Clone()));
        }
        Require(bodyNames.ContainsKey("pelvis") && bodyNames.ContainsKey("spine_03"), "ALS requires pelvis and spine_03 physics bodies.");
        var joints = new List<AlsPhysicsJoint>(); var parents = Enumerable.Repeat(-1,bodies.Count).ToArray();
        foreach (var c in row.GetProperty("constraints").EnumerateArray())
        {
            var index = c.GetProperty("index").GetInt32(); var child = Text(c,"childBone"); var parent = Text(c,"parentBone");
            Require(index == joints.Count && bodyNames.ContainsKey(child) && bodyNames.ContainsKey(parent), "Invalid constraint binding/index.");
            var a = bodyNames[child]; var b = bodyNames[parent];
            Require(a != b && parents[a] == -1, "Duplicate constraint parent or self constraint."); parents[a] = b;
            var native = c.GetProperty("nativeInstance"); var profile = native.GetProperty("profileInstance");
            foreach (var field in new[] {"linearLimit","coneLimit","twistLimit","linearDrive","angularDrive"})
                Require(profile.GetProperty(field).ValueKind == JsonValueKind.Object, "Missing native constraint configuration.");
            Require(string.Equals(Text(native,"constraintBone1"), child,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Text(native,"constraintBone2"), parent,StringComparison.OrdinalIgnoreCase), "Constraint native binding differs.");
            var profiles = c.GetProperty("profiles"); Require(profiles.ValueKind == JsonValueKind.Array,"Missing constraint profiles.");
            joints.Add(new(index,a,b,Pose(c.GetProperty("childFrame")),Pose(c.GetProperty("parentFrame")),native.Clone(),profiles.Clone()));
        }
        Require(joints.Count == bodies.Count - 1, "ALS physics rig must be a connected tree.");
        for (var i = 0; i < parents.Length; i++)
        {
            var depth = 0;
            for (var p = i; p >= 0; p = parents[p]) Require(++depth <= parents.Length,"Cyclic physics constraints.");
        }
        var disabled = new List<(int,int)>(); var unique = new HashSet<(int,int)>();
        foreach (var pair in row.GetProperty("collisionDisableTable").EnumerateArray())
        {
            var a = pair.GetProperty("a").GetInt32(); var b = pair.GetProperty("b").GetInt32();
            Require(a >= 0 && a < b && b < bodies.Count && unique.Add((a,b)),"Invalid collision exclusion.");
            // UE checks map membership, not the stored boolean value.
            _ = pair.GetProperty("value").GetBoolean(); disabled.Add((a,b));
        }
        return new(meshPath,asset,bones,bodies.ToArray(),joints.ToArray(),disabled.ToArray());
    }

    private static AlsPhysicsShape Shape(JsonElement s)
    {
        var type = Text(s,"type"); double radius = 0, length = 0; var size = AlsDoubleVector.Zero;
        AlsDoubleVector[] vertices = []; int[] indices = [];
        switch (type)
        {
            case "sphere": radius = Positive(s,"radiusCm"); break;
            case "capsule": radius = Positive(s,"radiusCm"); length = Nonnegative(s,"cylinderLengthCm"); break;
            case "box": size = Vector(s.GetProperty("sizeCm")); Require(size.X > 0 && size.Y > 0 && size.Z > 0,"Invalid box size."); break;
            case "convex":
                vertices = s.GetProperty("verticesCm").EnumerateArray().Select(Vector).ToArray();
                indices = s.GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Require(vertices.Length >= 4 && indices.Length >= 12 && indices.Length % 3 == 0 &&
                    indices.All(i => i >= 0 && i < vertices.Length),"Invalid convex geometry."); break;
            default: throw new InvalidDataException("Physics consumer does not support shape: " + type);
        }
        var collision = s.GetProperty("collisionEnabled").GetInt32(); Require(collision is >= 0 and <= 5,"Invalid shape collision mode.");
        return new(type,Pose(s.GetProperty("local"), allowScale: type == "convex"),radius,length,size,vertices,indices,
            s.GetProperty("contributesToMass").GetBoolean(),collision,Number(s,"restOffsetCm"));
    }
    private static AlsPrecisePose Pose(JsonElement e, bool allowScale = false)
    {
        var q = e.GetProperty("rotation").EnumerateArray().Select(x => x.GetDouble()).ToArray(); Require(q.Length == 4,"Invalid quaternion.");
        var pose = new AlsPrecisePose(Vector(e.GetProperty("translation")),new(q[0],q[1],q[2],q[3]),Vector(e.GetProperty("scale")));
        // GetRefFrame reconstructs a matrix from stored float axes; its scale can
        // differ from one by float roundoff. Preserve it instead of rewriting source.
        Require(Math.Abs(pose.Rotation.LengthSquared - 1) < .001 &&
            (allowScale ? pose.Scale.X > 0 && pose.Scale.Y > 0 && pose.Scale.Z > 0 :
                (pose.Scale - AlsDoubleVector.One).NearlyZero(1e-5)),"Invalid physics transform scale/rotation.");
        pose.Validate(.001); return pose;
    }
    private static AlsDoubleVector Vector(JsonElement e)
    { var a = e.EnumerateArray().Select(x => x.GetDouble()).ToArray(); Require(a.Length == 3,"Invalid vector."); return new(a[0],a[1],a[2]); }
    private static string Text(JsonElement e,string key)
    { var s = e.GetProperty(key).GetString(); Require(!string.IsNullOrWhiteSpace(s),"Missing physics identity."); return s!; }
    private static double Number(JsonElement e,string key) => e.GetProperty(key).GetDouble();
    private static double Positive(JsonElement e,string key) { var n = Number(e,key); Require(n > 0,"Invalid " + key); return n; }
    private static double Nonnegative(JsonElement e,string key) { var n = Number(e,key); Require(n >= 0,"Invalid " + key); return n; }
    private static void ValidateNumbers(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) ValidateNumbers(p.Value);
        else if (e.ValueKind == JsonValueKind.Array) foreach (var p in e.EnumerateArray()) ValidateNumbers(p);
        else if (e.ValueKind == JsonValueKind.Number) Require(double.IsFinite(e.GetDouble()),"Nonfinite physics data.");
    }
    private static void Require(bool condition,string error) { if (!condition) throw new InvalidDataException(error); }
}
