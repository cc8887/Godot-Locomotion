using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsObservedSimulationFilter(bool Simulation, AlsSimulationFilter Filter, byte QueryMaskFilter);

public static class AlsSimulationFilterCompiler
{
    public const string Observation = "Native full-world skeletal component: PhysicsBody, QueryAndPhysics, all channels Block; per-body effective shape filters after physics creation";
    public static Dictionary<(int Body, int Shape), AlsObservedSimulationFilter> Compile(string json,
        string runtimeJson, string authoredJson, string mesh)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("observation").GetString() == Observation,
            "Simulation filter observation differs.");
        Require(root.GetProperty("runtimeShapesSha256").GetString() == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runtimeJson))),
            "Simulation filters belong to different runtime shapes.");
        var runtime = AlsRuntimeShapeCompiler.Compile(runtimeJson, authoredJson, mesh).ToDictionary(s => (s.Body, s.Shape));
        var definition = AlsPhysicsAssetCompiler.Compile(authoredJson, mesh);
        var rig = root.GetProperty("meshes").EnumerateArray().Single(m => m.GetProperty("mesh").GetString() == mesh);
        var bodies = rig.GetProperty("bodies"); Require(bodies.GetArrayLength() == definition.Bodies.Length, "Filter body count differs.");
        var result = new Dictionary<(int, int), AlsObservedSimulationFilter>(); var seen = new HashSet<int>();
        foreach (var body in bodies.EnumerateArray())
        {
            var index = body.GetProperty("index").GetInt32();
            Require((uint)index < definition.Bodies.Length && seen.Add(index), "Invalid filter body identity.");
            Require(body.GetProperty("bone").GetString() == definition.Bodies[index].Bone, "Filter bone differs.");
            var shapes = body.GetProperty("shapes"); Require(shapes.GetArrayLength() == definition.Bodies[index].Shapes.Length, "Filter shape count differs.");
            foreach (var shape in shapes.EnumerateArray())
            {
                var key = (index, shape.GetProperty("authoredIndex").GetInt32());
                Require(runtime.TryGetValue(key, out var observed) && observed.NativeIndex == shape.GetProperty("nativeIndex").GetInt32(), "Filter shape binding differs.");
                var channel = shape.GetProperty("channel").GetInt32(); Require(channel is >= 0 and < 64, "Invalid native channel.");
                // Overlap events have a separate gameplay path; they never create blocking solver rows.
                _ = Mask(shape, "overlapChannels");
                // Chaos FShapeFilterData::NarrowFilter only intersects blocking channels.
                // MaskFilter belongs to query filtering, not simulation pair rejection.
                var filter = new AlsSimulationFilter(1UL << channel, Mask(shape, "blockChannels"));
                Require(result.TryAdd(key, new(shape.GetProperty("simulation").GetBoolean(), filter,
                    shape.GetProperty("maskFilter").GetByte())), "Duplicate filter shape.");
            }
        }
        Require(result.Count == runtime.Count, "Incomplete native simulation filters."); return result;
    }
    private static ulong Mask(JsonElement row, string key)
    {
        var value = row.GetProperty(key).GetString();
        Require(value?.Length == 16 && ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _), "Invalid native channel mask.");
        return ulong.Parse(value!, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
