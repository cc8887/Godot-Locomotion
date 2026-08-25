using Godot;
using GodotAls.Import.Compilation;

namespace GodotAls.Assets;

[GlobalClass]
public partial class AlsAnimationSetResource : Resource
{
    [Export] public int SchemaVersion { get; set; }

    [Export] public string ExporterVersion { get; set; } = string.Empty;

    [Export] public string SourceEngineVersion { get; set; } = string.Empty;

    [Export] public string SourceProjectId { get; set; } = string.Empty;

    [Export] public string DefinitionDigest { get; set; } = string.Empty;

    [Export(PropertyHint.MultilineText)] public string DefinitionJson { get; set; } = string.Empty;

    [Export] public string DefinitionPayloadSha256 { get; set; } = string.Empty;

    [Export] public Godot.Collections.Array<AlsAssetResourceEntry> Entries { get; set; } = [];

    [Export] public int ManifestAssetCount { get; set; }

    [Export] public int ManifestFileCount { get; set; }

    public AlsAnimationSetDefinition LoadDefinition()
    {
        var payloadHash = AlsAnimationSetPayload.ComputeSha256(DefinitionJson);
        if (!string.Equals(payloadHash, DefinitionPayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ALS animation-set payload hash mismatch: expected={DefinitionPayloadSha256} actual={payloadHash}");
        }

        var definition = AlsAnimationSetPayload.Deserialize(DefinitionJson);
        if (!string.Equals(definition.DefinitionDigest, DefinitionDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ALS animation-set payload digest mismatch: resource={DefinitionDigest} payload={definition.DefinitionDigest}");
        }

        return definition;
    }
}
