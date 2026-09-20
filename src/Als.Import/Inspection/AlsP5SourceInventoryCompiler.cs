using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Inspection;

public enum AlsP5InventoryNodeKind : byte { Structural, AssetPlayer, CacheRead, CacheWrite, Slot, PoseSnapshot }

public readonly record struct AlsP5InventoryNode(string Path, string NodeClass, int CompiledNodeIndex, int PropertyIndex, int CacheSourcePropertyIndex,
    AlsP5InventoryNodeKind Kind, int LocomotionPlayerId, bool Evaluator, bool Teleport, bool Loop,
    string SyncGroup, int SampleStart, int SampleCount);

public readonly record struct AlsP5InventorySample(int NodeIndex, int SourceIndex, int AnimationId,
    int LocomotionSampleId);

/// <summary>Native graph coverage, not a physical Godot occurrence allocation or a runtime clock.</summary>
public sealed class AlsP5SourceInventory
{
    private readonly AlsP5InventoryNode[] _nodes;
    private readonly AlsP5InventorySample[] _samples;
    public AlsP5InventoryNode[] Nodes => _nodes.ToArray();
    public AlsP5InventorySample[] Samples => _samples.ToArray();
    public string Digest { get; }
    public AlsLocomotionSourceStamp SourceStamp { get; }
    public string AnimationSetDefinitionDigest { get; }

    internal AlsP5SourceInventory(AlsP5InventoryNode[] nodes, AlsP5InventorySample[] samples, string digest,
        AlsLocomotionSourceStamp sourceStamp, string definitionDigest)
    {
        _nodes = nodes.ToArray(); _samples = samples.ToArray(); Digest = digest;
        SourceStamp = sourceStamp; AnimationSetDefinitionDigest = definitionDigest;
    }
}

public static class AlsP5SourceInventoryCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";

    public static AlsP5SourceInventory Compile(string json, AlsAnimationSetDefinition set, AlsLocomotionSourceProfile sources)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(sources);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("inventorySchemaVersion").GetInt32() != 1 || root.GetProperty("source").GetString() != Blueprint)
            throw new ArgumentException("Unsupported native graph inventory.");
        var animations = set.Animations.ToDictionary(a => a.ObjectPath, StringComparer.Ordinal);
        var blends = set.BlendSpaces.Concat(set.AimOffsets).ToDictionary(b => b.ObjectPath, StringComparer.Ordinal);
        var players = sources.Players.ToDictionary(p => p.SourceNode, StringComparer.Ordinal);
        var sourceSamples = sources.Samples;
        var sourceGroups = sources.SyncGroups;
        var propertyCount = root.GetProperty("compiledPropertyCount").GetInt32();
        if (propertyCount <= 0) throw new ArgumentException("Empty native compiled property table.");
        var nodes = new List<AlsP5InventoryNode>();
        var samples = new List<AlsP5InventorySample>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var indices = new HashSet<int>();
        var covered = new HashSet<int>();
        foreach (var entry in root.GetProperty("compiledNodeInventory").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!;
            var nodeClass = entry.GetProperty("class").GetString()!;
            var index = entry.GetProperty("compiledNodeIndex").GetInt32();
            var propertyIndex = entry.GetProperty("propertyIndex").GetInt32();
            var cacheSource = entry.GetProperty("cacheSourcePropertyIndex").GetInt32();
            if (!path.StartsWith(Blueprint + ":", StringComparison.Ordinal) || !paths.Add(path) ||
                !nodeClass.StartsWith("AnimGraphNode_", StringComparison.Ordinal) || index < -1 || index >= propertyCount ||
                propertyIndex != (index < 0 ? -1 : propertyCount - 1 - index) || index >= 0 && !indices.Add(index))
                throw new ArgumentException("Invalid or duplicate native graph node identity.");
            var isAsset = entry.GetProperty("assetPlayer").GetBoolean();
            var kind = nodeClass switch
            {
                "AnimGraphNode_UseCachedPose" => AlsP5InventoryNodeKind.CacheRead,
                "AnimGraphNode_SaveCachedPose" => AlsP5InventoryNodeKind.CacheWrite,
                "AnimGraphNode_Slot" => AlsP5InventoryNodeKind.Slot,
                "AnimGraphNode_PoseSnapshot" => AlsP5InventoryNodeKind.PoseSnapshot,
                _ => isAsset ? AlsP5InventoryNodeKind.AssetPlayer : AlsP5InventoryNodeKind.Structural
            };
            var knownAsset = nodeClass is "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator" or
                "AnimGraphNode_BlendSpacePlayer" or "AnimGraphNode_BlendSpaceEvaluator";
            if (index >= 0 && knownAsset != isAsset || isAsset && (index < 0 || kind != AlsP5InventoryNodeKind.AssetPlayer))
                throw new ArgumentException("Unclassified or inconsistent native asset player.");
            var start = samples.Count;
            var evaluator = false;
            var teleport = false;
            var loop = false;
            var group = "None";
            players.TryGetValue(path, out var source);
            if (isAsset)
            {
                evaluator = entry.GetProperty("evaluator").GetBoolean();
                teleport = entry.GetProperty("teleport").GetBoolean();
                loop = entry.GetProperty("loop").GetBoolean();
                group = entry.GetProperty("groupName").GetString()!;
                if (evaluator != nodeClass.EndsWith("Evaluator", StringComparison.Ordinal) || teleport && !evaluator || string.IsNullOrWhiteSpace(group))
                    throw new ArgumentException("Invalid native evaluator or synchronization metadata.");
                var asset = entry.GetProperty("asset").GetString()!;
                var samplePaths = entry.GetProperty("samples").EnumerateArray().Select(s => s.GetString()!).ToArray();
                // This ALS snapshot has no dynamic/empty asset defaults. Do not quietly mark an unresolved leaf as covered.
                if (samplePaths.Length == 0 || string.IsNullOrWhiteSpace(asset))
                    throw new ArgumentException("Native asset source requires explicit static sample coverage.");
                if (nodeClass.Contains("BlendSpace", StringComparison.Ordinal))
                {
                    if (!blends.TryGetValue(asset, out var blend) ||
                        !blend.Samples.Select(s => set.Animations[s.AnimationId].ObjectPath).SequenceEqual(samplePaths))
                        throw new ArgumentException("Native BlendSpace samples disagree with the imported asset.");
                }
                else if (samplePaths.Length != 1 || samplePaths[0] != asset)
                    throw new ArgumentException("Sequence player has inconsistent sample ownership.");
                for (var sampleIndex = 0; sampleIndex < samplePaths.Length; sampleIndex++)
                {
                    if (!animations.TryGetValue(samplePaths[sampleIndex], out var animation) || animation.SkeletonId != sources.SkeletonId)
                        throw new ArgumentException("Native sample is missing or uses a different skeleton.");
                    var sourceId = -1;
                    if (source is not null)
                    {
                        if (sampleIndex >= source.SampleCount) throw new ArgumentException("Source sample range differs from native inventory.");
                        var binding = sourceSamples[source.SampleStart + sampleIndex];
                        if (binding.AnimationId != animation.Id || binding.SourceIndex != sampleIndex)
                            throw new ArgumentException("Source sample identity differs from native inventory.");
                        sourceId = binding.SampleId;
                    }
                    samples.Add(new(nodes.Count, sampleIndex, animation.Id, sourceId));
                }
            }
            if (source is not null)
            {
                if (!isAsset || source.CompiledNodeIndex != index || source.SampleCount != samples.Count - start ||
                    (source.Kind == AlsLocomotionSourceKind.TeleportEvaluator) != (evaluator && teleport) ||
                    (source.SyncGroupId < 0 ? "None" : sourceGroups[source.SyncGroupId]) != group ||
                    source.Loop != loop || !covered.Add(source.PlayerId))
                    throw new ArgumentException($"Compiled locomotion player disagrees with native node {path}: " +
                        $"index={index}/{source.CompiledNodeIndex}, samples={samples.Count - start}/{source.SampleCount}, " +
                        $"evaluator={evaluator}, teleport={teleport}, kind={source.Kind}, group={group}, " +
                        $"loop={entry.GetProperty("loop").GetBoolean()}/{source.Loop}.");
            }
            nodes.Add(new(path, nodeClass, index, propertyIndex, cacheSource, kind, source?.PlayerId ?? -1,
                evaluator, teleport, loop, group, start, samples.Count - start));
        }
        if (nodes.Count == 0 || covered.Count != players.Count || indices.Count != propertyCount)
            throw new ArgumentException("Native inventory omitted a compiled node or bound locomotion player.");
        var properties = nodes.Where(n => n.PropertyIndex >= 0).ToDictionary(n => n.PropertyIndex);
        foreach (var node in nodes)
        {
            if (node.Kind == AlsP5InventoryNodeKind.CacheRead && node.CompiledNodeIndex >= 0)
            {
                if (!properties.TryGetValue(node.CacheSourcePropertyIndex, out var target) || target.Kind != AlsP5InventoryNodeKind.CacheWrite)
                    throw new ArgumentException("Native cache read does not resolve to its compiled cache producer.");
            }
            else if (node.CacheSourcePropertyIndex != -1)
                throw new ArgumentException("Non-cache node has a cache reference.");
        }
        var nodeArray = nodes.ToArray(); var sampleArray = samples.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, NativeDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            set.DefinitionDigest, sources.Digest, Nodes = nodeArray, Samples = sampleArray
        }))).ToLowerInvariant();
        return new(nodeArray, sampleArray, digest, sources.RuntimeStamp, set.DefinitionDigest);
    }
}
