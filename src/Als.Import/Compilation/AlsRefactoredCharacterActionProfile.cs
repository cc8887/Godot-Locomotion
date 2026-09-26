using System.Collections.ObjectModel;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Character Grounded action resources shared by Standing and weapon producers.
/// Authored Mantle/Roll actions and the complete locomotion graph remain separate bindings.</summary>
public sealed class AlsRefactoredCharacterActionProfile
{
    private readonly AlsSequenceMontageAsset[] _assets;
    private readonly Dictionary<int, string> _paths;
    internal readonly AlsRefactoredRestMontagePose Samples;
    internal readonly AlsPrecisePose[] Reference;
    public AlsRefactoredStandingHostProfile Standing { get; }
    public AlsRefactoredCrouchingHostProfile? Crouching { get; }
    public AlsRefactoredTransitionMontages Weapons { get; }
    public AlsRefactoredTransitionSlotGraph Transition { get; }
    public IReadOnlyDictionary<string, int> AnimationIds { get; }
    public ReadOnlySpan<AlsSequenceMontageAsset> Assets => _assets;
    public ReadOnlySpan<string> BoneNames => Samples.BoneNames;
    public ReadOnlySpan<int> Parents => Samples.Parents;
    public ReadOnlySpan<string> CurveNames => Samples.CurveNames;
    public string CatalogDigest => Standing.CatalogDigest;
    public string SourcePath(int id) => _paths.TryGetValue(id, out var path) ? path : throw new ArgumentException("Unknown character action ID.");

    public AlsRefactoredCharacterActionProfile(AlsRefactoredStandingHostProfile standing,
        IReadOnlyList<AlsRefactoredWeaponNotifyProfile> weapons, IReadOnlyDictionary<string, int> animationIds, int groundedGroupId,
        string? stanceMachines = null, AlsRefactoredSkeletonCurves? metadata = null)
    {
        var standingPaths = standing.ActionAssets.ToArray().Select(a => standing.ActionSource(a.AnimationId)).ToArray();
        var weaponPaths = weapons.SelectMany(w => w.Bindings.ToArray()).Select(b => b.Sequence).Distinct(StringComparer.Ordinal).ToArray();
        var paths = standingPaths.Concat(weaponPaths).Distinct(StringComparer.Ordinal).ToArray();
        if (animationIds.Count != paths.Length || paths.Any(p => !animationIds.TryGetValue(p, out var id) || id < 0) ||
            animationIds.Values.Distinct().Count() != paths.Length) throw new ArgumentException("Incomplete or aliased character action IDs.");
        var ids = paths.ToDictionary(p => p, p => animationIds[p], StringComparer.Ordinal);
        AnimationIds = new ReadOnlyDictionary<string, int>(ids); _paths = ids.ToDictionary(p => p.Value, p => p.Key);
        Standing = standing.BindActions(standingPaths.ToDictionary(p => p, p => ids[p]), groundedGroupId);
        if(stanceMachines is not null) Crouching=new(Standing,stanceMachines,metadata??throw new ArgumentException("Crouching requires skeleton metadata."));
        Weapons = new(standing.Catalog, standing.SlotInventory, weapons, weaponPaths.ToDictionary(p => p, p => ids[p]), groundedGroupId);
        _assets = Standing.ActionAssets.ToArray().Concat(Weapons.Assets.ToArray()).Distinct().OrderBy(a => a.AnimationId).ToArray();
        if (_assets.Select(a => a.AnimationId).Distinct().Count() != _assets.Length)
            throw new ArgumentException("Action producers disagree about a shared sequence policy.");
        Transition = new(standing.Catalog);
        Samples = new(standing.Catalog, CatalogDigest, _assets, SourcePath,
            Standing.Pose.CurveNames.ToArray().Concat(Crouching?.CurveNames.ToArray()??[]).Append("PoseStanding").Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Reference = standing.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence).ReferencePose.ToArray();
        if (!BoneNames.SequenceEqual(standing.Pose.BoneNames) || !Parents.SequenceEqual(standing.Pose.Rest.Parents))
            throw new ArgumentException("Foreign character action skeleton.");
    }
    public AlsRefactoredCharacterActionRuntime CreateRuntime(uint character, uint generation) => new(this, character, generation);
}
