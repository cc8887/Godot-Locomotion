using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Seeds target the actual owners; production exposes no test seed API.
public partial class LyraBlendSpaceInitializeSmoke:Node
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static object Field(object o,string n)=>o.GetType().GetField(n,Flags)!.GetValue(o)!;
    private static void Set(object o,string n,object value)=>o.GetType().GetProperty(n,Flags)!.SetValue(o,value);
    private static void Require(bool b,string text){if(!b)throw new InvalidOperationException(text);}
    private static float F(JsonElement e,string n)=>e.GetProperty(n).GetSingle();
    private static AlsAssetMarkerRecord Marker(JsonElement e)=>new(e.GetProperty("previousIndex").GetInt32(),e.GetProperty("nextIndex").GetInt32(),F(e,"previousDistance"),F(e,"nextDistance"));
    private static void CompareMarker(AlsAssetMarkerRecord m,JsonElement a)
    {
        Require(m.PreviousIndex==a.GetProperty("previousIndex").GetInt32()&&m.NextIndex==a.GetProperty("nextIndex").GetInt32()&&
            m.PreviousDistance==F(a,"previousDistance")&&m.NextDistance==F(a,"nextDistance")&&!m.Initialized,"BlendSpace marker initialization diverged.");
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("BlendSpace Initialize failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        var bytes=Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/blendspace-initialize-v2-native.json");
        using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/blendspace-initialize-v2-closure.json"));
        Require(LyraLogicalSourceBank.Sha(bytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale BlendSpace initialization reference.");
        using var data=JsonDocument.Parse(bytes);using var resources=new LyraLocomotionResources();var rows=0;var rejected=0;
        ImmutableArray<LyraMainLeanSampleState> seeded=[new(new(0,.75f,.2f),new(0,0,.4f,.2f,AlsAssetMarkerRecord.Invalid,.2f,.03125f))];
        foreach(var c in data.RootElement.GetProperty("cases").EnumerateArray())
        {
            var profile=c.GetProperty("profile").GetString()!;using var host=new LyraMainPoseHost(resources,profile);
            var owner=host.Main.LayerInstances.Single();var aim=(LyraAimingLayerHost)Field(owner,"_aiming");
            var lean=(LyraMainLeanSourceHost)Field(host.Main.MainState.Lean,"_lean");
            var initialize=(Action<int>)Field(host.GraphPhases,"_initializeNode");
            foreach(var row in c.GetProperty("sources").EnumerateArray())
            {
                var before=row.GetProperty("before");var after=row.GetProperty("after");var node=row.GetProperty("node").GetInt32();
                if(row.GetProperty("aim").GetBoolean())
                {
                    var index=node==79?0:1;var state=aim.Nodes[index];var other=aim.Nodes[1-index];
                    var filters=before.GetProperty("filters");
                    Set(aim,"Nodes",aim.Nodes.SetItem(index,state with{Initialized=true,X=F(before,"x"),Y=F(before,"y"),Time=F(before,"internal"),
                        Weight=F(before,"weight"),Alpha=F(before,"alpha"),Previous=F(before,"deltaPrevious"),Delta=F(before,"delta"),
                        Cache=before.GetProperty("triangle").GetInt32(),Samples=seeded,Marker=Marker(before),HasBeenFullWeight=true,LodEnabled=true,
                        FilterX=new(true,13.5f,4,F(filters[0],"output")),FilterY=new(true,13.5f,4,F(filters[1],"output"))}));
                    var worker=owner.PreparedWorkerState;owner.InitializePhaseNode(node);state=aim.Nodes[index];
                    Require(state.Initialized&&state.Time==F(after,"internal")&&state.Samples.IsEmpty&&state.Cache==after.GetProperty("triangle").GetInt32(),"Aiming clock/sample/cache reset diverged.");
                    Require(state.X==F(after,"x")&&state.Y==F(after,"y")&&state.Weight==F(after,"weight")&&state.Alpha==F(after,"alpha")&&
                        state.Previous==F(after,"deltaPrevious")&&state.Delta==F(after,"delta")&&!state.HasBeenFullWeight&&state.LodEnabled==after.GetProperty("lodEnabled").GetBoolean(),"Aiming retained fields diverged.");
                    Require(state.FilterX==default&&state.FilterY==default&&aim.Nodes[1-index]==other&&owner.PreparedWorkerState==worker,"Aiming initialization touched another occurrence or worker.");
                    CompareMarker(state.Marker,after);
                    var definition=(LyraAimingGraphDefinition)Field(aim,"_definition");
                    Require(after.GetProperty("asset").GetString()==(index==0?definition.RelaxedAsset:definition.IdleAsset),"Aiming exposed asset binding diverged.");
                    Require(after.GetProperty("filters")[0].GetProperty("valid").GetBoolean()==(((float[,])Field(aim,"_window"))[index,0]>0)&&
                        after.GetProperty("filters")[1].GetProperty("valid").GetBoolean()==(((float[,])Field(aim,"_window"))[index,1]>0),"Aiming filter configuration diverged.");
                }
                else
                {
                    var index=Array.IndexOf(new[]{22,16,12},node);var state=lean.States[index];var prior=lean.States;
                    Set(lean,"States",prior.SetItem(index,state with{Initialized=true,Pin=F(before,"x"),Time=F(before,"internal"),CachedWeight=F(before,"weight"),
                        DeltaPrevious=F(before,"deltaPrevious"),Delta=F(before,"delta"),Samples=seeded,Marker=Marker(before),
                        FilterOutput=F(before.GetProperty("filters")[0],"output"),CachedTriangle=before.GetProperty("triangle").GetInt32(),HasBeenFullWeight=true}));
                    var observation=Field(host.Main.MainState.Update,"_observation");var original=host.Main.MainState.Update.State;
                    Set(observation,"State",original with{Rotation=original.Rotation with{LeanAngle=after.GetProperty("x").GetDouble()}});
                    initialize(node);state=lean.States[index];
                    Require(state.Initialized&&state.Time==F(after,"internal")&&state.Pin==F(after,"x")&&state.Samples.IsEmpty&&state.FilterOutput==0&&
                        state.CachedTriangle==after.GetProperty("triangle").GetInt32()&&!state.HasBeenFullWeight,"Main Lean source initialization diverged.");
                    Require(state.CachedWeight==F(after,"weight")&&state.DeltaPrevious==F(after,"deltaPrevious")&&state.Delta==F(after,"delta"),"Main Lean initialization erased retained history.");
                    CompareMarker(state.Marker,after);for(var n=0;n<3;n++)if(n!=index)Require(lean.States[n]==prior[n],"Main Lean initialization touched another occurrence.");
                    Set(observation,"State",original);
                }
                Require(host.Main.SyncPlayers.IsEmpty&&host.Main.SyncSamples.IsEmpty,"BlendSpace initialization registered source work.");rows++;
            }
            var beforeAim=aim.Nodes;var ac=aim.Prepare(new(false,true,false,10,0,false,1d/60,0),0,0,new(false,1,false,true),aim.Weights);
            try{owner.InitializePhaseNode(79);throw new Exception("Pending Aiming accepted initialization.");}
            catch(InvalidOperationException e)when(e.Message.Contains("idle layer")){rejected++;}
            Require(aim.Nodes==beforeAim,"Rejected Aiming initialization changed history.");aim.Cancel();
            var ar=aim.Prepare(new(false,true,false,10,0,false,1d/60,0),0,0,new(false,1,false,true),aim.Weights);
            Require(ac.Nodes.SequenceEqual(ar.Nodes),"Hidden Aiming retry diverged.");aim.Collect(ar);aim.Resolve(ar,[],[]);aim.Commit(ar,true);
            Require(aim.Nodes.SequenceEqual(beforeAim),"Hidden Aiming committed source changes.");
            var beforeLean=lean.States;var lc=lean.Prepare(23.5f,1f/60,new float[]{1,0,0},new bool[3],new bool[3],new[]{0,1,2});
            try{initialize(22);throw new Exception("Pending Main Lean accepted initialization.");}
            catch(InvalidOperationException e)when(e.Message.Contains("idle source")){rejected++;}
            Require(lean.States==beforeLean,"Rejected Main Lean initialization changed history.");lean.Cancel();
            var lr=lean.Prepare(23.5f,1f/60,new float[]{1,0,0},new bool[3],new bool[3],new[]{0,1,2});
            Require(lc.Prepared.SequenceEqual(lr.Prepared),"Hidden Main Lean retry diverged.");var inputs=lean.CollectAtCommonSync(lr);var resolved=lean.Resolve(inputs,[],[]);lean.Commit(resolved);
            Require(lean.States.SequenceEqual(beforeLean),"Hidden Main Lean committed source changes.");
            var full=lean.Prepare(23.5f,1f/60,new float[]{1,0,0},new[]{true,false,false},new bool[3],new[]{0,1,2});
            Require(full.Prepared[0].HasBeenFullWeight&&!beforeLean[0].HasBeenFullWeight,"Full-weight history did not latch on Update.");lean.Cancel();
            Require(lean.States.SequenceEqual(beforeLean),"Cancelled full-weight update leaked.");
        }
        Require(rows==30&&rejected==6,"Incomplete BlendSpace initialization coverage.");
        GD.Print("LYRA_BLENDSPACE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=15 seededRows=30 pendingRejected=6 hiddenRetry=6 sourceUpdateDuringInitialize=false evaluate=false privateFilterHistoryCompared=false");
    }
}
