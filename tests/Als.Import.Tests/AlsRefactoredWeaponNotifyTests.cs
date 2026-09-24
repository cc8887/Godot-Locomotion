using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponNotifyTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    private static AlsRefactoredWeaponNotifyProfile Profile(AlsRefactoredWeaponKind kind)
    {
        var catalog = Catalog();
        return new(catalog, new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind)));
    }
    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void OriginalEventGraphsBindNativeLocalNotifiesToCorrectStandingSequences(AlsRefactoredWeaponKind kind)
    {
        var profile = Profile(kind);
        using var document = JsonDocument.Parse(MantlingHostFixture.Read("refactored_weapon_trace_" + kind));
        var native = document.RootElement.GetProperty("notifyDefinitions");
        Assert.Equal(2, profile.Bindings.Length);
        for (var i = 0; i < 2; i++)
        {
            var binding = profile.Bindings[i];
            Assert.Equal(native[i].GetProperty("index").GetInt32(), binding.GeneratedIndex);
            Assert.Equal(native[i].GetProperty("name").GetString(), binding.Name);
            Assert.Equal(kind == AlsRefactoredWeaponKind.Bow || i == 1 ? 1.5f : 1.75f, binding.PlayRate);
            Assert.Equal(.3f, binding.StartTime); Assert.Equal(.2f, binding.BlendIn); Assert.Equal(.2f, binding.BlendOut);
            Assert.Contains(kind is AlsRefactoredWeaponKind.Bow or AlsRefactoredWeaponKind.Rifle ? "Transition_Left" : "Transition_Right", binding.Sequence);
            Assert.Equal(binding, profile.Resolve(new(i, i == 0 ? 0 : 2)));
            Assert.Throws<ArgumentException>(() => profile.Resolve(new(i, 3)));
        }
    }

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void OrderedDispatchGuardsDuplicatesRollbackAndOwnerIdentity(AlsRefactoredWeaponKind kind)
    {
        var profile = Profile(kind); var machine = profile.Machine.CreateRuntime(); var runtime = profile.CreateRuntime(7, 2);
        var aim = new AlsRefactoredWeaponRuleInput("Als.RotationMode.Aiming", "", "", false, true);
        machine.Prepare(0, aim, .1f);
        var id = new AlsFrameIdentity(0, 7, 2); runtime.Prepare(id, machine, "Als.Stance.Standing", false);
        Assert.Empty(runtime.Commands.ToArray()); machine.Commit(0); runtime.Commit(id);
        machine.Prepare(1, aim with { RotationMode = "" }, 3); machine.Commit(1);
        machine.Prepare(2, aim, .01f);
        Assert.Equal(2, machine.Candidate.NotifyCount);
        id = new(2, 7, 2);
        foreach (var stance in new[] { "", "Als.Stance.Crouching", "Als.Stance.Standing.Child", "Als.Stance.Standing" })
        foreach (var moving in new[] { false, true })
        {
            runtime.Prepare(id, machine, stance, moving);
            Assert.Equal(stance == "Als.Stance.Standing" && !moving ? 2 : 0, runtime.Commands.Length);
            runtime.Cancel(); Assert.Empty(runtime.CommittedCommands.ToArray());
        }
        runtime.Prepare(id, machine, "Als.Stance.Standing", false); var first = runtime.Commands.ToArray();
        Assert.Equal(new[] { "ReadyToRelaxed", "RelaxedToReady" }, first.Select(c => c.Binding.Name));
        Assert.Equal(new[] { 0, 1 }, first.Select(c => c.QueueOrdinal));
        Assert.All(first, c => { Assert.Equal(id, c.Identity); Assert.Equal("Transition", c.Slot); Assert.Equal(1, c.LoopCount); Assert.Equal(0, c.BlendOutTriggerTime); });
        Assert.Throws<ArgumentException>(() => runtime.Commit(new(3, 7, 2))); runtime.Cancel();
        runtime.Prepare(id, machine, "Als.Stance.Standing", false); Assert.Equal(first, runtime.Commands.ToArray());
        runtime.ValidateCommit(id); machine.ValidateCommit(2); runtime.Commit(id); machine.Commit(2);
        Assert.Equal(first, runtime.CommittedCommands.ToArray());
        Assert.Throws<ArgumentException>(() => runtime.Prepare(id, machine, "Als.Stance.Standing", false));
        var foreign = Profile(kind).Machine.CreateRuntime(); foreign.Prepare(3, aim, .01f);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(new(3, 7, 2), foreign, "Als.Stance.Standing", false));
        machine.Prepare(3, aim, .01f);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(new(3, 8, 2), machine, "Als.Stance.Standing", false));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(new(3, 7, 3), machine, "Als.Stance.Standing", false));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(new(4, 7, 2), machine, "Als.Stance.Standing", false));
    }

    [Theory]
    [InlineData("side")]
    [InlineData("rate")]
    [InlineData("guard")]
    [InlineData("receiver")]
    public void ChangedOriginalDispatchSemanticsAreRejected(string change)
    {
        var catalog = Catalog(); var source = AlsRefactoredWeaponMachineResources.Blueprint(AlsRefactoredWeaponKind.Bow);
        var graph = AlsNativeNestedGraph.Extract(catalog.Read(source).GetProperty("nativeText").GetString()!, source, source + ":EventGraph");
        Assert.Equal(2, AlsRefactoredWeaponNotifyProfile.CompileEvents(graph, AlsRefactoredWeaponKind.Bow).Length);
        graph = change switch
        {
            "side" => graph.Replace("PlayTransitionLeftAnimation", "PlayTransitionRightAnimation"),
            "rate" => graph.Replace("DefaultValue=\"1.500000\"", "DefaultValue=\"1.750000\""),
            "guard" => graph.Replace("DefaultValue=\"true\"", "DefaultValue=\"false\""),
            "receiver" => graph.Replace("MemberName=\"GetParent\"", "MemberName=\"GetOther\""),
            _ => throw new ArgumentException()
        };
        Assert.Throws<ArgumentException>(() => AlsRefactoredWeaponNotifyProfile.CompileEvents(graph, AlsRefactoredWeaponKind.Bow));
    }
}
