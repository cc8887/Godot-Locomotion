using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public static class AlsConvexPropertiesCompiler
{
    public static Dictionary<(int Body, int Shape), AlsConvexProperties> Compile(string json,
        AlsRagdollPhysicsDefinition definition, IReadOnlyDictionary<(int Body, int Shape), AlsConvexTopology> topology)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Convex properties schema differs.");
        var rows = root.GetProperty("shapes").EnumerateArray().ToDictionary(r => r.GetProperty("bone").GetString()!, StringComparer.Ordinal);
        var result = new Dictionary<(int, int), AlsConvexProperties>();
        foreach (var (key, hull) in topology)
        {
            if (!rows.TryGetValue(definition.Bodies[key.Body].Bone, out var row)) throw new InvalidDataException("Missing native convex properties.");
            var vertices = row.GetProperty("vertices");
            if (vertices.GetArrayLength() != hull.VertexCount) throw new InvalidDataException("Native convex vertex count differs.");
            for (var i = 0; i < hull.VertexCount; i++)
                if (V(vertices[i]) != new AlsDoubleVector(hull.VertexAt(i))) throw new InvalidDataException("Convex properties belong to another hull.");
            var properties = new AlsConvexProperties(V(row.GetProperty("centerOfMass")), V(row.GetProperty("boundsMin")), V(row.GetProperty("boundsMax")));
            properties.Validate(); result.Add(key, properties);
        }
        return result;
    }
    private static AlsDoubleVector V(JsonElement row)
    {
        if (row.GetArrayLength() != 3) throw new InvalidDataException("Invalid convex property vector.");
        var v = new AlsDoubleVector(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble());
        if (!v.IsFinite) throw new InvalidDataException("Nonfinite convex property."); return v;
    }
}
