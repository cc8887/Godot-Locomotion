using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPelvisIkInputTests
{
    [Fact]
    public void CompilesAuthoredCurvesSpeedsAndDefaults()
    {
        var model = Compile();
        Assert.Equal("Enable_FootIK_L", model.LeftCurve); Assert.Equal("Enable_FootIK_R", model.RightCurve);
        Assert.Equal(10, model.UpSpeed); Assert.Equal(15, model.DownSpeed);
        Assert.Equal(default, model.InitialState);
    }

    [Theory]
    [InlineData("Less_DoubleDouble", "LessEqual_DoubleDouble")]
    [InlineData("Divide_DoubleDouble", "Multiply_DoubleDouble")]
    [InlineData("VInterpTo", "VInterpTo_Constant")]
    [InlineData("Enable_FootIK_L", "FootLock_L")]
    [InlineData("MemberScope=\"SetPelvisIKOffset\"", "MemberScope=\"OtherFunction\"")]
    [InlineData("MemberName=\"PelvisAlpha\"", "MemberName=\"OtherAlpha\"")]
    public void RejectsChangedFunctionSemantics(string before, string after)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "SetPelvisIKOffset")!;
        var text = graph["nativeText"]!.GetValue<string>(); Assert.Contains(before, text);
        graph["nativeText"] = text.Replace(before, after, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AlsPelvisIkInputCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void MatchesNativeVectorInterpolationIncludingCentimeterSnapThreshold()
    {
        var root = JsonNode.Parse(Read())!;
        var samples = root["nativeVectorInterpolation"]!.AsArray(); Assert.Equal(24, samples.Count);
        foreach (var sample in samples)
        {
            var speed = sample!["speed"]!.GetValue<float>();
            var model = new AlsPelvisIkInputModel("L", "R", speed, speed, default);
            var current = Vector(sample["current"]!); var target = Vector(sample["target"]!);
            var result = model.Evaluate(new(1, current), target, target, 1, 1, sample["delta"]!.GetValue<double>());
            var expected = Vector(sample["result"]!);
            for (var axis = 0; axis < 3; axis++) Assert.Equal(expected[axis], result.Offset[axis], 12);
        }
    }

    [Fact]
    public void SelectsWholeLowerVectorAndRightVectorOnEqualHeight()
    {
        var model = Compile();
        var lower = model.Evaluate(default, new(3, 7, -20), new(-5, 2, -10), 1, 1, 1);
        Assert.Equal(new AlsDoubleVector(3, 7, -20), lower.Offset);
        var tie = model.Evaluate(default, new(3, 7, -20), new(-5, 2, -20), 1, 1, 1);
        Assert.Equal(new AlsDoubleVector(-5, 2, -20), tie.Offset);
    }

    [Fact]
    public void AlphaIsUnclampedMeanAndDoesNotScaleTheStoredOffset()
    {
        var model = Compile(); var target = new AlsDoubleVector(0, 0, -80);
        var partial = model.Evaluate(default, target, target, 1, 0, 1);
        Assert.Equal(.5, partial.Alpha); Assert.Equal(target, partial.Offset);
        var over = model.Evaluate(default, target, target, 3, 1, 1);
        Assert.Equal(2, over.Alpha); Assert.Equal(target, over.Offset);
        var off = model.Evaluate(partial, target, target, -1, 0, 1.0 / 60);
        Assert.Equal(-.5, off.Alpha); Assert.Equal(default, off.Offset);
    }

    [Fact]
    public void RiseAndDescentUseNativeLinearDeltaWeights()
    {
        var model = Compile();
        var down = model.Evaluate(default, new(0, 0, -20), new(0, 0, -10), 1, 1, 1.0 / 60);
        Assert.Equal(-20 * ((float)(1.0 / 60) * 15), down.Offset.Z);
        var up = model.Evaluate(new(1, new(0, 0, -30)), new(0, 0, -20), new(0, 0, -10), 1, 1, 1.0 / 60);
        Assert.Equal(-30 + 10.0 * ((float)(1.0 / 60) * 10), up.Offset.Z);
    }

    [Fact]
    public void FailedOrDiscardedCallCannotAdvanceCallerHistory()
    {
        var model = Compile(); var previous = new AlsPelvisIkInputState(1, new(0, 0, -10));
        var target = new AlsDoubleVector(0, 0, -20);
        var candidate = model.Evaluate(previous, target, target, 1, 1, 1.0 / 60);
        Assert.Throws<ArgumentException>(() => model.Evaluate(previous, target, target, float.NaN, 1, 1.0 / 60));
        Assert.Equal(new AlsPelvisIkInputState(1, new(0, 0, -10)), previous);
        Assert.Equal(candidate, model.Evaluate(previous, target, target, 1, 1, 1.0 / 60));
    }

    private static AlsDoubleVector Vector(JsonNode value) => new(value[0]!.GetValue<double>(), value[1]!.GetValue<double>(), value[2]!.GetValue<double>());
    private static AlsPelvisIkInputModel Compile() => AlsPelvisIkInputCompiler.Compile(Read());
    private static string Read() => File.ReadAllText(Environment.GetEnvironmentVariable("ALS_FOOT_IK_TEST_INPUT") ??
        Path.Combine(RepositoryRoot.Find(), "assets/config/v4_foot_ik_inputs.json"));
}
