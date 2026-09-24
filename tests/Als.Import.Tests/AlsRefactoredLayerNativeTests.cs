using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLayerNativeTests(ITestOutputHelper output)
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    [Fact]
    public void OriginalLinkedLayerGraphMatchesContinuousNativePoseAndCurveTraces()
    {
        var graphJson=Read("layering_graphs");var inventory=Read("layering_inventory");var inputs=Read("base_pose_inputs");
        var definition=AlsRefactoredLayerGraphCompiler.Compile(graphJson,inventory,inputs);
        var resources=AlsRefactoredBasePoseCompiler.Compile(inputs,inventory,graphJson);var basis=resources[37].Pose;
        var oracle=JsonNode.Parse(Read("layer_trace"))!;var request=JsonNode.Parse(Read("layer_trace.request"))!;
        Assert.Equal(request["requestDigest"]!.GetValue<string>(),oracle["requestDigest"]!.GetValue<string>());
        foreach(var pair in new[]{("graphsSha256",graphJson),("inventorySha256",inventory),("baseInputsSha256",inputs)})
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Item2))),request[pair.Item1]!.GetValue<string>().ToUpperInvariant());
        Assert.Equal(basis.BoneNames.ToArray(),oracle["names"]!.AsArray().Select(n=>n!.GetValue<string>()));
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(new[]{"PoseStanding","PoseCrouching","OnlyLocomotion","OnlyOverlay"}).ToArray();
        var rest=basis.ReferencePose.ToArray().Select(FloatPose).ToArray();
        var frames=0;var bones=0;double maxP=0,maxQ=0,maxS=0,maxC=0;
        foreach(var trace in oracle["traces"]!.AsArray())
        {
            var runtime=new AlsLayerBlendingRuntime(definition,basis.BoneNames.ToArray(),basis.Parents.ToArray(),names,rest);
            var sink=new Sink(resources,names);var graph=default(AlsAnimationGraphFrame);var previous=default(AlsFrameIdentity);
            var curves=new AlsInertialCurve[names.Length];var pose=new AlsLocalPose[79];var serial=0;
            foreach(var row in trace!["frames"]!.AsArray())
            {
                var input=row!["input"]!;sink.Frame=input;var id=new AlsFrameIdentity(++serial,1,1);graph=graph.Next(id,(ulong)serial);
                var layering=JsonSerializer.Deserialize<AlsRefactoredLayeringInput>(input["layering"]!.ToJsonString()) with {Identity=id,FeedbackIdentity=previous};
                runtime.Prepare(new(id,1,input["delta"]!.GetValue<float>()),layering,serial==1?[]:curves,graph.Initialization,graph.Bones,graph.Evaluation,sink);
                runtime.Evaluate(pose,curves);runtime.Commit();previous=id;
                for(var bone=0;bone<79;bone++)
                {
                    double[] V(string key)=>row["pose"]![bone]![key]!.AsArray().Select(n=>n!.GetValue<double>()).ToArray();
                    var p=V("position");var q=V("rotation");var s=V("scale");var a=pose[bone];
                    var dp=Math.Sqrt(Math.Pow(a.Position.X-p[0],2)+Math.Pow(a.Position.Y-p[1],2)+Math.Pow(a.Position.Z-p[2],2));
                    var sign=a.Rotation.X*q[0]+a.Rotation.Y*q[1]+a.Rotation.Z*q[2]+a.Rotation.W*q[3]<0?-1:1;
                    var dq=new[]{Math.Abs(a.Rotation.X-sign*q[0]),Math.Abs(a.Rotation.Y-sign*q[1]),Math.Abs(a.Rotation.Z-sign*q[2]),Math.Abs(a.Rotation.W-sign*q[3])}.Max();
                    var ds=new[]{Math.Abs(a.Scale.X-s[0]),Math.Abs(a.Scale.Y-s[1]),Math.Abs(a.Scale.Z-s[2])}.Max();
                    maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);bones++;
                    Assert.True(dp<=1e-3&&dq<=1e-5&&ds<=1e-5,$"{trace["name"]} frame={serial} bone={basis.BoneNames[bone]} p={dp:R} q={dq:R} s={ds:R}");
                }
                var nativeCurves=row["curves"]!.AsObject();
                Assert.All(nativeCurves,p=>Assert.Contains(p.Key,names));
                for(var i=0;i<names.Length;i++)
                {
                    Assert.Equal(nativeCurves.ContainsKey(names[i]),curves[i].Present);
                    if(!curves[i].Present)continue;
                    var dc=Math.Abs(curves[i].Value-nativeCurves[names[i]]!.GetValue<double>());maxC=Math.Max(maxC,dc);Assert.True(dc<=1e-6);
                }
                frames++;
            }
        }
        Assert.Equal(210,frames);Assert.Equal(16590,bones);
        output.WriteLine($"frames={frames} bones={bones} max_position_cm={maxP:G17} max_quaternion={maxQ:G17} max_scale={maxS:G17} max_curve={maxC:G17}");
    }
    private static AlsLocalPose FloatPose(AlsPrecisePose p)=>new(new((float)p.Position.X,(float)p.Position.Y,(float)p.Position.Z),
        new((float)p.Rotation.X,(float)p.Rotation.Y,(float)p.Rotation.Z,(float)p.Rotation.W),new((float)p.Scale.X,(float)p.Scale.Y,(float)p.Scale.Z));
    private sealed class Sink : IAlsLayerBlendingSink
    {
        private readonly Dictionary<int,AlsLocalPose[]> _poses;private readonly string[] _names;
        public JsonNode Frame=null!;
        public Sink(IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> resources,string[] names)
        {_names=names;_poses=resources.ToDictionary(p=>p.Key,p=>{var pose=new AlsPrecisePose[79];p.Value.CreateSampler().Evaluate(pose,[]);return pose.Select(FloatPose).ToArray();});}
        public void InitializeInput(int index,string name){}
        public void CacheInputBones(int index,string name){}
        public void UpdateInput(int index,string name,in AlsPoseUpdateContext context){}
        public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            curves.Clear();if(_poses.TryGetValue(index,out var basis)){basis.CopyTo(pose);return;}
            var prefix=name=="Locomotion Input"?"locomotion":"overlay";
            _poses[Frame[prefix+"Standing"]!.GetValue<bool>()?37:38].CopyTo(pose);
            foreach(var pair in Frame[prefix+"Curves"]!.AsObject())curves[Array.IndexOf(_names,pair.Key)]=new(pair.Value!.GetValue<float>());
        }
        public void InitializeSlot(int index,string name){}
        public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>AlsSlotWeights.Passthrough;
        public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context){}
        public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool sourceEvaluated,ReadOnlySpan<AlsLocalPose> sourcePose,
            ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)=>throw new InvalidOperationException("No montage in native trace.");
        public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped){}
    }
}
