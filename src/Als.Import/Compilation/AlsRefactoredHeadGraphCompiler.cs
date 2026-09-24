using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredHeadProfile
{
    public AlsRefactoredHeadSettings Settings { get; }
    public AlsRefactoredLookPoseSource Look { get; }
    internal AlsRefactoredHeadProfile(AlsRefactoredHeadSettings settings,AlsRefactoredLookPoseSource look)
    {Settings=settings;Look=look;}
    public AlsRefactoredHeadRuntime CreateRuntime(uint character,uint generation)=>
        new(character,generation,Settings,Look.Parents,Look.CreateSampler());
}

public static class AlsRefactoredHeadGraphCompiler
{
    public static AlsRefactoredHeadProfile Compile(string graphsJson,string inventoryJson,string inputsJson)
    {
        const string source="/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head";
        using var graphs=JsonDocument.Parse(graphsJson);using var inventory=JsonDocument.Parse(inventoryJson);
        Require(inventory.RootElement.GetProperty("schemaVersion").GetInt32()==1&&
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graphsJson))).Equals(
                inventory.RootElement.GetProperty("graphsSha256").GetString(),StringComparison.OrdinalIgnoreCase),"Head inventory graph digest differs.");
        var authored=graphs.RootElement.GetProperty("blueprints").EnumerateArray().Single(r=>Text(r,"source")==source);
        var graph=new Graph(Text(authored,"nativeText").Replace("\r",""),true);
        var row=inventory.RootElement.GetProperty("blueprints").EnumerateArray().Single(r=>Text(r,"source")==source);
        Require(Text(row,"generatedClass")==source+"_C"&&row.GetProperty("compiledPropertyCount").GetInt32()==6,"Head compiled class differs.");
        var nodes=row.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("propertyIndex").GetInt32());
        Require(nodes.Count==6&&Enumerable.Range(0,6).All(nodes.ContainsKey),"Head node closure differs.");
        string[] classes=["Root","LinkedInputPose","ApplyMeshSpaceAdditive","CallFunction","CallFunction","BlendSpaceEvaluator"];
        for(var i=0;i<6;i++)
        {
            var node=nodes[i];Require(Text(node,"class")=="AnimGraphNode_"+classes[i]&&node.GetProperty("compiledNodeIndex").GetInt32()==5-i,"Head node identities differ.");
            Require(Text(node,"graph")==source+":AnimGraph"&&Text(node,"path").StartsWith(source+":AnimGraph.",StringComparison.Ordinal),"Foreign Head node.");
            Require(graph.Named(Name(node)).Kind==Text(node,"class"),"Head authored node differs.");
            if(i is not (2 or 5))Require(!graph.Named(Name(node)).Body.Contains("PropertyBindings=",StringComparison.Ordinal),"Unexpected Head property binding.");
            foreach(var value in Policies(node))
                foreach(var callback in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                    Require(Text(value.GetProperty(callback),"functionName")=="None","Unsupported implicit Head callback.");
        }
        Link(0,"result","Result",2);Link(2,"base","Base",1);Link(2,"additive","Additive",3);Link(3,"source","Source",4);Link(4,"source","Source",5);
        foreach(var value in Policies(nodes[1]))Require(Text(value,"name")=="View Input","Head linked input differs.");
        foreach(var value in Policies(nodes[2]))
        {
            var scale=value.GetProperty("alphaScaleBias");var clamp=value.GetProperty("alphaScaleBiasClamp");
            Require(!value.GetProperty("bRootSpaceAdditive").GetBoolean()&&Text(value,"alphaInputType")=="Float"&&
                value.GetProperty("lODThreshold").GetInt32()==-1&&scale.GetProperty("scale").GetSingle()==1&&scale.GetProperty("bias").GetSingle()==0&&
                !clamp.GetProperty("bMapRange").GetBoolean()&&!clamp.GetProperty("bClampResult").GetBoolean()&&!clamp.GetProperty("bInterpResult").GetBoolean()&&
                clamp.GetProperty("scale").GetSingle()==1&&clamp.GetProperty("bias").GetSingle()==0,"Unsupported Head alpha policy.");
        }
        for(var i=3;i<=4;i++)
        {
            foreach(var value in Policies(nodes[i]))Require(Text(value,"callSite")== (i==3?"OnBecomeRelevant":"OnUpdate"),"Head callback order differs.");
            Require(graph.Named(Name(nodes[i])).Body.Contains("FunctionReference=(MemberName=\""+(i==3?"InitializeHead":"RefreshHead")+"\",bSelfContext=True)",StringComparison.Ordinal),"Head callback target differs.");
        }
        foreach(var value in Policies(nodes[5]))Require(value.GetProperty("bTeleportToNormalizedTime").GetBoolean()&&
            Text(value,"method")=="DoNotSync"&&Text(value,"groupName")=="None"&&value.GetProperty("playRate").GetSingle()==1&&
            Text(value,"blendSpace")=="/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look","Head evaluator policy differs.");
        Bindings(2,["Alpha:ViewState:HeadBlendAmount"]);Bindings(5,["NormalizedTime:HeadState:YawAmount","X:HeadState:PitchAngle"]);
        return new(AlsRefactoredHeadSettingsCompiler.Compile(inputsJson,graphsJson),AlsRefactoredLookPoseCompiler.Compile(inputsJson,graphsJson));

        void Link(int from,string field,string pin,int to)
        {
            var link=nodes[from].GetProperty("runtime").GetProperty(field);
            Require(link.GetProperty("linkId").GetInt32()==to&&link.GetProperty("sourceLinkId").GetInt32()==from,"Head compiled pose link differs.");
            Require(graph.FollowReroutes(graph.Named(Name(nodes[from])),pin).Item1.Name==Name(nodes[to]),"Head authored pose link differs.");
        }
        void Bindings(int index,string[] expected)
        {
            var node=graph.Named(Name(nodes[index]));var body=node.Body;
            Require(node.Pins.Values.All(p=>p.Output||p.Links==""||index==2&&p.Name is "Base" or "Additive"),
                "Connected Head parameter requires explicit expression compilation.");
            var lines=Regex.Matches(body,@"(?m)^ +PropertyBindings=([^\n]+)");
            var entries=lines.SelectMany(line=>Regex.Matches(line.Value,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(\"GetParent\",\"([^\"]+)\",\"([^\"]+)\"\\).*?bIsBound=True")).ToArray();
            Require(entries.Length==lines.Sum(l=>Regex.Matches(l.Value,"PropertyName=").Count)&&
                entries.Select(m=>m.Groups[1].Value+":"+m.Groups[2].Value+":"+m.Groups[3].Value).Order().SequenceEqual(expected.Order()),"Head property bindings differ.");
        }
    }
    private static IEnumerable<JsonElement> Policies(JsonElement node)=>[node.GetProperty("runtime"),node.GetProperty("authoredProperties").GetProperty("Node")];
    private static string Name(JsonElement node)=>Text(node,"path").Split('.')[^1];
    private static string Text(JsonElement value,string field)=>value.GetProperty(field).GetString()!;
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
