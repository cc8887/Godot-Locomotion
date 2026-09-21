using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPrimitiveGeometryCompilerTests
{
    private static string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
    [Fact]
    public void NativePrimitiveFieldsBindToExactRuntimeSnapshot()
    {
        var json = Read("v4_physics_primitive_geometry"); var runtime = Read("v4_physics_runtime_shapes");
        var root = JsonNode.Parse(json)!; var total = 0; var baked = 0;
        foreach (var mesh in root["meshes"]!.AsArray())
        {
            var rows = AlsPrimitiveGeometryCompiler.Compile(json, runtime, mesh!["mesh"]!.GetValue<string>());
            foreach (var row in rows)
            {
                var source = mesh["bodies"]![row.Body]!["runtimeShapes"]!.AsArray().Single(s => s!["authoredIndex"]!.GetValue<int>() == row.Shape)!;
                var p = source["primitiveGeometry"]!;
                if (row.Capsule is { } c)
                {
                    Assert.Equal(p["radius"]!.GetValue<float>(), c.Radius); Assert.Equal(p["height"]!.GetValue<float>(), c.Height);
                    var point = p["endpoint0"]!.AsArray(); var axis = p["axis"]!.AsArray();
                    Assert.Equal(new AlsDoubleVector(point[0]!.GetValue<double>(), point[1]!.GetValue<double>(), point[2]!.GetValue<double>()), new(c.Endpoint0));
                    Assert.Equal(new AlsDoubleVector(axis[0]!.GetValue<double>(), axis[1]!.GetValue<double>(), axis[2]!.GetValue<double>()), new(c.Axis));
                    Assert.True((new AlsDoubleVector(0, 0, 1).Rotate(row.ProxyLocal.Rotation) - new AlsDoubleVector(c.Axis).RemoveScaling()).NearlyZero(1e-6));
                    if (row.Center != AlsDoubleVector.Zero) baked++;
                }
                total++;
            }
        }
        Assert.Equal(41, total); Assert.True(baked > 20);
    }
    [Theory]
    [InlineData("radius")]
    [InlineData("axis")]
    [InlineData("bounds")]
    [InlineData("snapshot")]
    public void RejectsStaleOrInconsistentPrimitiveGeometry(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_physics_primitive_geometry"))!;
        var shape = root["meshes"]![0]!["bodies"]![0]!["runtimeShapes"]![0]!;
        if (mutation == "radius") shape["primitiveGeometry"]!["radius"] = -1;
        if (mutation == "axis") shape["primitiveGeometry"]!["axis"]![0] = 10;
        if (mutation == "bounds") shape["primitiveGeometry"]!["endpoint0"]![0] = 1000;
        if (mutation == "snapshot") shape["marginCm"] = 12;
        Assert.Throws<InvalidDataException>(() => AlsPrimitiveGeometryCompiler.Compile(root.ToJsonString(), Read("v4_physics_runtime_shapes"),
            AlsPhysicsAssetCompiler.MeshRoot + "Mannequin.Mannequin"));
    }
}
