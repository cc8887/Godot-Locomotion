using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace GodotAls.Import.Compilation;

public sealed record AlsAnimationSetPayload(
    AlsSkeletonDefinition[] Skeletons,
    AlsSkeletalMeshDefinition[] SkeletalMeshes,
    AlsStaticMeshDefinition[] StaticMeshes,
    AlsAnimationDefinition[] Animations,
    AlsMontageDefinition[] Montages,
    AlsBlendDefinition[] BlendSpaces,
    AlsBlendDefinition[] AimOffsets,
    AlsMaterialDefinition[] Materials,
    AlsTextureDefinition[] Textures,
    AlsPhysicsAssetDefinition[] PhysicsAssets,
    AlsGenericAssetDefinition[] Curves,
    AlsGenericAssetDefinition[] ConfigAssets,
    string DefinitionDigest)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IncludeFields = true,
    };

    public static string Serialize(AlsAnimationSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(FromDefinition(definition), Options);
    }

    public static AlsAnimationSetDefinition Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("ALS animation-set payload is empty.");
        }

        var payload = JsonSerializer.Deserialize<AlsAnimationSetPayload>(json, Options)
            ?? throw new InvalidDataException("ALS animation-set payload deserialized to null.");
        return payload.ToDefinition();
    }

    public static string ComputeSha256(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string ComputeDefinitionDigest(AlsAnimationSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var canonicalPayload = Encoding.UTF8.GetBytes(Serialize(definition with
        {
            DefinitionDigest = string.Empty,
        }));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("GodotALS.CompiledDefinition.v1");
            writer.Write(canonicalPayload.Length);
            writer.Write(canonicalPayload);
            writer.Write(definition.Animations.Length);
            foreach (var animation in definition.Animations)
            {
                writer.Write(animation.Id);
                writer.Write(animation.StableId);

                var curves = animation.Curves;
                writer.Write(curves.Length);
                foreach (var curve in curves)
                {
                    writer.Write(curve.CurveId);
                    writer.Write((byte)curve.CanonicalKind);
                    writer.Write(curve.SourceName);
                    writer.Write((byte)curve.Provenance);

                    var keys = curve.Keys;
                    writer.Write(keys.Length);
                    foreach (var key in keys)
                    {
                        writer.Write(BitConverter.SingleToInt32Bits(key.TimeSeconds));
                        writer.Write(BitConverter.SingleToInt32Bits(key.Value));
                        writer.Write(BitConverter.SingleToInt32Bits(key.ArriveTangent));
                        writer.Write(BitConverter.SingleToInt32Bits(key.LeaveTangent));
                        writer.Write((byte)key.Interpolation);
                    }
                }

                var legacyCurveNames = animation.LegacyCurveNames;
                writer.Write(legacyCurveNames.Length);
                foreach (var name in legacyCurveNames)
                {
                    writer.Write(name);
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static AlsAnimationSetPayload FromDefinition(AlsAnimationSetDefinition definition) => new(
        definition.Skeletons,
        definition.SkeletalMeshes,
        definition.StaticMeshes,
        definition.Animations,
        definition.Montages,
        definition.BlendSpaces,
        definition.AimOffsets,
        definition.Materials,
        definition.Textures,
        definition.PhysicsAssets,
        definition.Curves,
        definition.ConfigAssets,
        definition.DefinitionDigest);

    private AlsAnimationSetDefinition ToDefinition()
    {
        var index = new AlsAssetIndex(
            Skeletons,
            SkeletalMeshes,
            StaticMeshes,
            Animations,
            Montages,
            BlendSpaces,
            AimOffsets,
            Materials,
            Textures,
            PhysicsAssets,
            Curves,
            ConfigAssets);
        return new AlsAnimationSetDefinition(
            Skeletons,
            SkeletalMeshes,
            StaticMeshes,
            Animations,
            Montages,
            BlendSpaces,
            AimOffsets,
            Materials,
            Textures,
            PhysicsAssets,
            Curves,
            ConfigAssets,
            index,
            DefinitionDigest);
    }
}
