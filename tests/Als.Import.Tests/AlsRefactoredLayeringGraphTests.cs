using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLayeringGraphTests
{
    private static string Json=>MantlingHostFixture.Read("refactored_layering_graphs");
    [Fact]
    public void AuthoredTailAccumulatesNegativeMantleControlsAndOverridesPoseCurves()
    {
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(["OnlyPoseBranch"]).ToArray();
        var tail=AlsRefactoredLayeringGraphCompiler.CompileCurveTail(Json,names);
        var locomotion=new AlsInertialCurve[names.Length];var overlay=new AlsInertialCurve[names.Length];
        var slot=new AlsInertialCurve[names.Length];var final=new AlsInertialCurve[names.Length];
        var head=Array.IndexOf(names,"LayerHead");var slotHead=Array.IndexOf(names,"LayerHeadSlot");
        locomotion[head]=new(-1);overlay[head]=new(1);overlay[slotHead]=new(.8f);
        tail.PrepareOverlay(overlay,slot);Assert.Equal(new AlsInertialCurve(0),slot[slotHead]);
        foreach(var name in names.Where(n=>n.EndsWith("Slot",StringComparison.Ordinal)))Assert.Equal(new AlsInertialCurve(0),slot[Array.IndexOf(names,name)]);
        // A real Curves Slot may override a reset value before the accumulation.
        slot[slotHead]=new(.6f);final[^1]=new(9);tail.Compose(locomotion,slot,final);
        Assert.Equal(new AlsInertialCurve(0),final[head]);Assert.Equal(new AlsInertialCurve(.6f),final[slotHead]);Assert.False(final[^1].Present);
        var input=new AlsRefactoredLayeringInputModel(names).Evaluate(new(2,1,1),new(1,1,1),final);
        Assert.Equal(0,input.HeadBlendAmount);Assert.Equal(.6f,input.HeadSlotBlendAmount);
        Assert.Equal(new AlsInertialCurve(.8f),overlay[slotHead]);
    }
    [Theory]
    [InlineData("position")][InlineData("mode")][InlineData("reset")][InlineData("hash")]
    public void RejectsChangedGraphSemanticsEvenWithARecomputedTextDigest(string change)
    {
        var json=JsonNode.Parse(Json)!;var row=json["blueprints"]![change=="position"?0:2]!;
        var text=row["nativeText"]!.GetValue<string>();
        text=change switch
        {
            "position"=>text.Replace("SlotName=\"PostLocomotion\"","SlotName=\"Other\"",StringComparison.Ordinal),
            "mode"=>text.Replace("BlendMode=Override","BlendMode=Combine",StringComparison.Ordinal),
            "reset"=>text.Replace("CurveValues=(0.000000,0.000000","CurveValues=(1.000000,0.000000",StringComparison.Ordinal),
            _=>text+" "
        };
        row["nativeText"]=text;if(change!="hash")row["textSha256"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        Assert.Throws<ArgumentException>(()=>AlsRefactoredLayeringGraphCompiler.CompileCurveTail(json.ToJsonString(),AlsRefactoredLayeringInputModel.CurveNames));
    }
    [Fact]
    public void InvalidInputDoesNotPartiallyOverwriteCurveOutput()
    {
        var names=AlsRefactoredLayeringInputModel.CurveNames;var tail=new AlsRefactoredLayeringCurveTail(names);
        var source=new AlsInertialCurve[names.Length];var result=Enumerable.Repeat(new AlsInertialCurve(3),names.Length).ToArray();
        source[^1]=new(float.NaN);Assert.Throws<ArgumentException>(()=>tail.PrepareOverlay(source,result));
        Assert.All(result,c=>Assert.Equal(new AlsInertialCurve(3),c));
        Assert.Throws<ArgumentException>(()=>tail.Compose(source,new AlsInertialCurve[source.Length],result));
        Assert.All(result,c=>Assert.Equal(new AlsInertialCurve(3),c));
    }
}
