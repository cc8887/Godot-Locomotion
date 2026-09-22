using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsWorldLeafTests
{
    [Fact]
    public void SimulationLeavesRetainImportedGeometryDuringActualContactWindow()
    {
        static string Read(string file) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file));
        using var doc = JsonDocument.Parse(Read("v4_physics_world_leaf_window.json"));
        var root = doc.RootElement;
        Assert.Equal(70, root.GetProperty("windowStartFrame").GetInt32());
        Assert.Equal(10, root.GetProperty("windowFrames").GetInt32());
        Assert.Equal(2, root.GetProperty("cases").GetArrayLength());
        var checkedShapes = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var setup = row.GetProperty("setup"); var mesh = setup.GetProperty("mesh").GetString()!;
            Assert.Equal(120, setup.GetProperty("hz").GetInt32());
            var authored = Read("v4_physics_asset_inputs.json");
            var definition = AlsPhysicsAssetCompiler.Compile(authored, mesh);
            var shapes = AlsRuntimeShapeCompiler.Compile(Read("v4_physics_runtime_shapes.json"), authored, mesh);
            var samples = row.GetProperty("samples"); Assert.Equal(11, samples.GetArrayLength());
            for (var frame = 1; frame <= 10; frame++)
            {
                var sample = samples[frame]; Assert.Equal(70 + frame, sample.GetProperty("frame").GetInt32());
                var bodies = sample.GetProperty("bodies"); Assert.Equal(definition.Bodies.Length, bodies.GetArrayLength());
                for (var body = 0; body < bodies.GetArrayLength(); body++)
                {
                    Assert.Equal(definition.Bodies[body].Bone, bodies[body].GetProperty("name").GetString());
                    Assert.Equal(shapes.Count(s => s.Body == body), bodies[body].GetProperty("shapeFilters").GetArrayLength());
                }
                foreach (var shape in shapes)
                {
                    var actual = bodies[shape.Body].GetProperty("shapeFilters")[shape.NativeIndex];
                    Assert.Equal(shape.Type, actual.GetProperty("leafType").GetString());
                    Assert.Equal(shape.MarginCm, actual.GetProperty("leafMargin").GetSingle());
                    Assert.Equal(shape.BoundsMinCm, V(actual, "leafBoundsMin"));
                    Assert.Equal(shape.BoundsMaxCm, V(actual, "leafBoundsMax"));
                    var local = actual.GetProperty("leafLocal"); var q = local.GetProperty("rotation");
                    Assert.Equal(shape.LeafLocal.Position, V(local, "translation"));
                    Assert.Equal(shape.LeafLocal.Scale, V(local, "scale"));
                    Assert.Equal(shape.LeafLocal.Rotation, new AlsQuaternion(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()));
                    checkedShapes++;
                }
            }
        }
        Assert.Equal(430, checkedShapes);
    }
}
