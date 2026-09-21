using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsCapsuleConvexReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ActualCookedFeetMatchNativeCapsuleManifolds()
    {
        const string file = "v4_physics_capsule_convex_reference.json";
        const int expectedCases = 4320;
        string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
        var definition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"), AlsPhysicsAssetCompiler.MeshRoot + "AnimMan.AnimMan");
        var hulls = AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"), definition).ToDictionary(t => definition.Bodies[t.Body].Bone, t => t.Topology);
        var counts = new int[4];
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file)));
        var work = new AlsCapsuleManifoldWorkspace(); var actual = new AlsDetectedContact[3];
        var index = 0; double maxP = 0, maxN = 0, maxPhi = 0; var errors = new List<string>();
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var geometry = new AlsCapsuleGeometry(V(row, "endpoint0").ToSingle(), row.TryGetProperty("axis", out _) ? V(row, "axis").ToSingle() : Vector3.UnitZ, (float)D(row, "length"), (float)D(row, "radius"));
            var relative = AlsPrecisePose.Relative(Pose(row.GetProperty("capsulePose")), Pose(row.GetProperty("boxPose")));
            var topology = hulls[row.GetProperty("bone").GetString()!];
            // Native TGJKShape queries the full hull; wrapper margin is not a support inset.
            var convex = row.GetProperty("mode").GetInt32() < 2 ? new AlsConvexPolygonShape(topology) : new AlsConvexPolygonShape(topology, V(row, "scale"));
            var count = AlsCapsuleConvexManifold.Build(geometry, relative, convex, work, actual, D(row, "cullDistance"));
            counts[count]++;
            var expected = row.GetProperty("points");
            if (count != expected.GetArrayLength()) { if (errors.Count < 20) errors.Add($"{index}: count={count} native={expected.GetArrayLength()}"); }
            else for (var i = 0; i < count; i++)
            {
                var p = Math.Max(Vector3.Distance(actual[i].Point0, V(expected[i], "point0").ToSingle()), Vector3.Distance(actual[i].Point1, V(expected[i], "point1").ToSingle()));
                var n = Vector3.Distance(actual[i].Normal1, V(expected[i], "normal1").ToSingle());
                var phi = Math.Abs(actual[i].NativePhi!.Value - (float)D(expected[i], "phi"));
                maxP = Math.Max(maxP, p); maxN = Math.Max(maxN, n); maxPhi = Math.Max(maxPhi, phi);
                if ((p != 0 || n != 0 || phi != 0) && errors.Count < 20) errors.Add($"{index}/{i}: p={p:R} n={n:R} phi={phi:R}");
            }
            index++;
        }
        output.WriteLine($"CAPSULE_CONVEX counts={string.Join(',', counts)} cases={index} point={maxP:R} normal={maxN:R} phi={maxPhi:R}");
        Assert.Equal(expectedCases, index); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
