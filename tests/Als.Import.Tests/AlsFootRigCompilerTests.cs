using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsFootRigCompilerTests
{
    internal static string PathInRepository(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "assets/config/refactored_foot_rig_inputs.json"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository not found."), relative);
    }
    internal static string Source() => File.ReadAllText(PathInRepository("assets/config/refactored_foot_rig_inputs.json"));

    [Fact]
    public void AuthoredLegFunctionCompilesWithDistinctAxesAndUnclampedMovingLerps()
    {
        var d = AlsFootRigCompiler.Compile(Source());
        Assert.Equal(new(-1, 0, 0), d.LeftPrimary); Assert.Equal(new(0, 1, 0), d.LeftSecondary);
        Assert.Equal(-d.LeftPrimary.X, d.RightPrimary.X); Assert.Equal(-d.LeftSecondary.Y, d.RightSecondary.Y);
        Assert.Equal(40, d.Leg.PoleDistance); Assert.Equal(.05f, d.Leg.PoleHalfLife);
        Assert.Equal(12, d.Leg.OffsetFrequency); Assert.Equal(2, d.Leg.OffsetDamping); Assert.Equal(.99f, d.Leg.MaximumStretch);
        Assert.Equal(56, d.Leg.MinimumPelvisDistance.Interpolate(1.2));
        Assert.Equal(3, d.Leg.Swing2Minimum.Interpolate(1.2));
        Assert.Equal(-1, d.Leg.Swing2Maximum.Interpolate(1.2));
        Assert.Equal("PoseMoving", d.MovingCurve);
    }

    [Theory]
    [InlineData("rigModel", "GetTransform", "bInitial", "false")]
    [InlineData("rigModel", "GetTransform", "Space", "LocalSpace")]
    [InlineData("feet", "ApplyFootIk", "ThighItem.Name", "thigh_r")]
    [InlineData("applyFoot", "GetCurveValue", "Curve", "Weight_Gait")]
    [InlineData("applyFoot", "TwoBoneIKSimplePerItem", "bEnableStretch", "true")]
    [InlineData("applyFoot", "Set Transform", "bPropagateToChildren", "False")]
    public void UnsupportedExecutionContractsAreRejected(string section, string node, string pin, string value)
    {
        var json = JsonNode.Parse(Source())!;
        var properties = json[section]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == node)!["pins"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == pin)!["properties"]!.AsArray();
        var index = Enumerable.Range(0, properties.Count).Single(i => properties[i]!.GetValue<string>().StartsWith("DefaultValue=", StringComparison.Ordinal));
        properties[index] = "DefaultValue=\"" + value + "\"";
        Assert.Throws<InvalidDataException>(() => AlsFootRigCompiler.Compile(json.ToJsonString()));
    }

    [Fact]
    public void ChangingTheCalledFunctionCannotPassAsTheSameNode()
    {
        var json = JsonNode.Parse(Source())!;
        var properties = json["feet"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "ApplyFootIk")!["properties"]!.AsArray();
        var index = Enumerable.Range(0, properties.Count).Single(i => properties[i]!.GetValue<string>().StartsWith("ReferencedFunctionHeader=", StringComparison.Ordinal));
        properties[index] = properties[index]!.GetValue<string>().Replace("RigVMFunctionLibrary.ApplyFootIk", "RigVMFunctionLibrary.RefreshHandIk", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AlsFootRigCompiler.Compile(json.ToJsonString()));
    }

    [Fact]
    public void RemovingTheIkToAnkleExecutionEdgeCannotPassAsTheSameGraph()
    {
        var json = JsonNode.Parse(Source())!;
        var nodes = json["applyFoot"]!.AsArray();
        nodes.Remove(nodes.Single(n => n!["properties"]!.AsArray().Any(p => p!.GetValue<string>() == "SourcePinPath=\"TwoBoneIKSimplePerItem.ExecutePin\"")));
        Assert.Throws<InvalidDataException>(() => AlsFootRigCompiler.Compile(json.ToJsonString()));
    }

    [Fact]
    public void BindingUsesLeftReferenceChainForBothLegsAndSelectsTheRequestedRoot()
    {
        var d = AlsFootRigCompiler.Compile(Source());
        string[] names = ["pelvis", "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r", "ik_foot_root", "VB foot_root"];
        int[] parents = [-1, 0, 1, 2, 0, 4, 5, -1, -1];
        double[] z = [100, 90, 50, 9, 90, 42, 12, 0, 0];
        var reference = z.Select(v => new AlsPrecisePose(new(0, 0, v), AlsQuaternion.Identity, AlsDoubleVector.One)).ToArray();
        var binding = d.Bind(names, parents, reference, true);
        Assert.Equal(9, binding.FootHeight); Assert.Equal(81, binding.LegLength); Assert.Equal(7, binding.FootRoot);
        Assert.Equal(8, d.Bind(names, parents, reference, false).FootRoot);
        Assert.Equal(binding, d.Bind(names.Select(n => n.ToUpperInvariant()).ToArray(), parents, reference, true));
        var duplicate = names.ToArray(); duplicate[8] = "PELVIS";
        Assert.Throws<InvalidDataException>(() => d.Bind(duplicate, parents, reference, true));
        parents[3] = 0;
        Assert.Throws<InvalidDataException>(() => d.Bind(names, parents, reference, true));
    }
}
