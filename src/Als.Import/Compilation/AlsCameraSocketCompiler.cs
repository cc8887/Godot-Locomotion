using System.Collections.ObjectModel;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsCameraSocket(string Name, string Bone, AlsPrecisePose Local);
public static class AlsCameraSocketCompiler
{
    public static IReadOnlyDictionary<string, AlsCameraSocket> Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("character").GetString() != "/ALS/ALS/Character/B_Als_Character.B_Als_Character_C" ||
            root.GetProperty("mesh").GetString() != "/ALS/ALS/Character/SKM_Als.SKM_Als" ||
            root.GetProperty("skeleton").GetString() != "/ALS/ALS/Character/SK_Als.SK_Als")
            throw new ArgumentException("Foreign camera socket source.");
        var sockets = new Dictionary<string, AlsCameraSocket>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.GetProperty("sockets").EnumerateArray())
        {
            var name = row.GetProperty("name").GetString()!; var bone = row.GetProperty("bone").GetString()!;
            var source = row.GetProperty("source").GetString() ?? "";
            if (!source.StartsWith(root.GetProperty("mesh").GetString() + ":", StringComparison.Ordinal) &&
                !source.StartsWith(root.GetProperty("skeleton").GetString() + ":", StringComparison.Ordinal))
                throw new ArgumentException("Foreign camera socket owner.");
            var p = Vector(row.GetProperty("translation")); var s = Vector(row.GetProperty("scale"));
            var rotation = row.GetProperty("rotation");
            if (rotation.GetArrayLength() != 4) throw new ArgumentException("Invalid socket quaternion shape.");
            var q = new AlsQuaternion(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble());
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(bone) || !double.IsFinite(q.LengthSquared) ||
                System.Math.Abs(q.LengthSquared - 1) > .00001 || s.X <= 0 || s.Y <= 0 || s.Z <= 0 ||
                !sockets.TryAdd(name, new(name, bone, new(p, q, s)))) throw new ArgumentException("Invalid or duplicate camera socket.");
        }
        string[] expected = ["FirstPersonCamera", "ThirdPersonTraceShoulderLeft", "ThirdPersonTraceShoulderRight"];
        if (sockets.Count != expected.Length || expected.Any(n => !sockets.ContainsKey(n))) throw new ArgumentException("Incomplete camera sockets.");
        return new ReadOnlyDictionary<string, AlsCameraSocket>(sockets);
        static AlsDoubleVector Vector(JsonElement value)
        {
            if (value.GetArrayLength() != 3) throw new ArgumentException("Invalid socket vector shape.");
            var v = new AlsDoubleVector(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
            return v.IsFinite ? v : throw new ArgumentException("Nonfinite socket vector.");
        }
    }
}
