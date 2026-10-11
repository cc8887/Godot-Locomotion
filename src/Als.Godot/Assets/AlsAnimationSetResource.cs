using Godot;
using GodotAls.Core.Diagnostics;
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

    [Export] public int EntryCount { get; set; }

    [Export] public int ManifestAssetCount { get; set; }

    [Export] public int ManifestFileCount { get; set; }

    public AlsAnimationSetDefinition LoadDefinition()
    {
        using var trace = AlsPerformanceTrace.Begin("animation_set.definition");
        string payloadHash;
        using (var hash = AlsPerformanceTrace.Begin("animation_set.payload_hash"))
        {
            payloadHash = AlsAnimationSetPayload.ComputeSha256(DefinitionJson);
            hash?.Complete();
        }
        if (!string.Equals(payloadHash, DefinitionPayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ALS animation-set payload hash mismatch: expected={DefinitionPayloadSha256} actual={payloadHash}");
        }

        AlsAnimationSetDefinition definition;
        using (var deserialize = AlsPerformanceTrace.Begin("animation_set.deserialize"))
        {
            definition = AlsAnimationSetPayload.Deserialize(DefinitionJson);
            deserialize?.Complete(EntryCount);
        }
        if (!string.Equals(definition.DefinitionDigest, DefinitionDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ALS animation-set payload digest mismatch: resource={DefinitionDigest} payload={definition.DefinitionDigest}");
        }

        trace?.Complete(EntryCount);
        return definition;
    }
}
