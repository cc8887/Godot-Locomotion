using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Nonzero seeds enter actual owners through reflection. The production API
// exposes only the real Initialize/CacheBones callbacks, never a seed path.
public partial class LyraSkeletalInitializeSmoke:Node
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static object Field(object o,string n)=>o.GetType().GetField(n,Flags)!.GetValue(o)!;
    private static void Set(object o,string n,object v)=>o.GetType().GetProperty(n,Flags)!.SetValue(o,v);
    private static void Require(bool b,string s){if(!b)throw new InvalidOperationException(s);}
    private static float F(JsonElement e,string n)=>e.GetProperty(n).GetSingle();
    private static AlsDoubleVector V(JsonElement e)=>LyraFootPlacementSmoke.V(e);
    private static AlsPrecisePose Pose(JsonElement e)=>new(V(e.GetProperty("p")),LyraFootPlacementSmoke.Q(e.GetProperty("q")),V(e.GetProperty("s")));
    private static AlsLinearBoolBlend Blend(JsonElement e)=>new(e.GetProperty("initialized").GetBoolean(),F(e,"begin"),F(e,"target"),F(e,"alpha"),F(e,"value"),F(e,"time"),F(e,"remaining"));
    private static AlsFootVectorSpring VS(JsonElement e)=>new(V(e.GetProperty("velocity")),V(e.GetProperty("target")),e.GetProperty("valid").GetBoolean());
    private static AlsFootQuaternionSpring QS(JsonElement e)=>new(V(e.GetProperty("velocity")),LyraFootPlacementSmoke.Q(e.GetProperty("target")),e.GetProperty("valid").GetBoolean());
    private static AlsFootFloatSpring FS(JsonElement e)=>new(F(e,"velocity"),F(e,"target"),e.GetProperty("valid").GetBoolean());
    private static void Float(float a,float b,string s)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),s+": "+a.ToString("R")+" != "+b.ToString("R"));
    private sealed class NeverGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>throw new InvalidOperationException("An inactive or initialization path queried physics.");}
    private static AlsFootPlacementHistory SeedFoot(JsonElement e)
    {
        var legs=e.GetProperty("legs").EnumerateArray().Select(l=>AlsFootLegHistory.Default with
        {HeightSpring=FS(l.GetProperty("heightSpring")),RotationSpring=QS(l.GetProperty("rotationSpring")),
            OffsetSpring=VS(l.GetProperty("offsetSpring")),OffsetRotationSpring=QS(l.GetProperty("offsetRotationSpring"))}).ToImmutableArray();
        return AlsFootPlacementHistory.Default with{First=e.GetProperty("first").GetBoolean(),Delta=F(e,"delta"),Counter=e.GetProperty("counter").GetInt16(),
            PelvisOffset=V(e.GetProperty("pelvisOffset")),PelvisSpring=VS(e.GetProperty("pelvisSpring")),Root=Pose(e.GetProperty("root")),
            Component=Pose(e.GetProperty("component")),ComponentDelta=V(e.GetProperty("componentDelta")),GroundNormal=V(e.GetProperty("groundNormal")),
            GroundSpring=QS(e.GetProperty("groundSpring")),OnGround=e.GetProperty("onGround").GetBoolean(),Legs=legs};
    }
    private static void Compare(LyraSkeletalControlsHost host,JsonElement e)
    {
        var state=host.UpdateState;var nodes=e.GetProperty("nodes");
        for(var i=0;i<8;i++)
        {
            var n=nodes[i];Float(state.Alphas[i],F(n,"alpha"),"ActualAlpha "+i);
            var control=state.Controls[i];Require(control.Bool==Blend(n.GetProperty("bool")),"Retained BoolBlend differs: "+i);
            Require(control.ClampInitialized==n.GetProperty("clampInitialized").GetBoolean(),"Clamp initialization differs: "+i);Float(control.ClampValue,F(n,"clampValue"),"Retained Clamp value "+i);
        }
        Require(state.Root==Blend(nodes[2].GetProperty("bool"))&&state.Foot==Blend(nodes[5].GetProperty("bool")),"Actual Bool alpha owner differs.");
        var foot=e.GetProperty("foot");var expected=SeedFoot(foot);var h=host.History.Foot;
        Float(state.FootDelta,F(foot,"delta"),"Foot update Delta");
        Require(state.FootFirst==expected.First&&state.FootCounter==expected.Counter,"Foot update counters differ.");
        Require((h with{Legs=default})==(expected with{Legs=default})&&h.Legs.SequenceEqual(expected.Legs),"Foot defined interpolation/retained history differs.");
        foreach(var (leg,i) in e.GetProperty("leg").GetProperty("legs").EnumerateArray().Select((l,i)=>(l,i)))
            Require(host.History.Legs[i]==new AlsLegIkBendHistory(V(leg.GetProperty("real")),V(leg.GetProperty("base"))),"FK bend history differs.");
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Skeletal initialization failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://artifacts/lyra-analysis/skeletal-initialize-v2-";
        var bytes=Godot.FileAccess.GetFileAsBytes(root+"native.json");using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"closure.json"));
        Require(LyraLogicalSourceBank.Sha(bytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale skeletal initialization native reference.");
        using var data=JsonDocument.Parse(bytes);using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        var rows=0;var rejected=0;var boneComparisons=0;var repeated=0;var deferredRejected=0;
        foreach(var c in data.RootElement.GetProperty("cases").EnumerateArray())
        {
            var profile=c.GetProperty("profile").GetString()!;using var main=new LyraMainPoseHost(resources,profile);var owner=main.Main.LayerInstances.Single();
            var host=(LyraSkeletalControlsHost)Field(owner,"_skeletal");var update=(LyraSkeletalControlUpdateHost)Field(host,"_update");
            var lookups=0;int Bone(string name){lookups++;return bank.Bone(name);}
            var deferred=new AlsLyraSkeletalControls(bank.Parents,bank.Reference,Bone,.5f,true);
            Require(lookups==0&&deferred.BoneBindings.Count==0,"Construction eagerly bound unused bone controls.");
            var output=new AlsPrecisePose[81];var alphas=new float[8];var initial=AlsLyraSkeletalHistory.Default;
            var character=new AlsFootCharacterInput(AlsPrecisePose.Identity,false,false,default,default,default);
            foreach(var node in new[]{103,102,104,110,109,105,107,106})
            {
                try{deferred.Evaluate(bank.Reference,alphas,character,new NeverGround(),initial,output);throw new Exception("Incomplete CacheBones accepted evaluation.");}
                catch(InvalidOperationException e)when(e.Message.Contains("complete CacheBones")){deferredRejected++;}
                Require(output.All(p=>p==default),"Rejected incomplete cache wrote a pose.");deferred.CacheBoneReferences(node);
            }
            var retained=deferred.Evaluate(bank.Reference,alphas,character,new NeverGround(),initial,output);
            Require(retained==initial&&output.AsSpan().SequenceEqual(bank.Reference),"Completed inactive controls changed pose/history.");
            var reference=c.GetProperty("reference");Require(reference.GetArrayLength()==81,"Incomplete native ALS81 reference.");
            for(var i=0;i<81;i++)
            {
                var r=reference[i];Require(bank.Bone(r.GetProperty("name").GetString()!)==i&&bank.Parents[i]==r.GetProperty("parent").GetInt32(),"Native target bone layout differs.");
                var expected=Pose(r);var actual=bank.Reference[i];
                Require((expected.Position-actual.Position).LengthSquared<=1e-16&&
                    Math.Abs(expected.Rotation.X-actual.Rotation.X)<=1e-10&&Math.Abs(expected.Rotation.Y-actual.Rotation.Y)<=1e-10&&
                    Math.Abs(expected.Rotation.Z-actual.Rotation.Z)<=1e-10&&Math.Abs(expected.Rotation.W-actual.Rotation.W)<=1e-10&&
                    (expected.Scale-actual.Scale).LengthSquared<=1e-24,"Native target reference differs: "+i);
            }
            Require(host.BoneBindings.Count==8,"Actual startup skipped a bone control cache.");
            foreach(var round in c.GetProperty("rounds").EnumerateArray())
            {
                var before=round.GetProperty("before");var after=round.GetProperty("after");var nativeNodes=before.GetProperty("nodes");
                var controls=nativeNodes.EnumerateArray().Select(n=>new LyraSkeletalControlStorage(Blend(n.GetProperty("bool")),n.GetProperty("clampInitialized").GetBoolean(),F(n,"clampValue"))).ToImmutableArray();
                var foot=SeedFoot(before.GetProperty("foot"));var history=new AlsLyraSkeletalHistory(foot,
                    before.GetProperty("leg").GetProperty("legs").EnumerateArray().Select(l=>new AlsLegIkBendHistory(V(l.GetProperty("real")),V(l.GetProperty("base")))).ToImmutableArray());
                Set(host,"History",history);Set(update,"State",update.State with{Root=controls[2].Bool,Foot=controls[5].Bool,
                    FootDelta=foot.Delta,FootFirst=foot.First,FootCounter=foot.Counter,Controls=controls,
                    Alphas=nativeNodes.EnumerateArray().Select(n=>F(n,"alpha")).ToImmutableArray()});
                Compare(host,before);var worker=owner.PreparedWorkerState;var trace=new List<string>();
                var counter=new AlsGraphTraversalCounter((short)(round.GetProperty("round").GetInt32()+50),100);
                owner.Phases.Initialize(LyraLayerHook.FullBody_SkeletalControls,counter,trace.Add);Compare(host,after);
                Require(trace.SequenceEqual(new[]{113,108,106,107,105,109,110,104,102,103,111,112}.Select(n=>"provider:"+n)),"Skeletal Initialize missed actual topology.");
                trace.Clear();owner.Phases.CacheBones(LyraLayerHook.FullBody_SkeletalControls,counter,trace.Add);Compare(host,round.GetProperty("cached"));
                foreach(var n in round.GetProperty("cached").GetProperty("nodes").EnumerateArray())
                {
                    var id=n.GetProperty("node").GetInt32();var actual=host.BoneBindings[id];
                    var expected=n.GetProperty("bones").EnumerateObject().Select(b=>b.Value).Where(b=>b.GetProperty("index").GetInt32()>=0).ToArray();
                    foreach(var b in expected){Require(bank.Bone(b.GetProperty("name").GetString()!)==b.GetProperty("index").GetInt32(),"Native control cache index differs.");boneComparisons++;}
                    Require(actual.ToHashSet().SetEquals(expected.Select(b=>b.GetProperty("index").GetInt32())),"Original control references differ: "+id);rows++;
                }
                for(var i=0;i<4;i++)Float(host.FootLengths[i],round.GetProperty("footLengths")[i].GetSingle(),"Native Foot cached reference length "+i);
                var snapshot=JsonSerializer.Serialize(new{host.History,host.UpdateState});
                owner.Phases.CacheBones(LyraLayerHook.FullBody_SkeletalControls,counter,trace.Add);
                Require(JsonSerializer.Serialize(new{host.History,host.UpdateState})==snapshot,"Repeated CacheBones cleared alpha, interpolation or bend history.");repeated++;
                Require(owner.PreparedWorkerState==worker&&main.Main.SyncPlayers.IsEmpty&&main.Main.SyncSamples.IsEmpty,"Skeletal phase ran worker/source work.");
                var oldState=host.UpdateState;var oldHistory=host.History;var input=new LyraSkeletalUpdateInput(1f/60,52,false,false,false,false,default);
                var pending=host.Prepare(input);
                foreach(var cache in new[]{false,true})
                {
                    try{if(cache)owner.CacheBonesPhaseNode(105);else owner.InitializePhaseNode(105);throw new Exception("Pending skeletal phase was accepted.");}
                    catch(InvalidOperationException e)when(e.Message.Contains("idle layer")){rejected++;}
                }
                Require(ReferenceEquals(host.UpdateState,oldState)&&ReferenceEquals(host.History,oldHistory),"Rejected skeletal phase published history.");
                host.Cancel();var retry=host.Prepare(input);
                Require(pending.Update.Updated==retry.Update.Updated&&pending.Updated==retry.Updated,"Hidden skeletal retry differs.");host.Commit(retry,true);
                Require(host.UpdateState==oldState&&host.History==oldHistory,"Hidden skeletal frame changed initialized history.");
            }
        }
        Require(rows==48&&rejected==12&&repeated==6&&deferredRejected==24,"Incomplete skeletal initialization coverage.");
        GD.Print($"LYRA_SKELETAL_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=24 seededRows={rows} cacheBoneRefs={boneComparisons} repeatedCache={repeated} pendingRejected={rejected} hiddenRetry=6 deferredRejected={deferredRejected} als=81 sourceUpdate=false evaluate=false privateFootLegStorageCompared=false");
    }
}
