using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsSphereBoxReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FullSphereBoxManifoldMatchesNativeGeometry()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_sphere_box_reference.json")));
        var rows = doc.RootElement.GetProperty("cases"); Assert.Equal(8064, rows.GetArrayLength());
        var index = 0; var active = 0; double maxPoint = 0, maxNormal = 0, maxPhi = 0; var errors = new List<string>();
        foreach (var row in rows.EnumerateArray())
        {
            var found = AlsSphereBoxManifold.Build(V(row, "center").ToSingle(), row.GetProperty("radius").GetSingle(), Pose(row.GetProperty("spherePose")),
                V(row, "boxMin"), V(row, "boxMax"), Pose(row.GetProperty("boxPose")), row.GetProperty("cull").GetSingle(), out var p);
            var points = row.GetProperty("points");
            if (found != (points.GetArrayLength() == 1)) { if (errors.Count < 15) errors.Add($"{index}: count"); }
            else if (found)
            {
                active++; var n = points[0]; var point = Math.Max(Vector3.Distance(p.Point0, V(n, "point0").ToSingle()), Vector3.Distance(p.Point1, V(n, "point1").ToSingle()));
                var normal = Vector3.Distance(p.Normal1, V(n, "normal1").ToSingle()); var phi = MathF.Abs(p.NativePhi!.Value - n.GetProperty("phi").GetSingle());
                maxPoint = Math.Max(maxPoint, point); maxNormal = Math.Max(maxNormal, normal); maxPhi = Math.Max(maxPhi, phi);
                if ((point != 0 || normal != 0 || phi != 0) && errors.Count < 15) errors.Add($"{index}: point={point:R} normal={normal:R} phi={phi:R}");
            }
            index++;
        }
        output.WriteLine($"SPHERE_BOX cases={index} active={active} point={maxPoint:R} normal={maxNormal:R} phi={maxPhi:R}");
        Assert.True(active > 0); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
