using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredBasePoseResource
{
    public int NodeIndex { get; }
    public AlsMantlingPoseSource Pose { get; }
    public AlsMantlingCurveSource Curves { get; }
    internal AlsRefactoredBasePoseResource(int nodeIndex,AlsMantlingPoseSource pose,AlsMantlingCurveSource curves)
    {NodeIndex=nodeIndex;Pose=pose;Curves=curves;}
    // Each worker owns its sampler. Fixed frame zero has no playback clock or notify traversal.
    public Sampler CreateSampler()=>new(Pose.CreateSampler(Curves));
    public sealed class Sampler
    {
        private readonly AlsMantlingPoseSource.Sampler _sampler;
        internal Sampler(AlsMantlingPoseSource.Sampler sampler)=>_sampler=sampler;
        public void Evaluate(Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)=>_sampler.Sample(0,true,false,false,pose,curves);
    }
}

public static class AlsRefactoredBasePoseCompiler
{
    public static IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> Compile(string inputsJson,string inventoryJson,string graphsJson)
    {
        var inventory=AlsRefactoredLayeringInventoryCompiler.Compile(graphsJson,inventoryJson);
        using var document=JsonDocument.Parse(inputsJson);var root=document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1&&Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventoryJson)))
            .Equals(root.GetProperty("inventorySha256").GetString(),StringComparison.OrdinalIgnoreCase),"Base pose inventory digest differs.");
        var nodes=inventory.Nodes.Values.Where(n=>n.Class=="AnimGraphNode_SequenceEvaluator"&&n.Index>=0).ToArray();
        const string prefix="/ALS/ALS/Animations/Base/";
        string[] paths=[prefix+"A_Als_Stand_Pose.A_Als_Stand_Pose",prefix+"A_Als_Crouch_Pose.A_Als_Crouch_Pose"];
        Require(nodes.Length==2&&nodes.Select(n=>n.Runtime.GetProperty("sequence").GetString()!).Order().SequenceEqual(paths.Order()),
            "Changed original base pose closure.");
        foreach(var node in nodes)
        {
            Require(!node.NativeBody.Contains("PropertyBindings=",StringComparison.Ordinal)&&
                Regex.Matches(node.NativeBody,@"(?m)^      CustomProperties Pin [^\n]+")
                    .All(m=>m.Value.Contains("Direction=\"EGPD_Output\"",StringComparison.Ordinal)||!m.Value.Contains("LinkedTo=",StringComparison.Ordinal)),
                "Dynamic base evaluator pins need an explicit runtime binding.");
            foreach(var value in new[]{node.Runtime,node.AuthoredProperties.GetProperty("Node")})
            {
                Require(value.GetProperty("bUseExplicitFrame").GetBoolean()&&value.GetProperty("explicitFrame").GetInt32()==0&&
                    value.GetProperty("explicitTime").GetSingle()==0&&value.GetProperty("startPosition").GetSingle()==0&&
                    value.GetProperty("bTeleportToExplicitTime").GetBoolean()&&value.GetProperty("bShouldLoop").GetBoolean()&&
                    value.GetProperty("method").GetString()=="DoNotSync"&&value.GetProperty("groupName").GetString()=="None"&&
                    value.GetProperty("reinitializationBehavior").GetString()=="ExplicitTime"&&
                    value.GetProperty("sequence").GetString()==node.Runtime.GetProperty("sequence").GetString(),
                    "Base pose evaluator no longer uses the authored fixed frame-zero policy.");
                foreach(var callback in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                    Require(value.GetProperty(callback).GetProperty("functionName").GetString()=="None","Unsupported base pose node callback.");
            }
        }
        var poses=AlsMantlingPoseCompiler.CompileStandaloneSequences(inputsJson,paths);
        var curves=AlsMantlingCurveCompiler.CompileEmbedded(inputsJson);
        var result=new Dictionary<int,AlsRefactoredBasePoseResource>();
        foreach(var node in nodes)
        {
            var path=node.Runtime.GetProperty("sequence").GetString()!;
            Require(poses[path].SkeletonPath=="/ALS/ALS/Character/SK_Als.SK_Als","Foreign base pose skeleton.");
            result.Add(node.Index,new(node.Index,poses[path],curves[path]));
        }
        return new ReadOnlyDictionary<int,AlsRefactoredBasePoseResource>(result);
    }
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
