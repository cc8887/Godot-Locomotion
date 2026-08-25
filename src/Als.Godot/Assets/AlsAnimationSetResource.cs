using Godot;

namespace GodotAls.Assets;

[GlobalClass]
public partial class AlsAnimationSetResource : Resource
{
    [Export] public int SchemaVersion { get; set; }

    [Export] public string ExporterVersion { get; set; } = string.Empty;

    [Export] public string SourceEngineVersion { get; set; } = string.Empty;

    [Export] public string SourceProjectId { get; set; } = string.Empty;

    [Export] public string DefinitionDigest { get; set; } = string.Empty;

    [Export] public Godot.Collections.Array<AlsAssetResourceEntry> Entries { get; set; } = [];

    [Export] public int ManifestAssetCount { get; set; }

    [Export] public int ManifestFileCount { get; set; }
}
