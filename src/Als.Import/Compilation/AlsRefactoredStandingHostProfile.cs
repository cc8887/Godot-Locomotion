using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

/// <summary>Frozen original Standing resources. Default action IDs are local;
/// BindActions supplies the character's explicit IDs without rebuilding the graph.</summary>
public sealed class AlsRefactoredStandingHostProfile
{
    internal readonly AlsRefactoredAnimationCatalog Catalog;
    internal readonly AlsRefactoredSyncBank Sync;
    internal readonly IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles;
    internal readonly AlsRefactoredStandingResources Standing;
    internal readonly AlsRefactoredMovementDetailsPoseGraph Details;
    internal readonly AlsRefactoredDirectionSourceProfile Direction;
    internal readonly AlsRefactoredDirectionPose DirectionPose;
    internal readonly AlsRefactoredMovementCacheProfile Movement;
    internal readonly AlsRefactoredMovementSettings MovementSettings;
    internal readonly AlsRefactoredStopPoseGraph Stop;
    internal readonly AlsRefactoredStandingRestGraph RestGraph;
    internal readonly AlsRefactoredStanceCallbacks Callbacks;
    internal readonly AlsRefactoredRestMontages Montages;
    internal readonly AlsRefactoredRestMontagePose MontagePose;
    internal readonly AlsRefactoredStandingActions Actions;
    internal readonly AlsRefactoredQuickStop QuickStop;
    internal readonly AlsRefactoredPivotNotify PivotNotify;
    internal readonly AlsSequenceMontageAsset[] Assets;
    internal readonly string SlotInventory, QuickStopSettings;
    public AlsRefactoredStandingPose Pose { get; }
    public string CatalogDigest => Catalog.IndexDigest;
    public ReadOnlySpan<AlsSequenceMontageAsset> ActionAssets => Assets;
    public string ActionSource(int id)
    {
        foreach (var asset in Montages.Assets) if (asset.AnimationId == id) return Montages.SourcePath(id);
        foreach (var asset in Actions.Assets) if (asset.AnimationId == id) return Actions.SourcePath(id);
        return QuickStop.SourcePath(id);
    }

    public AlsRefactoredStandingHostProfile(AlsRefactoredAnimationCatalog catalog, string machines,
        AlsRefactoredSyncBank sync, IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> triangles,
        AlsRefactoredMovementSettings movementSettings, AlsRefactoredRestSettings restSettings,
        AlsRefactoredSkeletonCurves metadata, string slotInventory, string quickStopSettings)
    {
        Catalog = catalog; Sync = sync; Triangles = new Dictionary<string, AlsRefactoredTriangulationProfile>(triangles);
        SlotInventory = slotInventory; QuickStopSettings = quickStopSettings;
        MovementSettings = movementSettings;
        if (catalog.IndexDigest != sync.CatalogDigest || catalog.IndexDigest != movementSettings.CatalogDigest)
            throw new ArgumentException("Foreign Standing host resources.");
        Standing = new(machines, catalog); Details = new(catalog, new(machines, catalog));
        Direction = new(catalog, new(catalog, new(machines, catalog, false)));
        PivotNotify = new(catalog, Direction.Graph.Resources);
        DirectionPose = new(catalog, Direction, Triangles); Movement = new(catalog, Triangles);
        Stop = new(catalog, new(machines, catalog)); RestGraph = new(catalog); Callbacks = new(catalog, false);
        var paths = restSettings.Turns.ToArray().Select(t => t.Sequence)
            .Concat(Enumerable.Range(0, 4).Select(i => restSettings.DynamicSequence(i >= 2, i % 2 == 0))).ToArray();
        Montages = new(catalog, restSettings, slotInventory, paths.Select((path, id) => (path, id)).ToDictionary(v => v.path, v => v.id), 0);
        Actions = new(catalog, Standing, Stop.Resources, Montages, 12, 13);
        using var quick = JsonDocument.Parse(quickStopSettings);
        string[] keys = ["standing_left", "standing_right", "crouching_left", "crouching_right"];
        var ids = keys.Select((key, i) => (path: quick.RootElement.GetProperty("sequences").GetProperty(key).GetString()!, id: 14 + i))
            .ToDictionary(v => v.path, v => v.id);
        QuickStop = new(quickStopSettings, catalog, Standing, Montages, ids);
        Assets = Montages.Assets.ToArray().Concat(Actions.Assets.ToArray()).Concat(QuickStop.Assets.ToArray()).ToArray();
        var initialRest = new AlsRefactoredStandingRestPose(catalog, RestGraph, metadata, []);
        MontagePose = new(catalog, Montages, initialRest.CurveNames);
        var rest = new AlsRefactoredStandingRestPose(catalog, RestGraph, metadata, MontagePose.CurveNames.ToArray());
        // This temporary owner only determines the frozen curve union; no frame runs here.
        var envelope = new AlsRefactoredMovementCacheRuntime(Movement, DirectionPose.BoneNames, DirectionPose.CurveNames);
        var details = new AlsRefactoredMovementDetailsPose(catalog, Details, Movement, envelope.CurveNames);
        Pose = new(Standing, rest, details, new(catalog, Stop, metadata, details.CurveNames));
    }
    public AlsRefactoredStandingHost CreateRuntime(uint character, uint generation) => new(this, character, generation);

    /// <summary>Share immutable graph resources while binding action IDs to the character bank.</summary>
    public AlsRefactoredStandingHostProfile BindActions(IReadOnlyDictionary<string, int> ids, int groupId)
        => new(this, ids, groupId);

    private AlsRefactoredStandingHostProfile(AlsRefactoredStandingHostProfile source,
        IReadOnlyDictionary<string, int> ids, int groupId)
    {
        var paths = source.Assets.Select(a => source.ActionSource(a.AnimationId)).ToArray();
        if (groupId < 0 || ids.Count != paths.Length || paths.Any(p => !ids.TryGetValue(p, out var id) || id < 0) ||
            ids.Values.Distinct().Count() != ids.Count) throw new ArgumentException("Invalid Standing character action IDs.");
        Catalog = source.Catalog; Sync = source.Sync; Triangles = source.Triangles; Standing = source.Standing;
        Details = source.Details; Direction = source.Direction; DirectionPose = source.DirectionPose; Movement = source.Movement;
        PivotNotify = source.PivotNotify;
        MovementSettings = source.MovementSettings; Stop = source.Stop; RestGraph = source.RestGraph; Callbacks = source.Callbacks;
        SlotInventory = source.SlotInventory; QuickStopSettings = source.QuickStopSettings; Pose = source.Pose;
        Dictionary<string, int> Map(ReadOnlySpan<AlsSequenceMontageAsset> assets) =>
            assets.ToArray().Select(a => source.ActionSource(a.AnimationId)).ToDictionary(p => p, p => ids[p]);
        Montages = new(Catalog, source.Montages.Settings, SlotInventory, Map(source.Montages.Assets), groupId);
        Actions = new(Catalog, Standing, Stop.Resources, Montages,
            ids[source.Actions.SourcePath(source.Actions.Assets[0].AnimationId)], ids[source.Actions.SourcePath(source.Actions.Assets[1].AnimationId)]);
        QuickStop = new(QuickStopSettings, Catalog, Standing, Montages, Map(source.QuickStop.Assets));
        Assets = Montages.Assets.ToArray().Concat(Actions.Assets.ToArray()).Concat(QuickStop.Assets.ToArray()).ToArray();
        MontagePose = new(Catalog, Montages, source.MontagePose.CurveNames);
    }
}
