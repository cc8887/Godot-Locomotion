using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsBoundConvexTopology(int Body, int Shape, AlsConvexTopology Topology);

public static class AlsConvexTopologyCompiler
{
    public const string Coordinates = "UE unscaled convex-local centimeters; native cooked float vertices and planes; element transform separate";
    public static AlsBoundConvexTopology[] Compile(string json, AlsRagdollPhysicsDefinition definition)
    {
        try { return CompileDocument(json, definition); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Invalid cooked convex topology or asset binding.", e); }
    }
    private static AlsBoundConvexTopology[] CompileDocument(string json, AlsRagdollPhysicsDefinition definition)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 2 && root.GetProperty("coordinates").GetString() == Coordinates, "Topology schema/units differ.");
        var rig = root.GetProperty("rigs").EnumerateArray().Single(r => r.GetProperty("mesh").GetString() == definition.Mesh);
        Require(rig.GetProperty("physicsAsset").GetString() == definition.PhysicsAsset, "Physics asset differs.");
        var result = new List<AlsBoundConvexTopology>(); var identities = new HashSet<(int, int)>();
        foreach (var row in rig.GetProperty("shapes").EnumerateArray())
        {
            var bodyIndex = row.GetProperty("bodyIndex").GetInt32(); var shapeIndex = row.GetProperty("shapeIndex").GetInt32();
            Require((uint)bodyIndex < definition.Bodies.Length, "Body index differs."); var body = definition.Bodies[bodyIndex];
            Require(body.Index == bodyIndex && body.Bone == row.GetProperty("bone").GetString() && (uint)shapeIndex < body.Shapes.Length, "Body/shape binding differs.");
            var shape = body.Shapes[shapeIndex];
            Require(shape.Type == "convex" && identities.Add((bodyIndex, shapeIndex)) &&
                body.Shapes.Take(shapeIndex).Count(s => s.Type == "convex") == row.GetProperty("convexIndex").GetInt32(), "Convex identity differs.");
            Require(row.GetProperty("windingOrder").GetDouble() == 1, "Unsupported native winding.");
            var local = row.GetProperty("local"); var rotation = local.GetProperty("rotation");
            Require(rotation.GetArrayLength() == 4, "Invalid convex rotation size.");
            var pose = new AlsPrecisePose(V(local.GetProperty("translation")),
                new(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble()), V(local.GetProperty("scale")));
            Require(pose == shape.Local && row.GetProperty("sourceVertices").EnumerateArray().Select(V).SequenceEqual(shape.VerticesCm) &&
                row.GetProperty("sourceIndices").EnumerateArray().Select(i => i.GetInt32()).SequenceEqual(shape.Indices), "Source convex geometry differs.");
            var vertices = row.GetProperty("vertices").EnumerateArray().Select(v => V(v).ToSingle()).ToArray();
            var planes = new List<AlsConvexPlane>(); var indices = new List<int>();
            foreach (var face in row.GetProperty("faces").EnumerateArray())
            {
                var loop = face.GetProperty("vertices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
                planes.Add(new(V(face.GetProperty("normal")).ToSingle(), V(face.GetProperty("point")).ToSingle(), indices.Count, loop.Length));
                indices.AddRange(loop);
            }
            var cached = row.GetProperty("vertexPlanes").EnumerateArray().Select(p =>
            {
                var slots = p.GetProperty("planes"); Require(slots.GetArrayLength() == 3, "Invalid native cache slots.");
                return new AlsConvexVertexPlanes(p.GetProperty("count").GetInt32(), slots[0].GetInt32(), slots[1].GetInt32(), slots[2].GetInt32());
            }).ToArray();
            Require(cached.Length == vertices.Length, "Missing native vertex-plane cache.");
            result.Add(new(bodyIndex, shapeIndex, new(vertices, planes.ToArray(), indices.ToArray(), row.GetProperty("margin").GetSingle(), cached)));
        }
        Require(result.Count == definition.Bodies.Sum(b => b.Shapes.Count(s => s.Type == "convex")), "Missing convex topology.");
        return result.ToArray();
    }
    private static AlsDoubleVector V(JsonElement e)
    { Require(e.GetArrayLength() == 3, "Invalid convex vector size."); return new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble()); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
