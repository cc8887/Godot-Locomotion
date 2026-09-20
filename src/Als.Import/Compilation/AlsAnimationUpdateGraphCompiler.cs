using System.Text.Json;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

// Validate the instance-global dispatch independently of pose-node relevance.
public static class AlsAnimationUpdateGraphCompiler
{
    private const string Source="/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    public static void Validate(string json)
    {
        using var document=JsonDocument.Parse(json); var root=document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1 && root.GetProperty("source").GetString()==Source,
            "Foreign animation update graph.");
        var row=root.GetProperty("graphs").EnumerateArray().Single(g=>g.GetProperty("path").GetString()==Source+":UpdateGraph");
        var graph=new Graph(row.GetProperty("nativeText").GetString()!,true);
        var character=Call("UpdateCharacterInfo"); var aim=Call("UpdateAimingValues");
        var layers=Call("UpdateLayerValues"); var feet=Call("UpdateFootIK");
        graph.Link(aim,"execute",character,"then"); graph.Link(layers,"execute",aim,"then");
        graph.Link(feet,"execute",layers,"then");
        var selector=graph.Named("K2Node_SwitchEnum_0");
        Require(selector.Kind=="K2Node_SwitchEnum" && selector.Body.Contains("ALS_MovementState.ALS_MovementState'",StringComparison.Ordinal),
            "Wrong global movement-state enum.");
        graph.Link(selector,"execute",feet,"then");
        var state=graph.One("K2Node_VariableGet","MovementState"); graph.Self(state);
        graph.Link(selector,"Selection",state,"MovementState");
        var shouldMove=graph.One("K2Node_VariableSet","ShouldMove"); graph.Self(shouldMove);
        var (groundBranch,pin)=graph.FollowReroutes(shouldMove,"execute");
        Require(groundBranch==selector && pin.Name=="NewEnumerator1","ShouldMove escaped the Grounded branch.");
        graph.Link(Call("UpdateInAirValues"),"execute",selector,"NewEnumerator2");
        graph.Link(Call("UpdateRagdollValues"),"execute",selector,"NewEnumerator3");
        var outputs=selector.Pins.Values.Where(p=>p.Output).ToArray();
        Require(outputs.Select(p=>p.Name).Order().SequenceEqual(new[]{"NewEnumerator0","NewEnumerator1","NewEnumerator2","NewEnumerator3","NewEnumerator4"}),
            "Changed movement-state dispatch outputs.");
        foreach(var name in new[]{"NewEnumerator0","NewEnumerator4"})
            Require(outputs.Single(p=>p.Name==name).Links.Length==0,"An unhandled state now updates movement properties.");
        Node Call(string name) { var node=graph.One("K2Node_CallFunction",name); graph.Self(node); return node; }
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
}
