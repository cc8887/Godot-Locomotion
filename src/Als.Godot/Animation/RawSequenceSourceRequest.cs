using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Export the actual production source closure for the read-only UE source-data job.
// Animation assets are resource identities; the 75 player owners remain separate.
public partial class RawSequenceSourceRequest : Node
{
    public override void _Ready()
    {
        try
        {
            var output = System.Environment.GetEnvironmentVariable("ALS_RAW_SOURCE_REQUEST_OUTPUT");
            if (string.IsNullOrEmpty(output) || !Path.IsPathFullyQualified(output))
                throw new ArgumentException("ALS_RAW_SOURCE_REQUEST_OUTPUT must be an absolute output file.");
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var locomotion = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var definition = AlsMovementGraphDefinition.Load(set, locomotion, loadRawSources: false);
            var core = definition.Binding.CreateCoreView();
            var ids = AlsRawAnimationSourceLoader.CollectRoots(set, definition);
            var source = definition.Sources.CreateCoreView();
            var arguments = OS.GetCmdlineUserArgs();
            var aim = arguments.Contains("--aim-source");
            var overlay = arguments.Contains("--overlay-source");
            var stop = arguments.Contains("--stop-source");
            var ragdoll = arguments.Contains("--ragdoll-source");
            var prop = arguments.Contains("--prop-source");
            if ((aim ? 1 : 0) + (overlay ? 1 : 0) + (stop ? 1 : 0) + (ragdoll ? 1 : 0) + (prop ? 1 : 0) > 1)
                throw new ArgumentException("Choose one source closure.");
            var stopProfile = stop ? AlsStopTransitionCompiler.Compile(
                Godot.FileAccess.GetFileAsString("res://assets/config/v4_overlay_transition_inputs.json"), set,
                AlsGroundedMachineCompiler.CompileGrounded(Godot.FileAccess.GetFileAsString("res://assets/config/v4_grounded_dependencies.json"))) : null;
            var overlayProfile = overlay ? AlsOverlaySourceCompiler.Compile(
                Godot.FileAccess.GetFileAsString("res://assets/config/v4_layering_inputs.json"),
                Godot.FileAccess.GetFileAsString("res://assets/config/v4_overlay_inputs.json"), set) : null;
            var aimProfile = aim ? AlsAimSamplingCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/v4_aim_sampling.json"), definition.AimPose, set) : null;
            var propProfile = prop ? AlsOverlayPropCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/v4_overlay_props_inputs.json"), set,
                set.SkeletalMeshes[definition.MannequinMeshId].SkeletonId) : null;
            if (aimProfile is not null) ids = aimProfile.AnimationIds.ToArray();
            if (overlayProfile is not null) ids = overlayProfile.AnimationIds.ToArray();
            if (stopProfile is not null) ids = stopProfile.Bindings.ToArray().Select(b => b.AnimationId).ToArray();
            if (ragdoll) ids = [definition.RagdollPose.AnimationId];
            if (propProfile is not null) ids = [propProfile.BowAnimationId];
            var players = stop ? 2 : overlay ? AlsOverlaySourceProfile.PlayerCount : aim ? AlsAimSamplingProfile.PlayerCount :
                ragdoll || prop ? 1 : source.Players.Length;
            var samples = stop ? 2 : overlay ? AlsOverlaySourceProfile.PlayerCount : aim ? AlsAimSamplingProfile.SampleCount :
                ragdoll || prop ? 1 : source.Samples.Length;
            var request = new
            {
                definitionDigest = set.DefinitionDigest,
                bindingDigest = propProfile?.Digest ?? (ragdoll ? definition.RagdollPose.BindingDigest : null) ??
                    stopProfile?.BindingDigest ?? overlayProfile?.BindingDigest ?? aimProfile?.BindingDigest ?? core.Digest.ToString("X16"),
                players,
                samples,
                rootAssets = ids.Select(id => set.Animations[id]).OrderBy(a => a.StableId, StringComparer.Ordinal)
                    .Select(a => new { assetId = a.StableId, source = a.ObjectPath }).ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            GD.Print($"RAW_SOURCE_REQUEST_OK aim={aim} overlay={overlay} stop={stop} ragdoll={ragdoll} prop={prop} roots={ids.Length} players={players} samples={samples} additive_dependencies=resolve_in_ue output={output}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
