using System.Text.Json;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal static class AlsRawAnimationSourceLoader
{
    private const string ConfigDirectory = "res://assets/config/";
    private const string GeneratedDirectory = "res://assets/generated/als_v4_raw/";

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
        return Compile("v4_recovery_movement_source_inputs.json", set,
            definition.Binding.CreateCoreView().Digest.ToString("X16"), source.Players.Length, source.Samples.Length,
            CollectRoots(set, definition));
    }

    public static AlsRawAnimationSourceBank LoadAim(AlsAnimationSetDefinition set, AlsAimSamplingProfile profile) =>
        Compile("v4_aim_source_inputs.json",
            set, profile.BindingDigest, AlsAimSamplingProfile.PlayerCount, AlsAimSamplingProfile.SampleCount,
            profile.AnimationIds);

    public static AlsRawAnimationSourceBank LoadRagdoll(AlsAnimationSetDefinition set, AlsRagdollPoseProfile profile) =>
        Compile("v4_ragdoll_source_inputs.json", set, profile.BindingDigest, 1, 1, [profile.AnimationId]);

    public static AlsRawAnimationSourceBank LoadOverlay(AlsAnimationSetDefinition set, AlsOverlaySourceProfile profile) =>
        Compile("v4_overlay_source_inputs.json",
            set, profile.BindingDigest, AlsOverlaySourceProfile.PlayerCount, AlsOverlaySourceProfile.PlayerCount,
            profile.AnimationIds);

    public static AlsRawAnimationSourceBank LoadStop(AlsAnimationSetDefinition set, GodotAls.Core.Locomotion.AlsStopTransitionDefinition definition) =>
        Compile("v4_stop_source_inputs.json", set, definition.BindingDigest, 2, 2,
            definition.Bindings.ToArray().Select(b => b.AnimationId).ToArray());

    public static AlsRawAnimationSourceBank LoadProp(AlsAnimationSetDefinition set, AlsOverlayPropProfile profile) =>
        Compile("v4_overlay_prop_source_inputs.json", set, profile.Digest, 1, 1, [profile.BowAnimationId]);

    private static AlsRawAnimationSourceBank Compile(string indexName, AlsAnimationSetDefinition set,
        string bindingDigest, int playerCount, int sampleCount, ReadOnlySpan<int> rootAnimationIds)
    {
        var directory = ConfigDirectory;
        var generatedIndex = GeneratedDirectory + indexName;
        if (Godot.FileAccess.FileExists(generatedIndex))
        {
            using var candidate = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(generatedIndex));
            if (candidate.RootElement.GetProperty("request").GetProperty("definitionDigest").GetString() == set.DefinitionDigest)
                directory = GeneratedDirectory;
        }
        return AlsRawAnimationSourceCompiler.Compile(Godot.FileAccess.GetFileAsString(directory + indexName), set,
            bindingDigest, playerCount, sampleCount, rootAnimationIds,
            relative => Godot.FileAccess.GetFileAsBytes(directory + relative));
    }
}
