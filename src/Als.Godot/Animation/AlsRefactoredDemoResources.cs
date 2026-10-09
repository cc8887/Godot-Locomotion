using GodotAls.Import.Compilation;
using GodotAls.Core.Locomotion;
using System.Text.Json;

namespace GodotAls.Animation;

// Built on Main during graph construction. Workers only read frozen resources.
internal static class AlsRefactoredDemoResources
{
    internal static readonly Lazy<AlsRefactoredPostLayerProfile> PostLayer = new(() =>
    {
        string Read(string name) => Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "refactored_" + name + ".json");
        return AlsRefactoredPostLayerCompiler.Compile(Read("layering_graphs"), Read("layering_inventory"),
            Read("base_pose_inputs"), Read("head_inputs"));
    });
    internal static readonly Lazy<AlsRefactoredCharacterActionProfile> Profile = new(Load);
    private static AlsRefactoredNotifyBank? _notifies;
    internal static readonly Lazy<AlsRefactoredNotifyBank> Notifies = new(() => { _ = Profile.Value; return _notifies!; });
    internal static readonly Lazy<AlsRefactoredLocomotionSettings> LocomotionSettings = new(() => new(
        Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "refactored_character_settings.json"),
        Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "refactored_movement_settings.json")));
    internal static readonly Lazy<AlsRefactoredLocomotionHostProfile> Locomotion = new(()=>
    {
        var actions=Profile.Value;
        return new(actions,Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "refactored_air_settings.json"),
            Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "refactored_locomotion_machines.json"));
    });
    internal static AlsLogicalVirtualBone[] NativeVirtuals { get; private set; } = [];
    internal static AlsPrecisePose[] NativeReference { get; private set; } = [];
    private static AlsRefactoredCharacterActionProfile Load()
    {
        string Read(string name) => Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + name + ".json");
        byte[] Bytes(string path) => Godot.FileAccess.GetFileAsBytes(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + path);
        var index = Read("refactored_animation_sources");
        var catalog = new AlsRefactoredAnimationCatalog(index, Bytes, AlsAnimationRuntimeOptions.SampleRootMotion);
        using var document = JsonDocument.Parse(index);
        NativeVirtuals = document.RootElement.GetProperty("skeletons").GetProperty("/ALS/ALS/Character/SK_Als.SK_Als")
            .GetProperty("virtualBones").EnumerateArray().Select(v => new AlsLogicalVirtualBone(
                v.GetProperty("bone").GetInt32(),v.GetProperty("source").GetInt32(),v.GetProperty("target").GetInt32())).ToArray();
        NativeReference = catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence).ReferencePose.ToArray();
        var machines = Read("refactored_stance_machines");
        var sync = new AlsRefactoredSyncBank(Read("refactored_sync_inputs"), catalog);
        _notifies = new(Read("refactored_notify_inputs"), catalog, sync);
        var metadata = new AlsRefactoredSkeletonCurves(Read("refactored_skeleton_curves"), catalog);
        var standing = new AlsRefactoredStandingHostProfile(catalog, machines,
            sync,
            AlsRefactoredTriangulationCompiler.Compile(Read("refactored_triangulation_inputs"), index, Bytes),
            new(Read("refactored_movement_settings"), catalog), new(Read("refactored_rest_settings"), catalog),
            metadata, Read("refactored_slot_inventory"), Read("refactored_quick_stop_settings"));
        var weapons = Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponNotifyProfile(catalog,
            new(catalog, new(Read("refactored_weapon_machines"), catalog, k)))).ToArray();
        var paths = standing.ActionAssets.ToArray().Select(a => standing.ActionSource(a.AnimationId))
            .Concat(weapons.SelectMany(w => w.Bindings.ToArray()).Select(b => b.Sequence)).Distinct().ToArray();
        return new(standing, weapons, paths.Select((p, i) => (p, i)).ToDictionary(v => v.p, v => v.i), 0, machines, metadata, Read("refactored_locomotion_machines"));
    }
}
