using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsCapsulePairReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void DynamicAndKinematicCapsulesMatchNativeManifolds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_capsule_pair_reference.json")));
        var root = doc.RootElement; var actual = new AlsDetectedContact[3]; var errors = new List<string>();
        Assert.Equal(.8f, (float)D(root, "CapsuleAxisAlignedThreshold"));
        Assert.Equal(.05f, (float)D(root, "CapsuleDeepPenetrationFraction"));
        Assert.Equal(.25f, (float)D(root, "CapsuleRadialContactFraction"));
        var index = 0; var counts = new int[4]; double maxP = 0, maxN = 0, maxPhi = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            static AlsCapsuleGeometry Geometry(JsonElement j) => new(V(j, "endpoint0").ToSingle(), V(j, "axis").ToSingle(), (float)D(j, "height"), (float)D(j, "radius"));
            var count = AlsCapsuleCapsuleManifold.Build(Geometry(row.GetProperty("a")), Pose(row.GetProperty("poseA")), B(row, "dynamicA"),
                Geometry(row.GetProperty("b")), Pose(row.GetProperty("poseB")), B(row, "dynamicB"), (float)D(row, "cull"), actual,
                (float)D(root, "CapsuleAxisAlignedThreshold"), (float)D(root, "CapsuleDeepPenetrationFraction"), (float)D(root, "CapsuleRadialContactFraction"));
            var expected = row.GetProperty("points"); counts[count]++;
            if (count != expected.GetArrayLength()) { if (errors.Count < 25) errors.Add($"{index}: count={count} native={expected.GetArrayLength()}"); }
            else for (var i = 0; i < count; i++)
            {
                var p = Math.Max(Vector3.Distance(actual[i].Point0, V(expected[i], "point0").ToSingle()), Vector3.Distance(actual[i].Point1, V(expected[i], "point1").ToSingle()));
                var n = Vector3.Distance(actual[i].Normal1, V(expected[i], "normal1").ToSingle());
                var phi = Math.Abs(actual[i].NativePhi!.Value - (float)D(expected[i], "phi"));
                maxP = Math.Max(maxP, p); maxN = Math.Max(maxN, n); maxPhi = Math.Max(maxPhi, phi);
                if ((p != 0 || n != 0 || phi != 0) && errors.Count < 25) errors.Add($"{index}/{i}: p={p:R} n={n:R} phi={phi:R}");
            }
            index++;
        }
        output.WriteLine($"CAPSULE_PAIR cases={index} counts={string.Join(',', counts)} point={maxP:R} normal={maxN:R} phi={maxPhi:R}");
        Assert.Equal(7056, index); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
