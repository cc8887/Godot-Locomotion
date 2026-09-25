using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

/// <summary>Frozen resources for the original Standing graph. Playback IDs are
/// local to this host's Grounded montage bank, not global character asset IDs.</summary>
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
    internal readonly AlsSequenceMontageAsset[] Assets;
    public AlsRefactoredStandingPose Pose { get; }
    public string CatalogDigest => Catalog.IndexDigest;

    public AlsRefactoredStandingHostProfile(AlsRefactoredAnimationCatalog catalog, string machines,
        AlsRefactoredSyncBank sync, IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> triangles,
        AlsRefactoredMovementSettings movementSettings, AlsRefactoredRestSettings restSettings,
        AlsRefactoredSkeletonCurves metadata, string slotInventory, string quickStopSettings)
    {
        Catalog = catalog; Sync = sync; Triangles = new Dictionary<string, AlsRefactoredTriangulationProfile>(triangles);
        MovementSettings = movementSettings;
        if (catalog.IndexDigest != sync.CatalogDigest || catalog.IndexDigest != movementSettings.CatalogDigest)
            throw new ArgumentException("Foreign Standing host resources.");
        Standing = new(machines, catalog); Details = new(catalog, new(machines, catalog));
        Direction = new(catalog, new(catalog, new(machines, catalog, false)));
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
}
