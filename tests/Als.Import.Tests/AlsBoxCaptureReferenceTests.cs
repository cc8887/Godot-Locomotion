using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsBoxCaptureReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void CapturedHandBoxPosesMatchColdAndSamePoseWarmNativeQueries()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_box_capture_reference.json")));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_runtime_shapes.json")))),
            doc.RootElement.GetProperty("provenance").GetProperty("runtimeShapesSha256").GetString());
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(45, cases.GetArrayLength());
        var work = new AlsConvexManifoldWorkspace(); var cache = new AlsGjkCache(); var points = new AlsDetectedContact[4];
        float maxPoint = 0, maxNormal = 0, maxPhi = 0; var errors = new List<string>(); var count = 0;
        foreach (var row in cases.EnumerateArray())
        {
            var input = row.GetProperty("input"); var id = $"{input.GetProperty("mesh")}/{input.GetProperty("frame")}";
            var a = new AlsBoxPolygonShape(V(input, "half0"), (float)D(input, "margin0"));
            var b = new AlsBoxPolygonShape(V(input, "half1"));
            var relative = AlsPrecisePose.Relative(Pose(input.GetProperty("pose1")), Pose(input.GetProperty("pose0")));
            var reverse = AlsPrecisePose.Relative(Pose(input.GetProperty("pose0")), Pose(input.GetProperty("pose1")));
            cache.Reset(); var passes = row.GetProperty("passes"); Assert.Equal(2, passes.GetArrayLength());
            for (var pass = 0; pass < 2; pass++)
            {
                var result = AlsPolygonManifold.Build(a, b, relative, cache, work, points, 6, (double)1e-6f, (double)1e-6f, 1, .001f, shape0To1: reverse);
                var expected = passes[pass]; Assert.Equal(expected.GetArrayLength(), result.Count);
                for (var i = 0; i < result.Count; i++)
                {
                    var e = expected[i];
                    var p = MathF.Max(Vector3.Distance(points[i].Point0, V(e, "point0").ToSingle()), Vector3.Distance(points[i].Point1, V(e, "point1").ToSingle()));
                    var n = Vector3.Distance(points[i].Normal1, V(e, "normal1").ToSingle());
                    var phi = MathF.Abs(points[i].NativePhi!.Value - (float)D(e, "phi"));
                    maxPoint = MathF.Max(maxPoint, p); maxNormal = MathF.Max(maxNormal, n); maxPhi = MathF.Max(maxPhi, phi); count++;
                    if ((p != 0 || n != 0 || phi != 0) && errors.Count < 20) errors.Add($"{id}/{pass}/{i}: point={p:R} normal={n:R} phi={phi:R}");
                }
            }
        }
        output.WriteLine($"BOX_CAPTURE points={count} maxPoint={maxPoint:R} maxNormal={maxNormal:R} maxPhi={maxPhi:R}");
        Assert.Equal(360, count);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
