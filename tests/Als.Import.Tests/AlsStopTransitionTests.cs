using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStopTransitionTests
{
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsGroundedMachinesProfile> Machines = new(() =>
        AlsGroundedMachineCompiler.CompileGrounded(Read("v4_grounded_dependencies.json")));
    private static AlsStopTransitionDefinition Compile(string? json = null) =>
        AlsStopTransitionCompiler.Compile(json ?? Read("v4_overlay_transition_inputs.json"), Set.Value, Machines.Value);

    [Fact]
    public void RawStopClosureRetainsBothSourcesAndTheirAuthoredAdditiveBase()
    {
        var definition = Compile();
        var bank = AlsRawAnimationSourceCompiler.Compile(Read("v4_stop_source_inputs.json"), Set.Value, definition.BindingDigest,
            2, 2, definition.Bindings.ToArray().Select(b => b.AnimationId).ToArray(),
            relative => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", relative)));
        Assert.Equal(2, bank.RootAnimationIds.Length); Assert.Equal(3, bank.Sources.Length);
        foreach (var binding in definition.Bindings)
            Assert.Equal(AlsRawAnimationAdditiveType.RotationOffsetMeshSpace, bank.GetSource(binding.AnimationId).Policy.AdditiveType);
        var assets = AlsStopTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), Set.Value, definition);
        Assert.All(assets, a => { Assert.Equal(1, a.GroupId); Assert.Equal(definition.Slot, a.Slot); });
    }

    [Fact]
    public void DynamicNotifyMergeRejectsForeignOwnerAndRepeatedSequence()
    {
        var stop = Read("v4_stop_notify_inputs.json"); var overlay = Read("v4_transition_notify_inputs.json");
        var combined = JsonNode.Parse(AlsStopTransitionCompiler.MergeNotifyMetadata(overlay, stop))!;
        Assert.Equal(4, combined["syncAssets"]!.AsArray().Count);
        Assert.Throws<ArgumentException>(() => AlsStopTransitionCompiler.MergeNotifyMetadata(stop, stop));
        var changed = JsonNode.Parse(stop)!; changed["source"] = "foreign";
        Assert.Throws<ArgumentException>(() => AlsStopTransitionCompiler.MergeNotifyMetadata(overlay, changed.ToJsonString()));
    }

    [Fact]
    public void OriginalStopConsumersAreUnconditionalAndUseExactAuthoredParameters()
    {
        var definition = Compile(); Assert.Equal(2, definition.Bindings.Length);
        foreach (var binding in definition.Bindings)
        {
            Assert.Equal(.2f, binding.BlendIn); Assert.Equal(.2f, binding.BlendOut);
            Assert.Equal(1.5f, binding.PlayRate); Assert.Equal(.4f, binding.StartTime);
            Assert.Equal(2, binding.AdditiveType);
        }
        for (var state = 3; state <= 6; state++)
        {
            var row = Machines.Value.Stop.Runtime.States[state];
            Assert.Equal(definition.Bindings[state is 3 or 5 ? 0 : 1],
                definition.Resolve(new(AlsGroundedEventKind.StateEntered, state, row.StartNotify)));
        }
        Assert.Throws<ArgumentException>(() => definition.Resolve(new(AlsGroundedEventKind.StateExited, 3, definition.Bindings[0].GeneratedIndex)));
        Assert.Throws<ArgumentException>(() => definition.Resolve(new(AlsGroundedEventKind.StateEntered, 3, definition.Bindings[1].GeneratedIndex)));
    }

    [Theory]
    [InlineData("asset")] [InlineData("gate")] [InlineData("rate")]
    public void RejectsChangedResourceExecutionOrInvalidPlayback(string change)
    {
        var json = JsonNode.Parse(Read("v4_overlay_transition_inputs.json"))!;
        var graph = json["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "EventGraph")!;
        var original = graph["nativeText"]!.GetValue<string>();
        var modified = change switch
        {
            "asset" => original.Replace("ALS_N_Stop_L_Down", "ALS_N_Stop_R_Down"),
            "gate" => original.Replace("MemberName=\"PlayTransition\"", "MemberName=\"CanOverlayTransition\""),
            _ => original.Replace("DefaultValue=\"1.500000\"", "DefaultValue=\"0.000000\"")
        };
        Assert.NotEqual(original, modified); graph["nativeText"] = modified;
        Assert.ThrowsAny<Exception>(() => Compile(json.ToJsonString()));
    }
}
