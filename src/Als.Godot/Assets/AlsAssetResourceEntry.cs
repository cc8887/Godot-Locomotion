using Godot;

namespace GodotAls.Assets;

[GlobalClass]
public partial class AlsAssetResourceEntry : Resource
{
    [Export] public string Section { get; set; } = string.Empty;

    [Export] public string StableId { get; set; } = string.Empty;

    [Export] public int IntegerId { get; set; }

    [Export] public string GodotResourcePath { get; set; } = string.Empty;

    [Export] public string SourceObjectPath { get; set; } = string.Empty;

    [Export] public int SkeletonId { get; set; } = -1;

    [Export] public string SkeletonHash { get; set; } = string.Empty;

    [Export] public int SemanticCount { get; set; }

    [Export] public bool Overlay { get; set; }

    [Export] public bool Prop { get; set; }
}
