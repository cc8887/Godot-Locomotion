using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPoseCurveTests(ITestOutputHelper output)
{
    private static readonly string[] Layout = ["PoseGrounded", "PoseInAir", "PoseMoving", "PoseStanding", "FootLeftIk", "FootRightIk", "FootLeftLock", "FootRightLock", "unrelated"];
    private static string Source() => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_pose_curve_inputs.json"));

    [Fact]
    public void MovementCacheBindsOnlyItsOwnCurveAndRejectsOtherGraphSites()
    {
        var definitions = AlsRefactoredPoseCurveCompiler.Compile(Source());
        string[] names = ["PoseMoving", "unrelated"];
        var runtime = new AlsRefactoredPoseCurveRuntime(definitions, names,
            [AlsRefactoredPoseCurveSite.StandingMovement, AlsRefactoredPoseCurveSite.CrouchingMovement]);
        AlsInertialCurve[] curves = [default, new(.7f)];
        runtime.Apply(AlsRefactoredPoseCurveSite.StandingMovement, curves, 0);
        Assert.Equal(new(1), curves[0]); Assert.Equal(new(.7f), curves[1]);
        curves[0] = default;
        runtime.Apply(AlsRefactoredPoseCurveSite.CrouchingMovement, curves, 0);
        Assert.Equal(new(1), curves[0]);
        Assert.Throws<ArgumentException>(() => runtime.Apply(AlsRefactoredPoseCurveSite.Grounded, curves, 0));
        Assert.Throws<ArgumentException>(() => new AlsRefactoredPoseCurveRuntime(definitions, names));
        Assert.Throws<ArgumentException>(() => new AlsRefactoredPoseCurveRuntime(definitions, names,
            [AlsRefactoredPoseCurveSite.StandingMovement, AlsRefactoredPoseCurveSite.StandingMovement]));
    }

    [Fact]
    public void OriginalProducersKeepCachePlacementAndDynamicAirFootAlpha()
    {
        var definitions = AlsRefactoredPoseCurveCompiler.Compile(Source());
        Assert.Equal(8, definitions.Length);
        Assert.Equal(2, definitions.Count(d => d.UseGroundPrediction));
        var runtime = new AlsRefactoredPoseCurveRuntime(definitions, Layout);
        var moving = new AlsInertialCurve[Layout.Length]; moving[^1] = new(.6f, true);
        runtime.Apply(AlsRefactoredPoseCurveSite.StandingMovement, moving, 0);
        Assert.Equal(new(1, true), moving[2]); Assert.False(moving[0].Present);
        // A moving branch's 1 must subsequently participate in the outer blend.
        var blended = AlsStandingCycleCurves.Lerp(default, moving[2], .25f);
        Assert.Equal(.25f, blended.Value);
        runtime.Apply(AlsRefactoredPoseCurveSite.FallFeet, moving, .4f);
        Assert.Equal(.4f, moving[4].Value); Assert.Equal(.4f, moving[5].Value);
        runtime.Apply(AlsRefactoredPoseCurveSite.FallPose, moving, .4f);
        Assert.Equal(1, moving[1].Value); Assert.Equal(1, moving[3].Value); Assert.Equal(.6f, moving[^1].Value);
        runtime.Apply(AlsRefactoredPoseCurveSite.Grounded, moving, 0);
        Assert.Equal(1, moving[0].Value); Assert.Equal(1, moving[4].Value);
    }

    [Theory]
    [InlineData(-.5f, .2f)]
    [InlineData(0f, .2f)]
    [InlineData(.5f, .6f)]
    [InlineData(1.5f, 1f)]
    public void AirFootWriteClampsNodeAlphaAndInsertsMissingCurves(float prediction, float expected)
    {
        var runtime = new AlsRefactoredPoseCurveRuntime(AlsRefactoredPoseCurveCompiler.Compile(Source()), Layout);
        var curves = new AlsInertialCurve[Layout.Length]; curves[4] = new(.2f);
        runtime.Apply(AlsRefactoredPoseCurveSite.JumpFeet, curves, prediction);
        Assert.Equal(expected, curves[4].Value);
        Assert.True(curves[5].Present);
        Assert.Equal(Math.Clamp(prediction, 0, 1), curves[5].Value);
        Assert.False(curves[0].Present);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("placement")]
    [InlineData("curve")]
    public void ChangingPredictionBindingCacheConnectionOrNameIsRejected(string change)
    {
        var json = JsonNode.Parse(Source())!;
        var writers = json["writers"]!.AsArray();
        var writer = writers.Single(w => w!["path"]!.GetValue<string>().Contains("AnimStateNode_1.Fall.AnimGraphNode_ModifyCurve_3", StringComparison.Ordinal))!;
        if (change == "binding")
        {
            var properties = writer["bindings"]!.AsArray().Single()!["properties"]!.AsArray();
            var index = Enumerable.Range(0, properties.Count).Single(i => properties[i]!.GetValue<string>().StartsWith("PropertyBindings=", StringComparison.Ordinal));
            properties[index] = properties[index]!.GetValue<string>().Replace("GroundPredictionAmount", "VerticalVelocityWorldSpace", StringComparison.Ordinal);
        }
        else if (change == "placement") writer["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "SourcePose")!["links"] = "AnimGraphNode_StateResult_0 fake,";
        else
        {
            var properties = writer["properties"]!.AsArray();
            var index = Enumerable.Range(0, properties.Count).Single(i => properties[i]!.GetValue<string>().StartsWith("Node=", StringComparison.Ordinal));
            properties[index] = properties[index]!.GetValue<string>().Replace("FootLeftIk", "Enable_FootIK_L", StringComparison.Ordinal);
        }
        Assert.Throws<InvalidDataException>(() => AlsRefactoredPoseCurveCompiler.Compile(json.ToJsonString()));
    }

    [Fact]
    public void CachedUpdatePoseAndCurrentRigCurveAreSeparateAndIdentityChecked()
    {
        var reader = new AlsRefactoredPoseCurveReader(Layout);
        var old = new AlsInertialCurve[Layout.Length]; old[0] = new(.2f); old[1] = new(.8f); old[2] = new(.15f);
        var identity = new AlsFrameIdentity(5, 2, 1);
        var history = reader.Read(identity, old);
        old[0] = new(1); old[1] = new(0); old[2] = new(1);
        Assert.Equal(.15f, history.Moving); Assert.Equal(.6f, history.PelvisAmount(.5f));
        var input = default(AlsRefactoredFootRigInput) with { PreviousIdentity = identity };
        Assert.Equal(.6f, history.Apply(input, .5f).PelvisAmount);
        Assert.Throws<ArgumentException>(() => history.Apply(input with { PreviousIdentity = default }, .5f));
    }

    [Fact]
    public void PelvisFormulaMatchesActualGetControlRigInput()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("tests/Als.Core.Tests/Fixtures/FootIk/native_rig_pose_input.json")));
        var count = 0; double error = 0;
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            var history = new AlsRefactoredPoseCurveHistory(default, row.GetProperty("grounded").GetSingle(), row.GetProperty("airborne").GetSingle(), 0);
            var actual = history.PelvisAmount(row.GetProperty("prediction").GetSingle());
            var expected = row.GetProperty("result").GetSingle();
            error = Math.Max(error, Math.Abs(actual - expected));
            Assert.Equal(expected, actual); count++;
        }
        output.WriteLine($"nativeSamples={count} maxPelvisAmountError={error:R}");
        Assert.Equal(294, count);
    }
}
