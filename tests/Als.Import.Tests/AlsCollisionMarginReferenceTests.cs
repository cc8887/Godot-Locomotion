using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsCollisionMarginReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void OrderedShapeAndMotionPairsMatchActualNativeConstraintMargins()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_margin_reference.json")));
        Assert.Equal(1,doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0,doc.RootElement.GetProperty("observedConvexZeroMargin").GetSingle());
        var rows=doc.RootElement.GetProperty("cases");Assert.Equal(1568,rows.GetArrayLength());
        var unique=new HashSet<(string,string,int,int,float)>();var feet=0;
        foreach(var row in rows.EnumerateArray())
        {
            var name0=row.GetProperty("shape0").GetString()!;var name1=row.GetProperty("shape1").GetString()!;
            var state0=row.GetProperty("state0").GetInt32();var state1=row.GetProperty("state1").GetInt32();
            var minimum=row.GetProperty("minimum").GetSingle();
            Assert.True(unique.Add((name0,name1,state0,state1,minimum)));
            var quadratic0=name0 is "sphere" or "capsule";var quadratic1=name1 is "sphere" or "capsule";
            Assert.Equal(quadratic0,row.GetProperty("quadratic0").GetBoolean());Assert.Equal(quadratic1,row.GetProperty("quadratic1").GetBoolean());
            Assert.Equal(state0 is 2 or 3,row.GetProperty("dynamic0").GetBoolean());Assert.Equal(state1 is 2 or 3,row.GetProperty("dynamic1").GetBoolean());
            var a=new AlsCollisionMarginInput(row.GetProperty("shapeMargin0").GetSingle(),quadratic0,(AlsCollisionMotionState)state0);
            var b=new AlsCollisionMarginInput(row.GetProperty("shapeMargin1").GetSingle(),quadratic1,(AlsCollisionMotionState)state1);
            if(name0.StartsWith("foot_")){Assert.Equal(0,a.ShapeMargin);feet++;}
            if(name1.StartsWith("foot_"))Assert.Equal(0,b.ShapeMargin);
            var result=AlsCollisionMargins.Resolve(a,b,minimum);
            Assert.Equal(row.GetProperty("margin0").GetSingle(),result.Margin0);Assert.Equal(row.GetProperty("margin1").GetSingle(),result.Margin1);
        }
        Assert.Equal(448,feet);output.WriteLine("NATIVE_PAIR_MARGINS cases=1568 exact=true cooked_foot_first=448 sleeping_dynamic=true");
    }
}
