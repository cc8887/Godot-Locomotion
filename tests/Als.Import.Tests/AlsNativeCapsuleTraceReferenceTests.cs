using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsNativeCapsuleTraceReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FailedChainFreshCapsuleBoxQueriesMatchNativeAtTheirActualCullDistance()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_native_capsule_trace_reference.json")));
        var root = doc.RootElement; var rows = 0; var active = 0; var pointCount = 0; double maxPoint = 0, maxNormal = 0, maxPhi = 0;
        Assert.Equal(726, root.GetProperty("traceCount").GetInt32());
        Assert.Equal(456, root.GetProperty("skippedNonCapsuleBoxCount").GetInt32());
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var source = row.GetProperty("source"); var points = row.GetProperty("points"); var captured = source.GetProperty("contacts");
            var swapped = row.GetProperty("swapped").GetBoolean();
            Assert.InRange(source.GetProperty("frame").GetInt32(), 1140, 1145);
            Assert.Equal((double)source.GetProperty("cullDistance").GetSingle(), D(row, "cullDistance"));
            Assert.Equal(points.GetArrayLength(), captured.GetArrayLength());
            if (points.GetArrayLength() > 0) active++;
            for (var i = 0; i < points.GetArrayLength(); i++)
            {
                var p = points[i]; var c = captured[i];
                var p0 = V(p, swapped ? "point1" : "point0").ToSingle();
                var p1 = V(p, swapped ? "point0" : "point1").ToSingle();
                var normal = V(p, "normal1");
                if (swapped) normal = (normal.Rotate(Pose(row.GetProperty("boxPose")).Rotation) * -1).Rotate(Pose(row.GetProperty("capsulePose")).Rotation.Conjugate());
                maxPoint = Math.Max(maxPoint, Math.Max(Vector3.Distance(p0, V(c, "localPoint0").ToSingle()), Vector3.Distance(p1, V(c, "localPoint1").ToSingle())));
                maxNormal = Math.Max(maxNormal, Vector3.Distance(normal.ToSingle(), V(c, "localNormal1").ToSingle()));
                maxPhi = Math.Max(maxPhi, Math.Abs(p.GetProperty("phi").GetSingle() - c.GetProperty("nativePhi").GetSingle()));
                pointCount++;
            }
            rows++;
        }
        output.WriteLine($"NATIVE_CAPSULE_TRACE pairs={rows} active={active} points={pointCount} point={maxPoint:R} normal={maxNormal:R} phi={maxPhi:R}");
        Assert.Equal(270, rows); Assert.Equal(24, active); Assert.Equal(54, pointCount);
        Assert.Equal(0, maxPoint); Assert.Equal(0, maxNormal); Assert.Equal(0, maxPhi);
    }
}
