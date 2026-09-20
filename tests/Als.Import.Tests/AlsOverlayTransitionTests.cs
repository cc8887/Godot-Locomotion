using System.Text.Json.Nodes;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayTransitionTests
{
    private sealed record Fixture(AlsAnimationSetDefinition Set, AlsOverlayStateGraph States, AlsOverlayTransitionDefinition Definition);
    private static readonly Lazy<Fixture> Data = new(() =>
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var sources = AlsOverlaySourceCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), set);
        var states = AlsOverlayStateCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), set, sources);
        return new(set, states, Compile(Read("v4_overlay_transition_inputs.json"), set, states));
    });
    private static string Read(string file) => AlsAimPoseCompilerTests.Read(file);
    private static AlsOverlayTransitionDefinition Compile(string json, AlsAnimationSetDefinition set, AlsOverlayStateGraph states) =>
        AlsOverlayTransitionCompiler.Compile(json, Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), set, states);

    [Fact]
    public void CompilesEightNativeConsumersIncludingWeaponSpecificFeetAndRates()
    {
        var f = Data.Value; Assert.Equal(8, f.Definition.Bindings.Length);
        foreach (var b in f.Definition.Bindings)
        {
            Assert.Equal(b.Machine is AlsOverlayMachineKind.Rifle or AlsOverlayMachineKind.Bow ? "ALS_N_Transition_L" : "ALS_N_Transition_R", f.Set.Animations[b.AnimationId].Name);
            Assert.Equal(b.GeneratedIndex % 2 == 0 && b.Machine != AlsOverlayMachineKind.Bow ? 1.75f : 1.5f, b.PlayRate);
            Assert.Equal(.2f, b.BlendIn); Assert.Equal(.2f, b.BlendOut); Assert.Equal(.3f, b.StartTime);
            Assert.Equal(2, b.AdditiveType);
        }
        Assert.Equal(3, f.Definition.Slot.Id); Assert.Equal(1, f.Definition.LoopCount); Assert.Equal(0, f.Definition.BlendOutTriggerTime);
    }

    [Fact]
    public void PhysicalTransitionAssetsUseTheSkeletonsGroundedMontageGroup()
    {
        var f = Data.Value;
        var assets = AlsOverlayTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), f.Set, f.Definition);
        Assert.Equal(2, assets.Length);
        foreach (var asset in assets)
        {
            Assert.Equal(1, asset.GroupId); Assert.Equal(3, asset.Slot.Id); Assert.Equal(2, asset.AdditiveType);
            Assert.Equal(f.Set.Animations[asset.AnimationId].PlayLength, asset.Duration);
        }
        var changed = JsonNode.Parse(Read("v4_turn_montage_inputs.json"))!;
        changed["skeletonText"] = changed["skeletonText"]!.GetValue<string>().Replace("\"Grounded Slot\"", "\"Other Slot\"", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => AlsOverlayTransitionCompiler.CompileAssets(changed.ToJsonString(), f.Set, f.Definition));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("owner")]
    [InlineData("gate")]
    [InlineData("slot")]
    [InlineData("loop")]
    public void RejectsChangedNativeConsumerSemantics(string mutation)
    {
        var f = Data.Value; var node = JsonNode.Parse(Read("v4_overlay_transition_inputs.json"))!;
        if (mutation == "name") node["notifyDefinitions"]![0]!["name"] = "Other";
        else if (mutation == "owner") node["overlaySha256"] = "foreign";
        else
        {
            var graph = node["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == (mutation == "gate" ? "CanOverlayTransition" : "EventGraph"))!;
            var text = graph["nativeText"]!.GetValue<string>();
            graph["nativeText"] = mutation switch
            {
                "gate" => text.Replace("MemberName=\"Not_PreBool\"", "MemberName=\"Other\"", StringComparison.Ordinal),
                "slot" => text.Replace("DefaultValue=\"Grounded Slot\"", "DefaultValue=\"BaseLayer\"", StringComparison.Ordinal),
                _ => text.Replace("DefaultValue=\"1\"", "DefaultValue=\"2\"", StringComparison.Ordinal),
            };
        }
        Assert.Throws<ArgumentException>(() => Compile(node.ToJsonString(), f.Set, f.States));
    }

    [Theory]
    [InlineData(AlsStance.Standing, false, 8)]
    [InlineData(AlsStance.Standing, true, 0)]
    [InlineData(AlsStance.Crouching, false, 0)]
    [InlineData(AlsStance.Crouching, true, 0)]
    public void DispatchGateUsesShouldMoveAndStanceAndPreservesRetry(AlsStance stance, bool move, int expected)
    {
        var f = Data.Value; var owner = new AlsOverlayTransitionRuntime(f.Definition, 11, 2); var id = new AlsFrameIdentity(1, 11, 2);
        Prepare(); var before = owner.Commands.ToArray(); Assert.Equal(expected, before.Length); Assert.Equal(8, owner.QueuedCount);
        owner.Cancel(); Assert.Empty(owner.CommittedCommands.ToArray()); Assert.Equal(default, owner.CommittedIdentity);
        Prepare(); Assert.Equal(before, owner.Commands.ToArray()); owner.Commit(id);
        Assert.Equal(before, owner.CommittedCommands.ToArray()); Assert.Throws<ArgumentException>(() => owner.Begin(id));
        void Prepare()
        {
            owner.Begin(id);
            foreach (var b in f.Definition.Bindings) owner.Queue(b.Machine, new(b.GeneratedIndex, b.Edge));
            owner.Resolve(stance, move);
        }
    }

    [Fact]
    public void PreservesRepeatedNamedNotifiesAndRejectsWrongOwnerBeforeMutation()
    {
        var f = Data.Value; var owner = new AlsOverlayTransitionRuntime(f.Definition, 11, 2); var id = new AlsFrameIdentity(1, 11, 2);
        Assert.Throws<ArgumentException>(() => owner.Begin(new(1, 12, 2)));
        owner.Begin(id); var b = f.Definition.Bindings[0];
        Assert.Throws<ArgumentException>(() => owner.Queue(AlsOverlayMachineKind.Bow, new(b.GeneratedIndex, b.Edge)));
        Assert.Equal(0, owner.QueuedCount);
        owner.Queue(b.Machine, new(b.GeneratedIndex, b.Edge)); owner.Queue(b.Machine, new(b.GeneratedIndex, b.Edge));
        owner.Resolve(AlsStance.Standing, false);
        Assert.Equal(new[] { 0, 1 }, owner.Commands.ToArray().Select(c => c.QueueOrdinal));
        Assert.Throws<ArgumentException>(() => owner.ValidateCommit(new(2, 11, 2)));
        owner.Commit(id); Assert.Equal(2, owner.CommittedCommands.Length);
    }

    [Fact]
    public void RepeatedEditorExportCompilesToTheSameConsumerSemantics()
    {
        var path = Environment.GetEnvironmentVariable("ALS_OVERLAY_TRANSITION_REPEAT");
        var f = Data.Value;
        var repeat = Compile(path is null ? Read("v4_overlay_transition_inputs.json") : File.ReadAllText(path), f.Set, f.States);
        Assert.True(repeat.Bindings.SequenceEqual(f.Definition.Bindings));
    }

    [Fact]
    public void NativeStateMachineTracesReachConsumerQueueWithTheOriginalOrder()
    {
        var f = Data.Value;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "tests/Als.Core.Tests/Fixtures/P3/v4_overlay_state_native.json")));
        var total = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            var kind = (AlsOverlayMachineKind)trace.GetProperty("machine").GetInt32(); if (kind == AlsOverlayMachineKind.Overlay) continue;
            var machine = new AlsOverlayStateMachine(f.States, kind); var state = machine.Initialize().State;
            var owner = new AlsOverlayTransitionRuntime(f.Definition, 11, 2);
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var serial = row.GetProperty("serial").GetInt32(); var id = new AlsFrameIdentity(serial, 11, 2);
                var input = row.GetProperty("input"); owner.Begin(id);
                if (input.GetProperty("relevant").GetBoolean())
                {
                    var values = new AlsOverlayStateInput((AlsOverlayKind)input.GetProperty("overlay").GetInt32(),
                        (AlsRotationMode)input.GetProperty("mode").GetInt32(), (AlsGait)input.GetProperty("gait").GetInt32(),
                        (AlsMovementStateInput)input.GetProperty("movement").GetInt32(), input.GetProperty("moving").GetBoolean(),
                        input.GetProperty("enable").GetSingle(), input.GetProperty("rotation").GetSingle());
                    var update = machine.Update(state, values, input.GetProperty("weight").GetSingle(), input.GetProperty("delta").GetSingle(), serial, input.GetProperty("inactive").GetBoolean());
                    state = update.State;
                    for (var n = 0; n < update.NotifyCount; n++) owner.Queue(kind, update.GetNotify(n));
                }
                // This test holds the dispatch gate open to check every native
                // queued occurrence; the four actual gate cases are tested above.
                owner.Resolve(AlsStance.Standing, false);
                Assert.Equal(row.GetProperty("notifies").EnumerateArray().Select(n => n.GetInt32()), owner.Commands.ToArray().Select(c => c.Binding.GeneratedIndex));
                total += owner.Commands.Length; owner.Commit(id);
            }
        }
        Assert.True(total > 0);
    }

    [Fact]
    public void WarmConsumerFramesAllocateNothing()
    {
        var definition = Data.Value.Definition; var owner = new AlsOverlayTransitionRuntime(definition, 11, 2);
        void Tick(int frame)
        {
            var id = new AlsFrameIdentity(frame, 11, 2); owner.Begin(id);
            foreach (var b in definition.Bindings) owner.Queue(b.Machine, new(b.GeneratedIndex, b.Edge));
            owner.Resolve(AlsStance.Standing, false); owner.Commit(id);
        }
        for (var frame = 1; frame <= 1000; frame++) Tick(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 11000; frame++) Tick(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
