using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

public partial class LyraNotifyDispatchSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Lyra notify dispatch: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        _=new LyraNotifyDispatchPolicy();using var resources=new LyraLocomotionResources(includeMontageActions:true);
        var catalog=resources.Catalog.Notifies;
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"notify_dispatch_v1_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"notify_dispatch_v1_native.json"));
        var reference=native.RootElement;
        Require(reference.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"notify_dispatch_v1_requests.json"))&&
            reference.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"notify_dispatch_v1_policy.json")),"Stale native notify reference.");
        int traces=0,frames=0,active=0,named=0,retries=0,endedHistory=0,inactiveHistory=0;
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var queue=new LyraNotifyQueueRuntime(catalog,(uint)traces+1);int i=0;bool bound=true;
            ImmutableArray<LyraNotifyReference> history=[];
            foreach(var f in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"dispatch/{traces}/{i}/{trace.GetProperty("mode")}";
                var expected=reference.GetProperty("trace").GetProperty("traces")[traces].GetProperty("frames")[i];
                bool replace=f.GetProperty("replaceHistory").GetBoolean();history=replace?queue.Queued:history.AddRange(queue.Queued);
                bool Any()=>history.Any(r=>catalog.Event(r.Core.PolicyIndex).Kind==LyraAssetNotifyKind.TransitionToLocomotion);
                bool Pivot()=>LyraNotifyQueueRuntime.WasTransitionActiveInMainSourceState(catalog,history,LyraNotifyDispatchPolicy.MainMachine,4);
                Require(Any()==expected.GetProperty("before").GetProperty("any").GetBoolean()&&Pivot()==expected.GetProperty("before").GetProperty("pivotRule").GetBoolean(),label+" previous source-state rule");
                if(replace)Require(queue.WasTransitionActiveInMainSourceState(LyraNotifyDispatchPolicy.MainMachine,4)==Pivot(),label+" committed history query");
                if(Pivot())active++;
                endedHistory+=history.Count(r=>r.Core.ReachedEnd&&catalog.Event(r.Core.PolicyIndex).Kind==LyraAssetNotifyKind.TransitionToLocomotion);
                inactiveHistory+=history.Count(r=>!r.Core.ActiveContext&&catalog.Event(r.Core.PolicyIndex).Kind==LyraAssetNotifyKind.TransitionToLocomotion);
                var windows=f.GetProperty("windows").EnumerateArray().Select((w,n)=>
                {
                    LyraNotifyMainStateContext c=default;
                    if(w.TryGetProperty("states",out var states)&&states.GetArrayLength()>0)c=new(states[0][0].GetInt32(),states[0][1].GetInt32(),true);
                    return new LyraNotifyHarvestWindow(new(LyraNotifySourceOwner.Linked,n,n,0,1,MainState:c),catalog.Asset(w.GetProperty("asset").GetString()!).Index,
                        w.GetProperty("previous").GetSingle(),w.GetProperty("delta").GetSingle(),w.GetProperty("current").GetSingle(),w.GetProperty("weight").GetSingle(),
                        w.GetProperty("leader").GetBoolean(),w.GetProperty("looping").GetBoolean(),w.GetProperty("active").GetBoolean());
                }).ToImmutableArray();
                var identity=new AlsFrameIdentity(i,(uint)traces+1,1);
                LyraNotifyQueueCandidate Prepare()=>queue.PrepareWindows(identity,f.GetProperty("delta").GetSingle(),windows,()=>{},mode:AlsAssetNotifyDispatchMode.ForceAnimGraphOnly);
                var old=queue.Queued;var cancelled=Prepare();queue.Cancel();Require(queue.Queued==old,label+" cancellation published history");
                var candidate=Prepare();Require(candidate.Queued.SequenceEqual(cancelled.Queued)&&candidate.Callbacks.SequenceEqual(cancelled.Callbacks),label+" retry changed queue");retries++;
                var states=candidate.States.Where(s=>catalog.Event(s.Reference.Core.PolicyIndex).Kind==LyraAssetNotifyKind.TransitionToLocomotion).Select(s=>catalog.Event(s.Reference.Core.PolicyIndex).ObjectPath);
                Require(states.SequenceEqual(expected.GetProperty("states").EnumerateArray().Select(s=>s.GetString()!)),label+" native active state");
                if(f.GetProperty("removeHandlers").GetBoolean())bound=false;
                var names=bound?candidate.Callbacks.Where(c=>c.Named).Select(c=>catalog.Event(c.Reference.Core.PolicyIndex).Name).ToArray():[];
                Require(names.SequenceEqual(expected.GetProperty("named").EnumerateArray().Select(s=>s.GetString()!)),label+" native external named event order");named+=names.Length;
                Require(Any()==expected.GetProperty("after").GetProperty("any").GetBoolean()&&Pivot()==expected.GetProperty("after").GetProperty("pivotRule").GetBoolean(),label+" dispatch mutated previous history");
                queue.Commit(candidate);frames++;i++;
            }
            queue.Retire();Require(queue.Queued.IsEmpty,traces+" retired queue survived");traces++;
        }
        Require(active>0&&named>0&&endedHistory>0&&inactiveHistory>0,"Missing notify history boundaries.");
        GD.Print($"LYRA_NOTIFY_DISPATCH_GODOT_OK traces={traces} frames={frames} retries={retries} pivotTrue={active} named={named} reachedEndHistory={endedHistory} inactiveHistory={inactiveHistory} wholeMainNative=false externalProductionHook=false");
    }
}
