using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal static class AlsRawAnimationSourceLoader
{
    public static int[] CollectRoots(AlsAnimationSetDefinition set, AlsMovementGraphDefinition definition)
    {
        var graph = definition.Binding.CreateGraphBuildView();
        var ids = AlsAnimationLibraryBuilder.GetP5aAnimationClosure(graph, definition.Binding.CreateCoreView()).ToHashSet();
        foreach (var asset in definition.TurnMontageAssets) ids.Add(asset.AnimationId);
        foreach (var asset in definition.AuthoredMontageAssets) ids.Add(asset.AnimationId);
        foreach (var evaluator in definition.BasePoses.Evaluators) ids.Add(evaluator.AnimationId);
        foreach (var id in ids)
            if ((uint)id >= (uint)set.Animations.Length || set.Animations[id].SkeletonId != graph.SkeletonId)
                throw new ArgumentException("Production source closure contains a foreign animation or skeleton.");
        return ids.Order().ToArray();
    }

    // Load once on the main thread; workers receive only the immutable compiled bank.
    public static AlsRawAnimationSourceBank Load(AlsAnimationSetDefinition set, AlsMovementGraphDefinition definition)
    {
        var source = definition.Sources.CreateCoreView();
        return AlsRawAnimationSourceCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_movement_source_inputs.json"), set,
            definition.Binding.CreateCoreView().Digest.ToString("X16"), source.Players.Length, source.Samples.Length,
            CollectRoots(set, definition), relative => Godot.FileAccess.GetFileAsBytes("res://assets/config/" + relative));
    }

    public static AlsRawAnimationSourceBank LoadAim(AlsAnimationSetDefinition set, AlsAimSamplingProfile profile) =>
        AlsRawAnimationSourceCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_aim_source_inputs.json"),
            set, profile.BindingDigest, AlsAimSamplingProfile.PlayerCount, AlsAimSamplingProfile.SampleCount,
            profile.AnimationIds, relative => Godot.FileAccess.GetFileAsBytes("res://assets/config/" + relative));

    public static AlsRawAnimationSourceBank LoadRagdoll(AlsAnimationSetDefinition set, AlsRagdollPoseProfile profile) =>
        AlsRawAnimationSourceCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_ragdoll_source_inputs.json"),
            set, profile.BindingDigest, 1, 1, [profile.AnimationId], relative => Godot.FileAccess.GetFileAsBytes("res://assets/config/" + relative));

    public static AlsRawAnimationSourceBank LoadOverlay(AlsAnimationSetDefinition set, AlsOverlaySourceProfile profile) =>
        AlsRawAnimationSourceCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_overlay_source_inputs.json"),
            set, profile.BindingDigest, AlsOverlaySourceProfile.PlayerCount, AlsOverlaySourceProfile.PlayerCount,
            profile.AnimationIds, relative => Godot.FileAccess.GetFileAsBytes("res://assets/config/" + relative));

    public static AlsRawAnimationSourceBank LoadStop(AlsAnimationSetDefinition set, GodotAls.Core.Locomotion.AlsStopTransitionDefinition definition) =>
        AlsRawAnimationSourceCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_stop_source_inputs.json"),
            set, definition.BindingDigest, 2, 2, definition.Bindings.ToArray().Select(b => b.AnimationId).ToArray(),
            relative => Godot.FileAccess.GetFileAsBytes("res://assets/config/" + relative));
}
