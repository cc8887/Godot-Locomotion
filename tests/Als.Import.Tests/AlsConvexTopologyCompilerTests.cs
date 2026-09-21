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
            Assert.True(hull.HasNativeVertexPlanes);
        }
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("binding")]
    [InlineData("source")]
    [InlineData("loop")]
    [InlineData("old-schema")]
    [InlineData("missing-cache")]
    [InlineData("cache-count")]
    [InlineData("cache-plane")]
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
            case "old-schema":root["schemaVersion"]=1;break;
            case "missing-cache":shape.AsObject().Remove("vertexPlanes");break;
            case "cache-count":shape["vertexPlanes"]![0]!["count"]=-1;break;
            case "cache-plane":shape["vertexPlanes"]![0]!["planes"]![0]=999;break;
        }
        Assert.Throws<InvalidDataException>(() => AlsConvexTopologyCompiler.Compile(root.ToJsonString(), Definition("AnimMan")));
    }
    [Fact]
    public void EveryVertexPreservesNativeCachedCountAndPlaneOrder()
    {
        var json=Read("v4_physics_convex_topology");var root=JsonNode.Parse(json)!;
        var rig=root["rigs"]!.AsArray().Single(r=>r!["mesh"]!.GetValue<string>().EndsWith("AnimMan.AnimMan"))!;
        var compiled=AlsConvexTopologyCompiler.Compile(json,Definition("AnimMan"));var total=0;var partial=0;
        foreach(var shape in compiled)
        {
            var row=rig["shapes"]!.AsArray().Single(s=>s!["bodyIndex"]!.GetValue<int>()==shape.Body&&s["shapeIndex"]!.GetValue<int>()==shape.Shape)!;
            var cache=row["vertexPlanes"]!.AsArray();Assert.Equal(shape.Topology.VertexCount,cache.Count);
            for(var vertex=0;vertex<cache.Count;vertex++)
            {
                var expected=cache[vertex]!;var actual=shape.Topology.VertexPlanesAt(vertex);
                Assert.Equal(expected["count"]!.GetValue<int>(),actual.Count);
                for(var slot=0;slot<3;slot++)Assert.Equal(expected["planes"]![slot]!.GetValue<int>(),actual.PlaneAt(slot));
                var incident=Enumerable.Range(0,shape.Topology.PlaneCount).Count(face=>shape.Topology.FaceVertices(face).Contains(vertex));
                if(incident!=actual.Count)partial++;
                total++;
            }
        }
        Assert.Equal(256,total);Assert.Equal(7,partial);
        output.WriteLine($"NATIVE_VERTEX_PLANES vertices={total} differing_from_face_incidence={partial} all_slots_exact=true");
    }
}
