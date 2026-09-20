using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsYawOffsetCompilerTests
{
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_yaw_inputs.json"));

    [Fact]
    public void CompilesNativePinsKeysAndAll2882VectorSamples()
    {
        var profile = AlsYawOffsetCompiler.Compile(Read());
        Assert.Equal(new Vector4(0, 0, 0, 0), profile.Sample(0));
        Assert.Equal(new Vector4(65, 0, 0, 0), profile.Sample(90));
        Assert.Equal(new Vector4(-60, -60, 0, 30), profile.Sample(-60));
        var prior = new Vector4(1, 2, 3, 4);
        Assert.Equal(prior, profile.Update(prior, false, true, 90));
        Assert.Equal(prior, profile.Update(prior, true, false, 90));
        Assert.Equal(profile.Sample(90), profile.Update(prior, true, true, 90));
    }

    [Theory]
    [InlineData("component")] [InlineData("control")] [InlineData("gate")]
    [InlineData("keys")] [InlineData("defaults")] [InlineData("sample")]
    public void RejectsChangedSourceSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "component": root["rotationGraphText"] = root["rotationGraphText"]!.GetValue<string>().Replace("ReturnValue_X", "ReturnValue_Z"); break;
            case "control": root["rotationGraphText"] = root["rotationGraphText"]!.GetValue<string>().Replace("GetControlRotation", "GetActorRotation"); break;
            case "gate": root["updateGraphText"] = root["updateGraphText"]!.GetValue<string>().Replace("WhileTrue", "ChangedToTrue"); break;
            case "keys": root["curves"]![0]!["nativeText"] = root["curves"]![0]!["nativeText"]!.GetValue<string>().Replace("Time=-207.300003", "InterpMode=RCIM_Cubic,Time=-207.300003"); break;
            case "defaults": root["yawDefaults"]![0] = 1; break;
            case "sample": root["curves"]![0]!["verification"]![0]!["x"] = 100; break;
        }
        Assert.Throws<ArgumentException>(() => AlsYawOffsetCompiler.Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData(0, -1, 0, 0)]
    [InlineData(1, 0, 0, 90)]
    [InlineData(-1, 0, 0, -90)]
    [InlineData(0, 1, 0, 180)]
    [InlineData(1, 0, -90, 0)]
    [InlineData(0, -1, 270, -90)]
    [InlineData(0, 0, 45, 45)]
    public void MapsVelocityAndControlYawWithoutUsingCharacterOrSmoothedAim(float x, float z, float controlDegrees, float expected)
    {
        Assert.InRange(MathF.Abs(AlsYawOffset.VelocityRelativeControlDegrees(new(x, 0, z),
            controlDegrees * MathF.PI / 180) - expected), 0, .00002f);
    }
}
