using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCharacterMovementCompilerTests
{
    [Fact]
    public void CompilesAuthoredCurvesAndAllModeStanceGaitBindings()
    {
        var model = AlsCharacterMovementCompiler.Compile(Read(), Rotation());
        Assert.Equal(0, model.VelocitySettings.BrakingFrictionFactor);
        Assert.Equal(25, model.VelocitySettings.MinAnalogSpeed);
        foreach (var mode in Enum.GetValues<AlsRotationMode>())
        foreach (var stance in Enum.GetValues<AlsStance>())
        foreach (var gait in Enum.GetValues<AlsGait>())
        {
            var start = model.Sample(0, mode, stance, gait);
            Assert.Equal(stance == AlsStance.Standing ? 2000 : 2500, start.MaxAcceleration);
            Assert.Equal(stance == AlsStance.Standing ? 5 : 10, start.GroundFriction);
            var walk = stance == AlsStance.Crouching ? 150 : mode == AlsRotationMode.Aiming ? 165 : 175;
            Assert.Equal(gait == AlsGait.Walking ? walk : stance == AlsStance.Crouching
                ? gait == AlsGait.Running ? 200 : 300 : gait == AlsGait.Running ? 375 : 650, start.MaxSpeed);
            Assert.Equal(0, start.MappedSpeed);
        }
        var run = model.Sample(375, AlsRotationMode.LookingDirection, AlsStance.Standing, AlsGait.Running);
        Assert.Equal(new AlsDynamicMovementSettings(2, 375, 2000, 1250, 4), run);
        var sprint = model.Sample(650, AlsRotationMode.LookingDirection, AlsStance.Standing, AlsGait.Sprinting);
        Assert.Equal(new AlsDynamicMovementSettings(3, 650, 750, 500, .5f), sprint);
        Assert.Equal(2000, model.Sample(390, AlsRotationMode.LookingDirection, AlsStance.Standing, AlsGait.Sprinting).MaxAcceleration);
    }

    [Theory]
    [InlineData("binding")] [InlineData("axis")] [InlineData("order")] [InlineData("curve")]
    [InlineData("sample")] [InlineData("table")] [InlineData("defaults")]
    public void RejectsIncompleteOrChangedMovementSources(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        if (mutation is "binding" or "axis" or "order")
        {
            var graph = root["graphs"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "UpdateDynamicMovementSettings")!;
            var value = graph["nativeText"]!.GetValue<string>();
            graph["nativeText"] = mutation switch
            {
                "binding" => value.Replace("GetVectorValue", "GetFloatValue", StringComparison.Ordinal),
                "axis" => value.Replace("ReturnValue_X", "ReturnValue_W", StringComparison.Ordinal),
                _ => value.Replace("then_1", "then_9", StringComparison.Ordinal)
            };
        }
        else if (mutation == "curve") root["movementCurves"]![0]!["nativeText"] = root["movementCurves"]![0]!["nativeText"]!
            .GetValue<string>().Replace("RCIM_Constant", "RCIM_Linear", StringComparison.Ordinal);
        else if (mutation == "sample") root["movementCurves"]![0]!["verification"]![0]!["value"]![0] = 99999;
        else if (mutation == "table") root["movementModel"]!["row"] = "Responsive";
        else root["componentDefaults"]!["min_analog_walk_speed"] = -1;
        Assert.ThrowsAny<Exception>(() => AlsCharacterMovementCompiler.Compile(root.ToJsonString(), Rotation()));
    }

    [Fact]
    public void VelocityIntegrationMatchesActualNativeComponent()
    {
        using var document = JsonDocument.Parse(Read());
        var root = document.RootElement;
        var defaults = root.GetProperty("componentDefaults");
        var settings = new AlsGroundVelocity.Settings(defaults.GetProperty("braking_friction_factor").GetSingle(),
            defaults.GetProperty("braking_sub_step_time").GetSingle(), defaults.GetProperty("use_separate_braking_friction").GetBoolean(),
            defaults.GetProperty("braking_friction").GetSingle(), defaults.GetProperty("min_analog_walk_speed").GetSingle());
        var cases = root.GetProperty("velocityVerification");
        Assert.Equal(3600, cases.GetArrayLength());
        var index = 0;
        foreach (var row in cases.EnumerateArray())
        {
            var actual = AlsGroundVelocity.Calculate(Vector(row.GetProperty("velocity")), Vector(row.GetProperty("acceleration")),
                row.GetProperty("delta").GetSingle(), row.GetProperty("friction").GetSingle(), row.GetProperty("brakingDeceleration").GetSingle(),
                row.GetProperty("maxSpeed").GetSingle(), row.GetProperty("analog").GetSingle(),
                settings with { BrakingFrictionFactor = row.GetProperty("brakingFrictionFactor").GetSingle() });
            var expected = Vector(row.GetProperty("result"));
            Assert.True((actual - expected).LengthSquared <= 1e-16,
                $"Native CalcVelocity case {index}: expected={expected}, actual={actual}, difference={actual - expected}");
            index++;
        }
    }

    private static AlsDoubleVector Vector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    internal static string Read()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_character_movement_inputs.json"));
    }
    private static string Rotation() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_character_rotation_inputs.json"));
}
