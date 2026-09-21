using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRuntimeShapeCompilerTests
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    private static string Source=>Read("v4_physics_asset_inputs");
    private static string Observed=>Read("v4_physics_runtime_shapes");
    private static string Mesh(string name)=>AlsPhysicsAssetCompiler.MeshRoot+name+"."+name;
    [Theory]
    [InlineData("Mannequin",21)]
    [InlineData("AnimMan",22)]
    public void RealRuntimeShapesBindToEveryAuthoredShape(string name,int count)
    {
        var rows=AlsRuntimeShapeCompiler.Compile(Observed,Source,Mesh(name));
        Assert.Equal(count,rows.Length);Assert.All(rows,r=>Assert.True(r.MarginCm>0));
        Assert.All(rows.Where(r=>r.Type=="Box"),r=>Assert.InRange(r.MarginCm,.3f,.6f));
    }
    [Fact]
    public void FeetUseObservedWrappersNotAuthoredElementScaleAndTransform()
    {
        var source=AlsPhysicsAssetCompiler.Compile(Source,Mesh("AnimMan"));
        var rows=AlsRuntimeShapeCompiler.Compile(Observed,Source,Mesh("AnimMan"));
        var left=rows.Single(r=>source.Bodies[r.Body].Bone=="foot_l");
        var right=rows.Single(r=>source.Bodies[r.Body].Bone=="foot_r");
        Assert.Equal("scaled",left.Wrapper);Assert.Equal(new AlsDoubleVector(1,1,.9999998807907104),left.Scale);
        Assert.Equal("instanced",right.Wrapper);Assert.Equal(AlsDoubleVector.One,right.Scale);
        Assert.Equal(.6178215742111206f,left.MarginCm);Assert.Equal(.6178215146064758f,right.MarginCm);
        Assert.Equal(0f,left.InnerMarginCm);Assert.Equal(0f,right.InnerMarginCm);
        Assert.Equal(AlsPrecisePose.Identity,left.LeafLocal);Assert.Equal(AlsPrecisePose.Identity,right.LeafLocal);
        Assert.NotEqual(source.Bodies[left.Body].Shapes[left.Shape].Local.Scale,left.Scale);
        Assert.NotEqual(source.Bodies[left.Body].Shapes[left.Shape].Local,left.LeafLocal);
        var cooked=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),source);
        foreach(var binding in cooked)
        {
            var observed=rows.Single(r=>r.Body==binding.Body&&r.Shape==binding.Shape);
            var vertices=Enumerable.Range(0,binding.Topology.VertexCount)
                .Select(i=>new AlsDoubleVector(binding.Topology.VertexAt(i))*observed.Scale).ToArray();
            var min=new AlsDoubleVector(vertices.Min(p=>p.X),vertices.Min(p=>p.Y),vertices.Min(p=>p.Z));
            var max=new AlsDoubleVector(vertices.Max(p=>p.X),vertices.Max(p=>p.Y),vertices.Max(p=>p.Z));
            Assert.True((min-observed.BoundsMinCm).NearlyZero(1e-6));
            Assert.True((max-observed.BoundsMaxCm).NearlyZero(1e-6));
        }
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("negative_margin")]
    [InlineData("wrapper")]
    [InlineData("scale")]
    [InlineData("bounds")]
    [InlineData("flags")]
    public void RejectsIncompleteOrMismatchedObservation(string change)
    {
        var root=JsonNode.Parse(Observed)!;var bodies=root["meshes"]![0]!["bodies"]!.AsArray();
        var rows=bodies[8]!["runtimeShapes"]!.AsArray();var row=rows[0]!;
        switch(change)
        {
            case "duplicate": rows[1]!["authoredIndex"]=row["authoredIndex"]!.DeepClone();break;
            case "missing": rows.RemoveAt(0);break;
            case "stale": bodies[0]!["nativeMassKg"]=999;break;
            case "negative_margin": row["marginCm"]=-1;break;
            case "wrapper": row["wrapper"]="unknown";break;
            case "scale": row["scale"]![0]=0;break;
            case "bounds": row["boundsMaxCm"]=row["boundsMinCm"]!.DeepClone();break;
            case "flags": row["typeCode"]=136;break;
        }
        Assert.Throws<InvalidDataException>(()=>AlsRuntimeShapeCompiler.Compile(root.ToJsonString(),Source,Mesh("Mannequin")));
    }
}
