using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal enum LyraCardinalDirection { Forward, Left, Right, Backward }
internal enum LyraGait { Walk, Jog }

internal readonly record struct LyraLayerContext(LyraCardinalDirection Direction, LyraGait Gait,
    bool IsCrouching = false, bool IsAiming = false);

internal interface ILyraAimingLayer
{
    int AppliedFrames { get; }
    void RestoreBase();
    void Apply(float yaw, float pitch, float aimOffsetBlendWeight);
    void EvaluatePose(ReadOnlySpan<AlsLocalPose> input,
        float yaw, float pitch, float aimOffsetBlendWeight,
        Span<AlsLocalPose> output);
    void ConfigureLogical(LyraLogicalSourceBank bank);
    void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float yaw, float pitch,
        float aimOffsetBlendWeight, Span<AlsPrecisePose> output);
}

internal interface ILyraItemAnimationLayers
{
    string Name { get; }
    string AnimationClassPath { get; }
    LyraUnarmedLayerDefaults PlaybackDefaults { get; }
    string ResolveStateClip(LyraLayerHook hook, in LyraLayerContext context);
    string ResolveTurnInPlace(bool right, bool isCrouching);
    ILyraAimingLayer CreateAimingLayer(LyraBoundRig rig);
    LyraHipFirePoseLayer CreateHipFireLayer(LyraBoundRig rig);
    LyraLeftHandPoseLayer CreateLeftHandPoseLayer(LyraBoundRig rig);
    LyraFullBodyAdditivesLayer CreateFullBodyAdditivesLayer(LyraBoundRig rig);
    LyraHandRetargetPoseLayer CreateHandRetargetLayer(LyraBoundRig rig);
    LyraRightHandIkPoseLayer CreateRightHandIkLayer(LyraBoundRig rig);
}

internal sealed class LyraUnarmedAnimationLayers : ILyraItemAnimationLayers
{
    private readonly LyraLinkedLayerProfile _profile;
    private readonly Dictionary<string, string> _slotsByAsset;

    public LyraUnarmedAnimationLayers(LyraUnarmedCatalog catalog,
        LyraUnarmedAuxCatalog? auxiliary = null, LyraUnarmedRemainingCatalog? remaining = null)
    {
        _profile = LyraLinkedLayerInventory.Load().Get("unarmed");
        _slotsByAsset = catalog.Clips.Values.Concat(auxiliary?.Clips.Values ?? [])
            .Concat(remaining?.Clips.Values ?? [])
            .ToDictionary(clip => clip.SourceObjectPath, clip => clip.Slot, StringComparer.Ordinal);
    }

    public string Name => "Unarmed";
    public string AnimationClassPath => _profile.ClassPath;
    public LyraUnarmedLayerDefaults PlaybackDefaults { get; } = LyraUnarmedLayerDefaults.Load();

    public ILyraAimingLayer CreateAimingLayer(LyraBoundRig rig) =>
        LyraUnarmedAimOffset.Load(rig.Skeleton);

    public LyraHipFirePoseLayer CreateHipFireLayer(LyraBoundRig rig) =>
        new(rig, _profile, "idle", "hipfire_crouch");
    public LyraLeftHandPoseLayer CreateLeftHandPoseLayer(LyraBoundRig rig) => new(rig, _profile);
    public LyraFullBodyAdditivesLayer CreateFullBodyAdditivesLayer(LyraBoundRig rig) => new(rig);
    public LyraHandRetargetPoseLayer CreateHandRetargetLayer(LyraBoundRig rig) => new(rig, _profile);
    public LyraRightHandIkPoseLayer CreateRightHandIkLayer(LyraBoundRig rig) => new(rig, _profile);

    public string ResolveTurnInPlace(bool right, bool isCrouching)
    {
        var property = LyraLayerClipResolver.TurnProperty(right, isCrouching);
        var asset = _profile.Asset(property);
        if (asset is null || !_slotsByAsset.TryGetValue(asset, out var slot))
            throw new InvalidOperationException($"Unarmed {property} has no exported clip.");
        return slot;
    }

    public string ResolveStateClip(LyraLayerHook hook, in LyraLayerContext context)
    {
        var asset = LyraLayerClipResolver.Resolve(_profile, hook, context);
        if (asset is null || !_slotsByAsset.TryGetValue(asset, out var slot))
            throw new InvalidOperationException($"Unarmed {hook} has no exported clip for {asset ?? "null"}.");
        return slot;
    }
}

internal abstract class LyraWeaponAnimationLayers : ILyraItemAnimationLayers
{
    private readonly LyraLinkedLayerProfile _profile;
    private readonly Dictionary<string, string> _slotsByAsset;

    protected LyraWeaponAnimationLayers(string profileName, IReadOnlyDictionary<string, LyraClip> clips)
    {
        _profile = LyraLinkedLayerInventory.Load().Get(profileName);
        _slotsByAsset = clips.Values.ToDictionary(
            clip => clip.SourceObjectPath, clip => clip.Slot, StringComparer.Ordinal);
        Name = char.ToUpperInvariant(profileName[0]) + profileName[1..];
        PlaybackDefaults = LyraUnarmedLayerDefaults.LoadWeapon(profileName);
    }

    public string Name { get; }
    public string AnimationClassPath => _profile.ClassPath;
    public LyraUnarmedLayerDefaults PlaybackDefaults { get; }

    protected abstract LyraUnarmedAimOffset LoadIdleAimOffset(LyraBoundRig rig);

    public ILyraAimingLayer CreateAimingLayer(LyraBoundRig rig)
    {
        if (_profile.Asset("RelaxedAimOffset") !=
            LyraLinkedLayerInventory.Load().Get("unarmed").Asset("RelaxedAimOffset"))
            throw new InvalidOperationException(Name + " relaxed AimOffset is no longer shared with Unarmed.");
        return new LyraItemAimingLayer(LyraUnarmedAimOffset.Load(rig.Skeleton),
            LoadIdleAimOffset(rig));
    }

    public LyraHipFirePoseLayer CreateHipFireLayer(LyraBoundRig rig) => new(rig, _profile,
        Slot("Aim_HipFirePose"), Slot("Aim_HipFirePose_Crouch"));
    public LyraLeftHandPoseLayer CreateLeftHandPoseLayer(LyraBoundRig rig) => new(rig, _profile);
    public LyraFullBodyAdditivesLayer CreateFullBodyAdditivesLayer(LyraBoundRig rig) => new(rig);
    public LyraHandRetargetPoseLayer CreateHandRetargetLayer(LyraBoundRig rig) => new(rig, _profile);
    public LyraRightHandIkPoseLayer CreateRightHandIkLayer(LyraBoundRig rig) => new(rig, _profile);

    public string ResolveTurnInPlace(bool right, bool isCrouching) =>
        Slot(LyraLayerClipResolver.TurnProperty(right, isCrouching));

    public string ResolveStateClip(LyraLayerHook hook, in LyraLayerContext context)
    {
        var asset = LyraLayerClipResolver.Resolve(_profile, hook, context);
        if (asset is null || !_slotsByAsset.TryGetValue(asset, out var slot))
            throw new InvalidOperationException($"{Name} {hook} has no exported clip for {asset ?? "null"}.");
        return slot;
    }

    private string Slot(string property)
    {
        var asset = _profile.Asset(property);
        if (asset is null || !_slotsByAsset.TryGetValue(asset, out var slot))
            throw new InvalidOperationException($"{Name} {property} has no exported clip.");
        return slot;
    }
}

internal sealed class LyraPistolAnimationLayers : LyraWeaponAnimationLayers
{
    public LyraPistolAnimationLayers(LyraPistolCatalog catalog) : base("pistol", catalog.Clips) { }

    protected override LyraUnarmedAimOffset LoadIdleAimOffset(LyraBoundRig rig) =>
        LyraUnarmedAimOffset.LoadPistol(rig.Skeleton);
}

internal sealed class LyraRifleAnimationLayers : LyraWeaponAnimationLayers
{
    public LyraRifleAnimationLayers(LyraRifleCatalog catalog) : base("rifle", catalog.Clips) { }

    protected override LyraUnarmedAimOffset LoadIdleAimOffset(LyraBoundRig rig) =>
        LyraUnarmedAimOffset.LoadRifle(rig.Skeleton);
}

internal static class LyraLayerClipResolver
{
    public static string TurnProperty(bool right, bool isCrouching) =>
        (isCrouching ? "Crouch_" : "") + "TurnInPlace_" + (right ? "Right" : "Left");

    public static string? Resolve(LyraLinkedLayerProfile profile, LyraLayerHook hook,
        in LyraLayerContext context) => hook switch
    {
        LyraLayerHook.FullBody_IdleState => profile.Asset(context.IsCrouching ? "Crouch_Idle" :
            context.IsAiming ? "Idle_ADS" : "Idle_Hipfire"),
        LyraLayerHook.FullBody_StartState => profile.Cardinal(
            context.IsCrouching ? "Crouch_Start_Cardinals" :
                context.IsAiming ? "ADS_Start_Cardinals" : "Jog_Start_Cardinals", context.Direction),
        LyraLayerHook.FullBody_CycleState => profile.Cardinal(context.IsCrouching
            ? "Crouch_Walk_Cardinals" : context.IsAiming || context.Gait == LyraGait.Walk
                ? "Walk_Cardinals" : "Jog_Cardinals", context.Direction),
        LyraLayerHook.FullBody_StopState => profile.Cardinal(
            context.IsCrouching ? "Crouch_Stop_Cardinals" :
                context.IsAiming ? "ADS_Stop_Cardinals" : "Jog_Stop_Cardinals", context.Direction),
        LyraLayerHook.FullBody_PivotState => profile.Cardinal(
            context.IsCrouching ? "Crouch_Pivot_Cardinals" :
                context.IsAiming ? "ADS_Pivot_Cardinals" : "Jog_Pivot_Cardinals", context.Direction),
        LyraLayerHook.FullBody_JumpStartState => profile.Asset("Jump_Start"),
        LyraLayerHook.FullBody_JumpStartLoopState => profile.Asset("Jump_StartLoop"),
        LyraLayerHook.FullBody_JumpApexState => profile.Asset("Jump_Apex"),
        LyraLayerHook.FullBody_FallLoopState => profile.Asset("Jump_FallLoop"),
        LyraLayerHook.FullBody_FallLandState => profile.Asset("Jump_FallLand"),
        _ => throw new NotSupportedException($"Lyra layer hook {hook} has not been ported."),
    };
}

internal sealed class LyraLinkedLayerRouter
{
    private readonly LyraUnarmedCatalog _catalog;
    private readonly LyraUnarmedAuxCatalog? _auxiliary;
    private readonly LyraUnarmedRemainingCatalog? _remaining;
    private readonly LyraPistolCatalog? _pistol;
    private readonly LyraRifleCatalog? _rifle;
    private ILyraItemAnimationLayers _layer;
    public LyraLinkedLayerContracts Contracts { get; } = LyraLinkedLayerContracts.Load();

    public LyraLinkedLayerRouter(LyraUnarmedCatalog catalog, ILyraItemAnimationLayers initial,
        LyraUnarmedAuxCatalog? auxiliary = null, LyraUnarmedRemainingCatalog? remaining = null,
        LyraPistolCatalog? pistol = null, LyraRifleCatalog? rifle = null)
    {
        _catalog = catalog;
        _auxiliary = auxiliary;
        _remaining = remaining;
        _pistol = pistol;
        _rifle = rifle;
        Validate(initial);
        _layer = initial;
    }

    public string ActiveName => _layer.Name;
    public string ActiveClassPath => _layer.AnimationClassPath;
    public int Revision { get; private set; }

    public void Link(ILyraItemAnimationLayers layer)
    {
        Validate(layer);
        if (IsCurrentClass(layer)) return;
        _layer = layer;
        Revision++;
    }

    public bool IsCurrentClass(ILyraItemAnimationLayers layer) =>
        layer.AnimationClassPath == ActiveClassPath;

    public LyraItemLayerInstance CreateInstance(LyraBoundRig rig, ILyraItemAnimationLayers? provider = null,
        LyraLogicalSourceBank? logical = null) => new(rig, provider ?? _layer, Contracts, logical);

    public void ValidateProvider(ILyraItemAnimationLayers provider) => Validate(provider);

    public string Resolve(LyraLayerHook hook, in LyraLayerContext context)
    {
        var slot = _layer.ResolveStateClip(hook, context);
        if (!HasClip(slot))
            throw new InvalidOperationException($"{_layer.Name} selected missing Lyra slot {slot}.");
        return slot;
    }

    public ILyraAimingLayer CreateAimingLayer(LyraBoundRig rig) => _layer.CreateAimingLayer(rig);
    public LyraHipFirePoseLayer CreateHipFireLayer(LyraBoundRig rig) => _layer.CreateHipFireLayer(rig);
    public LyraLeftHandPoseLayer CreateLeftHandPoseLayer(LyraBoundRig rig) => _layer.CreateLeftHandPoseLayer(rig);
    public LyraFullBodyAdditivesLayer CreateFullBodyAdditivesLayer(LyraBoundRig rig) => _layer.CreateFullBodyAdditivesLayer(rig);
    public LyraHandRetargetPoseLayer CreateHandRetargetLayer(LyraBoundRig rig) => _layer.CreateHandRetargetLayer(rig);

    public string ResolveTurnInPlace(bool right, bool isCrouching)
    {
        var slot = _layer.ResolveTurnInPlace(right, isCrouching);
        if (!HasClip(slot))
            throw new InvalidOperationException($"{_layer.Name} selected missing turn clip {slot}.");
        return slot;
    }

    private void Validate(ILyraItemAnimationLayers layer)
    {
        Contracts.Get(layer.AnimationClassPath);
        ArgumentNullException.ThrowIfNull(layer);
        foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
        foreach (var gait in Enum.GetValues<LyraGait>())
        foreach (var hook in new[] { LyraLayerHook.FullBody_IdleState, LyraLayerHook.FullBody_StartState,
                     LyraLayerHook.FullBody_CycleState, LyraLayerHook.FullBody_StopState,
                     LyraLayerHook.FullBody_PivotState })
        {
            var slot = layer.ResolveStateClip(hook, new(direction, gait));
            if (!HasClip(slot))
                throw new InvalidOperationException($"{layer.Name} cannot provide {hook}/{direction}/{gait}: {slot}.");
        }
        if (_auxiliary is null) return;
        if (_remaining is not null)
        foreach (var crouching in new[] { false, true })
        foreach (var right in new[] { false, true })
        {
            var slot = layer.ResolveTurnInPlace(right, crouching);
            if (!HasClip(slot))
                throw new InvalidOperationException($"{layer.Name} cannot turn in place: {slot}.");
        }
        if (_remaining is not null)
        foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
        foreach (var hook in new[] { LyraLayerHook.FullBody_StartState,
                     LyraLayerHook.FullBody_CycleState, LyraLayerHook.FullBody_StopState,
                     LyraLayerHook.FullBody_PivotState })
        {
            var slot = layer.ResolveStateClip(hook, new(direction, LyraGait.Jog, false, true));
            if (!HasClip(slot))
                throw new InvalidOperationException($"{layer.Name} cannot provide ADS {hook}/{direction}: {slot}.");
        }
        foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
        foreach (var hook in new[] { LyraLayerHook.FullBody_IdleState, LyraLayerHook.FullBody_StartState,
                     LyraLayerHook.FullBody_CycleState, LyraLayerHook.FullBody_StopState,
                     LyraLayerHook.FullBody_PivotState })
        {
            var slot = layer.ResolveStateClip(hook, new(direction, LyraGait.Walk, true));
            if (!HasClip(slot))
                throw new InvalidOperationException($"{layer.Name} cannot provide crouched {hook}/{direction}: {slot}.");
        }
        foreach (var hook in new[] { LyraLayerHook.FullBody_JumpStartState,
                     LyraLayerHook.FullBody_JumpStartLoopState, LyraLayerHook.FullBody_JumpApexState,
                     LyraLayerHook.FullBody_FallLoopState, LyraLayerHook.FullBody_FallLandState })
        {
            var slot = layer.ResolveStateClip(hook, new(LyraCardinalDirection.Forward, LyraGait.Jog));
            if (!HasClip(slot))
                throw new InvalidOperationException($"{layer.Name} cannot provide {hook}: {slot}.");
        }
    }

    private bool HasClip(string slot) => _catalog.Clips.ContainsKey(slot) ||
        _auxiliary?.Clips.ContainsKey(slot) == true || _remaining?.Clips.ContainsKey(slot) == true ||
        _pistol?.Clips.ContainsKey(slot) == true || _rifle?.Clips.ContainsKey(slot) == true;
}
