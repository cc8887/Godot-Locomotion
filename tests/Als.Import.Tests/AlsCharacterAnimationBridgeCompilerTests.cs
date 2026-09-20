using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCharacterAnimationBridgeCompilerTests
{
    [Fact]
    public void CompilesConnectedOriginalRotationAndMeshPolicy()
    {
        var policy = AlsCharacterAnimationBridgeCompiler.Compile(Read());
        Assert.Equal(new AlsCharacterAnimationBridgeSettings(3, 30, .001f), policy);
        // Check the sign against the actual axis conversion, independently of
        // the actor's scalar curve consumer.
        var ue = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f);
        var godot = AlsCoordinateConverter.Rotation(ue);
        Assert.InRange(MathF.Abs(godot.Y - MathF.Sin(-.2f)), 0, 1e-6f);
    }

    [Theory]
    [InlineData("source")] [InlineData("curve")] [InlineData("distance")]
    public void RejectsForeignSourceCurveOrInvalidMeshPolicy(string change)
    {
        var json = JsonNode.Parse(Read())!;
        if (change == "source") json["source"] = "OtherCharacter";
        else if (change == "distance") json["teleportDistanceThresholdCm"] = -1;
        else
        {
            var graph = json["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "UpdateGroudedRotation")!;
            graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("RotationAmount", "MissingCurve", StringComparison.Ordinal);
        }
        Assert.Throws<InvalidOperationException>(() => AlsCharacterAnimationBridgeCompiler.Compile(json.ToJsonString()));
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_character_animation_bridge.json"));
}
