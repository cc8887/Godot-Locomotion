using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsCullReferenceTests
{
    [Fact]
    public void CompilerTransportsObservedDetectorAndRejectsInvalidParameters()
    {
        var json = File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_cull_reference.json"));
        var settings = AlsContactDetectorCompiler.Compile(json);
        Assert.Equal(new AlsContactDetectorSettings(3, .01f, 1, 1, 3), settings);
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        root["detector"]!["boundsExpansion"] = -1;
        Assert.Throws<InvalidDataException>(() => AlsContactDetectorCompiler.Compile(root.ToJsonString()));
        root["detector"]!["boundsExpansion"] = 3;
        root["schemaVersion"] = 2;
        Assert.Throws<InvalidDataException>(() => AlsContactDetectorCompiler.Compile(root.ToJsonString()));
    }
    [Fact]
    public void NativeMidphaseSizeScaleAndPreVelocityDistanceMatch()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_cull_reference.json")));
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var cvars = root.GetProperty("cvars");
        var inverseSize = (float)D(cvars, "p.Chaos.Collision.CullDistanceReferenceSize");
        var minimum = (float)D(cvars, "p.Chaos.Collision.MinCullDistanceScale");
        var rows = root.GetProperty("cases"); Assert.Equal(324, rows.GetArrayLength());
        var expanded = 0; var excludedLargeBody = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var bodies = row.GetProperty("bodies"); var a = bodies[0]; var b = bodies[1];
            var scale = AlsContactCullDistance.Scale(Size(a), Size(b), inverseSize, minimum);
            Assert.Equal((float)D(row, "scale"), scale);
            var distance = AlsContactCullDistance.Calculate(D(row, "baseDistance"), scale, D(row, "dt"),
                V(a), V(b), D(row, "velocityInflation"), D(row, "maximumVelocityExpansion"));
            Assert.Equal((float)D(row, "distance"), distance);
            if (distance > (float)D(row, "baseDistance") * scale) expanded++;
            if (!b.GetProperty("dynamic").GetBoolean() && D(b, "boundsSize") > D(a, "boundsSize")) excludedLargeBody++;
        }
        Assert.True(expanded > 0); Assert.Equal(108, excludedLargeBody);
        Assert.True(D(root.GetProperty("detector"), "boundsExpansion") > 0);
    }
    private static double D(JsonElement e, string name) => e.GetProperty(name).GetDouble();
    private static double Size(JsonElement e) => e.GetProperty("dynamic").GetBoolean() && e.GetProperty("hasBounds").GetBoolean()
        ? D(e, "boundsSize") : 0;
    private static Vector3 V(JsonElement e)
    { var v = e.GetProperty("preV"); return new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()); }
}
