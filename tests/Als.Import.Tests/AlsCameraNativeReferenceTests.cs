using System.Text.Json;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraNativeReferenceTests
{
    [Fact]
    public void RotationMatchesActualNativeFunctionsAcrossRatesAndHalfTurnBoundaries()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "refactored_camera_inputs.json")));
        var root = doc.RootElement; var frames = 0;
        foreach (var trace in root.GetProperty("rotationReference").EnumerateArray())
        {
            var delta = trace.GetProperty("delta").GetSingle(); var halfLife = trace.GetProperty("halfLife").GetSingle();
            Assert.Equal(trace.GetProperty("alpha").GetSingle(), AlsCameraMath.DamperAlpha(delta, halfLife));
            var rotation = new AlsAimingRotation(10, 170, -5);
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                rotation = AlsCameraMath.Rotation(rotation, Read(frame.GetProperty("target")), delta, halfLife, true);
                Equal(Read(frame.GetProperty("rotation")), rotation); frames++;
            }
        }
        Assert.Equal(630, frames);
        var boundaries = 0;
        foreach (var sample in root.GetProperty("rotationBoundaries").EnumerateArray())
        {
            Equal(Read(sample.GetProperty("rotation")), AlsCameraMath.Rotation(Read(sample.GetProperty("current")), Read(sample.GetProperty("target")),
                sample.GetProperty("delta").GetSingle(), sample.GetProperty("halfLife").GetSingle(), true)); boundaries++;
        }
        Assert.Equal(112, boundaries);
    }
    private static AlsAimingRotation Read(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static void Equal(AlsAimingRotation expected, AlsAimingRotation actual)
    {
        Assert.InRange(System.Math.Abs(expected.Pitch - actual.Pitch), 0, 1e-9);
        Assert.InRange(System.Math.Abs(expected.Yaw - actual.Yaw), 0, 1e-9);
        Assert.InRange(System.Math.Abs(expected.Roll - actual.Roll), 0, 1e-9);
    }
}
