using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsConvexTopologyCompilerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string file) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file + ".json"));
    private static AlsRagdollPhysicsDefinition Definition(string name) => AlsPhysicsAssetCompiler.Compile(
        Read("v4_physics_asset_inputs"), AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
    [Fact]
    public void EveryAuthoredConvexBindsToValidatedCookedFaceLoops()
    {
        var json = Read("v4_physics_convex_topology");
        Assert.Empty(AlsConvexTopologyCompiler.Compile(json, Definition("Mannequin")));
        var definition = Definition("AnimMan"); var shapes = AlsConvexTopologyCompiler.Compile(json, definition);
        Assert.Equal(2, shapes.Length);
        Assert.Equal(new[] { "foot_l", "foot_r" }, shapes.Select(s => definition.Bodies[s.Body].Bone).Order().ToArray());
        foreach (var shape in shapes)
        {
            var hull = shape.Topology;
            Assert.True(hull.VertexCount >= 4 && hull.PlaneCount >= 4);
            output.WriteLine($"COOKED_CONVEX bone={definition.Bodies[shape.Body].Bone} vertices={hull.VertexCount} planes={hull.PlaneCount} margin={hull.Margin:R}");
            Assert.Contains(Enumerable.Range(0, hull.PlaneCount), p => hull.FaceVertices(p).Length > 3);
        }
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("binding")]
    [InlineData("source")]
    [InlineData("loop")]
    public void StaleAssetsAndBrokenLoopsAreRejected(string fault)
    {
        var root = JsonNode.Parse(Read("v4_physics_convex_topology"))!;
        var rig = root["rigs"]!.AsArray().Single(r => r!["mesh"]!.GetValue<string>().EndsWith("AnimMan.AnimMan"))!;
        var shapes = rig["shapes"]!.AsArray(); var shape = shapes[0]!;
        switch (fault)
        {
            case "missing": shapes.RemoveAt(0); break;
            case "duplicate": shapes.Add(shape.DeepClone()); break;
            case "binding": shape["bone"] = "wrong"; break;
            case "source": shape["sourceVertices"]![0]![0] = 999; break;
            case "loop": shape["faces"]![0]!["vertices"]![0] = -1; break;
        }
        Assert.Throws<InvalidDataException>(() => AlsConvexTopologyCompiler.Compile(root.ToJsonString(), Definition("AnimMan")));
    }
}
