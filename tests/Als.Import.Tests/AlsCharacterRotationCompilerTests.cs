using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCharacterRotationCompilerTests
{
    [Fact]
    public void CompilesNativeGraphSettingsCurvesAndRotatorVerification()
    {
        var model = AlsCharacterRotationCompiler.Compile(Read());
        Assert.Equal(.01f, model.Settings.MovingThreshold);
        Assert.Equal(1.5f, model.Settings.ForcedMoveThreshold);
        Assert.Equal(800, model.Settings.VelocityTargetRate);
        Assert.Equal(500, model.Settings.LookingTargetRate);
        Assert.Equal(1000, model.Settings.AimingTargetRate);
        Assert.Equal(20, model.Settings.AimingActorRate);
        Assert.Equal(2, model.Settings.RollActorRate);
        Assert.Equal(5, model.Settings.AirActorRate);
        Assert.Equal(15, model.Settings.AirAimingActorRate);
        Assert.Equal(-100, model.Settings.LimitMin); Assert.Equal(100, model.Settings.LimitMax);
        Assert.Equal(1.75f, model.Movement(AlsRotationMode.LookingDirection, AlsStance.Standing).WalkSpeed);
        Assert.Equal(1.65f, model.Movement(AlsRotationMode.Aiming, AlsStance.Standing).WalkSpeed);
        Assert.Equal(1.5f, model.Movement(AlsRotationMode.Aiming, AlsStance.Crouching).WalkSpeed);
        Assert.Equal(5, model.GroundedRate(3.75f, 0, AlsRotationMode.LookingDirection, AlsStance.Standing));
        Assert.Equal(15, model.GroundedRate(3.75f, 300, AlsRotationMode.LookingDirection, AlsStance.Standing));
        Assert.Equal(36, model.GroundedRate(3.75f, 500, AlsRotationMode.VelocityDirection, AlsStance.Standing));
    }

    [Theory]
    [InlineData("yaw")] [InlineData("smooth")] [InlineData("limit")] [InlineData("curve")]
    [InlineData("native")] [InlineData("table")] [InlineData("enum")]
    public void RejectsSourceChangesRatherThanKeepingApproximateRotation(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        if (mutation is "yaw" or "smooth" or "limit")
        {
            var name = mutation == "yaw" ? "UpdateGroudedRotation" : mutation == "smooth" ? "SmoothCharacterRotation" : "LimitRotation";
            var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == name)!;
            var before = mutation == "yaw" ? "Add_DoubleDouble" : mutation == "smooth" ? "RInterpTo_Constant" : "InRange_FloatFloat";
            graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace(before, "ChangedFunction", StringComparison.Ordinal);
        }
        else if (mutation == "curve")
            root["rotationCurves"]![0]!["nativeText"] = root["rotationCurves"]![0]!["nativeText"]!.GetValue<string>().Replace("Value=0.500000", "Value=5.000000", StringComparison.Ordinal);
        else if (mutation == "native") root["interpolationVerification"]![15]!["constant"] = 900;
        else if (mutation == "table") root["movementModel"]!["row"] = "Responsive";
        else
            root["enums"]![0]!["nativeText"] = root["enums"]![0]!["nativeText"]!.GetValue<string>().Replace("Walking", "DifferentGait", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AlsCharacterRotationCompiler.Compile(root.ToJsonString()));
    }

    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_character_rotation_inputs.json"));
}
