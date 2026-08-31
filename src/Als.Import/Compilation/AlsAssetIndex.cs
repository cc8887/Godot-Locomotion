namespace GodotAls.Import.Compilation;

public sealed class AlsAssetIndex
{
    private readonly Dictionary<string, int> _skeletonIds;
    private readonly Dictionary<string, int> _skeletalMeshIds;
    private readonly Dictionary<string, int> _staticMeshIds;
    private readonly Dictionary<string, int> _animationIds;
    private readonly Dictionary<string, int> _montageIds;
    private readonly Dictionary<string, int> _blendSpaceIds;
    private readonly Dictionary<string, int> _aimOffsetIds;
    private readonly Dictionary<string, int> _materialIds;
    private readonly Dictionary<string, int> _textureIds;
    private readonly Dictionary<string, int> _physicsAssetIds;
    private readonly Dictionary<string, int> _curveIds;
    private readonly Dictionary<string, int> _configAssetIds;
    private readonly Dictionary<string, int> _eventIds;
    private readonly Dictionary<string, int> _markerIds;

    internal AlsAssetIndex(
        AlsSkeletonDefinition[] skeletons,
        AlsSkeletalMeshDefinition[] skeletalMeshes,
        AlsStaticMeshDefinition[] staticMeshes,
        AlsAnimationDefinition[] animations,
        AlsMontageDefinition[] montages,
        AlsBlendDefinition[] blendSpaces,
        AlsBlendDefinition[] aimOffsets,
        AlsMaterialDefinition[] materials,
        AlsTextureDefinition[] textures,
        AlsPhysicsAssetDefinition[] physicsAssets,
        AlsGenericAssetDefinition[] curves,
        AlsGenericAssetDefinition[] configAssets)
    {
        _skeletonIds = CreateMap(skeletons, value => value.AssetId);
        _skeletalMeshIds = CreateMap(skeletalMeshes, value => value.StableId);
        _staticMeshIds = CreateMap(staticMeshes, value => value.StableId);
        _animationIds = CreateMap(animations, value => value.StableId);
        _montageIds = CreateMap(montages, value => value.StableId);
        _blendSpaceIds = CreateMap(blendSpaces, value => value.StableId);
        _aimOffsetIds = CreateMap(aimOffsets, value => value.StableId);
        _materialIds = CreateMap(materials, value => value.StableId);
        _textureIds = CreateMap(textures, value => value.StableId);
        _physicsAssetIds = CreateMap(physicsAssets, value => value.StableId);
        _curveIds = CreateMap(curves, value => value.StableId);
        _configAssetIds = CreateMap(configAssets, value => value.StableId);
        _eventIds = animations.SelectMany(value => value.Timeline)
            .Concat(montages.SelectMany(value => value.Timeline))
            .ToDictionary(value => value.StableEventId, value => value.EventId, StringComparer.Ordinal);
        _markerIds = animations.SelectMany(value => value.SyncMarkers)
            .ToDictionary(value => value.StableMarkerId, value => value.MarkerId, StringComparer.Ordinal);
    }

    public int GetSkeletonId(string stableId) => GetRequired(_skeletonIds, stableId, "skeleton");

    public int GetAnimationId(string stableId) => GetRequired(_animationIds, stableId, "animation");

    public int GetSkeletalMeshId(string stableId) => GetRequired(_skeletalMeshIds, stableId, "skeletal mesh");

    public int GetStaticMeshId(string stableId) => GetRequired(_staticMeshIds, stableId, "static mesh");

    public int GetMontageId(string stableId) => GetRequired(_montageIds, stableId, "montage");

    public int GetBlendSpaceId(string stableId) => GetRequired(_blendSpaceIds, stableId, "blend space");

    public int GetAimOffsetId(string stableId) => GetRequired(_aimOffsetIds, stableId, "aim offset");

    public int GetMaterialId(string stableId) => GetRequired(_materialIds, stableId, "material");

    public int GetTextureId(string stableId) => GetRequired(_textureIds, stableId, "texture");

    public int GetPhysicsAssetId(string stableId) => GetRequired(_physicsAssetIds, stableId, "physics asset");

    public int GetCurveId(string stableId) => GetRequired(_curveIds, stableId, "curve");

    public int GetConfigAssetId(string stableId) => GetRequired(_configAssetIds, stableId, "config asset");

    public int GetEventId(string stableId) => GetRequired(_eventIds, stableId, "timeline event");

    public int GetMarkerId(string stableId) => GetRequired(_markerIds, stableId, "sync marker");

    internal bool TryGetSkeletonId(string stableId, out int id) => _skeletonIds.TryGetValue(stableId, out id);

    internal bool TryGetAnimationId(string stableId, out int id) => _animationIds.TryGetValue(stableId, out id);

    private static Dictionary<string, int> CreateMap<T>(IReadOnlyList<T> values, Func<T, string> getStableId) =>
        values.Select((value, index) => (StableId: getStableId(value), Index: index))
            .ToDictionary(value => value.StableId, value => value.Index, StringComparer.Ordinal);

    private static int GetRequired(Dictionary<string, int> ids, string stableId, string kind) =>
        ids.TryGetValue(stableId, out var id)
            ? id
            : throw new KeyNotFoundException($"Unknown ALS {kind} stable ID '{stableId}'.");
}
