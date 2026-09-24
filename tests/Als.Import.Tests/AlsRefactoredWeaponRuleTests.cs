using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponRuleTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void OriginalGraphsPreserveExactTagsInclusiveDelayAndIndependentGates(AlsRefactoredWeaponKind kind)
    {
        var catalog = Catalog();
        var resources = new AlsRefactoredWeaponMachineResources(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind);
        var rules = AlsRefactoredWeaponRuleCompiler.Compile(catalog, resources);
        foreach (var elapsed in new[] { 0f, MathF.BitDecrement(3f), 3f, MathF.BitIncrement(3f), 10f })
        foreach (var rotation in new[] { "", "Als.RotationMode.ViewDirection", "Als.RotationMode.Aiming", "Als.RotationMode.Aiming.Child" })
        foreach (var gait in new[] { "", "Als.Gait.Sprinting", "Als.Gait.Sprinting.Child" })
        foreach (var mode in new[] { "", "Als.LocomotionMode.InAir", "Als.LocomotionMode.InAir.Child" })
        for (var bits = 0; bits < 4; bits++)
        {
            var input = new AlsRefactoredWeaponRuleInput(rotation, gait, mode, (bits & 1) != 0, (bits & 2) != 0);
            var aim = rotation == "Als.RotationMode.Aiming";
            bool[] expected = [aim, !aim, elapsed >= 3 && input.TransitionsAllowed, elapsed >= 3 && input.Moving,
                aim, gait == "Als.Gait.Sprinting" || mode == "Als.LocomotionMode.InAir"];
            Assert.Equal(expected, rules.Select(r => r.Matches(input, elapsed)));
            // Evaluate exits in baked priority order, never combine all Ready -> Relaxed edges.
            Assert.Equal(resources.States[2].Exits.ToArray().FirstOrDefault(e => expected[e], -1),
                resources.States[2].Exits.ToArray().FirstOrDefault(e => rules[e].Matches(input, elapsed), -1));
        }
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("property")]
    [InlineData("clock")]
    [InlineData("delay")]
    [InlineData("link")]
    [InlineData("operand")]
    public void UnsupportedNativeRuleChangesAreRejected(string change)
    {
        var catalog = Catalog(); var source = AlsRefactoredWeaponMachineResources.Blueprint(AlsRefactoredWeaponKind.PistolOneHanded);
        var payload = catalog.Read(source);
        var node = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("compiledNodeIndex").GetInt32() == 39);
        var graph = AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, source, node.GetProperty("graph").GetString()!);
        const string machine = "AB_Als_PistolOneHanded:Overlay.AnimGraphNode_StateMachine_0";
        Assert.Equal(AlsRefactoredWeaponRuleKind.AllowedAfterDelay, AlsRefactoredWeaponRuleCompiler.CompileGraph(graph, machine, 2).Kind);
        graph = change switch
        {
            "operator" => graph.Replace("GreaterEqual_DoubleDouble", "Greater_DoubleDouble"),
            "property" => graph.Replace("bTransitionsAllowed", "bMoving"),
            "clock" => graph.Replace(machine, "AB_Als_PistolOneHanded:Overlay.ForeignMachine"),
            "delay" => graph.Replace("3.000000", "2.000000"),
            "link" => graph.Replace("LinkedTo=(K2Node_AnimGetter_3", "LinkedTo=(MissingClock"),
            "operand" => graph.Replace("PinName=\"ErrorTolerance\"", "PinName=\"C\""),
            _ => throw new ArgumentException()
        };
        Assert.ThrowsAny<Exception>(() => AlsRefactoredWeaponRuleCompiler.CompileGraph(graph, machine, 2));
    }

    [Fact]
    public void NestedGraphIdentityMustBeUniqueAndComplete()
    {
        var catalog = Catalog(); var source = AlsRefactoredWeaponMachineResources.Blueprint(AlsRefactoredWeaponKind.Bow);
        var payload = catalog.Read(source); var text = payload.GetProperty("nativeText").GetString()!;
        var path = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("class").GetString() == "AnimGraphNode_TransitionResult").GetProperty("graph").GetString()!;
        Assert.Throws<ArgumentException>(() => AlsNativeNestedGraph.Extract(text + text, source, path));
        Assert.Throws<ArgumentException>(() => AlsNativeNestedGraph.Extract(text, source, path + ".Missing"));
        Assert.Throws<ArgumentException>(() => AlsNativeNestedGraph.Extract(text, source + "Foreign", path));
    }
}
