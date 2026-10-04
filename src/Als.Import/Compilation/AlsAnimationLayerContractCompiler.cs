using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Animation;

namespace GodotAls.Import;

public static class AlsAnimationLayerContractCompiler
{
    public static AlsAnimationLayerCatalog Compile(byte[] bytes, byte[]? inventoryBytes = null)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported animation contract schema.");
            if (inventoryBytes is not null && root.GetProperty("inventorySha256").GetString() != Sha(inventoryBytes))
                throw new InvalidOperationException("Stale animation contract inventory.");
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            var classes = new List<AlsAnimationLayerClassContract>();
            foreach (var entry in root.GetProperty("classes").EnumerateObject())
            {
                if (!aliases.Add(entry.Name)) throw new InvalidOperationException("Duplicate animation class alias.");
                var row = entry.Value;
                classes.Add(new(Name(row, "class"), Text(row, "skeleton"),
                    row.GetProperty("functions").EnumerateArray().Select(ReadFunction),
                    row.GetProperty("linkedNodes").EnumerateArray().Select(ReadCall),
                    Flag(row, "receiveNotifies"), Flag(row, "propagateNotifies"), Flag(row, "useMainMontageData")));
            }
            if (classes.Count == 0) throw new InvalidOperationException("Empty animation contract catalog.");
            return new(Sha(bytes), classes);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or KeyNotFoundException or FormatException)
        { throw new InvalidOperationException("Invalid compiled animation contract.", error); }
    }

    private static AlsAnimationLayerSignature ReadFunction(JsonElement row)
    {
        var name = Name(row, "name"); var implemented = row.GetProperty("implemented").GetBoolean();
        var poses = Names(row.GetProperty("inputPoses"));
        var parameters = row.GetProperty("inputProperties").EnumerateArray().Select(p =>
        {
            var type = Name(p, "functionType"); var binding = Text(p, "classType");
            if (binding.Length != 0 && binding != type || implemented && binding.Length == 0)
                throw new InvalidOperationException("Animation property binding differs: " + name);
            return new AlsAnimationLayerParameter(Name(p, "name"), Scalar(type), binding.Length != 0);
        }).ToImmutableArray();
        if (parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parameters.Length)
            throw new InvalidOperationException("Duplicate animation parameter: " + name);
        var blendIn = row.GetProperty("blendInTime").GetSingle(); var blendOut = row.GetProperty("blendOutTime").GetSingle();
        if (!float.IsFinite(blendIn) || !float.IsFinite(blendOut) || blendIn < -1 || blendOut < -1)
            throw new InvalidOperationException("Invalid animation blend time: " + name);
        return new(name, Text(row, "group"), implemented, poses, parameters, blendIn, blendOut,
            Text(row, "blendInProfile"), Text(row, "blendOutProfile"));
    }
    private static AlsAnimationLayerCallContract ReadCall(JsonElement row)
    {
        var fallback = Text(row, "instanceClass");
        return new(Name(row, "node"), Name(row, "layer"), Text(row, "interface"),
            fallback.Length == 0 ? null : fallback, Names(row.GetProperty("inputPoses")),
            Flag(row, "receiveNotifies"), Flag(row, "propagateNotifies"));
    }
    private static ImmutableArray<string> Names(JsonElement rows)
    {
        var names = rows.EnumerateArray().Select(v => v.GetString()!).ToImmutableArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidOperationException("Invalid or duplicate Pose input.");
        return names;
    }
    private static string Name(JsonElement row, string field)
    {
        var name = row.GetProperty(field).GetString();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Missing animation name: " + field);
        return name;
    }
    private static string Text(JsonElement row, string field) => row.GetProperty(field).GetString()
        ?? throw new InvalidOperationException("Null animation metadata: " + field);
    private static bool Flag(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.GetBoolean();
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static AlsAnimationLayerScalarType Scalar(string type) => type switch
    {
        "bool" => AlsAnimationLayerScalarType.Boolean,
        "int32" => AlsAnimationLayerScalarType.Int32,
        "int64" => AlsAnimationLayerScalarType.Int64,
        "float" => AlsAnimationLayerScalarType.Single,
        "double" => AlsAnimationLayerScalarType.Double,
        _ => throw new InvalidOperationException("Unsupported animation parameter type: " + type),
    };
}
