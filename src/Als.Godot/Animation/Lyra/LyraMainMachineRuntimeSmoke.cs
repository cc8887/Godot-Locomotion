using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainMachineRuntimeSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static void Require(bool condition,string label) { if(!condition) throw new InvalidOperationException(label); }
    private static void Exact(float actual,JsonElement row,string name,string label)
    { var expected=row.GetProperty(name).GetSingle();Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),$"{label}/{name}: {actual:R} != {expected:R}"); }
    private static LyraLocomotionRuleInputs Input(JsonElement row)
    {
        var f=row.GetProperty("fields");var source=row.GetProperty("relevant");
        bool B(string n)=>f.GetProperty(n).GetBoolean();double D(string n)=>f.GetProperty(n).GetDouble();int I(string n)=>f.GetProperty(n).GetInt32();
        AlsDoubleVector V(string n) {var a=f.GetProperty(n);return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());}
        return new(B("HasAcceleration"),B("HasVelocity"),B("GameplayTag_IsMelee"),B("IsRunningIntoWall"),B("LinkedLayerChanged"),B("CrouchStateChange"),B("ADSStateChanged"),
            B("IsJumping"),B("IsFalling"),B("IsOnGround"),V("LocalVelocity2D"),V("LocalAcceleration2D"),I("StartDirection"),I("LocalVelocityDirection"),I("PivotInitialDirection"),
            D("DisplacementSpeed"),D("RootYawOffset"),D("LastPivotTime"),D("TimeToJumpApex"),D("GroundDistance"),row.GetProperty("beforeElapsed").GetSingle(),
            row.GetProperty("pivotNotify").GetBoolean(),row.GetProperty("syncValid").GetBoolean(),source.GetProperty("valid").GetBoolean(),source.GetProperty("length").GetSingle(),
            source.GetProperty("time").GetSingle(),source.GetProperty("looping").GetBoolean(),source.GetProperty("previousValid").GetBoolean(),source.GetProperty("previous").GetSingle(),source.GetProperty("delta").GetSingle());
    }
    private static string Snapshot(LyraLocomotionMachineHost host)=>JsonSerializer.Serialize(new { host.State,host.Elapsed,
        weights=Enumerable.Range(0,12).Select(host.Weight).ToArray(),stack=Enumerable.Range(0,host.Stack.Count).Select(host.Stack.GetTransition).ToArray() });
    public override void _Ready()
    { try { Run();GetTree().Quit(); } catch(Exception error) {GD.PushError("Main machine runtime failed: "+error);GetTree().Quit(1);} }
    private static void Run()
    {
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_machine_runtime_v2_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_machine_runtime_v2_native.json"));var data=native.RootElement;
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"main_machine_runtime_v2_requests.json")),"Changed Main machine request");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)),"Changed Main machine dependency: "+dep.Name);
        var catalog=LyraRuntimeGraphCatalog.Load();var frames=0;var updates=0;var hidden=0;var inactive=0;var resets=0;var transitions=0;var autoEdges=0;var depth=0;var inertia=0;
        var states=new HashSet<int>();var edges=new HashSet<int>();var clockBoundaries=0;var syncFrames=0;var negativeTimes=0;
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var host=new LyraLocomotionMachineHost(catalog,0);
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"main-machine/{ti}/{i}";
                var input=Input(row);var delta=frame.GetProperty("delta").GetSingle();
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                var old=Snapshot(host);var cancelled=host.Prepare(input,delta,visit);host.Cancel();Require(Snapshot(host)==old,label+"/cancel published");
                var c=host.Prepare(input,delta,visit);
                Require(c.Frame==cancelled.Frame && c.BeforeState==cancelled.BeforeState && c.Updates.SequenceEqual(cancelled.Updates) &&
                    c.Initializations.SequenceEqual(cancelled.Initializations) && c.ClearWeights.SequenceEqual(cancelled.ClearWeights) &&
                    c.PreviousWeights.SequenceEqual(cancelled.PreviousWeights),label+"/retry differs");
                Require(c.BeforeState==row.GetProperty("beforeState").GetInt32(),label+"/before state");Exact(c.BeforeElapsed,row,"beforeElapsed",label);
                Require(host.PreparedState(c)==row.GetProperty("state").GetInt32(),label+"/selected state");Exact(host.PreparedElapsed(c),row,"elapsed",label);
                for(var s=0;s<12;s++)
                {
                    Require(c.Initializations[s]==row.GetProperty("initializations")[s].GetInt32(),$"{label}/initialize/{s}: {c.Initializations[s]} != {row.GetProperty("initializations")[s]}");
                    Require(BitConverter.SingleToInt32Bits(host.PreparedWeight(c,s))==BitConverter.SingleToInt32Bits(row.GetProperty("weights")[s].GetSingle()),label+"/weight/"+s);
                    Require(BitConverter.SingleToInt32Bits(c.PreviousWeights[s])==BitConverter.SingleToInt32Bits(row.GetProperty("previousWeights")[s].GetSingle()),label+"/previous recorded weight/"+s);
                }
                var active=row.GetProperty("active");var stack=host.PreparedStack(c);Require(stack.Count==active.GetArrayLength(),label+"/stack count");depth=Math.Max(depth,stack.Count);
                for(var j=0;j<stack.Count;j++)
                {
                    var a=stack.GetTransition(j);var e=active[j];Require(a.From==e.GetProperty("previous").GetInt32() && a.To==e.GetProperty("next").GetInt32(),label+"/edge endpoints");
                    Exact(a.Duration,e,"duration",label);Exact(a.Elapsed,e,"elapsed",label);Exact(a.Alpha,e,"alpha",label);
                }
                var us=row.GetProperty("updates");Require(us.GetArrayLength()==c.Updates.Length,label+"/updates length");
                for(var j=0;j<c.Updates.Length;j++)
                { var a=c.Updates[j];var e=us[j];Require(a.State==e.GetProperty("state").GetInt32() && a.Active==e.GetProperty("active").GetBoolean() && a.Inertial==e.GetProperty("inertial").GetBoolean(),label+"/update context/"+j);Exact(a.Weight,e,"weight",label);updates++; }
                if(c.Selected is {} selected)
                {
                    transitions++;edges.Add(selected.Edge);
                    if(selected.ConduitPath.Any(id=>catalog.Locomotion.States[catalog.Locomotion.Edges[id].Previous].Exits.Single(e=>e.Edge==id).Automatic)) autoEdges++;
                    if(selected.Inertial)
                    { Require(row.GetProperty("requests").EnumerateArray().Any(r=>r.GetProperty("duration").GetSingle()==selected.Duration && r.GetProperty("profile").GetString()=="" && r.GetProperty("useBlendMode").GetBoolean()),label+"/Main inertia absent");inertia++; }
                }
                if(input.SourceLooping && input.SourcePreviousValid && (input.SourceTime-input.SourcePrevious)*input.SourceDelta<0) clockBoundaries++;
                syncFrames+=input.LocomotionSyncValid?1:0;negativeTimes+=input.SourceTime<0?1:0;
                Require(Snapshot(host)==old,label+"/prepare publication");host.Validate(c);Require(Snapshot(host)==old,label+"/validation publication");
                host.Cancel();var staleRejected=false;try {host.Commit(c);} catch(InvalidOperationException){staleRejected=true;}Require(staleRejected && Snapshot(host)==old,label+"/stale commit");
                c=host.Prepare(input,delta,visit);host.Commit(c);states.Add(host.State);frames++;hidden+=visit.Visited?0:1;inactive+=visit.Visited && !visit.Active?1:0;resets+=c.AutomaticallyInitialized?1:0;
            }
        }
        Require(frames==7560 && states.Count==10 && depth>=2 && resets>0 && hidden>0 && inactive>0 && syncFrames>0 && negativeTimes>0 && autoEdges>0 && inertia>0,"Incomplete actual Main machine coverage.");
        GD.Print($"LYRA_MAIN_MACHINE_RUNTIME_GODOT_OK frames={frames} states={states.Count} transitions={transitions} automaticEdges={autoEdges} updates={updates} depth={depth} hidden={hidden} inactiveParent={inactive} resets={resets} inertia={inertia} loopBoundaries={clockBoundaries} syncFrames={syncFrames} negativeTimes={negativeTimes} edges={string.Join(',',edges.Order())} exactWeights=true retry=true sourceObservation=native production=false wholeMachinePose=false");
    }
}
