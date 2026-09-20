using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsSprintInputProfile(int FirstPlayerId,int ImpulsePlayerId,AlsOverlayAlphaPolicy Alpha);

public static class AlsSprintInputCompiler
{
    public static AlsSprintInputProfile Compile(string json,AlsLocomotionSourceProfile sources,AlsAnimationSetDefinition set)
    {
        using var doc=JsonDocument.Parse(json);
        var graph=doc.RootElement.GetProperty("graphs").EnumerateArray().Single(g=>Text(g,"path").EndsWith(
            ".(N) Locomotion Cycles.AnimStateNode_0.(N) Locomotion Cycles",StringComparison.Ordinal));
        var path=Text(graph,"path");
        var nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>Text(n,"name"));
        var layer=nodes.Values.Single(n=>Text(n,"class")=="AnimGraphNode_LinkedAnimLayer");
        var blend=Linked(layer,"Sprint","AnimGraphNode_TwoWayBlend");
        var data=blend.GetProperty("properties").GetProperty("BlendNode");
        Require(Text(data,"alphaInputType")=="Float" && data.GetProperty("bResetChildOnActivation").GetBoolean() &&
            !data.GetProperty("bAlwaysUpdateChildren").GetBoolean(),"Unsupported Sprint input visitation policy.");
        var scaleBias=data.GetProperty("alphaScaleBias");
        Require(Number(scaleBias,"scale")==1 && Number(scaleBias,"bias")==0,"Unsupported Sprint final alpha transform.");
        foreach(var name in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
            Require(Text(data.GetProperty(name),"functionName")=="None","Unsupported Sprint input callback.");
        var acceleration=Linked(blend,"Alpha","K2Node_VariableGet");
        Require(Text(acceleration.GetProperty("properties").GetProperty("VariableReference"),"memberName")=="RelativeAccelerationAmount" &&
            Text(Pin(blend,"Alpha").GetProperty("links")[0],"pin")=="RelativeAccelerationAmount_X","Wrong Sprint acceleration component.");
        var a=Source("A","ALS_N_Sprint_F"); var b=Source("B","ALS_N_Sprint_F_Impulse");
        Require(a.PlayerId!=b.PlayerId && a.SyncGroupId==b.SyncGroupId && a.SyncGroupId>=0,"Sprint inputs require independent grouped identities.");
        var clamp=data.GetProperty("alphaScaleBiasClamp");
        var input=clamp.GetProperty("inRange"); var output=clamp.GetProperty("outRange");
        var policy=new AlsOverlayAlphaPolicy(Number(clamp,"scale"),Number(clamp,"bias"),clamp.GetProperty("bClampResult").GetBoolean(),
            Number(clamp,"clampMin"),Number(clamp,"clampMax"),clamp.GetProperty("bInterpResult").GetBoolean(),
            Number(clamp,"interpSpeedIncreasing"),Number(clamp,"interpSpeedDecreasing"),clamp.GetProperty("bMapRange").GetBoolean(),
            Number(input,"min"),Number(input,"max"),Number(output,"min"),Number(output,"max"));
        Require(policy.Minimum<=policy.Maximum && policy.Increasing>=0 && policy.Decreasing>=0,"Invalid Sprint alpha policy.");
        return new(a.PlayerId,b.PlayerId,policy);

        JsonElement Linked(JsonElement n,string pin,string type)
        {
            var links=Pin(n,pin).GetProperty("links");Require(links.GetArrayLength()==1,"Ambiguous Sprint connection.");
            var target=nodes[Text(links[0],"node")];Require(Text(target,"class")==type,"Wrong Sprint input node type.");return target;
        }
        AlsLocomotionSourcePlayer Source(string pin,string asset)
        {
            var node=Linked(blend,pin,"AnimGraphNode_SequencePlayer");
            Require(Text(Pin(blend,pin).GetProperty("links")[0],"pin")=="Pose","Wrong Sprint source output.");
            var player=sources.Players.Single(p=>p.SourceNode==path+"."+Text(node,"name"));
            var sample=sources.Samples[player.SampleStart];var animation=set.Animations[sample.AnimationId];
            Require(player.Kind==AlsLocomotionSourceKind.Sequence && player.Domain==AlsLocomotionSourceDomain.Cycle &&
                player.SampleCount==1 && player.Loop && player.PlayRateInput=="StandingPlayRate" && animation.Name==asset &&
                Text(node.GetProperty("properties").GetProperty("Node"),"sequence")==animation.ObjectPath,
                "Sprint asset/source identity differs.");
            return player;
        }
    }
    private static JsonElement Pin(JsonElement node,string name)=>node.GetProperty("pins").EnumerateArray()
        .Single(p=>Text(p,"name")==name && Text(p,"direction")=="input");
    private static string Text(JsonElement node,string name)=>node.GetProperty(name).GetString()!;
    private static float Number(JsonElement node,string name)
    { var value=node.GetProperty(name).GetSingle();Require(float.IsFinite(value),"Nonfinite Sprint parameter.");return value; }
    private static void Require(bool value,string message) { if(!value)throw new FormatException(message); }
}
