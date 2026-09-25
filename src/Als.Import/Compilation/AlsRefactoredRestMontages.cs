using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredStandingSlotInertialization
{
    public AlsFrameIdentity Identity { get; }
    public string CatalogDigest { get; }
    public AlsOverlayInertialRequest Request { get; }
    internal AlsRefactoredStandingSlotInertialization(AlsFrameIdentity identity,string digest,AlsOverlayInertialRequest request)
    { Identity=identity;CatalogDigest=digest;Request=request; }
}

/// <summary>All original rest sequences in the shared Grounded montage group.
/// Host IDs are reserved externally so weapon transitions can share this group.</summary>
public sealed class AlsRefactoredRestMontages
{
    public AlsRefactoredRestSettings Settings { get; }
    public int HostGroupId { get; }
    public int NativeGroupIndex { get; }
    private readonly Dictionary<string,AlsSequenceMontageAsset> _sources = new(StringComparer.Ordinal);
    private readonly AlsSequenceMontageAsset[] _assets;
    public ReadOnlySpan<AlsSequenceMontageAsset> Assets => _assets;
    public string SourcePath(int animationId) => _sources.Single(s => s.Value.AnimationId == animationId).Key;
    public AlsRefactoredRestMontages(AlsRefactoredAnimationCatalog catalog, AlsRefactoredRestSettings settings,
        string inventoryJson, IReadOnlyDictionary<string,int> animationIds, int hostGroupId)
    {
        if (catalog.IndexDigest != settings.CatalogDigest || hostGroupId < 0) throw new ArgumentException("Foreign rest montage resources.");
        Settings = settings; HostGroupId = hostGroupId;
        using var document = JsonDocument.Parse(inventoryJson); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("searchRoot").GetString() != "/ALS") throw new ArgumentException("Foreign Slot inventory.");
        const string skeleton = "/ALS/ALS/Character/SK_Als.SK_Als";
        var meta = root.GetProperty("skeletons").GetProperty(skeleton); var groups = meta.GetProperty("groups").EnumerateArray().ToArray();
        var group = groups.Single(g => g.GetProperty("name").GetString() == "Grounded");
        string[] names = ["Transition","TurnInPlaceStanding","TurnInPlaceCrouching"];
        NativeGroupIndex = group.GetProperty("index").GetInt32();
        var native = Regex.Match(meta.GetProperty("nativeText").GetString()!,@"(?m)^\s+SlotGroups\("+NativeGroupIndex+"\\)=\\(GroupName=\"Grounded\",SlotNames=\\(([^\\r\\n]*)\\)\\)\\r?$");
        if (!group.GetProperty("slots").EnumerateArray().Select(s => s.GetString()).SequenceEqual(names) || !native.Success ||
            !Regex.Matches(native.Groups[1].Value,"\"([^\"]+)\"").Select(m => m.Groups[1].Value).SequenceEqual(names) ||
            names.Any(n => groups.Sum(g => g.GetProperty("slots").EnumerateArray().Count(s => s.GetString() == n)) != 1))
            throw new ArgumentException("Original Grounded Slot group differs.");
        var paths = settings.Turns.ToArray().Select(t => t.Sequence).Concat(Enumerable.Range(0,4).Select(i => settings.DynamicSequence(i >= 2,i%2 == 0))).ToArray();
        if (paths.Distinct().Count() != 12 || animationIds.Count != 12 || paths.Any(p => !animationIds.TryGetValue(p,out var id) || id < 0) || animationIds.Values.Distinct().Count() != 12)
            throw new ArgumentException("Incomplete rest montage IDs.");
        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i]; var source = catalog.Read(path); var evaluation = source.GetProperty("evaluation"); var additive = i < 8 ? 0 : 2;
            if (source.GetProperty("raw").GetProperty("skeletonSource").GetString() != skeleton || evaluation.GetProperty("enableRootMotion").GetBoolean() ||
                evaluation.GetProperty("additiveType").GetString() != (additive == 0 ? "AAT_None" : "AAT_RotationOffsetMeshSpace")) throw new ArgumentException("Rest montage sequence policy differs.");
            var scale = Regex.Match(source.GetProperty("nativeText").GetString()!,@"(?m)^   RateScale=([^\r\n]+)");
            if (scale.Success && float.Parse(scale.Groups[1].Value,CultureInfo.InvariantCulture) != 1) throw new ArgumentException("Rest sequence requires explicit rate-scale support.");
            AlsMontageSlot slot = i < 4 ? AlsTurnSlot.Standing : i < 8 ? AlsTurnSlot.Crouching : AlsMontageSlot.Transition;
            _sources.Add(path,new(animationIds[path],slot,hostGroupId,evaluation.GetProperty("sequencePlayLength").GetSingle(),additive));
        }
        _assets = paths.Select(p => _sources[p]).ToArray(); _ = new AlsMontageRuntime([],sequences:_assets);
    }
    private AlsSequenceMontageCommand Command(AlsRefactoredRestPlayback request)
    {
        if (!_sources.TryGetValue(request.Sequence,out var asset)) throw new ArgumentException("Foreign rest playback sequence.");
        var expected = asset.Slot.Id == 0 ? "TurnInPlaceStanding" : asset.Slot.Id == 1 ? "TurnInPlaceCrouching" : "Transition";
        if (request.Slot != expected || request.InertialBlendOut != (asset.Slot.Id < 2)) throw new ArgumentException("Foreign rest playback Slot/blend mode.");
        return new(asset.AnimationId,asset.Slot,request.PlayRate,request.StartTime,request.BlendIn,request.BlendOut) {InertialBlendOut = request.InertialBlendOut};
    }
    public void PlayQueued(AlsMontageRuntime bank, AlsRefactoredRestParentRuntime parent, in AlsFrameIdentity identity, bool stopTransitionsQueued = false)
    {
        bank.ValidateCommit(identity); parent.ValidateContext(identity,Settings.CatalogDigest);
        if (!ReferenceEquals(parent.Settings,Settings)) throw new ArgumentException("Foreign rest Parent settings.");
        if (stopTransitionsQueued) return;
        var frame = identity.FrameId; var transition = parent.Candidate.QueuedTransition; var turn = parent.Candidate.QueuedTurn;
        // Validate both original queues before changing either owner.
        Validate(transition); Validate(turn);
        void Validate(AlsRefactoredRestPlayback? request)
        {
            if(request is null)return;
            var command = Command(request); var expected = _sources[request.Sequence];
            if (!bank.TryGetSequenceAsset(command.AnimationId,command.Slot,out var actual) || actual != expected) throw new ArgumentException("Shared bank omitted rest resources.");
        }
        Play(transition,false); Play(turn,true); // NativePostUpdateAnimation order.
        void Play(AlsRefactoredRestPlayback? request,bool isTurn)
        {
            if (request is null) return;
            if (!bank.PlaySequence(Command(request))) throw new InvalidOperationException("Validated rest montage disappeared.");
            parent.AcceptPlayback(frame,request,isTurn);
        }
    }
    public void Stop(AlsMontageRuntime bank, in AlsFrameIdentity identity, float duration = -1)
    { bank.ValidateCommit(identity);bank.StopSlots([AlsMontageSlot.Transition,AlsTurnSlot.Standing,AlsTurnSlot.Crouching],duration); }

    public AlsRefactoredStandingSlotInertialization? StandingSlotRequest(AlsMontageFrame frame,in AlsFrameIdentity identity,AlsRefactoredStandingRestTraversal traversal)
    {
        if(frame.Identity!=identity)throw new ArgumentException("Stale rest montage frame.");
        if(!traversal.IdleUpdated(identity,Settings.CatalogDigest)||!frame.TryGetInertializationRequest(HostGroupId,out var request))return null;
        var blend=request.BlendOption switch
        {
            AlsActionBlendOption.Linear=>AlsTransitionBlend.Linear,
            AlsActionBlendOption.HermiteCubic=>AlsTransitionBlend.HermiteCubic,
            _=>throw new ArgumentException("Unsupported rest Slot inertia blend option.")
        };
        return new(identity,Settings.CatalogDigest,new(60,request.Duration,request.UseBlendMode,blend));
    }
}
