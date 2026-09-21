using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsSimulationFilterCompilerTests
{
    private static string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name));
    [Fact]
    public void NativeBothAssetsPreserveEveryShapeAndRootIgnore()
    {
        var json = Read("v4_physics_simulation_filters.json"); var root = JsonNode.Parse(json)!;
        var runtime = Read("v4_physics_runtime_shapes.json"); var authored = Read("v4_physics_asset_inputs.json");
        foreach (var mesh in root["meshes"]!.AsArray())
        {
            var name = mesh!["mesh"]!.GetValue<string>(); var definition = AlsPhysicsAssetCompiler.Compile(authored, name);
            var filters = AlsSimulationFilterCompiler.Compile(json, runtime, authored, name);
            Assert.Equal(definition.Bodies.Sum(b => b.Shapes.Length), filters.Count);
            foreach (var (key, observed) in filters)
            {
                Assert.True(observed.Simulation);
                Assert.Equal(1UL << 5, observed.Filter.Channel);
                Assert.Equal(definition.Bodies[key.Body].Bone == "root" ? 0UL : ulong.MaxValue, observed.Filter.BlockChannels);
                Assert.Equal((byte)0, observed.QueryMaskFilter);
                Assert.Equal(definition.Bodies[key.Body].Bone != "root", observed.Filter.Allows(AlsSimulationFilter.WorldStatic));
            }
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("bone")]
    [InlineData("nativeIndex")]
    [InlineData("missing")]
    [InlineData("channel")]
    [InlineData("mask")]
    public void RejectsStaleOrAmbiguousNativeBindings(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_physics_simulation_filters.json"))!;
        var rig = root["meshes"]![0]!; var body = rig["bodies"]![0]!; var shape = body["shapes"]![0]!;
        switch (mutation)
        {
            case "hash": root["runtimeShapesSha256"] = new string('0', 64); break;
            case "bone": body["bone"] = "wrong"; break;
            case "nativeIndex": shape["nativeIndex"] = 999; break;
            case "missing": body["shapes"]!.AsArray().RemoveAt(0); break;
            case "channel": shape["channel"] = 64; break;
            case "mask": shape["blockChannels"] = "FFFFFFFF"; break;
        }
        Assert.Throws<InvalidDataException>(() => AlsSimulationFilterCompiler.Compile(root.ToJsonString(),
            Read("v4_physics_runtime_shapes.json"), Read("v4_physics_asset_inputs.json"), rig["mesh"]!.GetValue<string>()));
    }

    [Fact]
    public void QueryMaskMetadataDoesNotChangeNativeSimulationNarrowFilter()
    {
        var root = JsonNode.Parse(Read("v4_physics_simulation_filters.json"))!;
        var rig = root["meshes"]![0]!;
        rig["bodies"]![0]!["shapes"]![0]!["maskFilter"] = 255;
        var filters = AlsSimulationFilterCompiler.Compile(root.ToJsonString(), Read("v4_physics_runtime_shapes.json"),
            Read("v4_physics_asset_inputs.json"), rig["mesh"]!.GetValue<string>());
        Assert.Equal((byte)255, filters[(0, 0)].QueryMaskFilter);
        Assert.True(filters[(0, 0)].Filter.Allows(filters[(1, 0)].Filter));
    }
}
