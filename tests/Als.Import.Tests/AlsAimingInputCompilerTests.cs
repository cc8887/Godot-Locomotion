using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAimingInputCompilerTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
    private static AlsAimingInputModel Model() => AlsAimingInputCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_aiming_inputs.json"));

    [Fact]
    public void MatchesActualBlueprintContinuousHistoriesAtAllThreeRates()
    {
        var model = Model();
        Assert.Equal(new AlsAimingInputSettings(10, 8), model.Settings);
        Assert.Equal(.5, model.InitialState.AimSweepTime);
        using var document = JsonDocument.Parse(Read("v4_aiming_inputs.json"));
        var count = 0;
        foreach (var trajectory in document.RootElement.GetProperty("trajectories").EnumerateArray())
        {
            var previous = model.InitialState; uint frame = 0;
            foreach (var row in trajectory.GetProperty("frames").EnumerateArray())
            {
                Equal(AlsAimingInputCompiler.ReadState(row.GetProperty("before")), previous);
                var movement = row.GetProperty("movement");
                var input = new AlsAimingObservation(new(++frame, 7, 1), row.GetProperty("delta").GetDouble(),
                    (AlsRotationMode)row.GetProperty("mode").GetInt32(), row.GetProperty("hasInput").GetBoolean(),
                    Rotation(row.GetProperty("actor")), Rotation(row.GetProperty("aim")), movement[0].GetDouble(), movement[1].GetDouble());
                var next = model.Evaluate(input, previous);
                Equal(AlsAimingInputCompiler.ReadState(row.GetProperty("after")), next);
                Assert.Equal(next, model.Evaluate(input, previous));
                Assert.Equal(input.Identity, next.Identity);
                if (input.Mode == AlsRotationMode.VelocityDirection)
                {
                    Assert.Equal(previous.SpineRotation, next.SpineRotation);
                    Assert.Equal(previous.AimSweepTime, next.AimSweepTime);
                }
                if (input.Mode != AlsRotationMode.VelocityDirection || !input.HasInput)
                    Assert.Equal(previous.InputYawOffsetTime, next.InputYawOffsetTime);
                previous = next; count++;
            }
        }
        Assert.Equal(1050, count);
    }

    [Fact]
    public void MatchesNativeWholeRotatorBoundaryCases()
    {
        using var document = JsonDocument.Parse(Read("v4_aiming_inputs.json"));
        var rows = document.RootElement.GetProperty("rotatorBoundaries");
        Assert.Equal(36, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
            Equal(Rotation(row.GetProperty("output")), AlsAimingInputModel.InterpolateRotation(
                Rotation(row.GetProperty("current")), Rotation(row.GetProperty("target")),
                row.GetProperty("delta").GetSingle(), row.GetProperty("speed").GetSingle()));
    }

    [Fact]
    public void RejectsStaleForeignAndRecycledOwnerHistory()
    {
        var model = Model();
        var input = new AlsAimingObservation(new(1, 7, 1), 1.0 / 60, AlsRotationMode.Aiming, true, default, new(25, 90, 0), 1, 0);
        var next = model.Evaluate(input, model.InitialState);
        Assert.Throws<ArgumentException>(() => model.Evaluate(input, next));
        Assert.Throws<ArgumentException>(() => model.Evaluate(input with { Identity = new(2, 8, 1) }, next));
        Assert.Throws<ArgumentException>(() => model.Evaluate(input with { Identity = new(2, 7, 2) }, next));
        Assert.Throws<ArgumentException>(() => model.Evaluate(input with { Delta = double.NaN }, model.InitialState));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("evaluation")]
    [InlineData("defaults")]
    [InlineData("initial")]
    [InlineData("function")]
    [InlineData("precision")]
    [InlineData("spine")]
    [InlineData("update_order")]
    public void RejectsChangedNativeSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_aiming_inputs.json"))!;
        switch (mutation)
        {
            case "source": root["source"] = "Other"; break;
            case "evaluation": root["evaluation"] = "replicated_formula"; break;
            case "defaults": root["settings"]!["InputYawOffsetInterpSpeed"] = 9; break;
            case "initial": root["initial"]!["AimSweepTime"] = 0; break;
            default:
                var graphName = mutation == "update_order" ? "UpdateGraph" : "UpdateAimingValues";
                var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == graphName)!;
                var text = graph["nativeText"]!.GetValue<string>();
                var (from, to) = mutation switch
                {
                    "function" => ("MemberName=\"RInterpTo\"", "MemberName=\"RInterpTo_Constant\""),
                    "precision" => ("PinSubCategory=\"float\"", "PinSubCategory=\"double\""),
                    "spine" => ("DefaultValue=\"4.000000\"", "DefaultValue=\"8.000000\""),
                    _ => ("MemberName=\"UpdateCharacterInfo\"", "MemberName=\"OtherUpdate\"")
                };
                Assert.Contains(from, text);
                graph["nativeText"] = text.Replace(from, to, StringComparison.Ordinal);
                break;
        }
        Assert.ThrowsAny<Exception>(() => AlsAimingInputCompiler.Compile(Read("v4_layering_inputs.json"), root.ToJsonString()));
    }

    private static AlsAimingRotation Rotation(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 1e-8,
        $"Native {expected:R}, actual {actual:R}, error {Math.Abs(expected - actual):R}");
    private static void Equal(AlsAimingRotation expected, AlsAimingRotation actual)
    { Near(expected.Pitch, actual.Pitch); Near(expected.Yaw, actual.Yaw); Near(expected.Roll, actual.Roll); }
    private static void Equal(AlsAimingInputState expected, AlsAimingInputState actual)
    {
        Equal(expected.SmoothedRotation, actual.SmoothedRotation); Equal(expected.SpineRotation, actual.SpineRotation);
        Near(expected.Angle.Yaw, actual.Angle.Yaw); Near(expected.Angle.Pitch, actual.Angle.Pitch);
        Near(expected.SmoothedAngle.Yaw, actual.SmoothedAngle.Yaw); Near(expected.SmoothedAngle.Pitch, actual.SmoothedAngle.Pitch);
        Near(expected.AimSweepTime, actual.AimSweepTime); Near(expected.InputYawOffsetTime, actual.InputYawOffsetTime);
        Near(expected.LeftYawTime, actual.LeftYawTime); Near(expected.RightYawTime, actual.RightYawTime); Near(expected.ForwardYawTime, actual.ForwardYawTime);
    }
}
