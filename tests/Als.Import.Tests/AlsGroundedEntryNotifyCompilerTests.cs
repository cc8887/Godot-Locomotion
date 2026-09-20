using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedEntryNotifyCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsGroundedMachinesProfile> Machines = new(() =>
        AlsGroundedMachineCompiler.CompileMovement(Read("v4_main_movement_graph.json")));
    private static readonly Lazy<AlsP5aAnimationRuntimeProfile> Runtime = new(() =>
        AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Set.Value));

    [Fact]
    public void ReadsTheSpacedNativePropertyAndOriginalEnumAndExitReset()
    {
        var profile = Compile(Read("v4_grounded_notify_semantics.json"));
        Assert.Equal(AlsTimelineGroundedEntryMode.FromRoll, profile.Binding.Mode);
        Assert.Equal(3, profile.Binding.SemanticId);
        var asset = Set.Value.Animations[profile.Binding.AnimationId];
        Assert.Equal("ALS_N_LandRoll_F", asset.Name);
        Assert.Equal(asset.Timeline.Single(t => t.SourceIndex == 2).EventId, profile.Binding.EventId);
        Assert.Equal(Machines.Value.Main.Runtime.States[0].EndNotify, profile.ResetNotifyIndex);
        Assert.Equal("Reset-GroundedEntryState", Machines.Value.NotifyNames[profile.ResetNotifyIndex]);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("value")]
    [InlineData("enum")]
    [InlineData("object")]
    [InlineData("class")]
    [InlineData("index")]
    [InlineData("connection")]
    [InlineData("target")]
    public void RejectsUnsupportedNativeSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_grounded_notify_semantics.json"))!;
        var row = root["notifies"]![0]!;
        switch (mutation)
        {
            case "property": row["nativeText"] = row["nativeText"]!.GetValue<string>().Replace("Grounded Entry State=", "GroundedEntryState="); break;
            case "value": row["nativeText"] = row["nativeText"]!.GetValue<string>().Replace("NewEnumerator2", "NewEnumerator9"); break;
            case "enum": root["enumText"] = root["enumText"]!.GetValue<string>().Replace("\"Roll\"", "\"Other\""); break;
            case "object": row["objectPath"] = "/Game/Other:Notify"; break;
            case "class": row["classPath"] = "/Game/Other_C"; break;
            case "index": row["sourceIndex"] = 0; break;
            case "connection":
            case "target":
                var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Received_Notify")!;
                graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace(
                    mutation == "connection" ? "Grounded Entry State" : "GetAnimInstance", "Unsupported"); break;
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsChangedResetConsumer()
    {
        var graph = Read("v4_overlay_transition_inputs.json").Replace("AnimNotify_Reset-GroundedEntryState", "AnimNotify_UnsupportedReset");
        Assert.ThrowsAny<Exception>(() => AlsGroundedEntryNotifyCompiler.Compile(Read("v4_grounded_notify_semantics.json"), graph, Set.Value, Machines.Value, Runtime.Value));
    }

    private static AlsGroundedEntryNotifyProfile Compile(string json) => AlsGroundedEntryNotifyCompiler.Compile(
        json, Read("v4_overlay_transition_inputs.json"), Set.Value, Machines.Value, Runtime.Value);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
